using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace FlightSaver.Services;

public sealed record AirportInfo(
    string IcaoCode,
    string IataCode,
    string Name,
    string Municipality,
    string Country,
    double Latitude,
    double Longitude);

public sealed record FlightRoute(AirportInfo? Origin, AirportInfo? Destination, string? AirlineName);

public sealed class RouteService
{
    public static readonly RouteService Instance = new();

    private static readonly HttpClient Http = CreateClient();

    private readonly ConcurrentDictionary<string, FlightRoute> _memory = new();
    private readonly ConcurrentDictionary<string, byte> _negative = new();
    private readonly ConcurrentDictionary<string, byte> _inflight = new();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("FlightSaver/1.0 (hobby Windows screensaver)");
        return c;
    }

    public FlightRoute? TryGet(string? callsign)
    {
        if (string.IsNullOrWhiteSpace(callsign)) return null;
        var key = callsign.Trim().ToUpperInvariant();
        if (_memory.TryGetValue(key, out var r)) return r;
        if (_negative.ContainsKey(key)) return null;
        Fetch(key);
        return null;
    }

    private void Fetch(string callsign)
    {
        if (!_inflight.TryAdd(callsign, 0)) return;
        _ = Task.Run(async () =>
        {
            try
            {
                var url = $"https://api.adsbdb.com/v0/callsign/{Uri.EscapeDataString(callsign)}";
                using var resp = await Http.GetAsync(url).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) { _negative.TryAdd(callsign, 0); return; }
                var json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);

                if (!doc.RootElement.TryGetProperty("response", out var response) ||
                    !response.TryGetProperty("flightroute", out var fr))
                {
                    _negative.TryAdd(callsign, 0);
                    return;
                }

                var origin = ParseAirport(fr, "origin");
                var destination = ParseAirport(fr, "destination");
                string? airlineName = null;
                if (fr.TryGetProperty("airline", out var airline) && airline.ValueKind == JsonValueKind.Object)
                {
                    if (airline.TryGetProperty("name", out var an) && an.ValueKind == JsonValueKind.String)
                        airlineName = an.GetString();
                }
                _memory[callsign] = new FlightRoute(origin, destination, airlineName);
            }
            catch
            {
                _negative.TryAdd(callsign, 0);
            }
            finally
            {
                _inflight.TryRemove(callsign, out _);
            }
        });
    }

    private static AirportInfo? ParseAirport(JsonElement parent, string field)
    {
        if (!parent.TryGetProperty(field, out var ap) || ap.ValueKind != JsonValueKind.Object) return null;
        string Get(string name) => ap.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() ?? "" : "";
        double GetD(string name) => ap.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.Number ? e.GetDouble() : 0;
        var icao = Get("icao_code");
        var iata = Get("iata_code");
        var name = Get("name");
        if (string.IsNullOrEmpty(icao) && string.IsNullOrEmpty(iata)) return null;
        return new AirportInfo(
            icao,
            iata,
            name,
            Get("municipality"),
            Get("country_name"),
            GetD("latitude"),
            GetD("longitude"));
    }
}
