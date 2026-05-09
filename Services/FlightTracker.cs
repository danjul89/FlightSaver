using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FlightSaver.Models;

namespace FlightSaver.Services;

public enum ConnectionStatus { Connecting, Online, Retrying, Offline }

public sealed class FlightTracker : IDisposable
{
    private readonly Config _config;
    private readonly OpenSkyClient _client = new();
    private readonly ConcurrentDictionary<string, Aircraft> _aircraft = new();
    private readonly CancellationTokenSource _cts = new();
    private Task? _loopTask;

    public ConnectionStatus Status { get; private set; } = ConnectionStatus.Connecting;
    public DateTime LastSuccessUtc { get; private set; } = DateTime.MinValue;

    public event EventHandler? AircraftUpdated;
    public event EventHandler? StatusChanged;

    public FlightTracker(Config config)
    {
        _config = config;
        var pwd = ConfigService.DecryptPassword(config.OpenSkyPasswordEncryptedBase64);
        _client.SetCredentials(config.OpenSkyUsername, pwd);
    }

    public IReadOnlyCollection<Aircraft> CurrentAircraft => _aircraft.Values.ToArray();

    public void Start()
    {
        _loopTask ??= Task.Run(() => LoopAsync(_cts.Token));
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        var backoff = TimeSpan.FromSeconds(5);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await PollOnceAsync(ct).ConfigureAwait(false);
                SetStatus(ConnectionStatus.Online);
                LastSuccessUtc = DateTime.UtcNow;
                backoff = TimeSpan.FromSeconds(5);
                await Task.Delay(GetPollInterval(), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { return; }
            catch
            {
                SetStatus(ConnectionStatus.Retrying);
                await Task.Delay(backoff, ct).ConfigureAwait(false);
                backoff = TimeSpan.FromSeconds(Math.Min(120, backoff.TotalSeconds * 2));
                if (DateTime.UtcNow - LastSuccessUtc > TimeSpan.FromMinutes(2))
                    SetStatus(ConnectionStatus.Offline);
            }
        }
    }

    private TimeSpan GetPollInterval()
    {
        var onBattery = SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Offline;
        var lowBattery = SystemInformation.PowerStatus.BatteryLifePercent < 0.15f;

        if (lowBattery) return TimeSpan.FromMinutes(5);

        if (_config.HasCredentials)
            return onBattery ? TimeSpan.FromSeconds(60) : TimeSpan.FromSeconds(30);

        return onBattery ? TimeSpan.FromMinutes(5) : TimeSpan.FromMinutes(4);
    }

    private async Task PollOnceAsync(CancellationToken ct)
    {
        var (latMin, latMax, lonMin, lonMax) = ComputeBoundingBox(_config.Latitude, _config.Longitude, _config.RadiusKm);
        var states = await _client.GetStatesInBoxAsync(latMin, latMax, lonMin, lonMax, ct).ConfigureAwait(false);

        var seen = new HashSet<string>();
        foreach (var ac in states)
        {
            if (ac.OnGround) continue;
            seen.Add(ac.Icao24);
            _aircraft.AddOrUpdate(ac.Icao24, ac, (_, existing) =>
            {
                existing.Callsign = ac.Callsign ?? existing.Callsign;
                existing.OriginCountry = ac.OriginCountry ?? existing.OriginCountry;
                existing.Latitude = ac.Latitude;
                existing.Longitude = ac.Longitude;
                existing.AltitudeMeters = ac.AltitudeMeters;
                existing.VelocityMetersPerSec = ac.VelocityMetersPerSec;
                existing.TrueTrackDegrees = ac.TrueTrackDegrees;
                existing.VerticalRateMetersPerSec = ac.VerticalRateMetersPerSec;
                existing.OnGround = ac.OnGround;
                existing.LastUpdateUtc = ac.LastUpdateUtc;
                return existing;
            });
        }

        var cutoff = DateTime.UtcNow - TimeSpan.FromSeconds(120);
        foreach (var key in _aircraft.Keys)
        {
            if (_aircraft.TryGetValue(key, out var existing) && existing.LastUpdateUtc < cutoff && !seen.Contains(key))
                _aircraft.TryRemove(key, out _);
        }

        AircraftUpdated?.Invoke(this, EventArgs.Empty);
    }

    public static (double latMin, double latMax, double lonMin, double lonMax) ComputeBoundingBox(double lat, double lon, int radiusKm)
    {
        double dLat = radiusKm / 111.0;
        double cosLat = Math.Cos(lat * Math.PI / 180.0);
        double dLon = radiusKm / (111.0 * Math.Max(0.01, cosLat));
        return (lat - dLat, lat + dLat, lon - dLon, lon + dLon);
    }

    private void SetStatus(ConnectionStatus next)
    {
        if (Status == next) return;
        Status = next;
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        try { _cts.Cancel(); } catch { }
        try { _loopTask?.Wait(TimeSpan.FromSeconds(2)); } catch { }
        _client.Dispose();
        _cts.Dispose();
    }
}
