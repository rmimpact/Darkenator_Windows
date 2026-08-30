using System.Runtime.InteropServices;

namespace Darkenator;

/// <summary>
/// Palette and window-frame tweaks so Darkenator's own UI follows the mode it is setting.
/// A theme switcher that stays stubbornly light while putting the system into dark mode
/// looks broken, so this is worth the small amount of manual painting WinForms needs.
/// </summary>
public static class UiTheme
{
    // 20 on 20H1 and later; 19 on the 1809-1909 builds that first shipped the attribute.
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_LEGACY = 19;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public sealed record Palette(
        Color Background,
        Color Surface,
        Color Border,
        Color Text,
        Color TextMuted,
        Color Accent,
        Color AccentText,
        Color MenuHighlight);

    public static readonly Palette Light = new(
        Background: Color.FromArgb(0xF3, 0xF3, 0xF6),
        Surface: Color.FromArgb(0xFF, 0xFF, 0xFF),
        Border: Color.FromArgb(0xDA, 0xDA, 0xE0),
        Text: Color.FromArgb(0x1A, 0x1A, 0x1F),
        TextMuted: Color.FromArgb(0x5C, 0x5C, 0x68),
        Accent: Color.FromArgb(0x3B, 0x6B, 0xF0),
        AccentText: Color.White,
        MenuHighlight: Color.FromArgb(0xE6, 0xEC, 0xFD));

    public static readonly Palette Dark = new(
        Background: Color.FromArgb(0x1E, 0x1E, 0x24),
        Surface: Color.FromArgb(0x27, 0x27, 0x2F),
        Border: Color.FromArgb(0x3A, 0x3A, 0x45),
        Text: Color.FromArgb(0xF0, 0xF0, 0xF5),
        TextMuted: Color.FromArgb(0xA0, 0xA0, 0xAE),
        Accent: Color.FromArgb(0x6E, 0x93, 0xFF),
        AccentText: Color.FromArgb(0x11, 0x11, 0x16),
        MenuHighlight: Color.FromArgb(0x38, 0x3C, 0x4C));

    public static Palette For(AppTheme theme) => theme == AppTheme.Dark ? Dark : Light;

    public static Palette Current => For(ThemeSwitcher.Current);

    /// <summary>Paints the non-client title bar to match, so the window frame is not two-tone.</summary>
    public static void ApplyTitleBar(IWin32Window window, bool dark)
    {
        int value = dark ? 1 : 0;
        try
        {
            if (DwmSetWindowAttribute(window.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int)) != 0)
            {
                DwmSetWindowAttribute(window.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE_LEGACY, ref value, sizeof(int));
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"could not set the title bar theme: {ex.Message}");
        }
    }
}

/// <summary>Context-menu renderer that follows the current mode instead of the fixed Office palette.</summary>
public sealed class ThemedMenuRenderer : ToolStripProfessionalRenderer
{
    private readonly UiTheme.Palette _palette;

    public ThemedMenuRenderer(UiTheme.Palette palette) : base(new ThemedColorTable(palette))
    {
        _palette = palette;
        RoundedEdges = false;
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item is { Enabled: false } ? _palette.TextMuted : _palette.Text;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = _palette.Text;
        base.OnRenderArrow(e);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        // The stock check glyph is a dark tick on a light plate; draw our own so it reads
        // correctly on a dark menu.
        Rectangle r = e.ImageRectangle;
        using var pen = new Pen(_palette.Accent, Math.Max(1.6f, r.Height * 0.14f))
        {
            StartCap = System.Drawing.Drawing2D.LineCap.Round,
            EndCap = System.Drawing.Drawing2D.LineCap.Round
        };
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        float left = r.Left + r.Width * 0.22f;
        float mid = r.Left + r.Width * 0.42f;
        float right = r.Left + r.Width * 0.78f;
        float top = r.Top + r.Height * 0.30f;
        float bottom = r.Top + r.Height * 0.68f;
        float centre = r.Top + r.Height * 0.52f;

        e.Graphics.DrawLines(pen, new[]
        {
            new PointF(left, centre),
            new PointF(mid, bottom),
            new PointF(right, top)
        });
    }
}

internal sealed class ThemedColorTable : ProfessionalColorTable
{
    private readonly UiTheme.Palette _p;

    public ThemedColorTable(UiTheme.Palette palette)
    {
        _p = palette;
        UseSystemColors = false;
    }

    public override Color ToolStripDropDownBackground => _p.Surface;
    public override Color ImageMarginGradientBegin => _p.Surface;
    public override Color ImageMarginGradientMiddle => _p.Surface;
    public override Color ImageMarginGradientEnd => _p.Surface;
    public override Color MenuBorder => _p.Border;
    public override Color MenuItemBorder => _p.MenuHighlight;
    public override Color MenuItemSelected => _p.MenuHighlight;
    public override Color MenuItemSelectedGradientBegin => _p.MenuHighlight;
    public override Color MenuItemSelectedGradientEnd => _p.MenuHighlight;
    public override Color MenuItemPressedGradientBegin => _p.MenuHighlight;
    public override Color MenuItemPressedGradientMiddle => _p.MenuHighlight;
    public override Color MenuItemPressedGradientEnd => _p.MenuHighlight;
    public override Color SeparatorDark => _p.Border;
    public override Color SeparatorLight => _p.Border;
    public override Color CheckBackground => _p.MenuHighlight;
    public override Color CheckSelectedBackground => _p.MenuHighlight;
    public override Color CheckPressedBackground => _p.MenuHighlight;
}
