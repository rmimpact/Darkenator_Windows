using System.Globalization;
using Microsoft.Win32;

namespace Darkenator;

/// <summary>
/// The whole configuration surface: mode, location, offsets, startup. Laid out in code so
/// there is no designer file to keep in sync, and painted to match the mode currently in force.
/// </summary>
public sealed class SettingsForm : Form
{
    private readonly AppSettings _working;

    private RadioButton _rbLight = null!;
    private RadioButton _rbDark = null!;
    private RadioButton _rbAuto = null!;

    private Panel _autoCard = null!;
    private TextBox _txtLat = null!;
    private TextBox _txtLon = null!;
    private Button _btnDetect = null!;
    private Label _lblLocation = null!;
    private Label _lblSunPreview = null!;
    private NumericUpDown _numSunrise = null!;
    private NumericUpDown _numSunset = null!;

    private CheckBox _chkStartup = null!;
    private CheckBox _chkStartMinimized = null!;
    private CheckBox _chkDeepSync = null!;
    private CheckBox _chkEnforce = null!;
    private CheckBox _chkNotify = null!;

    private Label _lblStatus = null!;
    private Button _btnSave = null!;
    private Button _btnCancel = null!;
    private LinkLabel _lnkLog = null!;

    private CancellationTokenSource? _detectCts;
    private UiTheme.Palette _palette = UiTheme.Light;

    /// <summary>The edited settings, valid once the dialog closes with OK.</summary>
    public AppSettings Result => _working;

