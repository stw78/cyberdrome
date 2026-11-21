// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

using System.Globalization;
using System.Text;

namespace Cyberdrome.Core.Geo;

/// <summary>En position (grader, WGS84).</summary>
public readonly record struct GeoPoint(double Lat, double Lon)
{
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Lat:0.000000}, {Lon:0.000000}");
}

public static class GeoMath
{
    public const double EarthRadiusM = 6_371_000;

    private static double Rad(double d) => d * Math.PI / 180;
    private static double Deg(double r) => r * 180 / Math.PI;

    /// <summary>Avstånd i meter (haversine).</summary>
    public static double Distance(GeoPoint a, GeoPoint b)
    {
        double dLat = Rad(b.Lat - a.Lat), dLon = Rad(b.Lon - a.Lon);
        double h = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                   Math.Cos(Rad(a.Lat)) * Math.Cos(Rad(b.Lat)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * EarthRadiusM * Math.Asin(Math.Min(1, Math.Sqrt(h)));
    }

    /// <summary>Kurs från a till b i grader (0 = norr, 90 = öster).</summary>
    public static double Bearing(GeoPoint a, GeoPoint b)
    {
        double y = Math.Sin(Rad(b.Lon - a.Lon)) * Math.Cos(Rad(b.Lat));
        double x = Math.Cos(Rad(a.Lat)) * Math.Sin(Rad(b.Lat)) - Math.Sin(Rad(a.Lat)) * Math.Cos(Rad(b.Lat)) * Math.Cos(Rad(b.Lon - a.Lon));
        return (Deg(Math.Atan2(y, x)) + 360) % 360;
    }

    /// <summary>Punkt på rak linje mellan a och b (f = 0..1). Räcker gott för korta sträckor.</summary>
    public static GeoPoint Lerp(GeoPoint a, GeoPoint b, double f) => new(a.Lat + (b.Lat - a.Lat) * f, a.Lon + (b.Lon - a.Lon) * f);
}

/// <summary>En rutt av raka sträckor mellan punkter. Bilen flyttas längs den med en viss sträcka.</summary>
public sealed class GeoRoute
{
    private readonly double[] _cumulative; // meter från start till varje punkt

    public GeoRoute(IReadOnlyList<GeoPoint> points)
    {
        if (points.Count == 0) throw new ArgumentException(L.T("GeoRoute_TheRouteNeedsAtLeastOne"));
        Points = points.ToArray();
        _cumulative = new double[Points.Count];
        for (int i = 1; i < Points.Count; i++)
            _cumulative[i] = _cumulative[i - 1] + GeoMath.Distance(Points[i - 1], Points[i]);
    }

    public IReadOnlyList<GeoPoint> Points { get; }
    public double Length => _cumulative[^1];

    /// <summary>Position och kurs efter <paramref name="meters"/> längs rutten (stannar vid slutet).</summary>
    public (GeoPoint Position, double Bearing) At(double meters)
    {
        if (Points.Count == 1) return (Points[0], 0);
        meters = Math.Clamp(meters, 0, Length);
        int i = Array.BinarySearch(_cumulative, meters);
        if (i < 0) i = ~i - 1;
        i = Math.Clamp(i, 0, Points.Count - 2);
        double seg = _cumulative[i + 1] - _cumulative[i];
        double f = seg <= 0 ? 0 : (meters - _cumulative[i]) / seg;
        return (GeoMath.Lerp(Points[i], Points[i + 1], f), GeoMath.Bearing(Points[i], Points[i + 1]));
    }
}

public static class GpsUnits
{
    /// <summary>km/h → knop (1 knop = 1,852 km/h).</summary>
    public static double KmhToKnots(double kmh) => kmh / 1.852;
}

/// <summary>Bygger NMEA 0183-meningar (GGA och RMC) som Android-emulatorns GPS förstår.</summary>
public static class Nmea
{
    /// <summary>XOR av alla tecken mellan '$' och '*', som två hex-siffror.</summary>
    public static string Checksum(string body)
    {
        byte c = 0;
        foreach (char ch in body) c ^= (byte)ch;
        return c.ToString("X2");
    }

    private static string Wrap(string body) => "$" + body + "*" + Checksum(body);

    private static string LatField(double lat)
    {
        double a = Math.Abs(lat);
        int d = (int)a;
        return string.Create(CultureInfo.InvariantCulture, $"{d:00}{(a - d) * 60:00.0000},{(lat >= 0 ? 'N' : 'S')}");
    }

    private static string LonField(double lon)
    {
        double a = Math.Abs(lon);
        int d = (int)a;
        return string.Create(CultureInfo.InvariantCulture, $"{d:000}{(a - d) * 60:00.0000},{(lon >= 0 ? 'E' : 'W')}");
    }

    /// <summary>$GPGGA: position, antal satelliter och höjd.</summary>
    public static string Gga(DateTime utc, GeoPoint p, double altitudeM, int satellites = 10) =>
        Wrap(string.Create(CultureInfo.InvariantCulture,
            $"GPGGA,{utc:HHmmss.ff},{LatField(p.Lat)},{LonField(p.Lon)},1,{satellites:00},0.9,{altitudeM:0.0},M,0.0,M,,"));

    /// <summary>$GPRMC: position, fart (knop) och kurs (grader).</summary>
    public static string Rmc(DateTime utc, GeoPoint p, double speedKmh, double courseDeg) =>
        Wrap(string.Create(CultureInfo.InvariantCulture,
            $"GPRMC,{utc:HHmmss.ff},A,{LatField(p.Lat)},{LonField(p.Lon)},{speedKmh / 1.852:0.0},{courseDeg:0.0},{utc:ddMMyy},,,A"));
}
