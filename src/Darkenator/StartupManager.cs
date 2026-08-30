using Microsoft.Win32;

namespace Darkenator;

/// <summary>
/// Run-at-login via the per-user Run key. No scheduled task, no admin rights, and it shows
/// up in Task Manager under Startup apps where people expect to find it.
/// </summary>
public static class StartupManager
{
    private const string RunSubKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Darkenator";

    private static string CommandLine => $"\"{AppPaths.ExecutablePath}\" --minimized";

    /// <summary>True only when the entry exists and points at this copy of the exe.</summary>
    public static bool IsEnabled()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunSubKey);
            return key?.GetValue(ValueName) as string == CommandLine;
        }
        catch (Exception ex)
        {
            Log.Warn($"could not read startup entry: {ex.Message}");
            return false;
        }
    }

    /// <summary>True if an entry exists at all, even a stale one from a previous location.</summary>
    public static bool HasAnyEntry()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunSubKey);
            return key?.GetValue(ValueName) is string;
        }
        catch
        {
            return false;
        }
    }

    public static bool Set(bool enabled)
    {
        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunSubKey, writable: true)
                                   ?? throw new InvalidOperationException("could not open the Run key");

            if (enabled)
            {
                key.SetValue(ValueName, CommandLine, RegistryValueKind.String);
            }
            else if (key.GetValue(ValueName) is not null)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            Log.Info($"startup entry {(enabled ? "enabled" : "removed")}");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"could not change startup entry: {ex}");
            return false;
        }
    }

    /// <summary>
    /// If the app has been moved since the entry was written, repoint it, so that
    /// "run at startup" keeps working after the exe is relocated.
    /// </summary>
    public static void RepairIfStale()
    {
        if (HasAnyEntry() && !IsEnabled())
        {
            Log.Info("startup entry pointed elsewhere, repointing at the current exe");
            Set(true);
        }
    }
}
