// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

using System.Collections.Concurrent;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Cyberdrome.Core.Geo;

namespace Cyberdrome.App.Views;

/// <summary>
/// Enkel karta med kartrutor (standard OpenStreetMap). Dra för att flytta, scrolla för att zooma,
/// klicka för att välja en punkt. Visar bilen (med riktning) och en rutt.
/// Rutorna sparas i %LocalAppData%\Cyberdrome\tiles så att samma ruta bara hämtas en gång.
/// </summary>
public sealed class OsmMapControl : Control
{
    private const int TileSize = 256;
    private static readonly HttpClient Http = CreateHttp();
    private static readonly ConcurrentDictionary<string, Bitmap?> Memory = new();
    private static readonly ConcurrentDictionary<string, byte> Loading = new();

    public static readonly StyledProperty<GeoPoint> CarPositionProperty =
        AvaloniaProperty.Register<OsmMapControl, GeoPoint>(nameof(CarPosition), GpsSimulator.DefaultPosition);
    public static readonly StyledProperty<double> CarBearingProperty =
        AvaloniaProperty.Register<OsmMapControl, double>(nameof(CarBearing));
    public static readonly StyledProperty<IReadOnlyList<GeoPoint>?> RouteProperty =
        AvaloniaProperty.Register<OsmMapControl, IReadOnlyList<GeoPoint>?>(nameof(Route));
    public static readonly StyledProperty<bool> FollowCarProperty =
        AvaloniaProperty.Register<OsmMapControl, bool>(nameof(FollowCar), true);
    public static readonly StyledProperty<string> TileUrlProperty =
        AvaloniaProperty.Register<OsmMapControl, string>(nameof(TileUrl), "https://tile.openstreetmap.org/{z}/{x}/{y}.png");

    static OsmMapControl()
    {
        AffectsRender<OsmMapControl>(CarPositionProperty, CarBearingProperty, RouteProperty, TileUrlProperty);
        ClipToBoundsProperty.OverrideDefaultValue<OsmMapControl>(true);
        FocusableProperty.OverrideDefaultValue<OsmMapControl>(true);
    }

    public GeoPoint CarPosition { get => GetValue(CarPositionProperty); set => SetValue(CarPositionProperty, value); }
    public double CarBearing { get => GetValue(CarBearingProperty); set => SetValue(CarBearingProperty, value); }
    public IReadOnlyList<GeoPoint>? Route { get => GetValue(RouteProperty); set => SetValue(RouteProperty, value); }
    public bool FollowCar { get => GetValue(FollowCarProperty); set => SetValue(FollowCarProperty, value); }
    public string TileUrl { get => GetValue(TileUrlProperty); set => SetValue(TileUrlProperty, value); }

    /// <summary>Användaren klickade på kartan (utan att dra).</summary>
    public event Action<GeoPoint>? MapClicked;

    private GeoPoint _center = GpsSimulator.DefaultPosition;
    private int _zoom = 15;
    private Point? _pressAt;
    private Point _lastDrag;
    private bool _dragged;

    public int Zoom
    {
        get => _zoom;
        set { _zoom = Math.Clamp(value, 3, 19); InvalidateVisual(); }
    }

