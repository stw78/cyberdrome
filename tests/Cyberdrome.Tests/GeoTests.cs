// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

using Cyberdrome.Core.Drives;
using Cyberdrome.Core.Geo;

namespace Cyberdrome.Tests;

public class GeoTests
{
    [Fact]
    public void DistanceAndBearing()
    {
        var a = new GeoPoint(59.0, 18.0);
        var north = new GeoPoint(59.01, 18.0);
        var east = new GeoPoint(59.0, 18.01);
        Assert.InRange(GeoMath.Distance(a, north), 1110, 1115); // 0,01° lat ≈ 1112 m
        Assert.InRange(GeoMath.Bearing(a, north), 359.9 - 360, 0.1);
        Assert.InRange(GeoMath.Bearing(a, east), 89.9, 90.1);
        Assert.InRange(GeoMath.Bearing(north, a), 179.9, 180.1);
    }

    [Fact]
    public void RouteInterpolatesAlongSegments()
    {
        var r = new GeoRoute([new(59.0, 18.0), new(59.01, 18.0), new(59.01, 18.02)]);
        double first = GeoMath.Distance(r.Points[0], r.Points[1]);
        var (mid, bearing) = r.At(first / 2);
        Assert.Equal(59.005, mid.Lat, 4);
        Assert.InRange(bearing, -0.1, 0.1);
        var (onSecond, b2) = r.At(first + 10);
        Assert.Equal(59.01, onSecond.Lat, 5);
        Assert.InRange(b2, 89, 91);
        Assert.Equal(r.Points[^1], r.At(1e9).Position); // stannar vid slutet
    }

    [Fact]
    public void SimulatorMovesWithSpeed()
    {
        var gps = new GpsSimulator();
        gps.SetRoute(new GeoRoute([new(59.0, 18.0), new(59.1, 18.0)]));
        gps.Advance(10, 36); // 10 s i 36 km/h = 100 m
        Assert.Equal(100, gps.Traveled, 3);
        Assert.InRange(GeoMath.Distance(new(59.0, 18.0), gps.Position), 99.9, 100.1);
        gps.Advance(10, 0);
        Assert.Equal(100, gps.Traveled, 3);
    }

    [Fact]
    public void NmeaChecksumAndFormat()
    {
        // Känt exempel från NMEA-standarden.
        Assert.Equal("47", Nmea.Checksum("GPGGA,123519,4807.038,N,01131.000,E,1,08,0.9,545.4,M,46.9,M,,"));

        var utc = new DateTime(2026, 11, 20, 12, 34, 56, DateTimeKind.Utc);
        var rmc = Nmea.Rmc(utc, new GeoPoint(59.33258, 18.06490), 50, 90);
        Assert.StartsWith("$GPRMC,123456.00,A,5919.9548,N,01803.8940,E,27.0,90.0,201126,,,A*", rmc);
        var body = rmc[1..rmc.IndexOf('*')];
        Assert.Equal(Nmea.Checksum(body), rmc[(rmc.IndexOf('*') + 1)..]);

        var gga = Nmea.Gga(utc, new GeoPoint(-33.5, -70.25), 12.3);
        Assert.Contains(",3330.0000,S,07015.0000,W,1,10,", gga);
    }

    [Fact]
    public void ExampleDrivesHaveGpsThatFollowsSpeed()
    {
        var city = ExampleDrives.CityDrive();
        Assert.All(city.Samples, s => Assert.NotNull(s.Lat));
        Assert.Equal(ExampleDrives.CityRoute[0].Lat, city.Samples[0].Lat!.Value, 5);
        // Under stillastående (första 8 s) står bilen kvar; sedan rör den sig.
        Assert.Equal(city.Samples[0].Lat, city.Samples[5].Lat);
        Assert.NotEqual(city.Samples[0].Lon, city.Samples[30].Lon);

        var cold = ExampleDrives.ColdStartDrive();
        Assert.All(cold.Samples, s => Assert.Equal(ExampleDrives.ParkedSpot.Lat, s.Lat!.Value, 6));

        var hw = ExampleDrives.HighwayDrive();
        var start = new GeoPoint(hw.Samples[0].Lat!.Value, hw.Samples[0].Lon!.Value);
        var end = new GeoPoint(hw.Samples[^1].Lat!.Value, hw.Samples[^1].Lon!.Value);
        Assert.InRange(GeoMath.Distance(start, end), 12_000, 20_000); // ca 18 km på 10 min
    }

    [Fact]
    public void LatLonSurviveJsonAndInterpolate()
    {
        var d = new Drive
        {
            Name = "gps",
            Samples = [new DriveSample { T = 0, Lat = 59.0, Lon = 18.0 }, new DriveSample { T = 10, Lat = 59.1, Lon = 18.2 }],
        };
        var back = Drive.FromJson(d.ToJson());
        Assert.Equal(59.1, back.Samples[1].Lat);
        Assert.Equal(59.05, back.At(5).Lat!.Value, 6);
        Assert.Equal(18.1, back.At(5).Lon!.Value, 6);
        Assert.Null(new Drive { Samples = [new DriveSample()] }.At(0).Lat);
    }
}
