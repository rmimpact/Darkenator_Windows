using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace Darkenator;

/// <summary>
/// Draws the notification-area icon at runtime instead of shipping bitmaps, so it comes out
/// crisp at whatever size and DPI the shell asks for, and can flip between a light-on-dark
/// and dark-on-light stroke to stay visible against either taskbar.
/// </summary>
public static class TrayIcons
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    /// <summary>
    /// A sun (light active) or crescent moon (dark active), tinted to contrast with the
    /// current taskbar. Caller owns the returned icon and must dispose it via <see cref="Dispose"/>.
    /// </summary>
    public static Icon Create(AppTheme active, bool taskbarIsLight, int? sideOverride = null)
    {
        Size size = SystemInformation.SmallIconSize;
        int side = sideOverride ?? Math.Max(16, Math.Max(size.Width, size.Height));

        // Draw at 4x and resample down: much cleaner edges than drawing straight at 16px,
        // where a one-pixel stroke either disappears or turns into a smear.
        const int Supersample = 4;
        int big = side * Supersample;

        using var oversized = new Bitmap(big, big, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(oversized))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            Color ink = taskbarIsLight
                ? Color.FromArgb(255, 32, 32, 36)
                : Color.FromArgb(255, 246, 246, 250);

            if (active == AppTheme.Light)
            {
                DrawSun(g, big, ink);
            }
            else
            {
                DrawMoon(g, big, ink);
            }
        }

        using var bitmap = new Bitmap(side, side, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.Clear(Color.Transparent);
            g.DrawImage(oversized, new Rectangle(0, 0, side, side));
        }

        IntPtr hIcon = bitmap.GetHicon();
        try
        {
            // Icon.FromHandle does not own the handle, so clone and release it immediately;
            // otherwise every redraw leaks a GDI object.
            using var temp = Icon.FromHandle(hIcon);
            return (Icon)temp.Clone();
        }
        finally
        {
            DestroyIcon(hIcon);
        }
    }

    private static void DrawSun(Graphics g, int side, Color ink)
    {
        using var brush = new SolidBrush(ink);
        using var pen = new Pen(ink, side * 0.085f) { StartCap = LineCap.Round, EndCap = LineCap.Round };

        float centre = side / 2f;
        float coreRadius = side * 0.22f;

        g.FillEllipse(brush, centre - coreRadius, centre - coreRadius, coreRadius * 2, coreRadius * 2);

        float rayInner = side * 0.32f;
        float rayOuter = side * 0.45f;
        for (int i = 0; i < 8; i++)
        {
            double angle = i * Math.PI / 4.0;
            float cos = (float)Math.Cos(angle);
            float sin = (float)Math.Sin(angle);
            g.DrawLine(pen,
                centre + cos * rayInner, centre + sin * rayInner,
                centre + cos * rayOuter, centre + sin * rayOuter);
        }
    }

    private static void DrawMoon(Graphics g, int side, Color ink)
    {
        float centre = side / 2f;
        float radius = side * 0.36f;

        // Crescent = full disc minus a disc offset up and to the right.
        using var path = new GraphicsPath();
        path.AddEllipse(centre - radius, centre - radius, radius * 2, radius * 2);

        using var cutout = new GraphicsPath();
        float cutRadius = radius * 0.92f;
        float cutOffsetX = radius * 0.52f;
        float cutOffsetY = -radius * 0.20f;
        cutout.AddEllipse(centre - cutRadius + cutOffsetX, centre - cutRadius + cutOffsetY,
            cutRadius * 2, cutRadius * 2);

        using var region = new Region(path);
        region.Exclude(cutout);

        using var brush = new SolidBrush(ink);
        g.FillRegion(brush, region);
    }

    public static void Dispose(ref Icon? icon)
    {
        icon?.Dispose();
        icon = null;
    }
}
