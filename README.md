# FlightSaver

A Windows screensaver that shows aircraft flying above your location in real time, sourced from the [OpenSky Network](https://opensky-network.org/).

A live ADS-B radar overlay rendered on top of an OpenStreetMap basemap (CartoDB dark or light), with a compass, altitude-coloured aircraft icons rotated to heading, fading 5-minute trails, a pulsing "you are here" marker, and an info panel for the closest aircraft. Helicopters get a dedicated rotorcraft icon when their ADS-B category is broadcast.

## Features

- Position resolved automatically via IP on first run; can be overridden by entering an address (geocoded via Nominatim/OpenStreetMap)
- Adjustable radius from 5 to 200 km
- Per-monitor rendering on multi-monitor setups
- 30 FPS smooth dead-reckoning between polls
- Altitude colour coding: red below 1000 m, yellow 1000-6000 m, cyan above 6000 m
- Closest aircraft is highlighted with a halo and an info panel (speed, vertical rate, origin country)
- Cached extrapolation when offline, with a small status indicator
- Adaptive polling: 30 s (registered + plugged in) → 5 min (anonymous / on battery)
- OpenSky credentials are optional and stored encrypted via Windows DPAPI
- Helicopter icon for ADS-B category 8 (rotorcraft); plane silhouette for everything else
- Map theme toggle (dark / light) in settings

## Quick install (recommended)

Open PowerShell (any version) and paste:

```powershell
iwr -useb https://raw.githubusercontent.com/danjul89/FlightSaver/main/install.ps1 | iex
```

This downloads the latest `FlightSaver.scr` from [GitHub Releases](https://github.com/danjul89/FlightSaver/releases/latest), copies it to `C:\Windows\System32\` (UAC prompt — click Yes), and opens the Windows screensaver dialog with FlightSaver pre-selected. Click **Settings...** to configure or just **OK** — sane defaults are used (auto IP location, satellite map, 50 km radius).

## Manual install

1. Download `FlightSaver.scr` from the [latest release](https://github.com/danjul89/FlightSaver/releases/latest)
2. Right-click the file in Explorer → **Install**
3. The screensaver settings dialog opens; click **Settings...** to configure if you want

If no config exists on first launch, FlightSaver looks up your approximate location via `ipapi.co`. The address can be overridden manually under **Settings → Location → Manual**.

## Build from source

Requires the **.NET 8 SDK** on Windows. On WSL/Linux: `EnableWindowsTargeting=true` is set in the `.csproj` so the project builds, but you can't run it from Linux — copy the artifact to Windows.

### Standalone single-file build

```bash
dotnet publish -c Release -r win-x64 \
  --self-contained \
  -p:PublishSingleFile=true
```

Output: `bin/Release/net8.0-windows10.0.17763.0/win-x64/publish/FlightSaver.exe` (~186 MB self-contained).

Rename to `.scr`:

```bash
mv FlightSaver.exe FlightSaver.scr
```

GitHub Actions builds and publishes a release automatically on every `v*.*.*` tag push (see [`.github/workflows/release.yml`](.github/workflows/release.yml)).

## OpenSky account (optional)

Anonymous access is enough for slow polling (~5 min). For 30 s polling, create a free account at [opensky-network.org](https://opensky-network.org/) and enter the username/password in Settings → **Test connection**.

The password is stored encrypted via Windows DPAPI (`%APPDATA%\FlightSaver\config.json`) and can only be decrypted by the same Windows user on the same machine.

## Exiting the screensaver

Mouse movement (>4 px), click, or any keypress exits.

## File layout

```
FlightSaver/
├── App.xaml(.cs)              # Entry point, parses /s /p /c args
├── Models/
│   ├── Aircraft.cs            # Plane state + ADS-B category
│   ├── AltitudeBand.cs        # Color mapping
│   └── Config.cs              # Persisted settings
├── Services/
│   ├── ConfigService.cs       # Load/save + DPAPI
│   ├── NominatimClient.cs     # Address geocoding + IP fallback
│   ├── OpenSkyClient.cs       # Bbox query (extended=1) + optional auth
│   ├── FlightTracker.cs       # Polling loop + dead-reckoning state
│   └── TileCache.cs           # Disk-cached basemap tile downloader
├── Rendering/
│   └── RadarCanvas.cs         # Custom WPF render
├── Views/
│   ├── ScreensaverWindow.xaml(.cs)  # Per-monitor fullscreen
│   ├── PreviewWindow.xaml(.cs)      # Embed in /p preview HWND
│   └── SettingsWindow.xaml(.cs)     # Config dialog
└── FlightSaver.csproj
```

## Map tiles

Background tiles come from [CartoDB Basemaps](https://carto.com/basemaps/) (`dark_nolabels` / `light_nolabels`) which are free for non-commercial use with attribution. Tiles are cached on disk under `%LocalAppData%\FlightSaver\tiles\` and re-used across sessions; no labels are rendered to keep the map readable behind the radar.

## Licence

Personal hobby project. Use at your own risk.
