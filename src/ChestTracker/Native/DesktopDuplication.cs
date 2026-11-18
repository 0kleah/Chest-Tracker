using System.Runtime.InteropServices;
using ChestTracker.Core;

namespace ChestTracker.Native;

/// <summary>
/// Fast screen pictures with Windows' Desktop Duplication (what screen recorders and Discord use): the graphics card
/// copies the part of the screen Fortnite is on and shrinks it, and only the small picture comes back. Much quicker
/// than the old GDI way on big screens. Only used while your map is open; nothing in the game is read or touched.
/// </summary>
internal sealed unsafe class DesktopDuplication : IDisposable
{
    [DllImport("dxgi.dll")] static extern int CreateDXGIFactory1(Guid* riid, IntPtr* factory);
    [DllImport("d3d11.dll")]
    static extern int D3D11CreateDevice(IntPtr adapter, int driverType, IntPtr software, uint flags, int* featureLevels,
                                        uint levels, uint sdkVersion, IntPtr* device, int* featureLevel, IntPtr* context);

    static readonly Guid IidFactory1 = new("770aae78-f26f-4dba-a829-253c83d1b387");
    static readonly Guid IidOutput1 = new("00cddea8-939b-4b83-a340-a685226666cc");
    static readonly Guid IidTexture2D = new("6f15aaf2-d208-4e89-9ab4-489535d34f9c");

    const int WaitTimeout = unchecked((int)0x887A0027);
    const int FormatBgra = 87;   // DXGI_FORMAT_B8G8R8A8_UNORM

    [StructLayout(LayoutKind.Sequential)]
    struct OutputDesc
    {
        public fixed char DeviceName[32];
        public Win32.RECT Rect;
        public int Attached;
        public int Rotation;
        public IntPtr Monitor;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct TextureDesc
    {
        public uint Width, Height, MipLevels, ArraySize;
        public int Format;
        public uint SampleCount, SampleQuality;
        public int Usage;
        public uint BindFlags, CpuAccessFlags, MiscFlags;
    }

    [StructLayout(LayoutKind.Sequential)] struct Box { public uint Left, Top, Front, Right, Bottom, Back; }
    [StructLayout(LayoutKind.Sequential)] struct Mapped { public byte* Data; public uint RowPitch, DepthPitch; }

    // ---- COM calls by vtable slot (no COM wrappers needed)
    static void* Slot(IntPtr obj, int i) => (*(void***)obj)[i];
    static void Release(IntPtr p) { if (p != IntPtr.Zero) ((delegate* unmanaged<IntPtr, uint>)Slot(p, 2))(p); }
    static int Query(IntPtr p, Guid iid, out IntPtr result)
    {
        IntPtr r;
        int hr = ((delegate* unmanaged<IntPtr, Guid*, IntPtr*, int>)Slot(p, 0))(p, &iid, &r);
        result = hr >= 0 ? r : IntPtr.Zero;
        return hr;
    }

    IntPtr _device, _context, _dupl;
    readonly Win32.RECT _output;      // where this monitor is on the desktop
    IntPtr _mipTex, _srv, _staging;   // reused GPU pictures for the current size
    uint _w, _h;
    int _mip = -1;
    GrayImage? _last;                 // the last picture, reused when nothing on screen changed

    DesktopDuplication(IntPtr device, IntPtr context, IntPtr dupl, Win32.RECT output)
    {
        _device = device; _context = context; _dupl = dupl; _output = output;
    }

    /// <summary>Is this area all on the monitor this one copies?</summary>
    public bool Covers(Win32.RECT a) =>
        a.Left >= _output.Left && a.Top >= _output.Top && a.Right <= _output.Right && a.Bottom <= _output.Bottom;

