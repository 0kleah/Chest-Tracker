using System.IO;
using System.Numerics;

namespace ChestTracker.Core;

/// <summary>A greyscale picture (one byte per pixel).</summary>
public sealed class GrayImage
{
    public int Width { get; }
    public int Height { get; }
    public byte[] Pixels { get; }

    public GrayImage(int width, int height, byte[]? pixels = null)
    {
        Width = width;
        Height = height;
        Pixels = pixels ?? new byte[width * height];
        if (Pixels.Length != width * height) throw new ArgumentException("wrong pixel count");
    }

    public byte At(int x, int y) => Pixels[y * Width + x];

    /// <summary>Resized with a triangle filter (smooth when shrinking, no blocky pixels when growing).</summary>
    public GrayImage Resize(int width, int height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        var rows = new float[width * Height];
        Resample(Pixels, Width, Height, rows, width, horizontal: true);
        var cols = new float[width * height];
        ResampleCols(rows, width, Height, cols, height);
        var px = new byte[width * height];
        for (int i = 0; i < px.Length; i++) px[i] = (byte)Math.Clamp((int)(cols[i] + 0.5f), 0, 255);
        return new GrayImage(width, height, px);
    }

    static (int[] Start, int[] Count, float[] Weights, int Taps) Kernel(int src, int dst)
    {
        double scale = (double)dst / src;
        double support = scale < 1 ? 1 / scale : 1;
        int taps = (int)Math.Ceiling(support) * 2 + 1;
        var start = new int[dst];
        var count = new int[dst];
        var w = new float[dst * taps];
        for (int i = 0; i < dst; i++)
        {
            double center = (i + 0.5) / scale - 0.5;
            int lo = (int)Math.Floor(center - support) + 1, hi = (int)Math.Floor(center + support);
            lo = Math.Max(lo, 0);
            hi = Math.Min(hi, src - 1);
            double sum = 0;
            int n = 0;
            for (int j = lo; j <= hi && n < taps; j++, n++)
            {
                double v = Math.Max(0, 1 - Math.Abs(j - center) / support);
                w[i * taps + n] = (float)v;
                sum += v;
            }
            if (sum <= 0) { w[i * taps] = 1; n = Math.Max(n, 1); sum = 1; lo = Math.Clamp((int)Math.Round(center), 0, src - 1); }
            for (int k = 0; k < n; k++) w[i * taps + k] /= (float)sum;
            start[i] = lo;
            count[i] = n;
        }
        return (start, count, w, taps);
    }

    static void Resample(byte[] src, int sw, int sh, float[] dst, int dw, bool horizontal)
    {
        var (start, count, w, taps) = Kernel(sw, dw);
        for (int y = 0; y < sh; y++)
        {
            int srow = y * sw, drow = y * dw;
            for (int x = 0; x < dw; x++)
            {
                float acc = 0;
                int s0 = start[x], n = count[x], wi = x * taps;
                for (int k = 0; k < n; k++) acc += src[srow + s0 + k] * w[wi + k];
                dst[drow + x] = acc;
            }
        }
    }

    static void ResampleCols(float[] src, int w, int sh, float[] dst, int dh)
    {
        var (start, count, wt, taps) = Kernel(sh, dh);
        for (int y = 0; y < dh; y++)
        {
            int s0 = start[y], n = count[y], wi = y * taps, drow = y * w;
            for (int x = 0; x < w; x++)
            {
                float acc = 0;
                for (int k = 0; k < n; k++) acc += src[(s0 + k) * w + x] * wt[wi + k];
                dst[drow + x] = acc;
            }
        }
    }

