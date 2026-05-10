# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What This Is

**FlightSaver** is a Windows desktop screensaver (.scr) built with C# 12 / .NET 8 / WPF. It displays real-time aircraft positions on an interactive map by polling the OpenSky Network ADS-B API, and is installed as a standard Windows screensaver.

## Build & Run

```powershell
# Development run (full-screen screensaver mode)
dotnet run -- /s

# Open settings dialog
dotnet run -- /c

# Publish standalone single-file release (~186 MB, self-contained)
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
# Output: bin/Release/net8.0-windows10.0.17763.0/win-x64/publish/FlightSaver.exe
# Rename to .scr to install as a screensaver
```

**Screensaver argument modes:**
- `/s` or `/S` — full-screen display
- `/p <HWND>` — preview embedded in Windows Settings dialog (P/Invoke reparent)
- `/c` or `/C` — configuration/settings dialog

**No test project or lint tooling** — the codebase relies on `nullable` reference types and standard C# compiler checks.

## Architecture

### Entry Point
`App.xaml.cs` parses the screensaver argument, loads `Config` via `ConfigService`, starts the `FlightTracker` polling loop, and creates per-monitor `ScreensaverWindow` instances.

### Data Flow
```
App.xaml.cs
  → ConfigService   (load/save %APPDATA%\FlightSaver\config.json, DPAPI-encrypted credentials)
  → FlightTracker   (adaptive polling loop: 30s registered+plugged-in, 5min anonymous/battery)
      → OpenSkyClient    (/api/states/all bounding box query)
      → dead-reckoning   (position extrapolation between polls)
  → RadarCanvas     (30 FPS WPF FrameworkElement via CompositionTarget.Rendering)
      → TileCache        (map tiles: CartoDB dark/light or ArcGIS satellite)
      → CityService      (city markers from embedded Resources/cities.json)
      → RouteService     (origin/destination airport lookup)
      → PhotoCache       (aircraft photos — cached but not yet wired to UI)
```

### Key Design Decisions

- **RadarCanvas** (`Rendering/RadarCanvas.cs`) is a single `FrameworkElement` that handles all drawing with `DrawingContext` — no WPF controls inside it. The aircraft positions are dead-reckoned at 30 FPS between API polls.

- **Adaptive polling** in `FlightTracker.cs`: interval shortens for authenticated registered users on AC power, lengthens on battery or anonymous to respect OpenSky rate limits.

- **Helicopter detection** uses two signals: the ADS-B `category` field ("B2" = helicopter) from the OpenSky state array, plus a callsign-prefix allowlist (POLICE, HEMS, RESCUE, COAST GUARD, etc.) for aircraft where category is absent.

- **Altitude bands** drive icon color: red < 1000 m, yellow 1000–6000 m, cyan > 6000 m (see `Models/AltitudeBand.cs`).

- **Multi-monitor**: one `ScreensaverWindow` per screen, positioned with DPI-aware bounds. Mouse movement > 4 px on any window exits all windows.

- **Config encryption**: OpenSky password stored with `System.Security.Cryptography.ProtectedData` (Windows DPAPI, current-user scope).

- **Tile caching**: disk tiles stored at `%LocalAppData%\FlightSaver\tiles\`; LRU eviction managed by `CacheManager.cs` with a user-configurable size cap.

- **Updates**: `UpdateService.cs` checks the GitHub Releases API, downloads the `.scr`, and spawns an elevated PowerShell installer.

- **Aircraft type** is fetched alongside route data from the adsbdb callsign API and displayed in the info panel as `"{manufacturer} {icao_type}"` (e.g., "Boeing B738"). Stored as `FlightRoute.AircraftType` in `RouteService.cs`.

- **Focus modes** (`Config.FocusMode`): `"closest"` always features the nearest aircraft; `"cycle"` zooms to a 2 km radius centered on each aircraft in turn; `"plain"` shows no map — only a centered panel listing the three nearest aircraft with callsign, type, altitude, speed, bearing, and route.

- **Cycle zoom**: when `FocusMode == "cycle"`, `RadarCanvas.OnRender` computes a `viewCenterKm` offset and passes it through every drawing method. `KmToPoint` subtracts `viewKm` so all positions render relative to the zoom center. `pxPerKm` becomes `radiusPx / 2.0` for the 2 km radius.

### Module Map

| Path | Responsibility |
|------|----------------|
| `App.xaml.cs` | Startup, mode dispatch, lifecycle |
| `Models/` | `Aircraft`, `Config`, `AltitudeBand` — pure data |
| `Services/OpenSkyClient.cs` | HTTP + JSON parsing for OpenSky `/api/states/all` |
| `Services/FlightTracker.cs` | Polling loop, dead-reckoning, backoff |
| `Services/TileCache.cs` | Map tile download & memory+disk cache |
| `Services/ConfigService.cs` | Config persistence + DPAPI credential encryption |
| `Services/NominatimClient.cs` | Geocoding (address→coords) + IP-geolocation on first run |
| `Services/RouteService.cs` | Airport route lookup |
| `Services/CityService.cs` | Loads embedded `Resources/cities.json` |
| `Services/CacheManager.cs` | Disk cache LRU eviction |
| `Services/UpdateService.cs` | GitHub release check + installer |
| `Rendering/RadarCanvas.cs` | All visual output — map, trails, icons, overlays |
| `Views/ScreensaverWindow.xaml.cs` | Full-screen per-monitor window |
| `Views/SettingsWindow.xaml.cs` | Configuration dialog |
| `Views/PreviewWindow.xaml.cs` | Preview pane (P/Invoke child-window embed) |

## CI/CD

`.github/workflows/release.yml` publishes on `v*.*.*` tags: runs `dotnet publish`, renames `.exe` → `.scr`, and creates a GitHub release with the `.scr` as an asset.
