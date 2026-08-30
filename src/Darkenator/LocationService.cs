using System.Globalization;
using System.Net.Http;
using System.Text.Json;

namespace Darkenator;

public readonly record struct GeoLocation(double Latitude, double Longitude, string Label);

/// <summary>
/// Best-effort coordinate lookup from the public IP address.
///
/// Only ever runs when the user presses the Detect button — the app never phones home on
/// its own, and once coordinates are stored the sunrise/sunset maths is entirely offline.
/// </summary>
public static class LocationService
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Darkenator/1.0");
        return client;
    }

    private sealed record Provider(string Url, string LatField, string LonField, string[] LabelFields);

    // Tried in order; the first one that answers with usable coordinates wins.
    private static readonly Provider[] Providers =
    {
        new("https://ipapi.co/json/", "latitude", "longitude", new[] { "city", "region", "country_name" }),
        new("https://ipwho.is/", "latitude", "longitude", new[] { "city", "region", "country" }),
        new("http://ip-api.com/json/", "lat", "lon", new[] { "city", "regionName", "country" })
    };

    public static async Task<GeoLocation?> DetectAsync(CancellationToken cancellationToken = default)
    {
        foreach (Provider provider in Providers)
        {
            try
            {
                string json = await Http.GetStringAsync(provider.Url, cancellationToken).ConfigureAwait(false);
                using JsonDocument doc = JsonDocument.Parse(json);
                JsonElement root = doc.RootElement;

                if (!TryReadDouble(root, provider.LatField, out double lat) ||
                    !TryReadDouble(root, provider.LonField, out double lon))
                {
                    continue;
                }

                if (lat is < -90 or > 90 || lon is < -180 or > 180) continue;

                var parts = new List<string>();
                foreach (string field in provider.LabelFields)
                {
                    if (root.TryGetProperty(field, out JsonElement e) &&
                        e.ValueKind == JsonValueKind.String &&
                        !string.IsNullOrWhiteSpace(e.GetString()))
                    {
                        parts.Add(e.GetString()!);
                    }
                }

                string label = parts.Count > 0
                    ? string.Join(", ", parts)
                    : FormatCoordinates(lat, lon);

                Log.Info($"location detected via {provider.Url}: {label}");
                return new GeoLocation(lat, lon, label);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log.Warn($"location provider {provider.Url} failed: {ex.Message}");
            }
        }

        return null;
    }

    private static bool TryReadDouble(JsonElement root, string name, out double value)
    {
        value = 0;
        if (!root.TryGetProperty(name, out JsonElement element)) return false;

        return element.ValueKind switch
        {
            JsonValueKind.Number => element.TryGetDouble(out value),
            // ipapi.co returns numbers, but some mirrors hand back strings.
            JsonValueKind.String => double.TryParse(element.GetString(), NumberStyles.Float,
                CultureInfo.InvariantCulture, out value),
            _ => false
        };
    }

    public static string FormatCoordinates(double lat, double lon) =>
        string.Format(CultureInfo.InvariantCulture, "{0:0.####}, {1:0.####}", lat, lon);
}