    /// <summary>A 5 x 5 box blur (two passes of running sums), used before comparing pixel pairs.</summary>
    public GrayImage Blur(bool parallel = false)
    {
        int w = Width, h = Height, r = 2, d = 2 * r + 1;
        var tmp = new byte[w * h];
        var outPx = new byte[w * h];
        var px = Pixels;
        void Row(int y)
        {
            int row = y * w, sum = 0;
            for (int k = -r; k <= r; k++) sum += px[row + Math.Clamp(k, 0, w - 1)];
            for (int x = 0; x < w; x++)
            {
                tmp[row + x] = (byte)(sum / d);
                sum += px[row + Math.Min(x + r + 1, w - 1)] - px[row + Math.Max(x - r, 0)];
            }
        }
        // the up-and-down pass, 64 columns at a time (same sums as one column at a time, but quicker to read)
        void Columns(int band)
        {
            int x0 = band * 64, x1 = Math.Min(w, x0 + 64);
            Span<int> sums = stackalloc int[64];
            for (int x = x0; x < x1; x++)
            {
                int s = 0;
                for (int k = -r; k <= r; k++) s += tmp[Math.Clamp(k, 0, h - 1) * w + x];
                sums[x - x0] = s;
            }
            for (int y = 0; y < h; y++)
            {
                int row = y * w, add = Math.Min(y + r + 1, h - 1) * w, sub = Math.Max(y - r, 0) * w;
                for (int x = x0; x < x1; x++)
                {
                    outPx[row + x] = (byte)(sums[x - x0] / d);
                    sums[x - x0] += tmp[add + x] - tmp[sub + x];
                }
            }
        }
        int bands = (w + 63) / 64;
        if (parallel) { Parallel.For(0, h, Row); Parallel.For(0, bands, Columns); }
        else { for (int y = 0; y < h; y++) Row(y); for (int b = 0; b < bands; b++) Columns(b); }
        return new GrayImage(w, h, outPx);
    }

    /// <summary>A copy of a rectangle of the picture.</summary>
    public GrayImage Crop(int x, int y, int width, int height)
    {
        var px = new byte[width * height];
        for (int row = 0; row < height; row++) Buffer.BlockCopy(Pixels, (y + row) * Width + x, px, row * width, width);
        return new GrayImage(width, height, px);
    }
}

/// <summary>Where the island map is on screen: screen = Scale × map pixel + Offset (in the screen picture's pixels).</summary>
public readonly record struct MapFix(double Scale, double OffsetX, double OffsetY, int Inliers);

/// <summary>
/// Finds the Fortnite map in a picture of the screen, at any zoom and position, by matching small landmarks (corners
/// of roads, buildings, coastlines) between the screen and the map picture. It only ever looks at pixels: nothing in
/// the game is read or changed. Landmarks of the map are worked out once, at many sizes, so any zoom level matches.
/// </summary>
public sealed class MapMatcher
{
    // ---- landmark settings
    const int Margin = 18;          // the pixel pairs reach 13 px from the corner, plus blur
    const int FastThreshold = 18;
    const int Cell = 32;            // keep the strongest corners per 32 x 32 cell, so they're spread out
    const int MaxDistance = 70;     // of 256 bits
    const float Ratio = 0.85f;

    /// <summary>Map sizes (screen pixels per map pixel) that landmarks are made for: 0.10 (the island small in the
    /// middle of the screen, zoomed all the way out) to 1.6 (zoomed right in), 20% apart.</summary>
    public static readonly double[] LevelScales = Enumerable.Range(0, 16).Select(k => 0.10 * Math.Pow(1.2, k)).ToArray();

    readonly float[] _mx, _my;      // landmark position on the map (map pixels)
    readonly byte[] _level;
    readonly ulong[] _desc;         // 4 per landmark
    public int MapWidth { get; }
    public int MapHeight { get; }
    public int Count => _mx.Length;

    MapMatcher(int w, int h, float[] mx, float[] my, byte[] level, ulong[] desc)
    {
        MapWidth = w; MapHeight = h; _mx = mx; _my = my; _level = level; _desc = desc;
    }

    // ------------------------------------------------------------------ building

    /// <summary>Works out the map's landmarks at every size (a few seconds; done once and saved).</summary>
    public static MapMatcher Build(GrayImage map, CancellationToken ct = default)
    {
        var mx = new List<float>(); var my = new List<float>(); var lv = new List<byte>(); var desc = new List<ulong>();
        for (int l = 0; l < LevelScales.Length; l++)
        {
            ct.ThrowIfCancellationRequested();
            double s = LevelScales[l];
            var img = map.Resize((int)Math.Round(map.Width * s), (int)Math.Round(map.Height * s));
            var feats = Detect(img, keepPerCell: 1, max: int.MaxValue, parallel: false);
            double sx = (double)img.Width / map.Width, sy = (double)img.Height / map.Height;
            foreach (var f in feats)
            {
                mx.Add((float)(f.X / sx));
                my.Add((float)(f.Y / sy));
                lv.Add((byte)l);
                desc.AddRange(f.D);
            }
        }
        return new MapMatcher(map.Width, map.Height, mx.ToArray(), my.ToArray(), lv.ToArray(), desc.ToArray());
    }

