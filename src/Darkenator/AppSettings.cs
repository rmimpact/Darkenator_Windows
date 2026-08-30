using System.Text.Json;
using System.Text.Json.Serialization;

namespace Darkenator;

public enum ThemeMode
{
    Light,
    Dark,
    Automatic
}

public sealed class AppSettings
{
    public ThemeMode Mode { get; set; } = ThemeMode.Automatic;

    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string LocationLabel { get; set; } = string.Empty;

    /// <summary>Minutes to shift the switch to light relative to sunrise. Negative = earlier.</summary>
    public int SunriseOffsetMinutes { get; set; }

    /// <summary>Minutes to shift the switch to dark relative to sunset. Negative = earlier.</summary>
    public int SunsetOffsetMinutes { get; set; }

    public bool RunAtStartup { get; set; }

    public bool StartMinimized { get; set; } = true;

    /// <summary>Also apply the matching Windows theme file so the Themes page agrees.</summary>
    public bool DeepSync { get; set; }

    /// <summary>
    /// When true, keep pushing the schedule's theme back if something else changes it.
    /// Off by default so a deliberate manual override sticks until the next sun event.
    /// </summary>
    public bool EnforceStrictly { get; set; }

    public bool ShowNotifications { get; set; }

    [JsonIgnore]
    public bool HasLocation => Latitude.HasValue && Longitude.HasValue;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static AppSettings Load()
    {
        try
        {
            string path = AppPaths.SettingsFile;
            if (File.Exists(path))
            {
                string json = File.ReadAllText(path);
                AppSettings? loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                if (loaded is not null)
                {
                    loaded.Normalize();
                    return loaded;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"settings unreadable, falling back to defaults: {ex.Message}");
        }

        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Normalize();
            Directory.CreateDirectory(AppPaths.DataDirectory);

            // Write to a sibling then swap, so a crash mid-write cannot leave a truncated file.
            string path = AppPaths.SettingsFile;
            string temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(this, JsonOptions));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Error($"could not save settings: {ex}");
        }
    }

    private void Normalize()
    {
        if (Latitude is { } lat && (double.IsNaN(lat) || lat < -90 || lat > 90)) Latitude = null;
        if (Longitude is { } lon && (double.IsNaN(lon) || lon < -180 || lon > 180)) Longitude = null;

        SunriseOffsetMinutes = Math.Clamp(SunriseOffsetMinutes, -240, 240);
        SunsetOffsetMinutes = Math.Clamp(SunsetOffsetMinutes, -240, 240);
    }

    public AppSettings Clone() => (AppSettings)MemberwiseClone();
}
