using System.Windows;
using System.Windows.Media;
using ChestTracker.Core;

namespace ChestTracker;

/// <summary>
/// Your colours. The app's colours are shared brushes, so changing a brush's colour repaints everything that uses it
/// straight away. Everything is worked out from four picks: accent, background, cards and text.
/// </summary>
public static class ThemeManager
{
    public static readonly (string Name, string Accent)[] AccentPresets =
    {
        ("Gold", "#FFCD50"), ("Ocean", "#4C9CFF"), ("Emerald", "#3DDC84"), ("Violet", "#B57BFF"),
        ("Rose", "#FF6B9A"), ("Fire", "#FF7A3C"), ("Ice", "#7FE7FF"), ("Silver", "#C9D1DE"),
    };

    public static readonly (string Name, string Background, string Card, string Text)[] BackgroundPresets =
    {
        ("Midnight", "#0B0E14", "#131822", "#EEF1F6"),
        ("Pure black", "#000000", "#0E0E10", "#F2F2F2"),
        ("Navy", "#0A1020", "#121B33", "#E8EEFF"),
        ("Forest", "#08120D", "#102018", "#E6F5EC"),
        ("Plum", "#120A16", "#1E1424", "#F3E9F7"),
        ("Slate", "#16191F", "#20242C", "#ECEFF4"),
    };

    public static Color Parse(string? hex, string fallback)
    {
        try { return (Color)ColorConverter.ConvertFromString(string.IsNullOrWhiteSpace(hex) ? fallback : hex.Trim()); }
        catch (FormatException) { return (Color)ColorConverter.ConvertFromString(fallback); }
    }

    public static bool IsValid(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return false;
        try { ColorConverter.ConvertFromString(hex.Trim()); return true; }
        catch (FormatException) { return false; }
    }

    public static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    public static Color Mix(Color a, Color b, double t) => Color.FromRgb(
        (byte)Math.Round(a.R + (b.R - a.R) * t), (byte)Math.Round(a.G + (b.G - a.G) * t), (byte)Math.Round(a.B + (b.B - a.B) * t));

    static Color Lighten(Color c, double t) => Mix(c, Colors.White, t);
    static Color Darken(Color c, double t) => Mix(c, Colors.Black, t);
    static double Luminance(Color c) => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255;

    /// <summary>A lighter or darker step away from a colour, whichever way has room (works for dark and light themes).</summary>
    static Color Step(Color c, double t) => Luminance(c) < 0.5 ? Lighten(c, t) : Darken(c, t);

    public static void Apply(AppSettings s)
    {
        var accent = Parse(s.AccentColor, Palette.DefaultAccent);
        var bg = Parse(s.BackgroundColor, Palette.DefaultBackground);
        var card = Parse(s.CardColor, Palette.DefaultCard);
        var text = Parse(s.TextColor, Palette.DefaultText);
        var res = Application.Current.Resources;

        SetBrush(res, "Gold", accent);
        SetBrush(res, "GoldDeep", Darken(accent, 0.14));
        SetBrush(res, "DarkInk", Luminance(accent) > 0.55 ? Color.FromRgb(0x1B, 0x13, 0x00) : Colors.White);
        SetGradient(res, "GoldGradient", Lighten(accent, 0.25), Darken(accent, 0.06));
        res["GoldColor"] = accent;
        res["ChestIcon"] = ChestIcon(Lighten(accent, 0.25), Darken(accent, 0.06), Darken(accent, 0.14));
        res["WhiteColor"] = text;

        SetBrush(res, "Bg", bg);
        SetBrush(res, "Sidebar", Step(bg, 0.02));
        SetBrush(res, "Surface", card);
        SetBrush(res, "Surface2", Step(card, 0.05));
        SetBrush(res, "Surface3", Step(card, 0.10));
        SetBrush(res, "Line", Step(card, 0.07));
        SetGradient(res, "HeroGradient", Step(card, 0.06), card, Mix(card, accent, 0.07));

        SetBrush(res, "Text", text);
        SetBrush(res, "TextDim", Mix(text, card, 0.40));
        SetBrush(res, "TextFaint", Mix(text, card, 0.62));
    }

    /// <summary>The little chest picture, in the accent colour (same shape as in Theme.xaml).</summary>
    static DrawingImage ChestIcon(Color lidTop, Color lidBottom, Color body)
    {
        var lid = new LinearGradientBrush(lidTop, lidBottom, new Point(0, 0), new Point(1, 1));
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(lid, null, Geometry.Parse("M3,10.2 L3,7.2 C3,5 4.8,3.2 7,3.2 L17,3.2 C19.2,3.2 21,5 21,7.2 L21,10.2 Z")));
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(body), null, Geometry.Parse("M3,11 L21,11 L21,19 C21,20.1 20.1,21 19,21 L5,21 C3.9,21 3,20.1 3,19 Z")));
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(0x1B, 0x13, 0x00)), null, Geometry.Parse("M10.3,8.4 L13.7,8.4 L13.7,14 L10.3,14 Z")));
        var image = new DrawingImage(group);
        image.Freeze();
        return image;
    }

    // Always a new brush: the app uses these colours as DynamicResource, so replacing them repaints everything.
    static void SetBrush(ResourceDictionary res, string key, Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        res[key] = b;
    }

    static void SetGradient(ResourceDictionary res, string key, params Color[] stops)
    {
        var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        double[] offsets = stops.Length == 3 ? new[] { 0, 0.55, 1.0 } : Enumerable.Range(0, stops.Length).Select(i => stops.Length == 1 ? 0 : i / (double)(stops.Length - 1)).ToArray();
        for (int i = 0; i < stops.Length; i++) g.GradientStops.Add(new GradientStop(stops[i], offsets[i]));
        g.Freeze();
        res[key] = g;
    }
}