    const int FileVersion = 1;

    public void Save(string path, string key)
    {
        using var bw = new BinaryWriter(File.Create(path));
        bw.Write(FileVersion); bw.Write(key); bw.Write(MapWidth); bw.Write(MapHeight); bw.Write(Count);
        for (int i = 0; i < Count; i++)
        {
            bw.Write(_mx[i]); bw.Write(_my[i]); bw.Write(_level[i]);
            for (int k = 0; k < 4; k++) bw.Write(_desc[i * 4 + k]);
        }
    }

    /// <summary>Loads saved landmarks, if they were made from the same map picture (key) by this version.</summary>
    public static MapMatcher? Load(string path, string key)
    {
        try
        {
            using var br = new BinaryReader(File.OpenRead(path));
            if (br.ReadInt32() != FileVersion || br.ReadString() != key) return null;
            int w = br.ReadInt32(), h = br.ReadInt32(), n = br.ReadInt32();
            if (n <= 0 || n > 2_000_000) return null;
            var mx = new float[n]; var my = new float[n]; var lv = new byte[n]; var d = new ulong[n * 4];
            for (int i = 0; i < n; i++)
            {
                mx[i] = br.ReadSingle(); my[i] = br.ReadSingle(); lv[i] = br.ReadByte();
                for (int k = 0; k < 4; k++) d[i * 4 + k] = br.ReadUInt64();
            }
            return new MapMatcher(w, h, mx, my, lv, d);
        }
        catch (Exception e) when (e is IOException or EndOfStreamException or UnauthorizedAccessException) { return null; }
    }

    // ------------------------------------------------------------------ landmarks (FAST corners + BRIEF bits)

    readonly record struct Feature(float X, float Y, ulong[] D);

    static readonly (int X, int Y)[] Circle =
    {
        (0, -3), (1, -3), (2, -2), (3, -1), (3, 0), (3, 1), (2, 2), (1, 3),
        (0, 3), (-1, 3), (-2, 2), (-3, 1), (-3, 0), (-3, -1), (-2, -2), (-1, -3),
    };

    /// <summary>256 fixed pixel pairs around a corner (the same every time, so landmarks can be compared).</summary>
    static readonly sbyte[] Pairs = MakePairs();

    static sbyte[] MakePairs()
    {
        var rng = new Random(20261113);
        var p = new sbyte[256 * 4];
        for (int i = 0; i < p.Length; i++)
        {
            double u1 = 1 - rng.NextDouble(), u2 = rng.NextDouble();
            double g = Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
            p[i] = (sbyte)Math.Clamp((int)Math.Round(g * 5.5), -13, 13);
        }
        return p;
    }

