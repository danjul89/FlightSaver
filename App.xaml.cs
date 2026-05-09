using System;
using System.IO;
using System.Threading;
using System.Windows;
using FlightSaver.Models;
using FlightSaver.Services;
using FlightSaver.Views;

namespace FlightSaver;

public partial class App : Application
{
    private FlightTracker? _tracker;

    private void OnStartup(object sender, StartupEventArgs e)
    {
        var mode = ParseMode(e.Args);
        var configExisted = File.Exists(ConfigService.ConfigPath);
        var config = ConfigService.LoadOrDefault();
        if (!configExisted)
        {
            TryDetectIpLocation(config);
            ConfigService.Save(config);
        }

        TileCache.Instance.Theme = config.MapTheme;

        switch (mode)
        {
            case ScreensaverMode.Config:
                LaunchConfig(config);
                break;
            case ScreensaverMode.Preview:
                LaunchPreview(e.Args, config);
                break;
            case ScreensaverMode.FullScreen:
            default:
                LaunchFullScreen(config);
                break;
        }

        Exit += (_, _) => _tracker?.Dispose();
    }

    private static ScreensaverMode ParseMode(string[] args)
    {
        if (args.Length == 0) return ScreensaverMode.Config;
        var arg = args[0].ToLowerInvariant().TrimStart('/', '-');
        if (arg.StartsWith('s')) return ScreensaverMode.FullScreen;
        if (arg.StartsWith('p')) return ScreensaverMode.Preview;
        if (arg.StartsWith('c')) return ScreensaverMode.Config;
        return ScreensaverMode.Config;
    }

    private static void TryDetectIpLocation(Config config)
    {
        try
        {
            using var nominatim = new NominatimClient();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var result = nominatim.ReverseFromIpAsync(cts.Token).GetAwaiter().GetResult();
            if (result is null) return;
            config.Latitude = result.Latitude;
            config.Longitude = result.Longitude;
            config.Address = result.DisplayName;
        }
        catch
        {
            // Offline or lookup failed — keep default position
        }
    }

    private void LaunchConfig(Config config)
    {
        var settings = new SettingsWindow(config);
        MainWindow = settings;
        settings.Show();
    }

    private void LaunchPreview(string[] args, Config config)
    {
        if (args.Length < 2 || !long.TryParse(args[1], out var hwndLong))
        {
            Shutdown(1);
            return;
        }
        _tracker = new FlightTracker(config);
        _tracker.Start();
        var preview = new PreviewWindow(config, _tracker, new IntPtr(hwndLong));
        MainWindow = preview;
        preview.Show();
    }

    private void LaunchFullScreen(Config config)
    {
        _tracker = new FlightTracker(config);
        _tracker.Start();

        foreach (var screen in System.Windows.Forms.Screen.AllScreens)
        {
            var win = new ScreensaverWindow(config, _tracker, screen.Bounds);
            MainWindow ??= win;
            win.Show();
        }
    }
}

public enum ScreensaverMode { FullScreen, Preview, Config }
