// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

namespace Cyberdrome.Core.Geo;

/// <summary>
/// Bilens GPS: flyttar bilen längs en rutt med bilens fart, eller använder en position direkt
/// (t.ex. från en körning med inspelade koordinater).
/// </summary>
public sealed class GpsSimulator
{
    private readonly object _lock = new();
    private GeoRoute? _route;
    private double _traveled;
    private GeoPoint _position;
    private double _bearing;

    /// <summary>Standardstart: Sergels torg, Stockholm.</summary>
    public static readonly GeoPoint DefaultPosition = new(59.33258, 18.06490);

    public GpsSimulator() => _position = DefaultPosition;

    public GeoPoint Position { get { lock (_lock) return _position; } }
    public double Bearing { get { lock (_lock) return _bearing; } }
    public GeoRoute? Route { get { lock (_lock) return _route; } }
    public double Traveled { get { lock (_lock) return _traveled; } }

    /// <summary>Sätt bilen på en plats (tar bort rutten).</summary>
    public void SetPosition(GeoPoint p)
    {
        lock (_lock) { _route = null; _traveled = 0; _position = p; }
    }

    /// <summary>Följ en rutt från början.</summary>
    public void SetRoute(GeoRoute route)
    {
        lock (_lock)
        {
            _route = route;
            _traveled = 0;
            (_position, _bearing) = route.At(0);
        }
    }

    /// <summary>Kör <paramref name="seconds"/> sekunder med farten <paramref name="speedKmh"/> längs rutten.</summary>
    public void Advance(double seconds, double speedKmh)
    {
        lock (_lock)
        {
            if (_route is null || seconds <= 0 || speedKmh <= 0) return;
            _traveled = Math.Min(_route.Length, _traveled + speedKmh / 3.6 * seconds);
            (_position, _bearing) = _route.At(_traveled);
        }
    }

    /// <summary>Använd en position från en körning; kursen räknas fram från förra positionen.</summary>
    public void FollowSample(GeoPoint p)
    {
        lock (_lock)
        {
            if (GeoMath.Distance(_position, p) > 0.5) _bearing = GeoMath.Bearing(_position, p);
            _position = p;
        }
    }
}
