using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace PowerPlanSwitcher.Services;

/// <summary>
/// Draws the tray icons at startup. Three small glyphs on a rounded square:
/// a moon for idle, a balanced bar for active, and a bolt for forced.
/// No icon files to ship, so nothing to keep in sync.
/// </summary>
internal static class TrayIcons
{
    private static readonly Color IdleFill = Color.FromArgb(0xB2, 0x6A, 0x00);
    private static readonly Color ActiveFill = Color.FromArgb(0x00, 0x6B, 0x2B);
    private static readonly Color BoostFill = Color.FromArgb(0xB0, 0x22, 0x22);
    private static readonly Color PausedFill = Color.FromArgb(0x5A, 0x5D, 0x66);

    private static readonly Lazy<Icon> IdleLazy = new(() => Build(IdleFill, GlyphKind.Moon));
    private static readonly Lazy<Icon> ActiveLazy = new(() => Build(ActiveFill, GlyphKind.Bars));
    private static readonly Lazy<Icon> BoostLazy = new(() => Build(BoostFill, GlyphKind.Bolt));
    private static readonly Lazy<Icon> PausedLazy = new(() => Build(PausedFill, GlyphKind.Pause));

    public static Icon Idle => IdleLazy.Value;

    public static Icon Active => ActiveLazy.Value;

    public static Icon Boost => BoostLazy.Value;

    public static Icon Paused => PausedLazy.Value;

    private enum GlyphKind
    {
        Moon,
        Bars,
        Bolt,
        Pause,
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool DestroyIcon(IntPtr handle);

    private static Icon Build(Color fill, GlyphKind kind)
    {
        const int size = 32;

        using Bitmap bitmap = new(size, size);
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);

            using (Brush brush = new SolidBrush(fill))
            using (GraphicsPath round = Rounded(1, 1, size - 3, size - 3, 7))
            {
                graphics.FillPath(brush, round);
            }

            switch (kind)
            {
                case GlyphKind.Moon:
                    DrawMoon(graphics);
                    break;
                case GlyphKind.Bars:
                    DrawBars(graphics);
                    break;
                case GlyphKind.Bolt:
                    DrawBolt(graphics);
                    break;
                case GlyphKind.Pause:
                    DrawPause(graphics);
                    break;
            }
        }

        IntPtr handle = bitmap.GetHicon();
        try
        {
            using Icon tmp = Icon.FromHandle(handle);
            return (Icon)tmp.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    private static GraphicsPath Rounded(int x, int y, int width, int height, int radius)
    {
        int diameter = radius * 2;
        var path = new GraphicsPath();

        path.AddArc(x, y, diameter, diameter, 180, 90);
        path.AddArc(x + width - diameter, y, diameter, diameter, 270, 90);
        path.AddArc(x + width - diameter, y + height - diameter, diameter, diameter, 0, 90);
        path.AddArc(x, y + height - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();

        return path;
    }

    private static void DrawMoon(Graphics graphics)
    {
        using var brush = new SolidBrush(Color.White);
        using var path = new GraphicsPath();

        // A crescent: a circle minus an offset circle, which GDI+ cannot punch out
        // directly, so the small circle is drawn in the accent colour instead.
        path.AddEllipse(9, 8, 15, 15);
        graphics.FillPath(brush, path);

        using var cut = new SolidBrush(IdleFill);
        using var cutPath = new GraphicsPath();
        cutPath.AddEllipse(15, 4, 13, 13);
        graphics.FillPath(cut, cutPath);
    }

    private static void DrawBars(Graphics graphics)
    {
        using var brush = new SolidBrush(Color.White);

        // Left bar short, middle tall, right short: a balanced scale.
        graphics.FillRectangle(brush, 8, 18, 4, 7);
        graphics.FillRectangle(brush, 14, 13, 4, 12);
        graphics.FillRectangle(brush, 20, 18, 4, 7);
    }

    private static void DrawBolt(Graphics graphics)
    {
        using var brush = new SolidBrush(Color.White);
        using var path = new GraphicsPath();

        var points = new[]
        {
            new PointF(17, 6),
            new PointF(9, 18),
            new PointF(14, 18),
            new PointF(12, 26),
            new PointF(22, 14),
            new PointF(16, 14),
        };

        path.AddPolygon(points);
        graphics.FillPath(brush, path);
    }

    private static void DrawPause(Graphics graphics)
    {
        using var brush = new SolidBrush(Color.White);

        graphics.FillRectangle(brush, 11, 9, 4, 14);
        graphics.FillRectangle(brush, 18, 9, 4, 14);
    }
}
