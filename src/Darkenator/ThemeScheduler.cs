namespace Darkenator;

/// <summary>
/// What the theme should be right now, and when that answer next changes.
/// </summary>
public readonly record struct Schedule(
    AppTheme Target,
    DateTime? NextChangeAt,
    AppTheme NextTarget,
    SunTimes Today,
    string Reason)
{
    public string NextChangeDescription => NextChangeAt is { } at
        ? $"{NextTarget.ToString().ToLowerInvariant()} at {at:HH:mm}"
        : "no scheduled change";
}

/// <summary>
/// Turns the settings into a decision. Kept free of UI and Win32 so it can be reasoned
/// about (and tested) on its own.
/// </summary>
public static class ThemeScheduler
{
    public static Schedule Resolve(AppSettings settings, DateTime now)
    {
        switch (settings.Mode)
        {
            case ThemeMode.Light:
                return new Schedule(AppTheme.Light, null, AppTheme.Light, default, "always light");

            case ThemeMode.Dark:
                return new Schedule(AppTheme.Dark, null, AppTheme.Dark, default, "always dark");

            case ThemeMode.Automatic:
                return ResolveAutomatic(settings, now);

            default:
                return new Schedule(AppTheme.Light, null, AppTheme.Light, default, "unknown mode");
        }
    }

    private static Schedule ResolveAutomatic(AppSettings settings, DateTime now)
    {
        if (!settings.HasLocation)
        {
            // Nothing sensible to compute yet; leave the system alone rather than guessing.
            return new Schedule(ThemeSwitcher.Current, null, ThemeSwitcher.Current, default,
                "automatic is on but no location is set");
        }

        double lat = settings.Latitude!.Value;
        double lon = settings.Longitude!.Value;

        SunTimes today = SolarCalculator.ForDate(now, lat, lon);

        switch (today.Outcome)
        {
            case SunOutcome.AlwaysUp:
                // Midnight sun: stay light, re-check after midnight.
                return new Schedule(AppTheme.Light, now.Date.AddDays(1), AppTheme.Light, today,
                    "the sun does not set today");

            case SunOutcome.AlwaysDown:
                return new Schedule(AppTheme.Dark, now.Date.AddDays(1), AppTheme.Dark, today,
                    "the sun does not rise today");
        }

        DateTime lightAt = today.Sunrise.AddMinutes(settings.SunriseOffsetMinutes);
        DateTime darkAt = today.Sunset.AddMinutes(settings.SunsetOffsetMinutes);

        // Offsets big enough to cross over each other would otherwise produce a nonsense
        // window; treat that as "dark all day" rather than flapping.
        if (darkAt <= lightAt)
        {
            return new Schedule(AppTheme.Dark, now.Date.AddDays(1), AppTheme.Dark, today,
                "the offsets leave no daylight window");
        }

        if (now < lightAt)
        {
            return new Schedule(AppTheme.Dark, lightAt, AppTheme.Light, today,
                $"before sunrise ({today.Sunrise:HH:mm})");
        }

        if (now < darkAt)
        {
            return new Schedule(AppTheme.Light, darkAt, AppTheme.Dark, today,
                $"daytime, sunset at {today.Sunset:HH:mm}");
        }

        // Past sunset: dark until tomorrow's sunrise.
        SunTimes tomorrow = SolarCalculator.ForDate(now.Date.AddDays(1), lat, lon);
        DateTime? nextLight = tomorrow.HasBoth
            ? tomorrow.Sunrise.AddMinutes(settings.SunriseOffsetMinutes)
            : now.Date.AddDays(1);

        return new Schedule(AppTheme.Dark, nextLight, AppTheme.Light, today,
            $"after sunset ({today.Sunset:HH:mm})");
    }
}
