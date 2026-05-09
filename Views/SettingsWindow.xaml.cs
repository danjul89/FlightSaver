using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using FlightSaver.Models;
using FlightSaver.Services;

namespace FlightSaver.Views;

public partial class SettingsWindow : Window
{
    private readonly Config _config;
    private double _resolvedLat;
    private double _resolvedLon;
    private bool _hasResolvedCoords;

    public SettingsWindow(Config config)
    {
        InitializeComponent();
        _config = config;
        _resolvedLat = config.Latitude;
        _resolvedLon = config.Longitude;
        _hasResolvedCoords = true;

        AddressBox.Text = config.Address;
        RadiusSlider.Value = config.RadiusKm;
        RadiusLabel.Text = $"{config.RadiusKm} km";
        var isLight = string.Equals(config.MapTheme, "light", StringComparison.OrdinalIgnoreCase);
        ThemeLightRadio.IsChecked = isLight;
        ThemeDarkRadio.IsChecked = !isLight;
        UsernameBox.Text = config.OpenSkyUsername ?? "";
        var existingPwd = ConfigService.DecryptPassword(config.OpenSkyPasswordEncryptedBase64);
        if (!string.IsNullOrEmpty(existingPwd)) PasswordBox.Password = existingPwd;

        RadiusSlider.ValueChanged += (_, e) => RadiusLabel.Text = $"{(int)e.NewValue} km";
        GeocodeResultText.Text = $"Sparad: {config.Latitude:F4}°N, {config.Longitude:F4}°E";
    }

    private async void OnFetchCoords(object sender, RoutedEventArgs e)
    {
        FetchCoordsButton.IsEnabled = false;
        GeocodeResultText.Text = "Slår upp adress...";
        try
        {
            using var nominatim = new NominatimClient();
            var result = await nominatim.GeocodeAsync(AddressBox.Text);
            if (result is null)
            {
                GeocodeResultText.Text = "Hittade ingen position för den adressen.";
                _hasResolvedCoords = false;
                return;
            }
            _resolvedLat = result.Latitude;
            _resolvedLon = result.Longitude;
            _hasResolvedCoords = true;
            GeocodeResultText.Text = $"{result.DisplayName}\n{result.Latitude:F4}°N, {result.Longitude:F4}°E";
        }
        catch (Exception ex)
        {
            GeocodeResultText.Text = $"Fel: {ex.Message}";
            _hasResolvedCoords = false;
        }
        finally
        {
            FetchCoordsButton.IsEnabled = true;
        }
    }

    private async void OnAutoDetect(object sender, RoutedEventArgs e)
    {
        AutoDetectButton.IsEnabled = false;
        GeocodeResultText.Text = "Försöker IP-baserad lokalisering...";
        try
        {
            using var nominatim = new NominatimClient();
            var result = await nominatim.ReverseFromIpAsync();
            if (result is null)
            {
                GeocodeResultText.Text = "IP-lokalisering misslyckades.";
                return;
            }
            _resolvedLat = result.Latitude;
            _resolvedLon = result.Longitude;
            _hasResolvedCoords = true;
            AddressBox.Text = result.DisplayName;
            GeocodeResultText.Text = $"{result.DisplayName}\n{result.Latitude:F4}°N, {result.Longitude:F4}°E";
        }
        catch (Exception ex)
        {
            GeocodeResultText.Text = $"Fel: {ex.Message}";
        }
        finally
        {
            AutoDetectButton.IsEnabled = true;
        }
    }

    private async void OnTestConnection(object sender, RoutedEventArgs e)
    {
        TestConnectionButton.IsEnabled = false;
        ConnectionResultText.Text = "Testar...";
        ConnectionResultText.Foreground = System.Windows.Media.Brushes.Gray;

        var user = UsernameBox.Text.Trim();
        var pwd = PasswordBox.Password;

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("FlightSaver/1.0");
            if (!string.IsNullOrEmpty(user) && !string.IsNullOrEmpty(pwd))
            {
                var raw = Encoding.UTF8.GetBytes($"{user}:{pwd}");
                http.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Basic", Convert.ToBase64String(raw));
            }

            var url = "https://opensky-network.org/api/states/all?lamin=59.0&lomin=17.5&lamax=59.7&lomax=18.5";
            using var resp = await http.GetAsync(url);
            if (resp.IsSuccessStatusCode)
            {
                ConnectionResultText.Text = string.IsNullOrEmpty(user)
                    ? "OK (anonym)"
                    : "OK (autentiserad)";
                ConnectionResultText.Foreground = System.Windows.Media.Brushes.LightGreen;
            }
            else if ((int)resp.StatusCode == 401)
            {
                ConnectionResultText.Text = "Fel användarnamn/lösenord";
                ConnectionResultText.Foreground = System.Windows.Media.Brushes.Salmon;
            }
            else
            {
                ConnectionResultText.Text = $"HTTP {(int)resp.StatusCode}";
                ConnectionResultText.Foreground = System.Windows.Media.Brushes.Salmon;
            }
        }
        catch (Exception ex)
        {
            ConnectionResultText.Text = $"Fel: {ex.Message}";
            ConnectionResultText.Foreground = System.Windows.Media.Brushes.Salmon;
        }
        finally
        {
            TestConnectionButton.IsEnabled = true;
        }
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (!_hasResolvedCoords)
        {
            MessageBox.Show("Hämta koordinater för adressen först.", "FlightSaver",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _config.Address = AddressBox.Text.Trim();
        _config.Latitude = _resolvedLat;
        _config.Longitude = _resolvedLon;
        _config.RadiusKm = (int)RadiusSlider.Value;
        _config.MapTheme = ThemeLightRadio.IsChecked == true ? "light" : "dark";

        var user = UsernameBox.Text.Trim();
        var pwd = PasswordBox.Password;
        if (!string.IsNullOrEmpty(user) && !string.IsNullOrEmpty(pwd))
        {
            _config.OpenSkyUsername = user;
            _config.OpenSkyPasswordEncryptedBase64 = ConfigService.EncryptPassword(pwd);
        }
        else
        {
            _config.OpenSkyUsername = null;
            _config.OpenSkyPasswordEncryptedBase64 = null;
        }

        ConfigService.Save(_config);
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Close();
}
