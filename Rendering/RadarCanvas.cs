using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using FlightSaver.Models;
using FlightSaver.Services;

namespace FlightSaver.Rendering;

public sealed class RadarCanvas : FrameworkElement
{
    private readonly Config _config;
    private readonly FlightTracker _tracker;
    private readonly Dictionary<string, AircraftState> _state = new();
    private DateTime _lastFrameUtc = DateTime.UtcNow;
    private double _pulseSeconds;
    private string? _focusedId;
    private DateTime _lastFocusAdvanceUtc;

    private static readonly Brush LabelBrushDark = Frozen(new SolidColorBrush(Color.FromArgb(0xAA, 0xFF, 0xFF, 0xFF)));
    private static readonly Brush LabelDimBrushDark = Frozen(new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)));
    private static readonly Brush LabelBrushLight = Frozen(new SolidColorBrush(Color.FromArgb(0xDD, 0x10, 0x18, 0x28)));
    private static readonly Brush LabelDimBrushLight = Frozen(new SolidColorBrush(Color.FromArgb(0x88, 0x20, 0x28, 0x40)));
    private static readonly Brush CenterBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x34, 0xD3, 0x99)));
    private static readonly Typeface LabelFace = new("Segoe UI");

    private static readonly RadialGradientBrush BackgroundBrushDark = BuildBackground(Color.FromRgb(0x05, 0x08, 0x10), Color.FromRgb(0x0A, 0x14, 0x28));
    private static readonly RadialGradientBrush BackgroundBrushLight = BuildBackground(Color.FromRgb(0xFA, 0xFB, 0xFC), Color.FromRgb(0xE2, 0xE6, 0xEC));
    private static readonly Pen PlaneOutlinePenLight = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromArgb(0xCC, 0x0A, 0x14, 0x28))), 0.6));
    private static readonly Geometry PlaneGeometry = BuildPlaneGeometry();
    private static readonly Geometry HelicopterGeometry = BuildHelicopterGeometry();

    private static readonly string[] HelicopterCallsignPrefixes =
    {
        "POL", "POLITI", "POLIISI",
        "HEMS", "MEDIC", "LIFE", "RESCUE",
        "COAST", "USCG", "KYV",
    };

    private static bool IsHelicopterByCallsign(string? callsign)
    {
        if (string.IsNullOrWhiteSpace(callsign)) return false;
        var upper = callsign.Trim().ToUpperInvariant();
        foreach (var prefix in HelicopterCallsignPrefixes)
        {
            if (!upper.StartsWith(prefix, StringComparison.Ordinal)) continue;
            if (upper.Length > prefix.Length && char.IsDigit(upper[prefix.Length])) return true;
        }
        return false;
    }

    private static readonly Brush PanelBgDark = Frozen(new SolidColorBrush(Color.FromArgb(0xB8, 0x05, 0x0A, 0x18)));
    private static readonly Brush PanelBgLight = Frozen(new SolidColorBrush(Color.FromArgb(0xD0, 0xFA, 0xFB, 0xFD)));
    private static readonly Brush PanelBorderDark = Frozen(new SolidColorBrush(Color.FromArgb(0x44, 0xFF, 0xFF, 0xFF)));
    private static readonly Brush PanelBorderLight = Frozen(new SolidColorBrush(Color.FromArgb(0x44, 0x05, 0x0A, 0x18)));

    private bool IsLight => string.Equals(_config.MapTheme, "light", StringComparison.OrdinalIgnoreCase);
    private bool IsSatellite => string.Equals(_config.MapTheme, "satellite", StringComparison.OrdinalIgnoreCase);
    private Brush LabelBrush => IsLight ? LabelBrushLight : LabelBrushDark;
    private Brush LabelDimBrush => IsLight ? LabelDimBrushLight : LabelDimBrushDark;
    private Brush BackgroundBrush => IsLight ? BackgroundBrushLight : BackgroundBrushDark;
    private Brush PanelBgBrush => IsLight ? PanelBgLight : PanelBgDark;
    private Brush PanelBorderBrush => IsLight ? PanelBorderLight : PanelBorderDark;
    private Pen? PlaneOutlinePen => (IsLight || IsSatellite) ? PlaneOutlinePenLight : null;

    private static T Frozen<T>(T freezable) where T : System.Windows.Freezable
    {
        if (freezable.CanFreeze) freezable.Freeze();
        return freezable;
    }

    private static Geometry BuildPlaneGeometry()
    {
        var g = Geometry.Parse(
            "M 0,-7 L 0.8,-2 L 8,0.5 L 1.2,1.5 L 0.8,4 L 3,5.5 L 0.4,5.5 " +
            "L 0,6 L -0.4,5.5 L -3,5.5 L -0.8,4 L -1.2,1.5 L -8,0.5 L -0.8,-2 Z");
        g.Freeze();
        return g;
    }

    private static Geometry BuildHelicopterGeometry()
    {
        var g = Geometry.Parse(
            "M -2.5,-3 L -3,0 L -2.5,4 L 2.5,4 L 3,0 L 2.5,-3 Z " +
            "M -0.7,4 L 0.7,4 L 0.7,7.5 L -0.7,7.5 Z " +
            "M -2.8,7 L 2.8,7 L 2.8,8 L -2.8,8 Z");
        g.Freeze();
        return g;
    }

    private static RadialGradientBrush BuildBackground(Color inner, Color outer)
    {
        var b = new RadialGradientBrush
        {
            GradientOrigin = new Point(0.5, 0.5),
            Center = new Point(0.5, 0.5),
            RadiusX = 0.7,
            RadiusY = 0.7,
        };
        b.GradientStops.Add(new GradientStop(inner, 0.0));
        b.GradientStops.Add(new GradientStop(outer, 1.0));
        b.Freeze();
        return b;
    }

    private Color BandColor(Models.AltitudeBand band)
    {
        var c = band.ToColor();
        if (!IsLight) return c;
        return Color.FromArgb(c.A, (byte)(c.R * 0.55), (byte)(c.G * 0.55), (byte)(c.B * 0.55));
    }

    public RadarCanvas(Config config, FlightTracker tracker)
    {
        _config = config;
        _tracker = tracker;
        _tracker.AircraftUpdated += OnAircraftUpdated;
        _tracker.StatusChanged += (_, _) => InvalidateVisual();
        CompositionTarget.Rendering += OnRendering;
        Loaded += (_, _) => InvalidateVisual();
        Unloaded += (_, _) =>
        {
            CompositionTarget.Rendering -= OnRendering;
            _tracker.AircraftUpdated -= OnAircraftUpdated;
        };
    }

    private void OnAircraftUpdated(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            var nowUtc = DateTime.UtcNow;
            foreach (var ac in _tracker.CurrentAircraft)
            {
                if (!_state.TryGetValue(ac.Icao24, out var st))
                {
                    st = new AircraftState();
                    _state[ac.Icao24] = st;
                }
                var pt = LatLonToKm(ac.Latitude, ac.Longitude);
                st.SnapshotKm = pt;
                st.LastSnapshotUtc = nowUtc;
                st.Velocity = ac.VelocityMetersPerSec;
                st.HeadingRad = ac.TrueTrackDegrees * Math.PI / 180.0;
                st.AltitudeMeters = ac.AltitudeMeters;
                st.Callsign = ac.DisplayCallsign;
                st.OriginCountry = ac.OriginCountry;
                st.VerticalRate = ac.VerticalRateMetersPerSec;
                st.OnGround = ac.OnGround;
                st.Category = ac.Category;
                st.LastUpdateUtc = ac.LastUpdateUtc;
                st.BlendStartUtc = nowUtc;
                st.BlendFromKm = st.DisplayKm ?? pt;
                st.DisplayKm ??= pt;
                if (st.FirstSeenUtc == default) st.FirstSeenUtc = nowUtc;
            }

            var seenIds = _tracker.CurrentAircraft.Select(a => a.Icao24).ToHashSet();
            foreach (var key in _state.Keys.ToArray())
            {
                if (!seenIds.Contains(key) && (nowUtc - _state[key].LastUpdateUtc) > TimeSpan.FromSeconds(120))
                    _state.Remove(key);
            }
        });
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;
        var elapsed = (now - _lastFrameUtc).TotalSeconds;
        if (elapsed < 1.0 / 30.0) return;
        _pulseSeconds += elapsed;
        _lastFrameUtc = now;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        dc.DrawRectangle(BackgroundBrush, null, new Rect(0, 0, w, h));

        var center = new Point(w / 2, h / 2);
        var radiusPx = Math.Min(w, h) * 0.45;
        var pxPerKm = radiusPx / _config.RadiusKm;

        DrawMap(dc, w, h, center, pxPerKm);
        DrawCities(dc, w, h, center, pxPerKm);
        DrawCompass(dc, center, radiusPx);

        var nowUtc = DateTime.UtcNow;
        UpdateDisplayPositions(nowUtc);

        DrawTrails(dc, center, pxPerKm);

        var aircraftToDraw = _state
            .Where(kv => kv.Value.DisplayKm.HasValue && (nowUtc - kv.Value.LastUpdateUtc) < TimeSpan.FromSeconds(120))
            .ToList();

        var focusedId = DetermineFocusedId(aircraftToDraw, nowUtc);

        foreach (var (id, st) in aircraftToDraw)
            DrawPlane(dc, center, pxPerKm, st, isClosest: id == focusedId);

        DrawClosestRouteAirports(dc, w, h, center, pxPerKm, focusedId);
        DrawCenter(dc, center);
        DrawStatusIndicator(dc, w, h);
        DrawClosestInfoPanel(dc, focusedId);
        DrawTrackerLog(dc, w, h);
        DrawAttribution(dc, w, h);
        DrawUpdateHint(dc, w, h);
    }

    private static readonly Brush LogInfoBrush =
        Frozen(new SolidColorBrush(Color.FromArgb(0xAA, 0xCC, 0xCC, 0xCC)));
    private static readonly Brush LogSuccessBrush =
        Frozen(new SolidColorBrush(Color.FromArgb(0xCC, 0x34, 0xD3, 0x99)));
    private static readonly Brush LogWarningBrush =
        Frozen(new SolidColorBrush(Color.FromArgb(0xCC, 0xFC, 0xD3, 0x4D)));
    private static readonly Brush LogErrorBrush =
        Frozen(new SolidColorBrush(Color.FromArgb(0xDD, 0xFB, 0x71, 0x85)));
    private static readonly Brush LogTimeBrush =
        Frozen(new SolidColorBrush(Color.FromArgb(0x88, 0xCC, 0xCC, 0xCC)));
    private static readonly Brush LogShadowBrush =
        Frozen(new SolidColorBrush(Color.FromArgb(0xCC, 0x00, 0x00, 0x00)));

    private void DrawTrackerLog(DrawingContext dc, double w, double h)
    {
        if (!_config.ShowDebugLog) return;
        var entries = _tracker.RecentLog;
        if (entries.Count == 0) return;

        const double padding = 16;
        const double dotDiameter = 12;
        const double dotGap = 12;
        const double timeGap = 10;
        const double rowGap = 6;
        const double fontSize = 14;
        const double rightMargin = 16;
        const double topMargin = 16;

        var rows = new List<(FormattedText time, FormattedText msg, Brush color)>();
        double maxRowWidth = 0;
        double rowHeight = 0;
        foreach (var entry in entries)
        {
            var color = entry.Level switch
            {
                TrackerLogLevel.Success => LogSuccessBrush,
                TrackerLogLevel.Warning => LogWarningBrush,
                TrackerLogLevel.Error => LogErrorBrush,
                _ => LogInfoBrush,
            };
            var time = new FormattedText(
                entry.UtcTime.ToLocalTime().ToString("HH:mm:ss"),
                System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                LabelFace, fontSize, LogTimeBrush, 1.0);
            var msg = new FormattedText(
                entry.Message,
                System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                LabelFace, fontSize, color, 1.0);
            msg.SetFontWeight(FontWeights.SemiBold);

            rows.Add((time, msg, color));
            maxRowWidth = Math.Max(maxRowWidth, dotDiameter + dotGap + time.Width + timeGap + msg.Width);
            rowHeight = Math.Max(rowHeight, msg.Height);
        }

        double boxWidth = maxRowWidth + padding * 2;
        double boxHeight = padding * 2 + rows.Count * rowHeight + (rows.Count - 1) * rowGap;
        double boxX = w - rightMargin - boxWidth;
        double boxY = topMargin;

        var borderPen = new Pen(PanelBorderBrush, 1);
        borderPen.Freeze();
        dc.DrawRoundedRectangle(PanelBgBrush, borderPen, new Rect(boxX, boxY, boxWidth, boxHeight), 10, 10);

        double y = boxY + padding;
        foreach (var (time, msg, color) in rows)
        {
            var dotCenter = new Point(boxX + padding + dotDiameter / 2, y + rowHeight / 2);
            dc.DrawEllipse(color, null, dotCenter, dotDiameter / 2, dotDiameter / 2);

            var timeX = boxX + padding + dotDiameter + dotGap;
            var msgX = timeX + time.Width + timeGap;
            dc.DrawText(time, new Point(timeX, y));
            dc.DrawText(msg, new Point(msgX, y));

            y += rowHeight + rowGap;
        }
    }

    private string? DetermineFocusedId(List<KeyValuePair<string, AircraftState>> aircraftToDraw, DateTime nowUtc)
    {
        if (aircraftToDraw.Count == 0)
        {
            _focusedId = null;
            return null;
        }

        var mode = string.Equals(_config.FocusMode, "cycle", StringComparison.OrdinalIgnoreCase) ? "cycle" : "closest";
        if (mode != "cycle")
        {
            _focusedId = FindClosestId(aircraftToDraw);
            return _focusedId;
        }

        var sortedIds = aircraftToDraw
            .OrderBy(kv => kv.Value.SnapshotKm.X * kv.Value.SnapshotKm.X + kv.Value.SnapshotKm.Y * kv.Value.SnapshotKm.Y)
            .Select(kv => kv.Key)
            .ToList();

        if (string.IsNullOrEmpty(_focusedId) || !sortedIds.Contains(_focusedId))
        {
            _focusedId = sortedIds[0];
            _lastFocusAdvanceUtc = nowUtc;
            return _focusedId;
        }

        var intervalSec = Math.Max(2, _config.CycleIntervalSeconds);
        if ((nowUtc - _lastFocusAdvanceUtc).TotalSeconds >= intervalSec)
        {
            var currentIdx = sortedIds.IndexOf(_focusedId);
            var nextIdx = (currentIdx + 1) % sortedIds.Count;
            _focusedId = sortedIds[nextIdx];
            _lastFocusAdvanceUtc = nowUtc;
        }

        return _focusedId;
    }

    private static readonly Brush UpdateHintBrush =
        Frozen(new SolidColorBrush(Color.FromArgb(0xCC, 0x34, 0xD3, 0x99)));

    private void DrawUpdateHint(DrawingContext dc, double w, double h)
    {
        if (!UpdateService.Instance.IsUpdateAvailable) return;
        var v = UpdateService.Instance.LatestVersion;
        if (string.IsNullOrEmpty(v)) return;
        var ft = new FormattedText(
            $"v{v} available — open Settings to update",
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            LabelFace, 11, UpdateHintBrush, 1.0);
        dc.DrawText(ft, new Point(20, h - ft.Height - 6));
    }

    private void DrawClosestRouteAirports(DrawingContext dc, double w, double h, Point center, double pxPerKm, string? closestId)
    {
        if (closestId is null) return;
        if (!_state.TryGetValue(closestId, out var st)) return;
        var route = RouteService.Instance.TryGet(st.Callsign);
        if (route is null) return;

        if (route.Origin is { } o) DrawAirportMarker(dc, w, h, center, pxPerKm, o, isOrigin: true);
        if (route.Destination is { } d) DrawAirportMarker(dc, w, h, center, pxPerKm, d, isOrigin: false);
    }

    private void DrawAirportMarker(DrawingContext dc, double w, double h, Point center, double pxPerKm, AirportInfo a, bool isOrigin)
    {
        if (a.Latitude == 0 && a.Longitude == 0) return;
        var km = LatLonToKm(a.Latitude, a.Longitude);
        var p = KmToPoint(center, pxPerKm, km);
        if (p.X < -8 || p.X > w + 8 || p.Y < -8 || p.Y > h + 8) return;

        var fill = isOrigin
            ? Color.FromArgb(0xCC, 0x52, 0xC4, 0x1A)
            : Color.FromArgb(0xCC, 0xF9, 0xA8, 0x25);
        var fillBrush = new SolidColorBrush(fill);
        fillBrush.Freeze();

        var ringBrush = new SolidColorBrush(IsLight ? Color.FromArgb(0xFF, 0x05, 0x0A, 0x18) : Colors.White);
        ringBrush.Freeze();
        var ringPen = new Pen(ringBrush, 1.0);
        ringPen.Freeze();

        dc.DrawRectangle(fillBrush, ringPen, new Rect(p.X - 5, p.Y - 5, 10, 10));

        var label = string.IsNullOrEmpty(a.IataCode) ? a.IcaoCode : a.IataCode;
        if (string.IsNullOrEmpty(label)) return;
        var prefix = isOrigin ? "↑ " : "↓ ";
        var ft = new FormattedText(
            prefix + label,
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            LabelFace, 11, LabelBrush, 1.0);
        ft.SetFontWeight(FontWeights.SemiBold);
        dc.DrawText(ft, new Point(p.X + 8, p.Y - ft.Height / 2));
    }

    private void DrawMap(DrawingContext dc, double w, double h, Point center, double pxPerKm)
    {
        if (pxPerKm <= 0) return;
        double lat = _config.Latitude;
        double lon = _config.Longitude;

        const double EarthCircM = 40075016.686;
        double mPerPxAt0 = EarthCircM * Math.Cos(lat * Math.PI / 180.0) / 256.0;
        double targetMPerPx = 1000.0 / pxPerKm;
        double zReal = Math.Log2(mPerPxAt0 / targetMPerPx);
        int z = (int)Math.Clamp(Math.Round(zReal), 1, 18);

        double mPerPxAtZ = mPerPxAt0 / Math.Pow(2, z);
        double tilePxPerKm = 1000.0 / mPerPxAtZ;
        double tileScale = pxPerKm / tilePxPerKm;
        double scaledTilePx = 256.0 * tileScale;

        double n = Math.Pow(2, z);
        double worldCx = n * (lon + 180.0) / 360.0;
        double latRad = lat * Math.PI / 180.0;
        double worldCy = n * (1.0 - Math.Asinh(Math.Tan(latRad)) / Math.PI) / 2.0;

        int centerTx = (int)Math.Floor(worldCx);
        int centerTy = (int)Math.Floor(worldCy);
        double offX = (worldCx - centerTx) * 256.0;
        double offY = (worldCy - centerTy) * 256.0;

        double centerTileScreenX = center.X - offX * tileScale;
        double centerTileScreenY = center.Y - offY * tileScale;

        int left = (int)Math.Ceiling(centerTileScreenX / scaledTilePx);
        int right = (int)Math.Ceiling((w - (centerTileScreenX + scaledTilePx)) / scaledTilePx) + 1;
        int up = (int)Math.Ceiling(centerTileScreenY / scaledTilePx);
        int down = (int)Math.Ceiling((h - (centerTileScreenY + scaledTilePx)) / scaledTilePx) + 1;

        int maxTile = (int)n;

        for (int dy = -up; dy <= down; dy++)
        {
            int ty = centerTy + dy;
            if (ty < 0 || ty >= maxTile) continue;
            for (int dx = -left; dx <= right; dx++)
            {
                int tx = ((centerTx + dx) % maxTile + maxTile) % maxTile;
                var tile = TileCache.Instance.TryGet(z, tx, ty);
                if (tile is null) continue;
                double sx = centerTileScreenX + dx * scaledTilePx;
                double sy = centerTileScreenY + dy * scaledTilePx;
                dc.DrawImage(tile, new Rect(sx, sy, scaledTilePx, scaledTilePx));
            }
        }
    }

    private void DrawAttribution(DrawingContext dc, double w, double h)
    {
        var text = IsSatellite
            ? "© Esri, Maxar, Earthstar Geographics"
            : "© OpenStreetMap © CARTO";
        var ft = new FormattedText(
            text,
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            LabelFace, 11, LabelDimBrush, 1.0);
        dc.DrawText(ft, new Point(w - ft.Width - 28, h - ft.Height - 6));
    }

    private static readonly Brush CityDotBrushDark =
        Frozen(new SolidColorBrush(Color.FromArgb(0xEE, 0xFF, 0xFF, 0xFF)));
    private static readonly Brush CityDotBrushLight =
        Frozen(new SolidColorBrush(Color.FromArgb(0xDD, 0x10, 0x18, 0x28)));
    private static readonly Brush CityDotStrokeSatellite =
        Frozen(new SolidColorBrush(Color.FromArgb(0xFF, 0x00, 0x00, 0x00)));
    private static readonly Brush CityHaloBrush =
        Frozen(new SolidColorBrush(Color.FromArgb(0xFF, 0x00, 0x00, 0x00)));
    private static readonly Brush CityLabelBrushLight =
        Frozen(new SolidColorBrush(Color.FromArgb(0xEE, 0x10, 0x18, 0x28)));
    private static readonly Brush CityLabelBrushDark =
        Frozen(new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF)));

    private void DrawCities(DrawingContext dc, double w, double h, Point center, double pxPerKm)
    {
        var dotFill = IsLight ? CityDotBrushLight : CityDotBrushDark;
        var labelFill = IsLight ? CityLabelBrushLight : CityLabelBrushDark;
        var needsHalo = IsSatellite;

        Pen? dotStroke = null;
        if (needsHalo)
        {
            var p = new Pen(CityDotStrokeSatellite, 1.5);
            p.Freeze();
            dotStroke = p;
        }

        var fontSize = needsHalo ? 12.0 : 11.0;

        Pen? haloPen = null;
        if (needsHalo)
        {
            var p = new Pen(CityHaloBrush, 4) { LineJoin = PenLineJoin.Round, MiterLimit = 2 };
            p.Freeze();
            haloPen = p;
        }

        foreach (var city in CityService.Cities)
        {
            var km = LatLonToKm(city.Latitude, city.Longitude);
            var p = KmToPoint(center, pxPerKm, km);
            if (p.X < 0 || p.X > w || p.Y < 0 || p.Y > h) continue;

            dc.DrawEllipse(dotFill, dotStroke, p, 3.5, 3.5);

            var ft = new FormattedText(
                city.Name,
                System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                LabelFace, fontSize, labelFill, 1.0);
            ft.SetFontWeight(FontWeights.SemiBold);

            var labelPos = new Point(p.X + 7, p.Y - ft.Height / 2);
            if (needsHalo && haloPen is not null)
            {
                var geometry = ft.BuildGeometry(labelPos);
                dc.DrawGeometry(null, haloPen, geometry);
                dc.DrawGeometry(labelFill, null, geometry);
            }
            else
            {
                dc.DrawText(ft, labelPos);
            }
        }
    }

    private void UpdateDisplayPositions(DateTime nowUtc)
    {
        const double blendSeconds = 0.5;
        const double trailIntervalSec = 1.0;
        foreach (var st in _state.Values)
        {
            if (!st.DisplayKm.HasValue) continue;
            if ((nowUtc - st.LastUpdateUtc) > TimeSpan.FromSeconds(120)) continue;

            var sinceSnapshot = (nowUtc - st.LastSnapshotUtc).TotalSeconds;
            if (sinceSnapshot < 0) sinceSnapshot = 0;

            double headingX = Math.Sin(st.HeadingRad);
            double headingY = Math.Cos(st.HeadingRad);
            double dxKm = st.Velocity * sinceSnapshot * headingX / 1000.0;
            double dyKm = st.Velocity * sinceSnapshot * headingY / 1000.0;
            var extrapolated = new Point(st.SnapshotKm.X + dxKm, st.SnapshotKm.Y + dyKm);

            var sinceBlend = (nowUtc - st.BlendStartUtc).TotalSeconds;
            if (sinceBlend < blendSeconds)
            {
                var t = Ease(sinceBlend / blendSeconds);
                st.DisplayKm = new Point(
                    st.BlendFromKm.X + (extrapolated.X - st.BlendFromKm.X) * t,
                    st.BlendFromKm.Y + (extrapolated.Y - st.BlendFromKm.Y) * t);
            }
            else
            {
                st.DisplayKm = extrapolated;
            }

            if ((nowUtc - st.LastTrailUtc).TotalSeconds >= trailIntervalSec)
            {
                st.Trail.Add((nowUtc, st.DisplayKm.Value));
                st.LastTrailUtc = nowUtc;
                while (st.Trail.Count > 0 && (nowUtc - st.Trail[0].time).TotalSeconds > TrailMaxAgeSeconds)
                    st.Trail.RemoveAt(0);
            }
        }
    }

    private static double Ease(double t)
    {
        t = Math.Clamp(t, 0, 1);
        return 1 - Math.Pow(1 - t, 3);
    }

    private void DrawCompass(DrawingContext dc, Point center, double radiusPx)
    {
        var n = new FormattedText(
            "N", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            LabelFace, 18, LabelBrush, 1.0);
        n.SetFontWeight(FontWeights.SemiBold);
        dc.DrawText(n, new Point(center.X - n.Width / 2, center.Y - radiusPx - n.Height - 4));

        foreach (var (label, angleRad) in new[] { ("E", Math.PI / 2), ("S", Math.PI), ("W", 3 * Math.PI / 2) })
        {
            var ft = new FormattedText(
                label, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                LabelFace, 14, LabelDimBrush, 1.0);
            var x = center.X + Math.Sin(angleRad) * (radiusPx + 18);
            var y = center.Y - Math.Cos(angleRad) * (radiusPx + 18);
            dc.DrawText(ft, new Point(x - ft.Width / 2, y - ft.Height / 2));
        }
    }

    private const double TrailMaxAgeSeconds = 300.0;
    private const byte TrailMaxAlpha = 150;

    private void DrawTrails(DrawingContext dc, Point center, double pxPerKm)
    {
        var nowUtc = DateTime.UtcNow;
        foreach (var st in _state.Values)
        {
            if (st.Trail.Count < 2) continue;
            var color = BandColor(st.Band);
            for (int i = st.Trail.Count - 1; i >= 1; i--)
            {
                var (tThen, ptThen) = st.Trail[i];
                var (_, ptPrev) = st.Trail[i - 1];
                var ageSec = (nowUtc - tThen).TotalSeconds;
                if (ageSec > TrailMaxAgeSeconds) break;
                var alpha = (byte)Math.Clamp(TrailMaxAlpha * (1 - ageSec / TrailMaxAgeSeconds), 0, TrailMaxAlpha);
                var brush = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
                brush.Freeze();
                var pen = new Pen(brush, 1.5);
                pen.Freeze();
                dc.DrawLine(pen, KmToPoint(center, pxPerKm, ptPrev), KmToPoint(center, pxPerKm, ptThen));
            }
            while (st.Trail.Count > 0 && (nowUtc - st.Trail[0].time).TotalSeconds > TrailMaxAgeSeconds)
                st.Trail.RemoveAt(0);
        }
    }

    private void DrawPlane(DrawingContext dc, Point center, double pxPerKm, AircraftState st, bool isClosest)
    {
        if (!st.DisplayKm.HasValue) return;
        var pos = KmToPoint(center, pxPerKm, st.DisplayKm.Value);
        if (Math.Abs(pos.X - center.X) > center.X || Math.Abs(pos.Y - center.Y) > center.Y) return;

        var color = BandColor(st.Band);
        var brush = new SolidColorBrush(color);
        brush.Freeze();

        var scale = Math.Clamp(1.25 + (200.0 - _config.RadiusKm) / 400.0, 1.25, 1.75);

        if (isClosest)
        {
            var glowBrush = new SolidColorBrush(Color.FromArgb(0x55, color.R, color.G, color.B));
            glowBrush.Freeze();
            dc.DrawEllipse(glowBrush, null, pos, 18 * scale, 18 * scale);
            var ringBrush = new SolidColorBrush(Color.FromArgb(0xCC, color.R, color.G, color.B));
            ringBrush.Freeze();
            var ringPen = new Pen(ringBrush, 1.5);
            ringPen.Freeze();
            dc.DrawEllipse(null, ringPen, pos, 14 * scale, 14 * scale);
        }

        dc.PushTransform(new TranslateTransform(pos.X, pos.Y));
        dc.PushTransform(new RotateTransform(st.HeadingRad * 180.0 / Math.PI));
        dc.PushTransform(new ScaleTransform(scale, scale));

        var isHelicopter = st.Category == 8 || IsHelicopterByCallsign(st.Callsign);
        if (isHelicopter)
        {
            var rotorBrush = new SolidColorBrush(Color.FromArgb(0x33, color.R, color.G, color.B));
            rotorBrush.Freeze();
            dc.DrawEllipse(rotorBrush, null, new Point(0, 0.5), 8.5, 8.5);
            dc.DrawGeometry(brush, PlaneOutlinePen, HelicopterGeometry);
        }
        else
        {
            dc.DrawGeometry(brush, PlaneOutlinePen, PlaneGeometry);
        }

        dc.Pop();
        dc.Pop();
        dc.Pop();

        var label = new FormattedText(
            $"{st.Callsign}\n{(int)st.AltitudeMeters} m",
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            LabelFace,
            12,
            LabelBrush,
            1.0);
        dc.DrawText(label, new Point(pos.X + 10, pos.Y - 8));
    }

    private void DrawCenter(DrawingContext dc, Point center)
    {
        var pulse = (Math.Sin(_pulseSeconds * Math.PI) + 1) / 2;
        var outerR = 6 + pulse * 6;
        var outerAlpha = (byte)(120 * (1 - pulse));
        var outerBrush = new SolidColorBrush(Color.FromArgb(outerAlpha, 0x34, 0xD3, 0x99));
        outerBrush.Freeze();
        dc.DrawEllipse(outerBrush, null, center, outerR, outerR);
        dc.DrawEllipse(CenterBrush, null, center, 3.5, 3.5);
    }

    private void DrawStatusIndicator(DrawingContext dc, double w, double h)
    {
        var color = _tracker.Status switch
        {
            ConnectionStatus.Online => Color.FromRgb(0x34, 0xD3, 0x99),
            ConnectionStatus.Connecting => Color.FromRgb(0xFC, 0xD3, 0x4D),
            ConnectionStatus.Retrying => Color.FromRgb(0xFC, 0xD3, 0x4D),
            ConnectionStatus.Offline => Color.FromRgb(0xFB, 0x71, 0x85),
            _ => Color.FromRgb(0xAA, 0xAA, 0xAA),
        };
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        dc.DrawEllipse(brush, null, new Point(w - 18, h - 18), 4, 4);
    }

    private void DrawClosestInfoPanel(DrawingContext dc, string? closestId)
    {
        if (closestId is null) return;
        if (!_state.TryGetValue(closestId, out var st)) return;

        var distKm = Math.Sqrt(st.SnapshotKm.X * st.SnapshotKm.X + st.SnapshotKm.Y * st.SnapshotKm.Y);
        var bearingDeg = (Math.Atan2(st.SnapshotKm.X, st.SnapshotKm.Y) * 180 / Math.PI + 360) % 360;
        var compass = BearingToCompass(bearingDeg);
        var velocityKnots = st.Velocity * 1.943844;
        var velocityKmh = st.Velocity * 3.6;
        var vRateArrow = st.VerticalRate > 0.5 ? "↗" : st.VerticalRate < -0.5 ? "↘" : "→";

        var route = RouteService.Instance.TryGet(st.Callsign);
        static string FormatAirport(AirportInfo? a)
        {
            if (a is null) return "—";
            var location = string.Join(", ", new[] { a.Municipality, a.Country }
                .Where(s => !string.IsNullOrEmpty(s)));
            var code = !string.IsNullOrEmpty(a.IataCode) ? a.IataCode : a.IcaoCode;
            if (!string.IsNullOrEmpty(location) && !string.IsNullOrEmpty(code))
                return $"{location} ({code})";
            if (!string.IsNullOrEmpty(location)) return location;
            if (!string.IsNullOrEmpty(code)) return code;
            return "—";
        }

        var rows = new (string Label, string Value)[]
        {
            ("Altitude", $"{st.AltitudeMeters:N0} m  {vRateArrow} {Math.Abs(st.VerticalRate):F0} m/s"),
            ("Speed", $"{velocityKnots:F0} kts ({velocityKmh:F0} km/h)"),
            ("Bearing", $"{distKm:F1} km {compass} of you"),
            ("Country", string.IsNullOrWhiteSpace(st.OriginCountry) ? "—" : st.OriginCountry!),
            ("Operator", string.IsNullOrWhiteSpace(route?.AirlineName) ? "—" : route!.AirlineName!),
            ("From", FormatAirport(route?.Origin)),
            ("To", FormatAirport(route?.Destination)),
        };

        var photo = PhotoCache.Instance.TryGet(closestId);

        const double boxLeft = 16;
        const double boxTop = 16;
        const double padding = 16;
        const double contentWidth = 320;
        const double boxWidth = contentWidth + padding * 2;
        const double labelColumnWidth = 90;
        const double headerSize = 20;
        const double rowSize = 14;
        const double creditSize = 11;
        const double rowGap = 22;
        const double sepGap = 12;

        double scaledPhotoWidth = 0;
        double scaledPhotoHeight = 0;
        if (photo != null && photo.Image.PixelWidth > 0 && photo.Image.PixelHeight > 0)
        {
            scaledPhotoWidth = contentWidth;
            var ratio = photo.Image.PixelHeight / (double)photo.Image.PixelWidth;
            scaledPhotoHeight = Math.Min(scaledPhotoWidth * ratio, 220);
        }

        double bodyHeight = headerSize + 6 + 1 + sepGap + rows.Length * rowGap;
        double photoBlock = photo != null ? sepGap + scaledPhotoHeight + 4 + creditSize + 4 : 0;
        double boxHeight = padding + bodyHeight + photoBlock + padding;

        var borderPen = new Pen(PanelBorderBrush, 1);
        borderPen.Freeze();
        dc.DrawRoundedRectangle(PanelBgBrush, borderPen, new Rect(boxLeft, boxTop, boxWidth, boxHeight), 10, 10);

        double y = boxTop + padding;
        double xLabel = boxLeft + padding;
        double xValue = xLabel + labelColumnWidth;

        var header = new FormattedText(
            string.IsNullOrWhiteSpace(st.Callsign) ? "—" : st.Callsign!,
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            LabelFace, headerSize, LabelBrush, 1.0);
        header.SetFontWeight(FontWeights.SemiBold);
        dc.DrawText(header, new Point(xLabel, y));
        y += headerSize + 6;

        var sepPen = new Pen(PanelBorderBrush, 1);
        sepPen.Freeze();
        dc.DrawLine(sepPen, new Point(xLabel, y), new Point(boxLeft + boxWidth - padding, y));
        y += sepGap;

        foreach (var (label, value) in rows)
        {
            var lt = new FormattedText(
                label, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                LabelFace, rowSize, LabelDimBrush, 1.0);
            var vt = new FormattedText(
                value, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                LabelFace, rowSize, LabelBrush, 1.0);
            dc.DrawText(lt, new Point(xLabel, y));
            dc.DrawText(vt, new Point(xValue, y));
            y += rowGap;
        }

        if (photo != null && scaledPhotoHeight > 0)
        {
            y += sepGap - 4;
            dc.DrawImage(photo.Image, new Rect(xLabel, y, scaledPhotoWidth, scaledPhotoHeight));
            y += scaledPhotoHeight + 4;
            var creditText = string.IsNullOrEmpty(photo.Photographer)
                ? "© Planespotters.net"
                : $"© {photo.Photographer} / Planespotters.net";
            var ct = new FormattedText(
                creditText, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                LabelFace, creditSize, LabelDimBrush, 1.0);
            dc.DrawText(ct, new Point(xLabel, y));
        }
    }

    private static string BearingToCompass(double deg)
    {
        string[] dirs = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
        int ix = (int)Math.Round(deg / 45.0) % 8;
        return dirs[ix];
    }

    private string? FindClosestId(List<KeyValuePair<string, AircraftState>> list)
    {
        string? bestId = null;
        double bestDist = double.MaxValue;
        foreach (var (id, st) in list)
        {
            var d = st.SnapshotKm.X * st.SnapshotKm.X + st.SnapshotKm.Y * st.SnapshotKm.Y;
            if (d < bestDist) { bestDist = d; bestId = id; }
        }
        return bestId;
    }

    private Point LatLonToKm(double lat, double lon)
    {
        var dLatKm = (lat - _config.Latitude) * 111.0;
        var dLonKm = (lon - _config.Longitude) * 111.0 * Math.Cos(_config.Latitude * Math.PI / 180.0);
        return new Point(dLonKm, dLatKm);
    }

    private static Point KmToPoint(Point center, double pxPerKm, Point km) =>
        new(center.X + km.X * pxPerKm, center.Y - km.Y * pxPerKm);

    private sealed class AircraftState
    {
        public Point SnapshotKm;
        public Point BlendFromKm;
        public Point? DisplayKm;
        public DateTime LastSnapshotUtc;
        public DateTime BlendStartUtc;
        public DateTime FirstSeenUtc;
        public DateTime LastUpdateUtc;
        public DateTime LastTrailUtc;
        public double Velocity;
        public double HeadingRad;
        public double AltitudeMeters;
        public double VerticalRate;
        public bool OnGround;
        public int Category;
        public string? Callsign;
        public string? OriginCountry;
        public AltitudeBand Band => AltitudeBands.Classify(AltitudeMeters);
        public List<(DateTime time, Point km)> Trail { get; } = new();
    }
}
