using System;

namespace FlightSaver.Models;

public sealed class Aircraft
{
    public required string Icao24 { get; init; }
    public string? Callsign { get; set; }
    public string? OriginCountry { get; set; }

    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double AltitudeMeters { get; set; }
    public double VelocityMetersPerSec { get; set; }
    public double TrueTrackDegrees { get; set; }
    public double VerticalRateMetersPerSec { get; set; }
    public bool OnGround { get; set; }
    public int Category { get; set; }

    public DateTime LastUpdateUtc { get; set; }
    public DateTime FirstSeenUtc { get; set; } = DateTime.UtcNow;

    public AltitudeBand Band => AltitudeBands.Classify(AltitudeMeters);

    public string DisplayCallsign => string.IsNullOrWhiteSpace(Callsign) ? Icao24.ToUpperInvariant() : Callsign.Trim();
}
