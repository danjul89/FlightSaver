namespace FlightSaver.Models;

public sealed class Config
{
    public string Address { get; set; } = "Sergels Torg, Stockholm, Sweden";
    public double Latitude { get; set; } = 59.3326;
    public double Longitude { get; set; } = 18.0649;
    public int RadiusKm { get; set; } = 50;

    public string MapTheme { get; set; } = "dark";

    public string? OpenSkyUsername { get; set; }
    public string? OpenSkyPasswordEncryptedBase64 { get; set; }

    public bool HasCredentials =>
        !string.IsNullOrWhiteSpace(OpenSkyUsername) &&
        !string.IsNullOrWhiteSpace(OpenSkyPasswordEncryptedBase64);
}
