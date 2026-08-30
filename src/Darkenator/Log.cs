using System.Text;

namespace Darkenator;

/// <summary>
/// Small append-only log next to the settings file. Exists so that "it didn't switch"
/// can be answered with a timestamp instead of a guess.
/// </summary>
public static class Log
{
    private const long MaxBytes = 256 * 1024;
    private static readonly object Gate = new();

    public static string FilePath => Path.Combine(AppPaths.DataDirectory, "darkenator.log");

    public static void Info(string message) => Write("INFO ", message);
    public static void Warn(string message) => Write("WARN ", message);
    public static void Error(string message) => Write("ERROR", message);

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(AppPaths.DataDirectory);
                string path = FilePath;

                // Keep one generation of history, then start over. Nobody needs more.
                var info = new FileInfo(path);
                if (info.Exists && info.Length > MaxBytes)
                {
                    string old = path + ".1";
                    File.Delete(old);
                    File.Move(path, old);
                }

                File.AppendAllText(path,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {level} {message}{Environment.NewLine}",
                    Encoding.UTF8);
            }
        }
        catch
        {
            // Logging must never be the reason the app falls over.
        }
    }
}

public static class AppPaths
{
    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Darkenator");

    public static string SettingsFile => Path.Combine(DataDirectory, "settings.json");

    /// <summary>Full path to the running .exe (not the managed .dll).</summary>
    public static string ExecutablePath
    {
        get
        {
            string? p = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(p) && p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                return p;
            }

            // Assembly.Location is empty in a single-file build, so fall back to the app
            // directory rather than to a path that would come out as just ".exe".
            return Path.Combine(AppContext.BaseDirectory, "Darkenator.exe");
        }
    }
}
