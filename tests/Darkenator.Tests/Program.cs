using Darkenator;

namespace Darkenator.Tests;

/// <summary>
/// Console harness for the pure logic. Not a unit-test framework: the point is a single
/// command that either prints PASS for everything or shows exactly which number drifted.
/// </summary>
internal static class Program
{
    private static int _failures;

    private static int Main()
    {
        Console.WriteLine($"Local time zone: {TimeZoneInfo.Local.Id}");
        Console.WriteLine();

        RenderTrayIcons();
        SunTimesForKnownPlaces();
        MatchesPublishedTimes();
        EquinoxDayLengthIsAboutTwelveHours();
        PolarRegionsAreHandled();
        SunriseIsBeforeSunset();
        OffsetsMoveTheBoundary();

        Console.WriteLine();
        Console.WriteLine(_failures == 0
            ? "All checks passed."
            : $"{_failures} check(s) FAILED.");

        return _failures == 0 ? 0 : 1;
    }

    /// <summary>
    /// Renders the tray icons to PNGs so they can be eyeballed at real size without hunting
    /// through the notification-area overflow.
    /// </summary>
    private static void RenderTrayIcons()
    {
        Section("Tray icon rendering");

        string outDir = Path.Combine(AppContext.BaseDirectory, "icons");
        Directory.CreateDirectory(outDir);

        foreach (int side in new[] { 16, 20, 32, 64 })
        {
            foreach (AppTheme theme in new[] { AppTheme.Light, AppTheme.Dark })
            {
                foreach (bool taskbarLight in new[] { true, false })
                {
                    using Icon icon = TrayIcons.Create(theme, taskbarLight, side);
                    using Bitmap bmp = icon.ToBitmap();

                    string name = $"{theme.ToString().ToLowerInvariant()}-on-" +
                                  $"{(taskbarLight ? "light" : "dark")}-{side}.png";
                    bmp.Save(Path.Combine(outDir, name), System.Drawing.Imaging.ImageFormat.Png);

                    Check($"{name} rendered at {bmp.Width}x{bmp.Height}",
                        bmp.Width == side && bmp.Height == side);
                }
            }
        }

        Console.WriteLine($"  icons written to {outDir}");
    }

    private static void SunTimesForKnownPlaces()
    {
        Section("Reference sun times (printed in the machine's local time)");

        Report("Sydney", new DateTime(2026, 8, 30), -33.8688, 151.2093);
        Report("Sydney (midsummer)", new DateTime(2026, 12, 21), -33.8688, 151.2093);
        Report("Sydney (midwinter)", new DateTime(2026, 6, 21), -33.8688, 151.2093);
        Report("Melbourne", new DateTime(2026, 8, 30), -37.8136, 144.9631);
        Report("Greenwich", new DateTime(2026, 3, 20), 51.4779, 0.0);
        Report("Quito (equator)", new DateTime(2026, 8, 30), -0.1807, -78.4678);
    }

    private static void Report(string name, DateTime date, double lat, double lon)
    {
        SunTimes t = SolarCalculator.ForDate(date, lat, lon);
        if (t.HasBoth)
        {
            TimeSpan length = t.Sunset - t.Sunrise;
            Console.WriteLine($"  {name,-22} {date:yyyy-MM-dd}  rise {t.Sunrise:HH:mm:ss}  set {t.Sunset:HH:mm:ss}  noon {t.Sunrise + (t.Sunset - t.Sunrise) / 2:HH:mm:ss}  " +
                              $"day {length.Hours}h{length.Minutes:00}m");
        }
        else
        {
            Console.WriteLine($"  {name,-22} {date:yyyy-MM-dd}  {t.Outcome}");
        }
    }

    /// <summary>
    /// Pins the output against published times. The references come from open-meteo, which
    /// runs the same NOAA equations; note that sunrise-sunset.org disagrees by a couple of
    /// minutes because it uses the older Almanac for Computers approximation.
    /// </summary>
    private static void MatchesPublishedTimes()
    {
        Section("Matches published sun times (within 2 minutes)");

        // date, latitude, longitude, IANA-zone sunrise, sunset -- all in the location's own zone.
        ExpectLocal("Sydney", new DateTime(2026, 8, 30), -33.8688, 151.2093, 10, "06:16", "17:35");
        ExpectLocal("Melbourne", new DateTime(2026, 8, 30), -37.8136, 144.9631, 10, "06:45", "17:56");
        ExpectLocal("London", new DateTime(2026, 8, 30), 51.5074, -0.1278, 1, "06:08", "19:53");
    }

