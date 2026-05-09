namespace FlightSaver.Models;

public sealed class Config
{
    public string Address { get; set; } = "Sergels Torg, Stockholm, Sweden";
    public double Latitude { get; set; } = 59.3326;
    public double Longitude { get; set; } = 18.0649;
    public int RadiusKm { get; set; } = 50;

    public string LocationMode { get; set; } = "auto";

    public string MapTheme { get; set; } = "satellite";

    public int CacheLimitMb { get; set; } = 200;

    public string FocusMode { get; set; } = "closest";
    public int CycleIntervalSeconds { get; set; } = 10;

    public bool ShowDebugLog { get; set; } = false;

    public string? OpenSkyUsername { get; set; }
    public string? OpenSkyPasswordEncryptedBase64 { get; set; }

    public bool HasCredentials =>
        !string.IsNullOrWhiteSpace(OpenSkyUsername) &&
        !string.IsNullOrWhiteSpace(OpenSkyPasswordEncryptedBase64);
}