    /// <summary>Sets up copying for the monitor an area is on, or null if Windows won't allow it there.</summary>
    public static DesktopDuplication? For(Win32.RECT area)
    {
        int cx = (area.Left + area.Right) / 2, cy = (area.Top + area.Bottom) / 2;
        IntPtr factory;
        var iid = IidFactory1;
        if (CreateDXGIFactory1(&iid, &factory) < 0) return null;
        try
        {
            for (uint a = 0; ; a++)
            {
                IntPtr adapter;
                if (((delegate* unmanaged<IntPtr, uint, IntPtr*, int>)Slot(factory, 12))(factory, a, &adapter) < 0) return null;
                try
                {
                    for (uint o = 0; ; o++)
                    {
                        IntPtr output;
                        if (((delegate* unmanaged<IntPtr, uint, IntPtr*, int>)Slot(adapter, 7))(adapter, o, &output) < 0) break;
                        try
                        {
                            OutputDesc desc;
                            if (((delegate* unmanaged<IntPtr, OutputDesc*, int>)Slot(output, 7))(output, &desc) < 0 || desc.Attached == 0) continue;
                            var r = desc.Rect;
                            if (cx < r.Left || cx >= r.Right || cy < r.Top || cy >= r.Bottom) continue;
                            // Fortnite spread over two monitors (a window across the gap): use the old way for it
                            if (area.Left < r.Left || area.Top < r.Top || area.Right > r.Right || area.Bottom > r.Bottom) return null;
                            if (desc.Rotation is not (0 or 1)) return null;   // a turned monitor: use the old way
                            IntPtr device, context;
                            int level;
                            if (D3D11CreateDevice(adapter, 0, IntPtr.Zero, 0x20, null, 0, 7, &device, &level, &context) < 0) return null;
                            if (Query(output, IidOutput1, out var output1) < 0) { Release(context); Release(device); return null; }
                            IntPtr dupl;
                            int hr = ((delegate* unmanaged<IntPtr, IntPtr, IntPtr*, int>)Slot(output1, 22))(output1, device, &dupl);
                            Release(output1);
                            if (hr < 0) { Release(context); Release(device); return null; }
                            return new DesktopDuplication(device, context, dupl, r);
                        }
                        finally { Release(output); }
                    }
                }
                finally { Release(adapter); }
            }
        }
        finally { Release(factory); }
    }

    /// <summary>A greyscale picture of the area, shrunk by halves to about <paramref name="height"/> tall. Null if it failed.</summary>
    public GrayImage? Gray(Win32.RECT area, int height)
    {
        if (_dupl == IntPtr.Zero || !Covers(area)) return null;
        uint w = (uint)(area.Right - area.Left), h = (uint)(area.Bottom - area.Top);
        int mip = ScreenCapture.Halvings((int)h, height);
        if (!Prepare(w, h, mip)) return null;
        uint ow = Math.Max(1, w >> mip), oh = Math.Max(1, h >> mip);

        // the newest screen picture
        byte* info = stackalloc byte[64];
        IntPtr resource;
        int hr = ((delegate* unmanaged<IntPtr, uint, byte*, IntPtr*, int>)Slot(_dupl, 8))(_dupl, 16, info, &resource);
        if (hr == WaitTimeout && _last is { } last && last.Width == ow && last.Height == oh) return last;   // nothing changed
        if (hr == WaitTimeout) hr = ((delegate* unmanaged<IntPtr, uint, byte*, IntPtr*, int>)Slot(_dupl, 8))(_dupl, 200, info, &resource);
        if (hr < 0) return null;   // lost (screen mode changed, and so on): the caller starts again
        try
        {
            if (Query(resource, IidTexture2D, out var desktop) < 0) return null;
            try
            {
                var box = new Box
                {
                    Left = (uint)(area.Left - _output.Left), Top = (uint)(area.Top - _output.Top), Front = 0,
                    Right = (uint)(area.Right - _output.Left), Bottom = (uint)(area.Bottom - _output.Top), Back = 1,
                };
                var target = mip == 0 ? _staging : _mipTex;
                // CopySubresourceRegion (slot 46)
                ((delegate* unmanaged<IntPtr, IntPtr, uint, uint, uint, uint, IntPtr, uint, Box*, void>)Slot(_context, 46))
                    (_context, target, 0, 0, 0, 0, desktop, 0, &box);
            }
            finally { Release(desktop); }
        }
        finally
        {
            Release(resource);
            ((delegate* unmanaged<IntPtr, int>)Slot(_dupl, 14))(_dupl);   // ReleaseFrame
        }
        if (mip > 0)
        {
            ((delegate* unmanaged<IntPtr, IntPtr, void>)Slot(_context, 54))(_context, _srv);   // GenerateMips: halve on the GPU
            ((delegate* unmanaged<IntPtr, IntPtr, uint, uint, uint, uint, IntPtr, uint, Box*, void>)Slot(_context, 46))
                (_context, _staging, 0, 0, 0, 0, _mipTex, (uint)mip, null);
        }
        Mapped map;
        if (((delegate* unmanaged<IntPtr, IntPtr, uint, int, uint, Mapped*, int>)Slot(_context, 14))(_context, _staging, 0, 1, 0, &map) < 0) return null;
        var gray = new byte[ow * oh];
        try
        {
            nint data = (nint)map.Data;
            int pitch = (int)map.RowPitch;
            Parallel.For(0, (int)oh, y =>
            {
                byte* row = (byte*)data + (nint)y * pitch;
                int o = y * (int)ow;
                for (int x = 0; x < ow; x++, row += 4)
                    gray[o + x] = (byte)((row[0] * 29 + row[1] * 150 + row[2] * 77) >> 8);
            });
        }
        finally { ((delegate* unmanaged<IntPtr, IntPtr, uint, void>)Slot(_context, 15))(_context, _staging, 0); }   // Unmap
        return _last = new GrayImage((int)ow, (int)oh, gray);
    }