    static List<Feature> Detect(GrayImage img, int keepPerCell, int max, bool parallel = true)
    {
        int w = img.Width, h = img.Height;
        if (w < 2 * Margin + 4 || h < 2 * Margin + 4) return new List<Feature>();
        var px = img.Pixels;
        var offs = Circle.Select(c => c.Y * w + c.X).ToArray();
        int cellsX = (w + Cell - 1) / Cell, cellsY = (h + Cell - 1) / Cell;
        // best corners per cell: (score, x, y)
        var best = new (int Score, int X, int Y)[cellsX * cellsY * keepPerCell];

        // one band of 32 rows at a time: each band only touches its own cells, so bands can run side by side
        void Band(int cy)
        {
            Span<int> ring = stackalloc int[16];
            int y0 = Math.Max(Margin, cy * Cell), y1 = Math.Min(h - Margin, (cy + 1) * Cell);
            for (int y = y0; y < y1; y++)
            {
                int row = y * w;
                for (int x = Margin; x < w - Margin; x++)
                {
                    int i = row + x, p = px[i], hiT = p + FastThreshold, loT = p - FastThreshold;
                    // quick rejection: at least 3 of the 4 compass points must be clearly brighter or darker
                    int a = px[i + offs[0]], b = px[i + offs[4]], c = px[i + offs[8]], d = px[i + offs[12]];
                    int brighter = (a > hiT ? 1 : 0) + (b > hiT ? 1 : 0) + (c > hiT ? 1 : 0) + (d > hiT ? 1 : 0);
                    int darker = (a < loT ? 1 : 0) + (b < loT ? 1 : 0) + (c < loT ? 1 : 0) + (d < loT ? 1 : 0);
                    if (brighter < 3 && darker < 3) continue;
                    for (int k = 0; k < 16; k++) ring[k] = px[i + offs[k]];
                    if (!Arc(ring, hiT, loT)) continue;
                    int score = 0;
                    for (int k = 0; k < 16; k++) score += Math.Max(0, Math.Abs(ring[k] - p) - FastThreshold);
                    int cell = (y / Cell) * cellsX + x / Cell, slot = cell * keepPerCell;
                    // keep the strongest few in this cell
                    int weakest = slot;
                    for (int k = 1; k < keepPerCell; k++) if (best[slot + k].Score < best[weakest].Score) weakest = slot + k;
                    if (score > best[weakest].Score) best[weakest] = (score, x, y);
                }
            }
        }
        if (parallel) Parallel.For(0, cellsY, Band);
        else for (int cy = 0; cy < cellsY; cy++) Band(cy);

        var chosen = best.Where(b => b.Score > 0).OrderByDescending(b => b.Score).Take(max).ToArray();
        var bp = img.Blur(parallel).Pixels;
        var result = new Feature[chosen.Length];
        void Describe(int n)
        {
            var (_, x, y) = chosen[n];
            var d = new ulong[4];
            int center = y * w + x;
            for (int k = 0; k < 256; k++)
            {
                int q = k * 4;
                int v1 = bp[center + Pairs[q + 1] * w + Pairs[q]];
                int v2 = bp[center + Pairs[q + 3] * w + Pairs[q + 2]];
                if (v1 < v2) d[k >> 6] |= 1UL << (k & 63);
            }
            result[n] = new Feature(x, y, d);
        }
        if (parallel) Parallel.For(0, chosen.Length, Describe);
        else for (int n = 0; n < chosen.Length; n++) Describe(n);
        return result.ToList();
    }

    /// <summary>9 or more pixels in a row around the circle all clearly brighter, or all clearly darker.</summary>
    static bool Arc(Span<int> ring, int hiT, int loT)
    {
        int runHi = 0, runLo = 0;
        for (int k = 0; k < 16 + 8; k++)
        {
            int v = ring[k & 15];
            runHi = v > hiT ? runHi + 1 : 0;
            runLo = v < loT ? runLo + 1 : 0;
            if (runHi >= 9 || runLo >= 9) return true;
        }
        return false;
    }

    // ------------------------------------------------------------------ finding the map

