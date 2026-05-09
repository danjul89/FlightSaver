# FlightSaver

En Windows-skärmsläckare som visar flygplan ovanför dig i realtid via [OpenSky Network](https://opensky-network.org/).

Mörk radar-vy med kompass, avståndsringar, höjd-färgkodade flygplanikoner roterade efter heading, korta tonande spår, pulserande "du är här"-punkt i mitten, och info om närmaste plan.

## Funktioner

- Sätt position via adress (geokodas via Nominatim/OpenStreetMap) eller IP-detektering
- Justerbar radie 5-200 km
- Per-skärm rendering på multi-monitor setup
- 30 FPS smooth dead-reckoning mellan polls
- Höjd-färgkodning: röd <1000 m, gul 1000-6000 m, cyan >6000 m
- Närmaste plan får highlight + extra info (hastighet, vertical rate, ursprungsland)
- Cached extrapolering vid offline + diskret status-prick
- Adaptiv polling: 30 sek (registrerad + plugged in) → 5 min (anonym/batteri)
- OpenSky-credentials valfritt, lagras encrypted med Windows DPAPI

## Bygga

Kräver **.NET 8 SDK** på Windows. På WSL/Linux: kräver `EnableWindowsTargeting=true` (redan satt i `.csproj`) men du kan inte testa körningen från Linux — kopiera artefakten till Windows.

### Standalone single-file build

```bash
dotnet publish -c Release -r win-x64 \
  --self-contained \
  -p:PublishSingleFile=true
```

Resultat: `bin/Release/net8.0-windows/win-x64/publish/FlightSaver.exe` (~70 MB).

Döp om till `.scr`:

```bash
mv FlightSaver.exe FlightSaver.scr
```

## Installera

1. Kopiera `FlightSaver.scr` till Windows (om byggt på WSL/Linux)
2. Högerklicka → **Installera**
3. Windows öppnar Skärmsläckar-inställningarna med FlightSaver vald
4. Klicka **Inställningar...** → ange din adress → **Hämta koordinater** → **Spara**

Default-position är Sergels Torg (Stockholm) så skärmsläckaren funkar direkt även utan konfiguration.

## OpenSky-konto (valfritt)

Anonym åtkomst räcker för långsam uppdatering (~5 min). För 30 sek-uppdatering, registrera ett gratis konto på [opensky-network.org](https://opensky-network.org/) och fyll i användarnamn/lösenord under Inställningar → **Testa anslutning**.

Lösenordet lagras encrypted med Windows DPAPI (`%APPDATA%\FlightSaver\config.json`) och kan bara dekrypteras av samma Windows-användare på samma maskin.

## Avsluta skärmsläckaren

Mus-rörelse (>4 px), klick eller tangenttryckning avslutar.

## Filstruktur

```
FlightSaver/
├── App.xaml(.cs)              # Entry point, parses /s /p /c args
├── Models/
│   ├── Aircraft.cs            # Plane state
│   ├── AltitudeBand.cs        # Color mapping
│   └── Config.cs              # Persisted settings
├── Services/
│   ├── ConfigService.cs       # Load/save + DPAPI
│   ├── NominatimClient.cs     # Address geocoding + IP fallback
│   ├── OpenSkyClient.cs       # Bbox query + optional auth
│   └── FlightTracker.cs       # Polling loop + dead reckoning state
├── Rendering/
│   └── RadarCanvas.cs         # Custom WPF render
├── Views/
│   ├── ScreensaverWindow.xaml(.cs)  # Per-monitor fullscreen
│   ├── PreviewWindow.xaml(.cs)      # Embed in /p preview HWND
│   └── SettingsWindow.xaml(.cs)     # Config dialog
└── FlightSaver.csproj
```

## Licens

Privat hobbyprojekt. Använd på egen risk.
