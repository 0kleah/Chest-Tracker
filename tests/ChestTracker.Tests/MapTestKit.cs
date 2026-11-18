using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ChestTracker.Core;

/// <summary>Makes fake "Fortnite map open" screens for the map overlay tests.</summary>
static class MapTestKit
{
    public static GrayImage LoadGray(string path)
    {
        using var fs = File.OpenRead(path);
        var frame = BitmapDecoder.Create(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
        var gray = new FormatConvertedBitmap(frame, PixelFormats.Gray8, null, 0);
        var px = new byte[gray.PixelWidth * gray.PixelHeight];
        gray.CopyPixels(px, gray.PixelWidth, 0);
        return new GrayImage(gray.PixelWidth, gray.PixelHeight, px);
    }

    /// <summary>
    /// The map drawn at <paramref name="scale"/> screen pixels per map pixel, moved by the offset, a bit darker and
    /// with less contrast (the game's colours differ), on a dark background, with text-like labels, round markers,
    /// thin grid lines and noise on top.
    /// </summary>
    public static GrayImage FakeScreen(GrayImage map, int w, int h, double scale, double ox, double oy, int seed)
    {
        var rng = new Random(seed);
        var big = map.Resize((int)Math.Round(map.Width * scale), (int)Math.Round(map.Height * scale));
        var px = new byte[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int mx = (int)Math.Round(x - ox), my = (int)Math.Round(y - oy);
                int v = mx >= 0 && my >= 0 && mx < big.Width && my < big.Height ? big.At(mx, my) : 18;
                v = (int)(v * 0.82 + 12);
                px[y * w + x] = (byte)Math.Clamp(v, 0, 255);
            }
        // grid lines every 10% of the map
        for (int g = 1; g < 10; g++)
        {
            int gx = (int)(ox + big.Width * g / 10.0), gy = (int)(oy + big.Height * g / 10.0);
            for (int i = 0; i < h; i++) if (gx >= 0 && gx < w) px[i * w + gx] = (byte)Math.Min(255, px[i * w + gx] + 25);
            for (int i = 0; i < w; i++) if (gy >= 0 && gy < h) px[gy * w + i] = (byte)Math.Min(255, px[gy * w + i] + 25);
        }
        // labels (white blocks with dark gaps, like text) and markers (circles)
        for (int n = 0; n < (w >= 240 && h >= 60 ? 14 : 0); n++)
        {
            int lx = rng.Next(w - 160), ly = rng.Next(h - 20), len = rng.Next(60, 150);
            for (int y = ly; y < ly + 14; y++)
                for (int x = lx; x < lx + len; x++)
                    if ((x / 7) % 3 != 2) px[y * w + x] = 235;
        }
        for (int n = 0; n < (w >= 80 && h >= 80 ? 8 : 0); n++)
        {
            int cx = rng.Next(20, w - 20), cy = rng.Next(20, h - 20), r = rng.Next(6, 14);
            for (int y = -r; y <= r; y++)
                for (int x = -r; x <= r; x++)
                    if (x * x + y * y <= r * r) px[(cy + y) * w + cx + x] = 250;
        }
        for (int i = 0; i < px.Length; i++) px[i] = (byte)Math.Clamp(px[i] + rng.Next(-6, 7), 0, 255);
        return new GrayImage(w, h, px);
    }

    public static GrayImage Noise(int w, int h, int seed)
    {
        var rng = new Random(seed);
        var px = new byte[w * h];
        // blobs of colour, like a game scene (not the map)
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                px[y * w + x] = (byte)(128 + 60 * Math.Sin(x * 0.013 + y * 0.007) + 40 * Math.Cos(y * 0.021 - x * 0.004) + rng.Next(-25, 26));
        return new GrayImage(w, h, px);
    }
}
