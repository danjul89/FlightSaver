using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FlightSaver.Services;

public sealed record GeocodeResult(double Latitude, double Longitude, string DisplayName);

public sealed class NominatimClient : IDisposable
{
    private const string UserAgent = "FlightSaver/1.0 (FlightSaver@danjul.se)";
    private const string Endpoint = "https://nominatim.openstreetmap.org/search";

    private readonly HttpClient _http;

    public NominatimClient()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        _http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en");
    }

    public async Task<GeocodeResult?> GeocodeAsync(string address, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(address)) return null;

        var url = $"{Endpoint}?format=json&limit=1&q={Uri.EscapeDataString(address)}";
        using var resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode) return null;

        await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
        if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
            return null;

        var first = doc.RootElement[0];
        if (!first.TryGetProperty("lat", out var latEl) ||
            !first.TryGetProperty("lon", out var lonEl) ||
            !first.TryGetProperty("display_name", out var nameEl))
            return null;

        if (!double.TryParse(latEl.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var lat) ||
            !double.TryParse(lonEl.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var lon))
            return null;

        return new GeocodeResult(lat, lon, nameEl.GetString() ?? address);
    }

    public async Task<GeocodeResult?> ReverseFromIpAsync(CancellationToken ct = default)
    {
        try
        {
            using var resp = await _http.GetAsync("https://ipapi.co/json/", ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;
            await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
            var root = doc.RootElement;
            if (!root.TryGetProperty("latitude", out var latEl) ||
                !root.TryGetProperty("longitude", out var lonEl))
                return null;
            var lat = latEl.GetDouble();
            var lon = lonEl.GetDouble();
            var city = root.TryGetProperty("city", out var c) ? c.GetString() : null;
            var country = root.TryGetProperty("country_name", out var cn) ? cn.GetString() : null;
            var name = string.Join(", ", new[] { city, country }.Where(s => !string.IsNullOrEmpty(s))!);
            return new GeocodeResult(lat, lon, string.IsNullOrEmpty(name) ? "Detected via IP" : name);
        }
        catch
        {
            return null;
        }
    }

    public void Dispose() => _http.Dispose();
}
