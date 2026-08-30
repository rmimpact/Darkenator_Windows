using System.Runtime.InteropServices;

namespace Darkenator;

/// <summary>
/// Optional second stage of a theme switch.
///
/// The registry + broadcast route changes the mode everywhere, but Settings &gt;
/// Personalization &gt; Themes keeps showing whatever theme was last *selected*, marked as
/// modified. Handing the matching .theme file to the shell's own theme manager
/// (the undocumented IThemeManager2 COM object that the Settings app itself drives)
/// makes that page agree as well.
///
/// It is opt-in because applying a theme file also resets the accent colour to the one
/// baked into aero.theme / dark.theme. Wallpaper, cursors, sounds, desktop icons and the
/// screensaver are explicitly preserved via the ignore flags below.
/// </summary>
internal static class DeepThemeSync
{
    private static readonly Guid ClsidThemeManager2 = new("9324da94-50ec-4a14-a770-e90ca03e7c8f");
    private static readonly Guid IidThemeManager2 = new("c1e8c83e-845d-4d95-81db-e283fdffc000");

    private const uint CLSCTX_INPROC_SERVER = 0x1;

    [Flags]
    private enum ThemeApplyFlags
    {
        IgnoreBackground = 1 << 0,
        IgnoreCursor = 1 << 1,
        IgnoreDesktopIcons = 1 << 2,
        IgnoreColor = 1 << 3,
        IgnoreSound = 1 << 4,
        IgnoreScreensaver = 1 << 5,
        NoHourglass = 1 << 8
    }

    [Flags]
    private enum ThemePackFlags
    {
        Silent = 1 << 2
    }

    // Everything except the colour scheme is left alone: switching the mode should not
    // wipe out the user's wallpaper or cursor set.
    private const ThemeApplyFlags PreserveEverythingButColour =
        ThemeApplyFlags.IgnoreBackground |
        ThemeApplyFlags.IgnoreCursor |
        ThemeApplyFlags.IgnoreDesktopIcons |
        ThemeApplyFlags.IgnoreSound |
        ThemeApplyFlags.IgnoreScreensaver |
        ThemeApplyFlags.NoHourglass;

    /// <summary>
    /// Vtable layout of IThemeManager2. Every slot up to the one we call has to be declared
    /// in order, even the ones we never touch. All methods are HRESULT-returning, so they are
    /// declared PreserveSig and the results are checked by hand.
    /// </summary>
    [ComImport, Guid("c1e8c83e-845d-4d95-81db-e283fdffc000"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IThemeManager2
    {
        [PreserveSig] int Init(int initFlags);
        [PreserveSig] int InitAsync(IntPtr hwnd, int unk1);
        [PreserveSig] int Refresh();
        [PreserveSig] int RefreshAsync(IntPtr hwnd, int unk1);
        [PreserveSig] int RefreshComplete();
        [PreserveSig] int GetThemeCount(out int count);
        [PreserveSig] int GetTheme(int index, out IntPtr theme);
        [PreserveSig] int IsThemeDisabled(int index, out int disabled);
        [PreserveSig] int GetCurrentTheme(out int index);
        [PreserveSig] int SetCurrentTheme(IntPtr parent, int themeIndex, int applyNow, int applyFlags, int packFlags);
        [PreserveSig] int GetCustomTheme(out int index);
        [PreserveSig] int GetDefaultTheme(out int index);
        [PreserveSig] int CreateThemePack(IntPtr hwnd, [MarshalAs(UnmanagedType.LPWStr)] string unk1, int packFlags);
        [PreserveSig] int CloneAndSetCurrentTheme(IntPtr hwnd, [MarshalAs(UnmanagedType.LPWStr)] string unk1,
            [MarshalAs(UnmanagedType.LPWStr)] out string unk2);
        [PreserveSig] int InstallThemePack(IntPtr hwnd, [MarshalAs(UnmanagedType.LPWStr)] string unk1, int unk2,
            int packFlags, [MarshalAs(UnmanagedType.LPWStr)] out string unk3, out IntPtr unk4);
        [PreserveSig] int DeleteTheme([MarshalAs(UnmanagedType.LPWStr)] string unk1);
        [PreserveSig] int OpenTheme(IntPtr hwnd, [MarshalAs(UnmanagedType.LPWStr)] string path, int packFlags);
        [PreserveSig] int AddAndSelectTheme(IntPtr hwnd, [MarshalAs(UnmanagedType.LPWStr)] string path,
            int applyFlags, int packFlags);
    }

    [DllImport("ole32.dll")]
    private static extern int CoCreateInstance(
        ref Guid rclsid, IntPtr pUnkOuter, uint dwClsContext, ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out object ppv);

    public static string LightThemePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Resources", "Themes", "aero.theme");

    public static string DarkThemePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Resources", "Themes", "dark.theme");

    /// <summary>True when both stock theme files are present, i.e. deep sync can work at all.</summary>
    public static bool IsAvailable => File.Exists(LightThemePath) && File.Exists(DarkThemePath);

    /// <summary>
    /// Applies the stock light or dark theme file. Runs on its own STA thread because
    /// applying a theme blocks for up to a second or two and must not freeze the tray.
    /// </summary>
    public static void Apply(AppTheme theme)
    {
        string path = theme == AppTheme.Light ? LightThemePath : DarkThemePath;

        if (!File.Exists(path))
        {
            Log.Warn($"deep sync skipped, missing theme file: {path}");
            return;
        }

        var worker = new Thread(() => ApplyCore(path))
        {
            IsBackground = true,
            Name = "Darkenator deep sync"
        };
        worker.SetApartmentState(ApartmentState.STA);
        worker.Start();
    }

    private static void ApplyCore(string path)
    {
        object? comObject = null;
        try
        {
            Guid clsid = ClsidThemeManager2;
            Guid iid = IidThemeManager2;

            int hr = CoCreateInstance(ref clsid, IntPtr.Zero, CLSCTX_INPROC_SERVER, ref iid, out comObject);
            if (hr < 0 || comObject is not IThemeManager2 manager)
            {
                Log.Warn($"deep sync unavailable, CoCreateInstance returned 0x{hr:X8}");
                return;
            }

            hr = manager.Init(0);
            if (hr < 0)
            {
                Log.Warn($"deep sync: Init failed 0x{hr:X8}");
                return;
            }

            hr = manager.AddAndSelectTheme(IntPtr.Zero, path,
                (int)PreserveEverythingButColour, (int)ThemePackFlags.Silent);

            if (hr < 0)
            {
                Log.Warn($"deep sync: AddAndSelectTheme failed 0x{hr:X8} for {path}");
                return;
            }

            Log.Info($"deep sync applied {Path.GetFileName(path)}");
        }
        catch (Exception ex)
        {
            Log.Warn($"deep sync threw: {ex.Message}");
        }
        finally
        {
            if (comObject is not null && Marshal.IsComObject(comObject))
            {
                Marshal.ReleaseComObject(comObject);
            }
        }
    }
}
