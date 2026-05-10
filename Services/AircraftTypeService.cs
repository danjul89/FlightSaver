using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace FlightSaver.Services;

public sealed class AircraftTypeService
{
    public static readonly AircraftTypeService Instance = new();

    private static readonly HttpClient Http = CreateClient();
    private readonly ConcurrentDictionary<string, string> _memory = new();
    private readonly ConcurrentDictionary<string, byte> _negative = new();
    private readonly ConcurrentDictionary<string, byte> _inflight = new();

    public string? TryGet(string? icao24)
    {
        if (string.IsNullOrWhiteSpace(icao24)) return null;
        var key = icao24.Trim().ToLowerInvariant();
        if (_memory.TryGetValue(key, out var r)) return r;
        if (_negative.ContainsKey(key)) return null;
        Fetch(key);
        return null;
    }

    private void Fetch(string icao24)
    {
        if (!_inflight.TryAdd(icao24, 0)) return;
        _ = Task.Run(async () =>
        {
            try
            {
                var result = await TryAdsbDb(icao24).ConfigureAwait(false)
                          ?? await TryHexDb(icao24).ConfigureAwait(false);
                if (result is not null)
                    _memory[icao24] = result;
                else
                    _negative.TryAdd(icao24, 0);
            }
            catch
            {
                _negative.TryAdd(icao24, 0);
            }
            finally
            {
                _inflight.TryRemove(icao24, out _);
            }
        });
    }

    private static async Task<string?> TryAdsbDb(string icao24)
    {
        try
        {
            var url = $"https://api.adsbdb.com/v0/aircraft/{Uri.EscapeDataString(icao24)}";
            using var resp = await Http.GetAsync(url).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;
            var json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("response", out var response) ||
                !response.TryGetProperty("aircraft", out var aircraft) ||
                aircraft.ValueKind != JsonValueKind.Object) return null;
            string? mfr      = Get(aircraft, "manufacturer");
            string? icaoType = Get(aircraft, "icao_type");
            if (string.IsNullOrEmpty(icaoType)) return null;
            return string.IsNullOrEmpty(mfr) ? icaoType : $"{mfr} {icaoType}";
        }
        catch { return null; }
    }

    private static async Task<string?> TryHexDb(string icao24)
    {
        try
        {
            var url = $"https://hexdb.io/api/v1/aircraft/{Uri.EscapeDataString(icao24)}";
            using var resp = await Http.GetAsync(url).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;
            var json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            string? mfr      = Get(doc.RootElement, "Manufacturer");
            string? icaoType = Get(doc.RootElement, "ICAOTypeCode");
            if (string.IsNullOrEmpty(icaoType)) return null;
            return string.IsNullOrEmpty(mfr) ? icaoType : $"{mfr} {icaoType}";
        }
        catch { return null; }
    }

    private static string? Get(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("FlightSaver/1.0 (hobby Windows screensaver)");
        return c;
    }
}