    /// <summary>Makes the GPU pictures for this size (kept until the size changes).</summary>
    bool Prepare(uint w, uint h, int mip)
    {
        if (w == _w && h == _h && mip == _mip && _staging != IntPtr.Zero) return true;
        FreeTextures();
        uint ow = Math.Max(1, w >> mip), oh = Math.Max(1, h >> mip);
        var staging = new TextureDesc
        {
            Width = ow, Height = oh, MipLevels = 1, ArraySize = 1, Format = FormatBgra, SampleCount = 1,
            Usage = 3 /* STAGING */, CpuAccessFlags = 0x20000 /* READ */,
        };
        IntPtr tex;
        if (((delegate* unmanaged<IntPtr, TextureDesc*, IntPtr, IntPtr*, int>)Slot(_device, 5))(_device, &staging, IntPtr.Zero, &tex) < 0) return false;
        _staging = tex;
        if (mip > 0)
        {
            var mips = new TextureDesc
            {
                Width = w, Height = h, MipLevels = (uint)mip + 1, ArraySize = 1, Format = FormatBgra, SampleCount = 1,
                Usage = 0 /* DEFAULT */, BindFlags = 0x8 | 0x20 /* SHADER_RESOURCE | RENDER_TARGET */, MiscFlags = 0x1 /* GENERATE_MIPS */,
            };
            if (((delegate* unmanaged<IntPtr, TextureDesc*, IntPtr, IntPtr*, int>)Slot(_device, 5))(_device, &mips, IntPtr.Zero, &tex) < 0) { FreeTextures(); return false; }
            _mipTex = tex;
            IntPtr srv;
            if (((delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr*, int>)Slot(_device, 7))(_device, _mipTex, IntPtr.Zero, &srv) < 0) { FreeTextures(); return false; }
            _srv = srv;
        }
        _w = w; _h = h; _mip = mip;
        _last = null;
        return true;
    }

    void FreeTextures()
    {
        Release(_srv); Release(_mipTex); Release(_staging);
        _srv = _mipTex = _staging = IntPtr.Zero;
        _w = _h = 0;
        _mip = -1;
    }

    public void Dispose()
    {
        FreeTextures();
        Release(_dupl); Release(_context); Release(_device);
        _dupl = _context = _device = IntPtr.Zero;
        _last = null;
    }
}