    /// <summary>
    /// Compares against a time expressed in the location's own zone. The calculator works in
    /// the machine's zone, so the reference is shifted by the difference before comparing.
    /// </summary>
    private static void ExpectLocal(string name, DateTime date, double lat, double lon,
        int locationUtcOffsetHours, string expectedRise, string expectedSet)
    {
        SunTimes t = SolarCalculator.ForDate(date, lat, lon);
        double machineOffset = TimeZoneInfo.Local.GetUtcOffset(date.AddHours(12)).TotalHours;
        double shift = machineOffset - locationUtcOffsetHours;

        DateTime riseRef = date.Add(TimeSpan.Parse(expectedRise)).AddHours(shift);
        DateTime setRef = date.Add(TimeSpan.Parse(expectedSet)).AddHours(shift);

        double riseDelta = Math.Abs((t.Sunrise - riseRef).TotalMinutes);
        double setDelta = Math.Abs((t.Sunset - setRef).TotalMinutes);

        Check($"{name} sunrise is within 2 min of {expectedRise} (off by {riseDelta:0.0} min)", riseDelta <= 2.0);
        Check($"{name} sunset is within 2 min of {expectedSet} (off by {setDelta:0.0} min)", setDelta <= 2.0);
    }

    private static void EquinoxDayLengthIsAboutTwelveHours()
    {
        Section("Equinox day length is ~12h everywhere");

        (string Name, double Lat, double Lon)[] places =
        {
            ("Equator", 0.0, 0.0),
            ("Sydney", -33.8688, 151.2093),
            ("London", 51.5074, -0.1278),
            ("Reykjavik", 64.1466, -21.9426),
            ("Santiago", -33.4489, -70.6693)
        };

        // At the equinox the sun is up for a little over twelve hours: the disc's radius and
        // atmospheric refraction both add a few minutes at each end.
        foreach ((string name, double lat, double lon) in places)
        {
            SunTimes t = SolarCalculator.ForDate(new DateTime(2026, 3, 20), lat, lon);
            double hours = (t.Sunset - t.Sunrise).TotalHours;
            Check($"{name} equinox day length {hours:0.00}h is within 12.0-12.5h",
                t.HasBoth && hours is > 11.9 and < 12.6);
        }
    }

    private static void PolarRegionsAreHandled()
    {
        Section("Polar day and polar night");

        SunTimes winter = SolarCalculator.ForDate(new DateTime(2026, 12, 21), 78.2232, 15.6267);
        Check("Longyearbyen in December is polar night", winter.Outcome == SunOutcome.AlwaysDown);

        SunTimes summer = SolarCalculator.ForDate(new DateTime(2026, 6, 21), 78.2232, 15.6267);
        Check("Longyearbyen in June is midnight sun", summer.Outcome == SunOutcome.AlwaysUp);

        SunTimes antarctic = SolarCalculator.ForDate(new DateTime(2026, 6, 21), -77.85, 166.67);
        Check("McMurdo in June is polar night", antarctic.Outcome == SunOutcome.AlwaysDown);
    }

    private static void SunriseIsBeforeSunset()
    {
        Section("Sunrise precedes sunset across a whole year");

        var date = new DateTime(2026, 1, 1);
        int checkedDays = 0;
        bool ok = true;

        while (date.Year == 2026)
        {
            SunTimes t = SolarCalculator.ForDate(date, -37.8136, 144.9631); // Melbourne
            if (!t.HasBoth || t.Sunset <= t.Sunrise)
            {
                ok = false;
                Console.WriteLine($"    out of order on {date:yyyy-MM-dd}: {t.Sunrise:HH:mm} / {t.Sunset:HH:mm}");
                break;
            }
            checkedDays++;
            date = date.AddDays(1);
        }

        Check($"Melbourne: {checkedDays} days all have sunrise before sunset", ok && checkedDays == 365);
    }

    private static void OffsetsMoveTheBoundary()
    {
        Section("Day length changes in the right direction through the year");

        SunTimes june = SolarCalculator.ForDate(new DateTime(2026, 6, 21), -33.8688, 151.2093);
        SunTimes december = SolarCalculator.ForDate(new DateTime(2026, 12, 21), -33.8688, 151.2093);

        double juneHours = (june.Sunset - june.Sunrise).TotalHours;
        double decemberHours = (december.Sunset - december.Sunrise).TotalHours;

        Check($"Sydney's shortest day ({juneHours:0.0}h) is in June, not December ({decemberHours:0.0}h)",
            juneHours < decemberHours);

        SunTimes londonJune = SolarCalculator.ForDate(new DateTime(2026, 6, 21), 51.5074, -0.1278);
        SunTimes londonDecember = SolarCalculator.ForDate(new DateTime(2026, 12, 21), 51.5074, -0.1278);

        Check("London's longest day is in June",
            (londonJune.Sunset - londonJune.Sunrise) > (londonDecember.Sunset - londonDecember.Sunrise));
    }

    private static void Section(string title)
    {
        Console.WriteLine();
        Console.WriteLine(title);
        Console.WriteLine(new string('-', title.Length));
    }

    private static void Check(string description, bool condition)
    {
        Console.WriteLine($"  [{(condition ? "PASS" : "FAIL")}] {description}");
        if (!condition) _failures++;
    }
}
