using Microsoft.Win32;

namespace Darkenator;

/// <summary>
/// The app itself: a notification-area icon, its menu, and the timer that keeps the
/// system theme in step with the schedule.
/// </summary>
public sealed class TrayApp : ApplicationContext
{
    // Half a minute is frequent enough that a sunset boundary is never noticeably late,
    // and cheap enough to be invisible: the tick is a handful of arithmetic operations.
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);

    private readonly NotifyIcon _notifyIcon;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly ToolStripMenuItem _headerItem;
    private readonly ToolStripMenuItem _lightItem;
    private readonly ToolStripMenuItem _darkItem;
    private readonly ToolStripMenuItem _autoItem;

    /// <summary>Hidden window used purely to marshal calls from background threads onto the UI thread.</summary>
    private readonly Control _marshaller;

    private AppSettings _settings;
    private SettingsForm? _settingsForm;
    private Icon? _currentIcon;

    /// <summary>The target from the previous evaluation, so switches are edge-triggered.</summary>
    private AppTheme? _lastTarget;

    private bool _disposed;

    public TrayApp(AppSettings settings, bool openSettingsOnStart)
    {
        _settings = settings;

        _marshaller = new Control();
        _marshaller.CreateControl();

        _headerItem = new ToolStripMenuItem { Enabled = false };

        _lightItem = new ToolStripMenuItem("Light", null, (_, _) => SetMode(ThemeMode.Light));
        _darkItem = new ToolStripMenuItem("Dark", null, (_, _) => SetMode(ThemeMode.Dark));
        _autoItem = new ToolStripMenuItem("Automatic", null, (_, _) => SetMode(ThemeMode.Automatic));

        var menu = new ContextMenuStrip
        {
            ShowImageMargin = true,
            Renderer = new ThemedMenuRenderer(UiTheme.Current)
        };
        menu.Items.AddRange(new ToolStripItem[]
        {
            _headerItem,
            new ToolStripSeparator(),
            _lightItem,
            _darkItem,
            _autoItem,
            new ToolStripSeparator(),
            new ToolStripMenuItem("Settings...", null, (_, _) => ShowSettings()),
            new ToolStripMenuItem("Exit", null, (_, _) => ExitApp())
        });
        menu.Opening += (_, _) => RefreshMenu();

        _notifyIcon = new NotifyIcon
        {
            Text = "Darkenator",
            ContextMenuStrip = menu,
            Visible = true
        };
        _notifyIcon.DoubleClick += (_, _) => ShowSettings();

        _timer = new System.Windows.Forms.Timer { Interval = (int)PollInterval.TotalMilliseconds };
        _timer.Tick += (_, _) => Evaluate(forceApply: false);
        _timer.Start();

        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.TimeChanged += OnTimeChanged;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

        StartupManager.RepairIfStale();

        Log.Info($"started, mode={_settings.Mode}, location={(_settings.HasLocation ? _settings.LocationLabel : "none")}");

        Evaluate(forceApply: false);

        if (openSettingsOnStart)
        {
            ShowSettings();
        }
    }

    #region Scheduling

    /// <summary>
    /// Works out what the theme should be and applies it if needed.
    /// </summary>
    /// <param name="forceApply">
    /// Apply even when the system already reports the right mode. Used when the user picks a
    /// mode by hand, which doubles as a "kick it, it's stuck" button.
    /// </param>
    private void Evaluate(bool forceApply)
    {
        Schedule schedule;
        try
        {
            schedule = ThemeScheduler.Resolve(_settings, DateTime.Now);
        }
        catch (Exception ex)
        {
            Log.Error($"scheduling failed: {ex}");
            return;
        }

        // Automatic with nothing to compute from: leave the user's theme alone entirely.
        bool automaticWithoutLocation = _settings.Mode == ThemeMode.Automatic && !_settings.HasLocation;

        if (!automaticWithoutLocation)
        {
            bool targetChanged = _lastTarget != schedule.Target;
            bool systemDisagrees = ThemeSwitcher.Current != schedule.Target;

            // Edge-triggered by default. Without this, a deliberate manual switch to light at
            // 11pm would be undone within thirty seconds, which is maddening.
            bool shouldApply = forceApply
                               || (targetChanged && systemDisagrees)
                               || (_settings.EnforceStrictly && systemDisagrees);

            if (shouldApply)
            {
                ApplyTheme(schedule.Target, announce: targetChanged || forceApply);
            }

            _lastTarget = schedule.Target;
        }

        UpdateTrayVisuals(schedule, automaticWithoutLocation);
    }

    private void ApplyTheme(AppTheme theme, bool announce)
    {
        bool changed = ThemeSwitcher.Current != theme;

        if (!ThemeSwitcher.Apply(theme, _settings.DeepSync))
        {
            _notifyIcon.BalloonTipTitle = "Darkenator";
            _notifyIcon.BalloonTipText = "Could not change the Windows theme. See the log for details.";
            _notifyIcon.BalloonTipIcon = ToolTipIcon.Warning;
            _notifyIcon.ShowBalloonTip(5000);
            return;
        }

        if (announce && changed && _settings.ShowNotifications)
        {
            _notifyIcon.BalloonTipTitle = "Darkenator";
            _notifyIcon.BalloonTipText = $"Switched to {theme.ToString().ToLowerInvariant()} mode.";
            _notifyIcon.BalloonTipIcon = ToolTipIcon.None;
            _notifyIcon.ShowBalloonTip(3000);
        }
    }

    private void UpdateTrayVisuals(Schedule schedule, bool idle)
    {
        AppTheme active = ThemeSwitcher.Current;
        bool taskbarIsLight = ThemeSwitcher.CurrentSystemTheme == AppTheme.Light;

        Icon fresh = TrayIcons.Create(active, taskbarIsLight);
        Icon? previous = _currentIcon;
        _currentIcon = fresh;
        _notifyIcon.Icon = fresh;
        previous?.Dispose();

        string headline = _settings.Mode switch
        {
            ThemeMode.Light => "Always light",
            ThemeMode.Dark => "Always dark",
            _ when idle => "Automatic - no location set",
            _ => $"Automatic - {schedule.NextChangeDescription}"
        };

        _headerItem.Text = headline;

        // NotifyIcon.Text is capped at 63 characters by the shell; keep it short by design.
        string tip = $"Darkenator - {active.ToString().ToLowerInvariant()}";
        if (_settings.Mode == ThemeMode.Automatic && !idle && schedule.NextChangeAt is { } next)
        {
            tip += $", {schedule.NextTarget.ToString().ToLowerInvariant()} at {next:HH:mm}";
        }
        _notifyIcon.Text = tip.Length > 63 ? tip[..63] : tip;

        if (_notifyIcon.ContextMenuStrip is { } menu)
        {
            menu.Renderer = new ThemedMenuRenderer(UiTheme.For(active));
        }
    }

    private void RefreshMenu()
    {
        _lightItem.Checked = _settings.Mode == ThemeMode.Light;
        _darkItem.Checked = _settings.Mode == ThemeMode.Dark;
        _autoItem.Checked = _settings.Mode == ThemeMode.Automatic;
        Evaluate(forceApply: false);
    }

    private void SetMode(ThemeMode mode)
    {
        _settings.Mode = mode;
        _settings.Save();
        Log.Info($"mode set to {mode} from the tray menu");

        // A deliberate pick should take effect immediately, even if the registry already
        // claims to be in that mode - re-broadcasting fixes apps that missed the last change.
        _lastTarget = null;
        Evaluate(forceApply: true);
    }

    #endregion

    #region System events

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
        {
            // The machine may have been asleep across a sunset. Re-evaluate, and let the
            // shell settle first so the broadcast is not swallowed during resume.
            Log.Info("resumed from sleep, re-evaluating");
            _lastTarget = null;
            DelayThen(TimeSpan.FromSeconds(3), () => Evaluate(forceApply: false));
        }
    }

    private void OnTimeChanged(object? sender, EventArgs e)
    {
        Log.Info("system clock or time zone changed, re-evaluating");
        _lastTarget = null;
        Evaluate(forceApply: false);
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        // Someone changed the theme elsewhere (Settings, another tool). Repaint the tray icon
        // so it reflects reality; whether to push back is decided by EnforceStrictly.
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color)
        {
            DelayThen(TimeSpan.FromMilliseconds(400), () => Evaluate(forceApply: false));
        }
    }

    /// <summary>Runs <paramref name="action"/> on the UI thread after a delay, once.</summary>
    private void DelayThen(TimeSpan delay, Action action)
    {
        var timer = new System.Windows.Forms.Timer { Interval = (int)delay.TotalMilliseconds };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            timer.Dispose();
            if (!_disposed) action();
        };
        timer.Start();
    }

    #endregion

    #region Settings window

    public void ShowSettings()
    {
        if (_settingsForm is { IsDisposed: false })
        {
            if (_settingsForm.WindowState == FormWindowState.Minimized)
            {
                _settingsForm.WindowState = FormWindowState.Normal;
            }
            _settingsForm.Activate();
            _settingsForm.BringToFront();
            return;
        }

        _settingsForm = new SettingsForm(_settings);
        _settingsForm.FormClosed += (_, _) =>
        {
            SettingsForm? form = _settingsForm;
            _settingsForm = null;

            if (form is { DialogResult: DialogResult.OK })
            {
                _settings = form.Result;
                _settings.Save();
                Log.Info($"settings saved, mode={_settings.Mode}");

                _lastTarget = null;
                Evaluate(forceApply: true);
            }

            form?.Dispose();
        };

        _settingsForm.Show();
        _settingsForm.Activate();
    }

    #endregion

    /// <summary>
    /// Surfaces a problem without stealing focus. A tray utility that throws modal dialogs at
    /// you while you are working is worse than the problem it is reporting.
    /// </summary>
    public void ShowError(string message)
    {
        if (_disposed) return;

        _notifyIcon.BalloonTipTitle = "Darkenator";
        _notifyIcon.BalloonTipText = message;
        _notifyIcon.BalloonTipIcon = ToolTipIcon.Warning;
        _notifyIcon.ShowBalloonTip(6000);
    }

    /// <summary>Runs <paramref name="action"/> on the UI thread. Safe to call from any thread.</summary>
    public void PostToUi(Action action)
    {
        if (_disposed || !_marshaller.IsHandleCreated) return;

        try
        {
            _marshaller.BeginInvoke(action);
        }
        catch (Exception ex)
        {
            Log.Warn($"could not marshal onto the UI thread: {ex.Message}");
        }
    }

    private void ExitApp()
    {
        Log.Info("exiting");
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;

            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            SystemEvents.TimeChanged -= OnTimeChanged;
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;

            _timer.Stop();
            _timer.Dispose();

            // Hide before disposing, otherwise the icon can linger in the tray until hover.
            _notifyIcon.Visible = false;
            _notifyIcon.ContextMenuStrip?.Dispose();
            _notifyIcon.Dispose();

            _currentIcon?.Dispose();
            _settingsForm?.Dispose();
            _marshaller.Dispose();
        }

        base.Dispose(disposing);
    }
}