    public void CenterOn(GeoPoint p) { _center = p; InvalidateVisual(); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == CarPositionProperty && FollowCar) _center = CarPosition;
        if (change.Property == FollowCarProperty && FollowCar) { _center = CarPosition; InvalidateVisual(); }
    }

    // ---------------- Web Mercator ----------------

    private static double WorldSize(int z) => TileSize * Math.Pow(2, z);

    private static Point ToWorld(GeoPoint p, int z)
    {
        double s = WorldSize(z);
        double lat = Math.Clamp(p.Lat, -85.05112878, 85.05112878) * Math.PI / 180;
        double x = (p.Lon + 180) / 360 * s;
        double y = (1 - Math.Log(Math.Tan(lat) + 1 / Math.Cos(lat)) / Math.PI) / 2 * s;
        return new Point(x, y);
    }

    private static GeoPoint FromWorld(Point w, int z)
    {
        double s = WorldSize(z);
        double lon = w.X / s * 360 - 180;
        double n = Math.PI - 2 * Math.PI * w.Y / s;
        double lat = 180 / Math.PI * Math.Atan(Math.Sinh(n));
        return new GeoPoint(lat, lon);
    }

    private Point ScreenFor(GeoPoint p)
    {
        var c = ToWorld(_center, _zoom);
        var w = ToWorld(p, _zoom);
        return new Point(w.X - c.X + Bounds.Width / 2, w.Y - c.Y + Bounds.Height / 2);
    }

    private GeoPoint GeoFor(Point screen)
    {
        var c = ToWorld(_center, _zoom);
        return FromWorld(new Point(c.X + screen.X - Bounds.Width / 2, c.Y + screen.Y - Bounds.Height / 2), _zoom);
    }

    // ---------------- Rita ----------------

    public override void Render(DrawingContext ctx)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        ctx.FillRectangle(new SolidColorBrush(Color.FromRgb(229, 227, 223)), new Rect(0, 0, w, h));

        var c = ToWorld(_center, _zoom);
        double left = c.X - w / 2, top = c.Y - h / 2;
        int n = 1 << _zoom;
        int x0 = (int)Math.Floor(left / TileSize), x1 = (int)Math.Floor((left + w) / TileSize);
        int y0 = Math.Max(0, (int)Math.Floor(top / TileSize)), y1 = Math.Min(n - 1, (int)Math.Floor((top + h) / TileSize));
        for (int ty = y0; ty <= y1; ty++)
        for (int tx = x0; tx <= x1; tx++)
        {
            int wx = ((tx % n) + n) % n;
            var bmp = GetTile(_zoom, wx, ty);
            var dest = new Rect(tx * TileSize - left, ty * TileSize - top, TileSize, TileSize);
            if (bmp is not null) ctx.DrawImage(bmp, new Rect(0, 0, TileSize, TileSize), dest);
        }

        // Rutt
        var route = Route;
        if (route is { Count: > 0 })
        {
            var pen = new Pen(new SolidColorBrush(Color.FromArgb(220, 37, 99, 235)), 5, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
            for (int i = 1; i < route.Count; i++) ctx.DrawLine(pen, ScreenFor(route[i - 1]), ScreenFor(route[i]));
            var dot = new SolidColorBrush(Color.FromRgb(37, 99, 235));
            foreach (var p in route) ctx.DrawEllipse(Brushes.White, new Pen(dot, 2), ScreenFor(p), 5, 5);
        }

        // Bilen: cirkel med en pil i färdriktningen
        var car = ScreenFor(CarPosition);
        double a = CarBearing * Math.PI / 180;
        var tip = new Point(car.X + Math.Sin(a) * 20, car.Y - Math.Cos(a) * 20);
        ctx.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(220, 38, 38)), 4, lineCap: PenLineCap.Round), car, tip);
        ctx.DrawEllipse(new SolidColorBrush(Color.FromRgb(220, 38, 38)), new Pen(Brushes.White, 3), car, 10, 10);

        // Upphovsrätt (krävs av OpenStreetMap)
        var text = new FormattedText("© OpenStreetMap-bidragsgivare", System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, Typeface.Default, 11, Brushes.Black);
        var box = new Rect(w - text.Width - 10, h - text.Height - 6, text.Width + 8, text.Height + 4);
        ctx.FillRectangle(new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)), box);
        ctx.DrawText(text, new Point(box.X + 4, box.Y + 2));
    }

    // ---------------- Kartrutor ----------------

    private static HttpClient CreateHttp()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        // OpenStreetMaps regler kräver att programmet identifierar sig.
        h.DefaultRequestHeaders.UserAgent.ParseAdd("CyberdromeCockpit/0.8 (utvecklingsverktyg)");
        return h;
    }

    public static string CacheFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create), "Cyberdrome", "tiles");

    private Bitmap? GetTile(int z, int x, int y)
    {
        var key = $"{z}/{x}/{y}";
        if (Memory.TryGetValue(key, out var bmp)) return bmp;
        var file = Path.Combine(CacheFolder, z.ToString(), x.ToString(), y + ".png");
        if (File.Exists(file))
        {
            try { bmp = new Bitmap(file); Memory[key] = bmp; return bmp; }
            catch { File.Delete(file); }
        }
        if (Loading.TryAdd(key, 0)) _ = DownloadAsync(key, file, z, x, y);
        return null;
    }

    private async Task DownloadAsync(string key, string file, int z, int x, int y)
    {
        try
        {
            var url = TileUrl.Replace("{z}", z.ToString()).Replace("{x}", x.ToString()).Replace("{y}", y.ToString());
            var bytes = await Http.GetByteArrayAsync(url).ConfigureAwait(false);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            await File.WriteAllBytesAsync(file, bytes).ConfigureAwait(false);
            using var ms = new MemoryStream(bytes);
            Memory[key] = new Bitmap(ms);
        }
        catch { /* ingen nätanslutning: rutan förblir grå */ }
        finally
        {
            Loading.TryRemove(key, out _);
            Dispatcher.UIThread.Post(InvalidateVisual);
        }
    }

    // ---------------- Mus ----------------

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        _pressAt = _lastDrag = e.GetPosition(this);
        _dragged = false;
        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_pressAt is null) return;
        var p = e.GetPosition(this);
        if (!_dragged && Math.Abs(p.X - _pressAt.Value.X) + Math.Abs(p.Y - _pressAt.Value.Y) < 6) return;
        _dragged = true;
        FollowCar = false;
        var c = ToWorld(_center, _zoom);
        _center = FromWorld(new Point(c.X - (p.X - _lastDrag.X), c.Y - (p.Y - _lastDrag.Y)), _zoom);
        _lastDrag = p;
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        e.Pointer.Capture(null);
        if (_pressAt is not null && !_dragged) MapClicked?.Invoke(GeoFor(e.GetPosition(this)));
        _pressAt = null;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        // Zooma kring muspekaren
        var mouse = e.GetPosition(this);
        var before = GeoFor(mouse);
        Zoom += e.Delta.Y > 0 ? 1 : -1;
        var after = GeoFor(mouse);
        _center = new GeoPoint(_center.Lat + before.Lat - after.Lat, _center.Lon + before.Lon - after.Lon);
        InvalidateVisual();
    }
}
