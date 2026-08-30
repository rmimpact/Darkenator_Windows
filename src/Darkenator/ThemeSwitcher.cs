using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Darkenator;

/// <summary>
/// Applies the Windows light/dark mode and — this is the part that PowerShell one-liners
/// always miss — tells the running system that it changed.
///
/// Writing the two Personalize registry values is only half the job. Nothing repaints until
/// the immersive colour policy cache is invalidated and the change is broadcast to every
/// top-level window. The full sequence below is the same one the Windows shell itself runs,
/// and is what makes Settings, Explorer, the taskbar and third-party apps agree.
/// </summary>
public static class ThemeSwitcher
{
    private const string PersonalizeSubKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string AppsValueName = "AppsUseLightTheme";
    private const string SystemValueName = "SystemUsesLightTheme";

    #region Win32

    private static readonly IntPtr HwndBroadcast = new(0xFFFF);
    private const uint WM_SETTINGCHANGE = 0x001A;
    private const uint WM_THEMECHANGED = 0x031A;
    private const uint WM_SYSCOLORCHANGE = 0x0015;
    private const uint SMTO_ABORTIFHUNG = 0x0002;
    private const uint BroadcastTimeoutMs = 3000;

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "SendMessageTimeoutW")]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd, uint msg, UIntPtr wParam, string? lParam,
        uint flags, uint timeout, out UIntPtr result);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadLibraryW(string lpLibFileName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, IntPtr lpProcName);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void UxThemeVoidProc();

    // uxtheme.dll exports these by ordinal only. 104 drops the cached immersive colour
    // policy (without it, apps keep reading the *old* mode), 136 rebuilds menu themes.
    private const int OrdinalRefreshImmersiveColorPolicyState = 104;
    private const int OrdinalFlushMenuThemes = 136;

    private static readonly Lazy<UxThemeVoidProc?> RefreshImmersiveColorPolicyState =
        new(() => GetUxThemeProc(OrdinalRefreshImmersiveColorPolicyState));

    private static readonly Lazy<UxThemeVoidProc?> FlushMenuThemes =
        new(() => GetUxThemeProc(OrdinalFlushMenuThemes));

    private static UxThemeVoidProc? GetUxThemeProc(int ordinal)
    {
        try
        {
            IntPtr module = LoadLibraryW("uxtheme.dll");
            if (module == IntPtr.Zero) return null;

            IntPtr proc = GetProcAddress(module, new IntPtr(ordinal));
            if (proc == IntPtr.Zero) return null;

            return Marshal.GetDelegateForFunctionPointer<UxThemeVoidProc>(proc);
        }
        catch (Exception ex)
        {
            Log.Warn($"could not resolve uxtheme ordinal {ordinal}: {ex.Message}");
            return null;
        }
    }

    #endregion

    /// <summary>The mode the system is in right now, read straight from the registry.</summary>
    public static AppTheme Current => CurrentAppsTheme;

    public static AppTheme CurrentAppsTheme => ReadValue(AppsValueName);

    public static AppTheme CurrentSystemTheme => ReadValue(SystemValueName);

    private static AppTheme ReadValue(string name)
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(PersonalizeSubKey);
            // The value is absent on a fresh profile, where Windows defaults to light.
            object? raw = key?.GetValue(name);
            if (raw is int i) return i == 0 ? AppTheme.Dark : AppTheme.Light;
        }
        catch (Exception ex)
        {
            Log.Warn($"could not read {name}: {ex.Message}");
        }
        return AppTheme.Light;
    }

    /// <summary>
    /// Applies <paramref name="theme"/> to both the app and system (taskbar/Start) surfaces.
    /// </summary>
    /// <param name="deepSync">
    /// Also hand the matching Windows theme file to the shell's own theme manager, so the
    /// Settings &gt; Personalization &gt; Themes page shows the right theme selected too.
    /// </param>
    public static bool Apply(AppTheme theme, bool deepSync = false)
    {
        int value = theme == AppTheme.Light ? 1 : 0;

        try
        {
            // CreateSubKey, not OpenSubKey: the Personalize key genuinely does not exist on
            // some freshly provisioned profiles, and OpenSubKey would hand back null.
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(PersonalizeSubKey, writable: true)
                                   ?? throw new InvalidOperationException("could not open the Personalize key");

            // Must be REG_DWORD. A REG_SZ "0" is silently ignored by the shell — this is the
            // single most common reason a hand-rolled script appears to do nothing.
            key.SetValue(AppsValueName, value, RegistryValueKind.DWord);
            key.SetValue(SystemValueName, value, RegistryValueKind.DWord);
            key.Flush();
        }
        catch (Exception ex)
        {
            Log.Error($"failed writing theme registry values: {ex}");
            return false;
        }

        NotifySystem();

        if (deepSync)
        {
            DeepThemeSync.Apply(theme);
        }

        Log.Info($"applied {theme} theme (deepSync={deepSync})");
        return true;
    }

    /// <summary>
    /// Re-runs only the notification half of the switch. Useful when an app was launched
    /// before the last change and is still showing stale colours.
    /// </summary>
    public static void NotifySystem()
    {
        // 1. Invalidate the shell's cached colour policy so subsequent reads see the new mode.
        TryInvoke(RefreshImmersiveColorPolicyState.Value, "RefreshImmersiveColorPolicyState");

        // 2. Rebuild menu themes, otherwise Win32 context menus stay in the old colours.
        TryInvoke(FlushMenuThemes.Value, "FlushMenuThemes");

        // 3. The message every modern app actually listens for.
        Broadcast(WM_SETTINGCHANGE, "ImmersiveColorSet");

        // 4. Classic control repaint path, for older Win32 apps.
        Broadcast(WM_THEMECHANGED, null);
        Broadcast(WM_SYSCOLORCHANGE, null);
    }

    private static void TryInvoke(UxThemeVoidProc? proc, string name)
    {
        if (proc is null) return;
        try
        {
            proc();
        }
        catch (Exception ex)
        {
            Log.Warn($"{name} threw: {ex.Message}");
        }
    }

    private static void Broadcast(uint message, string? lParam)
    {
        try
        {
            SendMessageTimeout(HwndBroadcast, message, UIntPtr.Zero, lParam,
                SMTO_ABORTIFHUNG, BroadcastTimeoutMs, out _);
        }
        catch (Exception ex)
        {
            Log.Warn($"broadcast of 0x{message:X} failed: {ex.Message}");
        }
    }
}