    /// <summary>
    /// Finds the map in a screen picture. With the previous position (the map was found a moment ago) only nearby sizes
    /// and the part of the map that can be on screen are tried first, which is much quicker; that works even after a
    /// big zoom or move in one go. Returns null if the map isn't on screen.
    /// </summary>
    public MapFix? Find(GrayImage screen, MapFix? previous = null)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        double detect = 0;
        try
        {
            if (previous is { } prev)
            {
                // following: only look where the map can be now (where it was, with room for a zoom or move),
                // and only the strongest landmarks
                var near = DetectNear(screen, prev, max: 900);
                detect = sw.Elapsed.TotalMilliseconds;
                LastFeatures = near.Count;
                if (near.Count >= 20 && Match(near, screen, prev) is { } tracked) return tracked;
            }
            var t0 = sw.Elapsed.TotalMilliseconds;
            var feats = Detect(screen, keepPerCell: 2, max: 1500);
            detect += sw.Elapsed.TotalMilliseconds - t0;
            LastFeatures = feats.Count;
            return feats.Count < 20 ? null : Match(feats, screen, null);
        }
        finally
        {
            LastDetectMs = detect;
            LastMatchMs = sw.Elapsed.TotalMilliseconds - detect;
        }
    }

    /// <summary>Landmarks in the part of the screen where the map can be, given where it was a moment ago.</summary>
    List<Feature> DetectNear(GrayImage screen, MapFix prev, int max)
    {
        double mw = prev.Scale * MapWidth, mh = prev.Scale * MapHeight;
        // room for the map to grow 1.6x around its middle, or move a quarter of the screen
        double padX = 0.3 * mw + 0.25 * screen.Height, padY = 0.3 * mh + 0.25 * screen.Height;
        int x0 = Math.Max(0, (int)(prev.OffsetX - padX)), y0 = Math.Max(0, (int)(prev.OffsetY - padY));
        int x1 = Math.Min(screen.Width, (int)(prev.OffsetX + mw + padX)), y1 = Math.Min(screen.Height, (int)(prev.OffsetY + mh + padY));
        if (x1 - x0 < 2 * Margin + 40 || y1 - y0 < 2 * Margin + 40) return new List<Feature>();
        if ((x1 - x0) * (double)(y1 - y0) > 0.8 * screen.Width * screen.Height) return Detect(screen, keepPerCell: 2, max: max);
        var part = Detect(screen.Crop(x0, y0, x1 - x0, y1 - y0), keepPerCell: 2, max: max);
        return part.Select(f => new Feature(f.X + x0, f.Y + y0, f.D)).ToList();
    }

    /// <summary>How long the last <see cref="Find"/> spent finding landmarks on screen, and matching them (for tuning).</summary>
    public double LastDetectMs { get; private set; }
    public double LastMatchMs { get; private set; }
    public int LastFeatures { get; private set; }

    MapFix? Match(List<Feature> feats, GrayImage screen, MapFix? prev)
    {
        int levels = LevelScales.Length;
        // Which landmarks to compare against. After the last position: sizes within about 1.6x either way (one
        // scroll of zoom), and only the part of the map that could be on screen, even if it moved a long way.
        var candidates = new List<int>();
        double padX = screen.Width * 0.6, padY = screen.Height * 0.6;
        for (int i = 0; i < _mx.Length; i++)
        {
            if (prev is { } p)
            {
                double ls = LevelScales[_level[i]];
                if (Math.Abs(Math.Log(ls / p.Scale)) > 0.47) continue;
                // where this landmark is on screen at the old zoom, allowing for a zoom of up to 1.6x around the middle
                double sx = p.Scale * _mx[i] + p.OffsetX, sy = p.Scale * _my[i] + p.OffsetY;
                if (sx < -padX || sy < -padY || sx > screen.Width + padX || sy > screen.Height + padY) continue;
            }
            candidates.Add(i);
        }
        if (candidates.Count == 0) return null;
        var cand = candidates.ToArray();

        // best match per screen landmark and map size (with a "clearly better than the next best" test per size),
        // spread over the CPU's cores
        var found = new int[feats.Count * levels];
        Parallel.For(0, feats.Count, f =>
        {
            Span<int> best = stackalloc int[levels];
            Span<int> second = stackalloc int[levels];
            Span<int> bestIdx = stackalloc int[levels];
            best.Fill(999); second.Fill(999); bestIdx.Fill(-1);
            var ft = feats[f];
            ulong d0 = ft.D[0], d1 = ft.D[1], d2 = ft.D[2], d3 = ft.D[3];
            foreach (int i in cand)
            {
                int bi = i * 4;
                int d = BitOperations.PopCount(d0 ^ _desc[bi]) + BitOperations.PopCount(d1 ^ _desc[bi + 1])
                      + BitOperations.PopCount(d2 ^ _desc[bi + 2]) + BitOperations.PopCount(d3 ^ _desc[bi + 3]);
                int l = _level[i];
                if (d < best[l]) { second[l] = best[l]; best[l] = d; bestIdx[l] = i; }
                else if (d < second[l]) second[l] = d;
            }
            for (int l = 0; l < levels; l++)
                found[f * levels + l] = bestIdx[l] >= 0 && best[l] <= MaxDistance && best[l] < Ratio * second[l] ? bestIdx[l] : -1;
        });
        var matches = new List<(int Screen, float SX, float SY, float MX, float MY)>();
        for (int f = 0; f < feats.Count; f++)
            for (int l = 0; l < levels; l++)
                if (found[f * levels + l] is int i and >= 0)
                    matches.Add((f, feats[f].X, feats[f].Y, _mx[i], _my[i]));
        if (matches.Count < 10) return null;
        return Ransac(matches, feats.Count, screen.Width, screen.Height, MapWidth, MapHeight);
    }

    static MapFix? Ransac(List<(int Screen, float SX, float SY, float MX, float MY)> m, int screenFeatures, int screenW, int screenH, int mapW, int mapH)
    {
        var rng = new Random(7);
        const double tol = 3.5, tol2 = tol * tol;
        int bestCount = 0;
        double bs = 0, btx = 0, bty = 0;
        var seen = new int[screenFeatures];
        int stamp = 0;
        for (int it = 0; it < 700; it++)
        {
            var a = m[rng.Next(m.Count)];
            var b = m[rng.Next(m.Count)];
            if (a.Screen == b.Screen) continue;
            double dmx = b.MX - a.MX, dmy = b.MY - a.MY, dsx = b.SX - a.SX, dsy = b.SY - a.SY;
            double mm = dmx * dmx + dmy * dmy;
            if (mm < 400) continue;   // too close together to tell the size
            double s = (dsx * dmx + dsy * dmy) / mm;
            if (s < 0.07 || s > 3.0) continue;
            // the two must agree on the size along both directions
            if (Math.Abs(dsx - s * dmx) > 6 || Math.Abs(dsy - s * dmy) > 6) continue;
            double tx = a.SX - s * a.MX, ty = a.SY - s * a.MY;
            int count = CountInliers(m, s, tx, ty, tol2, seen, ++stamp);
            if (count > bestCount) { bestCount = count; bs = s; btx = tx; bty = ty; }
            if (it >= 120 && bestCount >= 60) break;   // clearly found: no need to keep trying
        }
        if (bestCount < 12) return null;

        // refine with every inlier (least squares), twice
        for (int pass = 0; pass < 2; pass++)
        {
            double sumMx = 0, sumMy = 0, sumSx = 0, sumSy = 0;
            var inl = new List<(float SX, float SY, float MX, float MY)>();
            foreach (var x in m)
            {
                double ex = bs * x.MX + btx - x.SX, ey = bs * x.MY + bty - x.SY;
                if (ex * ex + ey * ey <= tol2) { inl.Add((x.SX, x.SY, x.MX, x.MY)); sumMx += x.MX; sumMy += x.MY; sumSx += x.SX; sumSy += x.SY; }
            }
            if (inl.Count < 12) return null;
            double mxm = sumMx / inl.Count, mym = sumMy / inl.Count, sxm = sumSx / inl.Count, sym = sumSy / inl.Count;
            double num = 0, den = 0;
            foreach (var x in inl)
            {
                num += (x.MX - mxm) * (x.SX - sxm) + (x.MY - mym) * (x.SY - sym);
                den += (x.MX - mxm) * (x.MX - mxm) + (x.MY - mym) * (x.MY - mym);
            }
            if (den <= 0) return null;
            bs = num / den;
            btx = sxm - bs * mxm;
            bty = sym - bs * mym;
        }
        int final = CountInliers(m, bs, btx, bty, tol2, seen, ++stamp);

        // The landmarks that agree must be spread over a good part of where the map would be on screen. The full map
        // (zoomed in or right out, on any shape of screen) passes; the small minimap in the corner doesn't: it would
        // put the map all over the screen while its landmarks sit in one small corner.
        double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue;
        foreach (var x in m)
        {
            double ex = bs * x.MX + btx - x.SX, ey = bs * x.MY + bty - x.SY;
            if (ex * ex + ey * ey > tol2) continue;
            minX = Math.Min(minX, x.SX); maxX = Math.Max(maxX, x.SX); minY = Math.Min(minY, x.SY); maxY = Math.Max(maxY, x.SY);
        }
        double visW = Math.Min(screenW, btx + bs * mapW) - Math.Max(0, btx);
        double visH = Math.Min(screenH, bty + bs * mapH) - Math.Max(0, bty);
        if (final < 12 || visW <= 0 || visH <= 0) return null;
        if (maxX - minX < Math.Max(60, 0.35 * visW) || maxY - minY < Math.Max(60, 0.35 * visH)) return null;
        return new MapFix(bs, btx, bty, final);
    }

    static int CountInliers(List<(int Screen, float SX, float SY, float MX, float MY)> m, double s, double tx, double ty,
                            double tol2, int[] seen, int stamp)
    {
        int count = 0;
        foreach (var x in m)
        {
            double ex = s * x.MX + tx - x.SX, ey = s * x.MY + ty - x.SY;
            if (ex * ex + ey * ey <= tol2 && seen[x.Screen] != stamp) { seen[x.Screen] = stamp; count++; }
        }
        return count;
    }
}
