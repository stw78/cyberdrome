// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cyberdrome.Core.Drives;
using Cyberdrome.Core.Geo;
using Cyberdrome.Vm;

namespace Cyberdrome.App.ViewModels;

/// <summary>Sidan Karta: bilens position, rutter och GPS till Android.</summary>
public sealed partial class MainViewModel
{
    private readonly GpsSimulator _gps = new();
    private DispatcherTimer? _gpsTimer;
    private DateTime _gpsLastTick = DateTime.UtcNow;
    private bool _gpsSending;
    private bool _driveHasGps;

    /// <summary>Punkter man klickat ut för en ny rutt.</summary>
    private readonly List<GeoPoint> _draftRoute = new();

    [ObservableProperty] private GeoPoint _carPosition = GpsSimulator.DefaultPosition;
    [ObservableProperty] private double _carBearing;
    [ObservableProperty] private IReadOnlyList<GeoPoint>? _mapRoute;
    [ObservableProperty] private bool _followCar = true;

    /// <summary>0 = klick sätter bilens position, 1 = klick lägger till punkter i en rutt.</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(MapHelp))] private int _mapClickMode;

    [ObservableProperty] private string _gpsStatus = "";

    public string[] MapClickModes { get; } = ["Klick flyttar bilen", "Klick lägger till ruttpunkt"];

    public string MapHelp => MapClickMode == 0
        ? "Klicka på kartan för att sätta bilen där. Dra för att flytta kartan, scrolla för att zooma."
        : "Klicka ut punkter i ordning och tryck sedan Kör rutten. Bilen följer rutten med farten från reglaget (eller körningen).";

    public string MapTileUrl
    {
        get => _appSettings.MapTileUrl;
        set { if (value != _appSettings.MapTileUrl) { _appSettings.MapTileUrl = value; _appSettings.Save(); OnPropertyChanged(); } }
    }

    public bool SendGpsToAndroid
    {
        get => _appSettings.SendGpsToAndroid;
        set { if (value != _appSettings.SendGpsToAndroid) { _appSettings.SendGpsToAndroid = value; _appSettings.Save(); OnPropertyChanged(); } }
    }

    private void InitMap()
    {
        DriveSampleApplied += OnDriveSampleForGps;
        _gpsTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => GpsTick());
        _gpsTimer.Start();
        UpdateCarFromGps();
    }

    /// <summary>Anropas från kartan när man klickar.</summary>
    public void MapClick(GeoPoint p)
    {
        if (MapClickMode == 0)
        {
            _gps.SetPosition(p);
            _draftRoute.Clear();
            MapRoute = null;
            UpdateCarFromGps();
            GpsStatus = "Bilen flyttad till " + p;
        }
        else
        {
            if (_draftRoute.Count == 0) _draftRoute.Add(_gps.Position); // rutten börjar där bilen står
            _draftRoute.Add(p);
            MapRoute = _draftRoute.ToList();
            GpsStatus = $"{_draftRoute.Count} punkter – tryck Kör rutten.";
        }
    }

    [RelayCommand]
    private void StartRoute()
    {
        if (_draftRoute.Count < 2) { GpsStatus = "Klicka ut minst en punkt först (läget ”Klick lägger till ruttpunkt”)."; return; }
        var route = new GeoRoute(_draftRoute.ToList());
        _gps.SetRoute(route);
        MapRoute = route.Points;
        GpsStatus = $"Kör rutten: {route.Length / 1000:0.0} km. Bilen rör sig med farten på Översikt.";
        if (Speed < 1 && !IsPlayingDrive) GpsStatus += " Farten är 0 – dra upp hastigheten.";
        UpdateCarFromGps();
    }

    [RelayCommand]
    private void ClearRoute()
    {
        _draftRoute.Clear();
        _gps.SetPosition(_gps.Position);
        MapRoute = null;
        GpsStatus = "Rutten borttagen.";
    }

    private void OnDriveSampleForGps(DriveSample s)
    {
        _driveHasGps = s.Lat is not null && s.Lon is not null;
        if (!_driveHasGps) return;
        _gps.FollowSample(new GeoPoint(s.Lat!.Value, s.Lon!.Value));
        UpdateCarFromGps();
    }

    partial void OnIsPlayingDriveChanged(bool value)
    {
        if (value && _player is not null)
        {
            // Visa körningens rutt på kartan.
            var pts = _player.Drive.Samples.Where(x => x.Lat is not null)
                .Select(x => new GeoPoint(x.Lat!.Value, x.Lon!.Value)).ToList();
            MapRoute = pts.Count > 1 ? Thin(pts) : null;
        }
        else _driveHasGps = false;
    }

    /// <summary>Glesar ut en lång punktlista (för ritning).</summary>
    private static List<GeoPoint> Thin(List<GeoPoint> pts)
    {
        var res = new List<GeoPoint> { pts[0] };
        foreach (var p in pts.Skip(1))
            if (GeoMath.Distance(res[^1], p) > 15) res.Add(p);
        if (res[^1] != pts[^1]) res.Add(pts[^1]);
        return res;
    }

    private void UpdateCarFromGps()
    {
        CarPosition = _gps.Position;
        CarBearing = _gps.Bearing;
    }

    private void GpsTick()
    {
        var now = DateTime.UtcNow;
        double dt = (now - _gpsLastTick).TotalSeconds;
        _gpsLastTick = now;

        // Utan körning med GPS: följ rutten med farten från reglaget.
        if (!(IsPlayingDrive && _driveHasGps)) _gps.Advance(dt, Speed);
        UpdateCarFromGps();
        _ = SendGpsAsync();
    }

    private async Task SendGpsAsync()
    {
        if (!SendGpsToAndroid || _gpsSending || _android is not EmulatorVm emu || !AndroidRunning) return;
        _gpsSending = true;
        try
        {
            var utc = DateTime.UtcNow;
            var p = _gps.Position;
            double speed = Ignition ? Speed : 0;
            var ok = await emu.SendGpsFixAsync(p.Lat, p.Lon, 25, speed);
            GpsStatus = ok
                ? $"GPS till Android: {p}  ·  {speed:0} km/h  ·  kurs {_gps.Bearing:0}°"
                : "Kunde inte skicka GPS till Android.";
        }
        catch (Exception ex) { GpsStatus = "GPS: " + ex.Message; }
        finally { _gpsSending = false; }
    }
}