    public SettingsForm(AppSettings current)
    {
        _working = current.Clone();
        BuildLayout();
        LoadFromSettings();
        ApplyPalette(UiTheme.For(ThemeSwitcher.Current));
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;

            // Dispose runs more than once (Form.WmClose, then the owner's Dispose), and
            // Cancel() on an already-disposed source throws. Detach first, then tear down.
            CancellationTokenSource? cts = _detectCts;
            _detectCts = null;

            if (cts is not null)
            {
                try
                {
                    cts.Cancel();
                }
                catch (ObjectDisposedException)
                {
                    // Already torn down by a concurrent Detect; nothing left to cancel.
                }

                cts.Dispose();
            }
        }
        base.Dispose(disposing);
    }

    #region Layout

    private const int Pad = 20;
    private const int CardPad = 16;
    private const int FormWidth = 500;

    private void BuildLayout()
    {
        Text = "Darkenator";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Font;
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        ClientSize = new Size(FormWidth, 700);
        KeyPreview = true;

        try
        {
            Icon = Icon.ExtractAssociatedIcon(AppPaths.ExecutablePath);
        }
        catch
        {
            // Running from an unusual host: the stock form icon will do.
        }

        int y = Pad;

        var title = new Label
        {
            Text = "Darkenator",
            Font = new Font("Segoe UI Semibold", 15F, FontStyle.Regular, GraphicsUnit.Point),
            AutoSize = true,
            Location = new Point(Pad, y)
        };
        Controls.Add(title);
        y += 36;

        _lblStatus = new Label
        {
            Text = "...",
            AutoSize = false,
            Size = new Size(FormWidth - 2 * Pad, 18),
            Location = new Point(Pad, y)
        };
        Controls.Add(_lblStatus);
        y += 30;

        // --- Theme mode -----------------------------------------------------------
        Panel modeCard = CreateCard("Theme", ref y, 136, out int inner);

        _rbLight = CreateRadio("Light", inner);
        _rbDark = CreateRadio("Dark", inner + 30);
        _rbAuto = CreateRadio("Automatic  -  dark from sunset to sunrise", inner + 60);
        modeCard.Controls.AddRange(new Control[] { _rbLight, _rbDark, _rbAuto });

        _rbLight.CheckedChanged += OnModeChanged;
        _rbDark.CheckedChanged += OnModeChanged;
        _rbAuto.CheckedChanged += OnModeChanged;

        // --- Automatic ------------------------------------------------------------
        _autoCard = CreateCard("Location", ref y, 212, out inner);

        int cardInnerWidth = FormWidth - 2 * Pad - 2 * CardPad;

        _lblLocation = new Label
        {
            Text = "Not set",
            AutoSize = false,
            Size = new Size(cardInnerWidth, 18),
            Location = new Point(CardPad, inner)
        };

        var latLabel = new Label { Text = "Latitude", AutoSize = true, Location = new Point(CardPad, inner + 30) };
        _txtLat = new TextBox
        {
            Location = new Point(CardPad + 62, inner + 27),
            Width = 84,
            BorderStyle = BorderStyle.FixedSingle
        };

        var lonLabel = new Label { Text = "Longitude", AutoSize = true, Location = new Point(CardPad + 162, inner + 30) };
        _txtLon = new TextBox
        {
            Location = new Point(CardPad + 232, inner + 27),
            Width = 84,
            BorderStyle = BorderStyle.FixedSingle
        };

        _btnDetect = new Button
        {
            Text = "Detect",
            Location = new Point(CardPad + cardInnerWidth - 80, inner + 26),
            Size = new Size(80, 26),
            FlatStyle = FlatStyle.Flat
        };
        _btnDetect.FlatAppearance.BorderSize = 1;
        _btnDetect.Click += async (_, _) => await DetectLocationAsync();

        _txtLat.TextChanged += (_, _) => UpdateSunPreview();
        _txtLon.TextChanged += (_, _) => UpdateSunPreview();

        _lblSunPreview = new Label
        {
            AutoSize = false,
            Size = new Size(cardInnerWidth, 38),
            Location = new Point(CardPad, inner + 62)
        };

        var sunriseLabel = new Label { Text = "Go light", AutoSize = true, Location = new Point(CardPad, inner + 112) };
        _numSunrise = CreateOffsetInput(new Point(CardPad + 62, inner + 109));
        var sunriseSuffix = new Label
        {
            Text = "min relative to sunrise",
            AutoSize = true,
            Location = new Point(CardPad + 136, inner + 112)
        };

        var sunsetLabel = new Label { Text = "Go dark", AutoSize = true, Location = new Point(CardPad, inner + 144) };
        _numSunset = CreateOffsetInput(new Point(CardPad + 62, inner + 141));
        var sunsetSuffix = new Label
        {
            Text = "min relative to sunset",
            AutoSize = true,
            Location = new Point(CardPad + 136, inner + 144)
        };

        _numSunrise.ValueChanged += (_, _) => UpdateSunPreview();
        _numSunset.ValueChanged += (_, _) => UpdateSunPreview();

        _autoCard.Controls.AddRange(new Control[]
        {
            _lblLocation, latLabel, _txtLat, lonLabel, _txtLon, _btnDetect, _lblSunPreview,
            sunriseLabel, _numSunrise, sunriseSuffix,
            sunsetLabel, _numSunset, sunsetSuffix
        });

        // --- Behaviour ------------------------------------------------------------
        Panel behaviourCard = CreateCard("Startup and behaviour", ref y, 182, out inner);

        _chkStartup = CreateCheck("Run Darkenator when Windows starts", inner);
        _chkStartMinimized = CreateCheck("Start hidden in the notification area", inner + 30);
        _chkDeepSync = CreateCheck("Deep sync  -  also switch the Windows theme file", inner + 60);
        _chkEnforce = CreateCheck("Re-apply if something else changes the theme", inner + 90);
        _chkNotify = CreateCheck("Show a notification when the theme switches", inner + 120);

        _chkDeepSync.Enabled = DeepThemeSync.IsAvailable;

        behaviourCard.Controls.AddRange(new Control[]
        {
            _chkStartup, _chkStartMinimized, _chkDeepSync, _chkEnforce, _chkNotify
        });

        // --- Footer ---------------------------------------------------------------
        y += 6;

        _lnkLog = new LinkLabel
        {
            Text = "Open log",
            AutoSize = true,
            Location = new Point(Pad, y + 9)
        };
        _lnkLog.LinkClicked += (_, _) => OpenLog();
        Controls.Add(_lnkLog);

        _btnCancel = new Button
        {
            Text = "Cancel",
            Size = new Size(96, 32),
            Location = new Point(FormWidth - Pad - 96 - 8 - 96, y),
            FlatStyle = FlatStyle.Flat,
            DialogResult = DialogResult.Cancel
        };
        _btnCancel.FlatAppearance.BorderSize = 1;

        _btnSave = new Button
        {
            Text = "Save",
            Size = new Size(96, 32),
            Location = new Point(FormWidth - Pad - 96, y),
            FlatStyle = FlatStyle.Flat
        };
        _btnSave.FlatAppearance.BorderSize = 0;
        _btnSave.Click += OnSave;

        Controls.Add(_btnCancel);
        Controls.Add(_btnSave);

        AcceptButton = _btnSave;
        CancelButton = _btnCancel;

        ClientSize = new Size(FormWidth, y + 32 + Pad);
    }

    /// <summary>Adds a titled panel and advances <paramref name="y"/> past it.</summary>
    private Panel CreateCard(string heading, ref int y, int height, out int innerTop)
    {
        var card = new Panel
        {
            Location = new Point(Pad, y),
            Size = new Size(FormWidth - 2 * Pad, height)
        };
        card.Paint += (s, e) =>
        {
            var panel = (Panel)s!;
            using var pen = new Pen(_palette.Border);
            e.Graphics.DrawRectangle(pen, 0, 0, panel.Width - 1, panel.Height - 1);
        };

        var label = new Label
        {
            Text = heading,
            Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Regular, GraphicsUnit.Point),
            AutoSize = true,
            Location = new Point(CardPad, 14),
            Tag = "heading"
        };
        card.Controls.Add(label);

        Controls.Add(card);
        innerTop = 44;
        y += height + 14;
        return card;
    }

    private static RadioButton CreateRadio(string text, int top) => new()
    {
        Text = text,
        AutoSize = true,
        Location = new Point(CardPad, top),
        FlatStyle = FlatStyle.Flat
    };

    private static CheckBox CreateCheck(string text, int top) => new()
    {
        Text = text,
        AutoSize = true,
        Location = new Point(CardPad, top),
        FlatStyle = FlatStyle.Flat
    };

    private static NumericUpDown CreateOffsetInput(Point location) => new()
    {
        // NumericUpDown derives from ContainerControl, so it runs its own auto-scale pass and
        // ends up shrunk and shifted away from the bounds set here. Opting out pins it down.
        AutoScaleMode = AutoScaleMode.None,
        Location = location,
        Width = 64,
        Minimum = -240,
        Maximum = 240,
        Increment = 5,
        TextAlign = HorizontalAlignment.Right,
        BorderStyle = BorderStyle.FixedSingle
    };

    #endregion

    #region Theming

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.General
            or UserPreferenceCategory.Color
            or UserPreferenceCategory.VisualStyle)
        {
            if (IsHandleCreated && !IsDisposed)
            {
                BeginInvoke(new Action(() => ApplyPalette(UiTheme.For(ThemeSwitcher.Current))));
            }
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        UiTheme.ApplyTitleBar(this, ThemeSwitcher.Current == AppTheme.Dark);
    }

    private void ApplyPalette(UiTheme.Palette palette)
    {
        _palette = palette;

        BackColor = palette.Background;
        ForeColor = palette.Text;

        foreach (Control control in Controls)
        {
            StyleControl(control);
        }

        if (IsHandleCreated)
        {
            UiTheme.ApplyTitleBar(this, ReferenceEquals(palette, UiTheme.Dark));
        }

        StyleButton(_btnSave, primary: true);
        StyleButton(_btnCancel, primary: false);
        StyleButton(_btnDetect, primary: false);

        _lnkLog.LinkColor = palette.Accent;
        _lnkLog.ActiveLinkColor = palette.Accent;
        _lnkLog.VisitedLinkColor = palette.Accent;
        _lnkLog.BackColor = palette.Background;

        Invalidate(true);
    }

    private void StyleControl(Control control)
    {
        bool onCard = control.Parent is Panel;
        Color surface = onCard ? _palette.Surface : _palette.Background;
        bool dark = ReferenceEquals(_palette, UiTheme.Dark);

        switch (control)
        {
            case Panel panel:
                panel.BackColor = _palette.Surface;
                panel.ForeColor = _palette.Text;
                foreach (Control child in panel.Controls) StyleControl(child);
                return;

            case TextBox textBox:
                textBox.BackColor = dark ? _palette.Background : Color.White;
                textBox.ForeColor = _palette.Text;
                return;

            case NumericUpDown numeric:
                numeric.BackColor = dark ? _palette.Background : Color.White;
                numeric.ForeColor = _palette.Text;
                return;

            case CheckBox or RadioButton:
                control.BackColor = surface;
                control.ForeColor = _palette.Text;
                if (control is ButtonBase toggle)
                {
                    toggle.FlatAppearance.BorderColor = _palette.Border;
                    toggle.FlatAppearance.CheckedBackColor = surface;
                    toggle.FlatAppearance.MouseOverBackColor = surface;
                }
                return;

            case Button:
            case LinkLabel:
                return; // styled explicitly so the primary button keeps its accent fill

            case Label label:
                label.BackColor = surface;
                bool muted = ReferenceEquals(label, _lblSunPreview)
                             || ReferenceEquals(label, _lblStatus)
                             || ReferenceEquals(label, _lblLocation);
                label.ForeColor = muted ? _palette.TextMuted : _palette.Text;
                return;

            default:
                control.BackColor = surface;
                control.ForeColor = _palette.Text;
                return;
        }
    }

    private void StyleButton(Button button, bool primary)
    {
        button.ForeColor = primary ? _palette.AccentText : _palette.Text;
        button.BackColor = primary ? _palette.Accent : _palette.Surface;
        button.FlatAppearance.BorderColor = primary ? _palette.Accent : _palette.Border;
        button.FlatAppearance.MouseOverBackColor = primary
            ? ControlPaint.Light(_palette.Accent, 0.15f)
            : _palette.MenuHighlight;
        button.FlatAppearance.MouseDownBackColor = primary
            ? ControlPaint.Dark(_palette.Accent, 0.05f)
            : _palette.MenuHighlight;
    }

    #endregion

    #region State

    private void LoadFromSettings()
    {
        _rbLight.Checked = _working.Mode == ThemeMode.Light;
        _rbDark.Checked = _working.Mode == ThemeMode.Dark;
        _rbAuto.Checked = _working.Mode == ThemeMode.Automatic;

        _txtLat.Text = _working.Latitude?.ToString("0.####", CultureInfo.InvariantCulture) ?? string.Empty;
        _txtLon.Text = _working.Longitude?.ToString("0.####", CultureInfo.InvariantCulture) ?? string.Empty;
        _lblLocation.Text = string.IsNullOrWhiteSpace(_working.LocationLabel) ? "Not set" : _working.LocationLabel;

        _numSunrise.Value = _working.SunriseOffsetMinutes;
        _numSunset.Value = _working.SunsetOffsetMinutes;

        // The registry is the truth for startup, not the settings file: the two drift if the
        // entry is removed by hand or by a cleanup tool.
        _chkStartup.Checked = StartupManager.IsEnabled();
        _chkStartMinimized.Checked = _working.StartMinimized;
        _chkDeepSync.Checked = _working.DeepSync && DeepThemeSync.IsAvailable;
        _chkEnforce.Checked = _working.EnforceStrictly;
        _chkNotify.Checked = _working.ShowNotifications;

        OnModeChanged(this, EventArgs.Empty);
    }

    private void OnModeChanged(object? sender, EventArgs e)
    {
        bool automatic = _rbAuto.Checked;

        foreach (Control child in _autoCard.Controls)
        {
            if (child is Label { Tag: "heading" }) continue;
            child.Enabled = automatic;
        }

        UpdateSunPreview();
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        string system = ThemeSwitcher.Current.ToString().ToLowerInvariant();
        string mode = _rbLight.Checked ? "Light" : _rbDark.Checked ? "Dark" : "Automatic";
        _lblStatus.Text = $"Windows is currently {system}   ·   mode: {mode}";
    }

    private bool TryReadCoordinates(out double lat, out double lon)
    {
        lon = 0;
        return double.TryParse(_txtLat.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out lat)
               && double.TryParse(_txtLon.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out lon)
               && lat is >= -90 and <= 90
               && lon is >= -180 and <= 180;
    }

    private void UpdateSunPreview()
    {
        if (!_rbAuto.Checked)
        {
            _lblSunPreview.Text = "Sun times are only used in Automatic mode.";
            return;
        }

        if (!TryReadCoordinates(out double lat, out double lon))
        {
            _lblSunPreview.Text = "Enter coordinates, or press Detect to look them up from your IP address.";
            return;
        }

        SunTimes times = SolarCalculator.ForDate(DateTime.Now, lat, lon);

        _lblSunPreview.Text = times.Outcome switch
        {
            SunOutcome.AlwaysUp => "The sun does not set here today - Darkenator will stay light.",
            SunOutcome.AlwaysDown => "The sun does not rise here today - Darkenator will stay dark.",
            _ => $"Today: sunrise {times.Sunrise:HH:mm}, sunset {times.Sunset:HH:mm}." + Environment.NewLine +
                 $"Goes light at {times.Sunrise.AddMinutes((double)_numSunrise.Value):HH:mm}, " +
                 $"dark at {times.Sunset.AddMinutes((double)_numSunset.Value):HH:mm}."
        };
    }

    private async Task DetectLocationAsync()
    {
        CancellationTokenSource? previousCts = _detectCts;
        if (previousCts is not null)
        {
            try
            {
                previousCts.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }

            previousCts.Dispose();
        }

        _detectCts = new CancellationTokenSource();

        string previous = _btnDetect.Text;
        _btnDetect.Enabled = false;
        _btnDetect.Text = "Finding...";

        try
        {
            GeoLocation? found = await LocationService.DetectAsync(_detectCts.Token);

            if (IsDisposed) return;

            if (found is { } location)
            {
                _txtLat.Text = location.Latitude.ToString("0.####", CultureInfo.InvariantCulture);
                _txtLon.Text = location.Longitude.ToString("0.####", CultureInfo.InvariantCulture);
                _lblLocation.Text = location.Label;
                UpdateSunPreview();
            }
            else
            {
                MessageBox.Show(this,
                    "Could not look up your location. Check your internet connection, "
                    + "or type the coordinates in by hand.",
                    "Darkenator", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        catch (OperationCanceledException)
        {
            // The form is closing, or a second Detect superseded this one.
        }
        finally
        {
            if (!IsDisposed)
            {
                _btnDetect.Enabled = _rbAuto.Checked;
                _btnDetect.Text = previous;
            }
        }
    }

    private void OnSave(object? sender, EventArgs e)
    {
        _working.Mode = _rbLight.Checked ? ThemeMode.Light
            : _rbDark.Checked ? ThemeMode.Dark
            : ThemeMode.Automatic;

        bool blank = _txtLat.Text.Trim().Length == 0 && _txtLon.Text.Trim().Length == 0;

        if (!blank && !TryReadCoordinates(out _, out _))
        {
            MessageBox.Show(this,
                "Latitude must be between -90 and 90, and longitude between -180 and 180."
                + Environment.NewLine
                + "Use a dot as the decimal separator, for example 51.5074 and -0.1278.",
                "Darkenator", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _txtLat.Focus();
            return;
        }

        if (_working.Mode == ThemeMode.Automatic && blank)
        {
            DialogResult answer = MessageBox.Show(this,
                "Automatic mode needs a location to work out sunset. Save anyway and leave the "
                + "theme alone until a location is set?",
                "Darkenator", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (answer != DialogResult.Yes) return;
        }

        if (blank)
        {
            _working.Latitude = null;
            _working.Longitude = null;
            _working.LocationLabel = string.Empty;
        }
        else if (TryReadCoordinates(out double lat, out double lon))
        {
            _working.Latitude = lat;
            _working.Longitude = lon;
            _working.LocationLabel = _lblLocation.Text is { Length: > 0 } and not "Not set"
                ? _lblLocation.Text
                : LocationService.FormatCoordinates(lat, lon);
        }

        _working.SunriseOffsetMinutes = (int)_numSunrise.Value;
        _working.SunsetOffsetMinutes = (int)_numSunset.Value;
        _working.StartMinimized = _chkStartMinimized.Checked;
        _working.DeepSync = _chkDeepSync.Checked;
        _working.EnforceStrictly = _chkEnforce.Checked;
        _working.ShowNotifications = _chkNotify.Checked;

        if (_chkStartup.Checked != StartupManager.IsEnabled() && !StartupManager.Set(_chkStartup.Checked))
        {
            MessageBox.Show(this,
                "Could not update the Windows startup entry. The rest of your settings were saved.",
                "Darkenator", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        _working.RunAtStartup = StartupManager.IsEnabled();

        DialogResult = DialogResult.OK;
        Close();
    }

    private void OpenLog()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataDirectory);
            if (!File.Exists(Log.FilePath)) Log.Info("log opened from settings");

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = Log.FilePath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not open the log: {ex.Message}",
                "Darkenator", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            DialogResult = DialogResult.Cancel;
            Close();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    #endregion
}
