namespace Darkenator;

public enum SunOutcome
{
    /// <summary>Normal day: both a sunrise and a sunset exist.</summary>
    RisesAndSets,
    /// <summary>Midnight sun — the sun stays above the horizon all day.</summary>
    AlwaysUp,
    /// <summary>Polar night — the sun stays below the horizon all day.</summary>
    AlwaysDown
}

public readonly record struct SunTimes(SunOutcome Outcome, DateTime Sunrise, DateTime Sunset)
{
    public bool HasBoth => Outcome == SunOutcome.RisesAndSets;
}

/// <summary>
/// Sunrise and sunset from latitude/longitude using the NOAA solar position equations.
/// Pure arithmetic — no network, no API key, works offline forever. Accurate to about a
/// minute for latitudes below ~65 degrees, which is well inside what "switch at sunset" needs.
/// </summary>
public static class SolarCalculator
{
    // Standard zenith for sunrise/sunset: 90 degrees plus refraction and the solar disc radius.
    private const double SunriseZenithDegrees = 90.833;

    /// <summary>
    /// Sunrise and sunset for <paramref name="localDate"/> at the given coordinates,
    /// returned in the machine's local time (DST-aware).
    /// </summary>
    public static SunTimes ForDate(DateTime localDate, double latitude, double longitude)
    {
        DateTime date = localDate.Date;
        double tzOffsetHours = TimeZoneInfo.Local.GetUtcOffset(date.AddHours(12)).TotalHours;

        // Evaluate the sun's position at local solar noon: that is where NOAA's day-granularity
        // equations are most accurate, and it keeps the result stable across the date boundary.
        double julianDay = JulianDayAtMidnightUtc(date) + (12.0 - tzOffsetHours) / 24.0;
        double t = (julianDay - 2451545.0) / 36525.0;

        double geomMeanLong = Mod360(280.46646 + t * (36000.76983 + t * 0.0003032));
        double geomMeanAnom = 357.52911 + t * (35999.05029 - 0.0001537 * t);
        double eccent = 0.016708634 - t * (0.000042037 + 0.0000001267 * t);

        double sunEqOfCentre =
            Math.Sin(Rad(geomMeanAnom)) * (1.914602 - t * (0.004817 + 0.000014 * t)) +
            Math.Sin(Rad(2 * geomMeanAnom)) * (0.019993 - 0.000101 * t) +
            Math.Sin(Rad(3 * geomMeanAnom)) * 0.000289;

        double sunTrueLong = geomMeanLong + sunEqOfCentre;
        double sunAppLong = sunTrueLong - 0.00569 - 0.00478 * Math.Sin(Rad(125.04 - 1934.136 * t));

        double meanObliq = 23.0 + (26.0 + (21.448 - t * (46.815 + t * (0.00059 - t * 0.001813))) / 60.0) / 60.0;
        double obliqCorr = meanObliq + 0.00256 * Math.Cos(Rad(125.04 - 1934.136 * t));

        double declination = Deg(Math.Asin(Math.Sin(Rad(obliqCorr)) * Math.Sin(Rad(sunAppLong))));

        double vary = Math.Pow(Math.Tan(Rad(obliqCorr / 2.0)), 2);
        double eqOfTimeMinutes = 4.0 * Deg(
            vary * Math.Sin(2 * Rad(geomMeanLong))
            - 2 * eccent * Math.Sin(Rad(geomMeanAnom))
            + 4 * eccent * vary * Math.Sin(Rad(geomMeanAnom)) * Math.Cos(2 * Rad(geomMeanLong))
            - 0.5 * vary * vary * Math.Sin(4 * Rad(geomMeanLong))
            - 1.25 * eccent * eccent * Math.Sin(2 * Rad(geomMeanAnom)));

        // Solar noon expressed as minutes after local midnight.
        double solarNoonMinutes = 720.0 - 4.0 * longitude - eqOfTimeMinutes + tzOffsetHours * 60.0;

        double hourAngleCos =
            Math.Cos(Rad(SunriseZenithDegrees)) / (Math.Cos(Rad(latitude)) * Math.Cos(Rad(declination)))
            - Math.Tan(Rad(latitude)) * Math.Tan(Rad(declination));

        // Outside [-1, 1] the sun never crosses the horizon on this date.
        if (hourAngleCos > 1.0)
        {
            return new SunTimes(SunOutcome.AlwaysDown, date, date);
        }
        if (hourAngleCos < -1.0)
        {
            return new SunTimes(SunOutcome.AlwaysUp, date, date);
        }

        double hourAngleMinutes = 4.0 * Deg(Math.Acos(hourAngleCos));

        DateTime sunrise = MinutesToLocal(date, solarNoonMinutes - hourAngleMinutes);
        DateTime sunset = MinutesToLocal(date, solarNoonMinutes + hourAngleMinutes);

        return new SunTimes(SunOutcome.RisesAndSets, sunrise, sunset);
    }

    /// <summary>Julian day number for 00:00 UT on the given calendar date.</summary>
    private static double JulianDayAtMidnightUtc(DateTime date)
    {
        int year = date.Year;
        int month = date.Month;
        int day = date.Day;

        if (month <= 2)
        {
            year -= 1;
            month += 12;
        }

        int a = year / 100;
        int b = 2 - a + a / 4;

        return Math.Floor(365.25 * (year + 4716))
             + Math.Floor(30.6001 * (month + 1))
             + day + b - 1524.5;
    }

    private static DateTime MinutesToLocal(DateTime date, double minutesAfterMidnight)
    {
        // Very high or low longitudes relative to the timezone can push the result past the
        // day boundary; AddMinutes carries it into the neighbouring day correctly.
        return date.AddMinutes(minutesAfterMidnight);
    }

    private static double Rad(double degrees) => degrees * Math.PI / 180.0;

    private static double Deg(double radians) => radians * 180.0 / Math.PI;

    private static double Mod360(double value)
    {
        double m = value % 360.0;
        return m < 0 ? m + 360.0 : m;
    }
}
