using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FlightSaver.Models;

namespace FlightSaver.Services;

public sealed class OpenSkyClient : IDisposable
{
    private const string Endpoint = "https://opensky-network.org/api/states/all";
    private readonly HttpClient _http;

    public OpenSkyClient()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("FlightSaver/1.0");
    }

    public void SetCredentials(string? username, string? password)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            _http.DefaultRequestHeaders.Authorization = null;
            return;
        }
        var raw = Encoding.UTF8.GetBytes($"{username}:{password}");
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", Convert.ToBase64String(raw));
    }

    public async Task<IReadOnlyList<Aircraft>> GetStatesInBoxAsync(
        double latMin, double latMax, double lonMin, double lonMax, CancellationToken ct = default)
    {
        var url = $"{Endpoint}?lamin={Fmt(latMin)}&lomin={Fmt(lonMin)}&lamax={Fmt(latMax)}&lomax={Fmt(lonMax)}&extended=1";
        using var resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);

        var root = doc.RootElement;
        if (!root.TryGetProperty("states", out var states) || states.ValueKind != JsonValueKind.Array)
            return Array.Empty<Aircraft>();

        var nowUtc = DateTime.UtcNow;
        var list = new List<Aircraft>(states.GetArrayLength());
        foreach (var s in states.EnumerateArray())
        {
            if (s.ValueKind != JsonValueKind.Array || s.GetArrayLength() < 17) continue;

            string icao = s[0].GetString() ?? "";
            string? callsign = s[1].ValueKind == JsonValueKind.String ? s[1].GetString() : null;
            string? originCountry = s[2].ValueKind == JsonValueKind.String ? s[2].GetString() : null;
            double? lon = ReadNullableDouble(s[5]);
            double? lat = ReadNullableDouble(s[6]);
            double? baroAlt = ReadNullableDouble(s[7]);
            bool onGround = s[8].ValueKind == JsonValueKind.True;
            double? velocity = ReadNullableDouble(s[9]);
            double? trueTrack = ReadNullableDouble(s[10]);
            double? verticalRate = ReadNullableDouble(s[11]);
            double? geoAlt = ReadNullableDouble(s[13]);
            int category = s.GetArrayLength() > 17 && s[17].ValueKind == JsonValueKind.Number ? s[17].GetInt32() : 0;

            if (lat is null || lon is null) continue;

            var altitude = geoAlt ?? baroAlt ?? 0;

            list.Add(new Aircraft
            {
                Icao24 = icao,
                Callsign = callsign,
                OriginCountry = originCountry,
                Latitude = lat.Value,
                Longitude = lon.Value,
                AltitudeMeters = altitude,
                VelocityMetersPerSec = velocity ?? 0,
                TrueTrackDegrees = trueTrack ?? 0,
                VerticalRateMetersPerSec = verticalRate ?? 0,
                OnGround = onGround,
                Category = category,
                LastUpdateUtc = nowUtc,
            });
        }
        return list;
    }

    private static double? ReadNullableDouble(JsonElement el) =>
        el.ValueKind == JsonValueKind.Number ? el.GetDouble() : null;

    private static string Fmt(double v) =>
        v.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture);

    public void Dispose() => _http.Dispose();
}
