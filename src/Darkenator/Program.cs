namespace Darkenator;

internal static class Program
{
    private const string InstanceMutexName = @"Local\Darkenator.SingleInstance";
    private const string ShowUiEventName = @"Local\Darkenator.ShowSettings";

    [STAThread]
    private static void Main(string[] args)
    {
        bool startMinimizedRequested = args.Any(a =>
            a.Equals("--minimized", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("/minimized", StringComparison.OrdinalIgnoreCase));

        using var instanceMutex = new Mutex(initiallyOwned: true, InstanceMutexName, out bool isFirstInstance);

        if (!isFirstInstance)
        {
            // A copy is already running. Poke it so the settings window comes forward, which
            // is what double-clicking the exe a second time should obviously do.
            try
            {
                using var show = EventWaitHandle.OpenExisting(ShowUiEventName);
                show.Set();
            }
            catch (Exception ex)
            {
                Log.Warn($"could not signal the running instance: {ex.Message}");
            }
            return;
        }

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Log.Error($"unhandled exception: {e.ExceptionObject}");

        bool firstRun = !File.Exists(AppPaths.SettingsFile);
        AppSettings settings = AppSettings.Load();

        // Show the window on a first run so the app is not just a mystery icon, and whenever
        // it was launched by hand rather than by the startup entry.
        bool openSettings = firstRun || (!startMinimizedRequested && !settings.StartMinimized);

        using var trayApp = new TrayApp(settings, openSettings);

        // A background utility should never interrupt with a modal error box: log it, and put
        // a balloon in the tray so the problem is discoverable without being in the way.
        Application.ThreadException += (_, e) =>
        {
            Log.Error($"UI thread exception: {e.Exception}");
            trayApp.ShowError($"Something went wrong: {e.Exception.Message}. See the log for details.");
        };

        using var showRequested = new EventWaitHandle(false, EventResetMode.AutoReset, ShowUiEventName);
        StartShowUiListener(showRequested, trayApp);

        Application.Run(trayApp);

        GC.KeepAlive(instanceMutex);
    }

    /// <summary>
    /// Waits in the background for another launch of the exe and brings the settings window up.
    /// </summary>
    private static void StartShowUiListener(EventWaitHandle handle, TrayApp trayApp)
    {
        var thread = new Thread(() =>
        {
            while (handle.WaitOne())
            {
                try
                {
                    // Marshal onto the UI thread; NotifyIcon and Form both demand it.
                    trayApp.PostToUi(trayApp.ShowSettings);
                }
                catch (Exception ex)
                {
                    Log.Warn($"could not show settings on request: {ex.Message}");
                    return;
                }
            }
        })
        {
            IsBackground = true,
            Name = "Darkenator show-UI listener"
        };
        thread.Start();
    }
}
