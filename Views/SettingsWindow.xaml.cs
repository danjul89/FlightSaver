using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
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
        ApplySystemTheme();
        _config = config;
        _resolvedLat = config.Latitude;
        _resolvedLon = config.Longitude;
        _hasResolvedCoords = true;

        VersionText.Text = $"FlightSaver v{UpdateService.Instance.CurrentVersion}";
        RefreshUpdateUi();
        RefreshCacheStats();
        UpdateService.Instance.StatusChanged += OnUpdateStatusChanged;
        Closed += (_, _) => UpdateService.Instance.StatusChanged -= OnUpdateStatusChanged;

        AddressBox.Text = config.Address;
        var isAutoLocation = !string.Equals(config.LocationMode, "manual", StringComparison.OrdinalIgnoreCase);
        LocationAutoRadio.IsChecked = isAutoLocation;
        LocationManualRadio.IsChecked = !isAutoLocation;
        ToggleLocationVisibility();
        AutoLocationText.Text = string.IsNullOrWhiteSpace(config.Address)
            ? $"{config.Latitude:F4}°N, {config.Longitude:F4}°E"
            : $"{config.Address}\n{config.Latitude:F4}°N, {config.Longitude:F4}°E";
        RadiusSlider.Value = config.RadiusKm;
        RadiusLabel.Text = $"{config.RadiusKm} km";
        var theme = (config.MapTheme ?? "satellite").ToLowerInvariant();
        ThemeSatelliteRadio.IsChecked = theme == "satellite";
        ThemeLightRadio.IsChecked = theme == "light";
        ThemeDarkRadio.IsChecked = theme == "dark";
        var isCycle = string.Equals(config.FocusMode, "cycle", StringComparison.OrdinalIgnoreCase);
        FocusCycleRadio.IsChecked = isCycle;
        FocusClosestRadio.IsChecked = !isCycle;
        CycleIntervalSection.Visibility = isCycle ? Visibility.Visible : Visibility.Collapsed;
        SelectCycleInterval(config.CycleIntervalSeconds);
        DebugLogOnRadio.IsChecked = config.ShowDebugLog;
        DebugLogOffRadio.IsChecked = !config.ShowDebugLog;
        SelectCacheLimit(config.CacheLimitMb);
        UsernameBox.Text = config.OpenSkyUsername ?? "";
        var existingPwd = ConfigService.DecryptPassword(config.OpenSkyPasswordEncryptedBase64);
        if (!string.IsNullOrEmpty(existingPwd)) PasswordBox.Password = existingPwd;

        RadiusSlider.ValueChanged += (_, e) => RadiusLabel.Text = $"{(int)e.NewValue} km";
        GeocodeResultText.Text = $"Saved: {config.Latitude:F4}°N, {config.Longitude:F4}°E";
    }

    private async void OnFetchCoords(object sender, RoutedEventArgs e)
    {
        FetchCoordsButton.IsEnabled = false;
        GeocodeResultText.Text = "Looking up address...";
        try
        {
            using var nominatim = new NominatimClient();
            var result = await nominatim.GeocodeAsync(AddressBox.Text);
            if (result is null)
            {
                GeocodeResultText.Text = "No position found for that address.";
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
            GeocodeResultText.Text = $"Error: {ex.Message}";
            _hasResolvedCoords = false;
        }
        finally
        {
            FetchCoordsButton.IsEnabled = true;
        }
    }

    private void OnLocationModeChanged(object sender, RoutedEventArgs e) =>
        ToggleLocationVisibility();

    private void ToggleLocationVisibility()
    {
        if (LocationAutoRadio == null || LocationAutoSection == null || LocationManualSection == null) return;
        var isAuto = LocationAutoRadio.IsChecked == true;
        LocationAutoSection.Visibility = isAuto ? Visibility.Visible : Visibility.Collapsed;
        LocationManualSection.Visibility = isAuto ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void OnTestConnection(object sender, RoutedEventArgs e)
    {
        TestConnectionButton.IsEnabled = false;
        ConnectionResultText.Text = "Testing...";
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
                    ? "OK (anonymous)"
                    : "OK (authenticated)";
                ConnectionResultText.Foreground = System.Windows.Media.Brushes.LightGreen;
            }
            else if ((int)resp.StatusCode == 401)
            {
                ConnectionResultText.Text = "Wrong username/password";
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
            ConnectionResultText.Text = $"Error: {ex.Message}";
            ConnectionResultText.Foreground = System.Windows.Media.Brushes.Salmon;
        }
        finally
        {
            TestConnectionButton.IsEnabled = true;
        }
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var isManual = LocationManualRadio.IsChecked == true;
        if (isManual)
        {
            if (!_hasResolvedCoords)
            {
                MessageBox.Show("Resolve coordinates for the address first.", "FlightSaver",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            _config.Address = AddressBox.Text.Trim();
            _config.Latitude = _resolvedLat;
            _config.Longitude = _resolvedLon;
        }
        _config.LocationMode = isManual ? "manual" : "auto";
        _config.RadiusKm = (int)RadiusSlider.Value;
        _config.MapTheme = ThemeSatelliteRadio.IsChecked == true ? "satellite"
            : ThemeLightRadio.IsChecked == true ? "light"
            : "dark";
        _config.FocusMode = FocusCycleRadio.IsChecked == true ? "cycle" : "closest";
        _config.CycleIntervalSeconds = ReadCycleInterval();
        _config.ShowDebugLog = DebugLogOnRadio.IsChecked == true;
        _config.CacheLimitMb = ReadCacheLimit();

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

    private void ApplySystemTheme()
    {
        if (!IsSystemLightMode()) return;
        SetBrush("WindowBg",     "#F3F3F3");
        SetBrush("Surface",      "#FBFBFB");
        SetBrush("SurfaceAlt",   "#F0F0F0");
        SetBrush("Input",        "#FFFFFF");
        SetBrush("Border",       "#DCDCDC");
        SetBrush("BorderHover",  "#B0B0B0");
        SetBrush("TextPrimary",  "#1F1F1F");
        SetBrush("TextSecondary","#5C5C5C");
        SetBrush("TextDim",      "#8C8C8C");
    }

    private void SetBrush(string key, string hex)
    {
        if (Resources[key] is SolidColorBrush b)
            b.Color = (Color)ColorConverter.ConvertFromString(hex);
    }

    private static bool IsSystemLightMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 1;
        }
        catch
        {
            return false;
        }
    }

    private void SelectCacheLimit(int mb)
    {
        foreach (System.Windows.Controls.ComboBoxItem item in CacheLimitCombo.Items)
        {
            if (item.Tag is string tag && int.TryParse(tag, out var v) && v == mb)
            {
                CacheLimitCombo.SelectedItem = item;
                return;
            }
        }
        // No matching value — pick recommended
        foreach (System.Windows.Controls.ComboBoxItem item in CacheLimitCombo.Items)
        {
            if (item.Tag is string tag && tag == "200") { CacheLimitCombo.SelectedItem = item; return; }
        }
    }

    private int ReadCacheLimit()
    {
        if (CacheLimitCombo.SelectedItem is System.Windows.Controls.ComboBoxItem item &&
            item.Tag is string tag &&
            int.TryParse(tag, out var v))
            return v;
        return 200;
    }

    private void OnUpdateStatusChanged(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(RefreshUpdateUi);

    private void RefreshUpdateUi()
    {
        var svc = UpdateService.Instance;
        if (svc.IsChecking)
        {
            UpdateStatusText.Text = "Checking…";
            UpdateNowButton.Visibility = Visibility.Collapsed;
            CheckUpdateButton.IsEnabled = false;
            return;
        }
        CheckUpdateButton.IsEnabled = true;
        if (svc.LastError is not null)
        {
            UpdateStatusText.Text = $"Check failed: {svc.LastError}";
            UpdateNowButton.Visibility = Visibility.Collapsed;
            return;
        }
        if (svc.IsUpdateAvailable)
        {
            UpdateStatusText.Text = $"Update available: v{svc.LatestVersion}";
            UpdateNowButton.Visibility = string.IsNullOrEmpty(svc.LatestDownloadUrl)
                ? Visibility.Collapsed
                : Visibility.Visible;
            return;
        }
        if (!string.IsNullOrEmpty(svc.LatestVersion))
        {
            UpdateStatusText.Text = $"Up to date (latest: v{svc.LatestVersion})";
        }
        else if (svc.LastCheckUtc != default)
        {
            UpdateStatusText.Text = "No releases published yet.";
        }
        else
        {
            UpdateStatusText.Text = "";
        }
        UpdateNowButton.Visibility = Visibility.Collapsed;
    }

    private async void OnCheckUpdate(object sender, RoutedEventArgs e)
    {
        await UpdateService.Instance.CheckAsync();
    }

    private void OnFocusModeChanged(object sender, RoutedEventArgs e)
    {
        if (CycleIntervalSection == null) return;
        CycleIntervalSection.Visibility = FocusCycleRadio.IsChecked == true
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void SelectCycleInterval(int seconds)
    {
        foreach (System.Windows.Controls.ComboBoxItem item in CycleIntervalCombo.Items)
        {
            if (item.Tag is string tag && int.TryParse(tag, out var v) && v == seconds)
            {
                CycleIntervalCombo.SelectedItem = item;
                return;
            }
        }
        foreach (System.Windows.Controls.ComboBoxItem item in CycleIntervalCombo.Items)
        {
            if (item.Tag is string tag && tag == "10") { CycleIntervalCombo.SelectedItem = item; return; }
        }
    }

    private int ReadCycleInterval()
    {
        if (CycleIntervalCombo.SelectedItem is System.Windows.Controls.ComboBoxItem item &&
            item.Tag is string tag &&
            int.TryParse(tag, out var v))
            return v;
        return 10;
    }

    private void OnCacheLimitChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        RefreshCacheStats();
    }

    private void OnClearCache(object sender, RoutedEventArgs e)
    {
        ClearCacheButton.IsEnabled = false;
        try
        {
            CacheManager.ClearAll();
        }
        finally
        {
            RefreshCacheStats();
            ClearCacheButton.IsEnabled = true;
        }
    }

    private void RefreshCacheStats()
    {
        var stats = CacheManager.GetStats();
        var totalMb = stats.TotalBytes / 1024.0 / 1024.0;
        CacheUsageValue.Text = totalMb < 0.05 ? "0 MB" : $"{totalMb:F1} MB";
        var tileLabel = stats.TileCount == 1 ? "1 tile" : $"{stats.TileCount:N0} tiles";
        var photoLabel = stats.PhotoCount == 1 ? "1 photo" : $"{stats.PhotoCount:N0} photos";
        CacheUsageText.Text = $"{tileLabel} · {photoLabel}";

        var limit = ReadCacheLimit();
        if (limit > 0)
        {
            CacheProgressBar.Maximum = limit;
            CacheProgressBar.Value = Math.Min(totalMb, limit);
            CacheProgressBar.Visibility = Visibility.Visible;
        }
        else if (limit == 0)
        {
            CacheProgressBar.Maximum = Math.Max(totalMb, 1);
            CacheProgressBar.Value = totalMb;
            CacheProgressBar.Visibility = Visibility.Visible;
        }
        else
        {
            CacheProgressBar.Visibility = Visibility.Collapsed;
        }
    }

    private async void OnUpdateNow(object sender, RoutedEventArgs e)
    {
        UpdateNowButton.IsEnabled = false;
        UpdateStatusText.Text = "Downloading update…";
        var progress = new Progress<double>(p =>
            UpdateStatusText.Text = $"Downloading update… {p * 100:F0}%");
        var path = await UpdateService.Instance.DownloadAsync(progress);
        if (path is null)
        {
            UpdateStatusText.Text = "Download failed.";
            UpdateNowButton.IsEnabled = true;
            return;
        }
        UpdateStatusText.Text = "Requesting elevation…";
        var launched = UpdateService.Instance.LaunchInstaller(path);
        if (!launched)
        {
            UpdateStatusText.Text = "Update cancelled (UAC declined).";
            UpdateNowButton.IsEnabled = true;
            return;
        }
        UpdateStatusText.Text = "Updating in background. Closing settings…";
        await System.Threading.Tasks.Task.Delay(800);
        Application.Current.Shutdown();
    }
}
