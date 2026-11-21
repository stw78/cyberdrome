// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

using Cyberdrome.Core;
using Cyberdrome.Core.Drives;

namespace Cyberdrome.Tests;

public class DriveTests
{
    private static Drive Simple() => new()
    {
        Name = "Test",
        Samples =
        [
            new DriveSample { T = 0, Speed = 0, Rpm = 800, Gear = Gear.P },
            new DriveSample { T = 10, Speed = 100, Rpm = 3000, Gear = Gear.D, Dtcs = ["P0301"] },
            new DriveSample { T = 20, Speed = 50, Rpm = 2000, Gear = Gear.D },
        ],
    };

    [Fact]
    public void InterpolatesNumbersLinearly()
    {
        var d = Simple();
        Assert.Equal(50, d.At(5).Speed, 6);
        Assert.Equal(1900, d.At(5).Rpm, 6);
        Assert.Equal(75, d.At(15).Speed, 6);
    }

    [Fact]
    public void DiscreteValuesComeFromPreviousSample()
    {
        var d = Simple();
        Assert.Equal(Gear.P, d.At(9.9).Gear);
        Assert.Equal(Gear.D, d.At(10).Gear);
        Assert.Empty(d.At(9.9).Dtcs);
        Assert.Equal(["P0301"], d.At(12).Dtcs);
    }

    [Fact]
    public void ClampsOutsideTheDrive()
    {
        var d = Simple();
        Assert.Equal(0, d.At(-5).Speed);
        Assert.Equal(50, d.At(99).Speed);
        Assert.Equal(20, d.Duration);
        Assert.Equal(100, d.MaxSpeed);
    }

    [Fact]
    public void PlayerAdvancesWithRateAndFinishes()
    {
        var p = new DrivePlayer(Simple()) { Rate = 2 };
        Assert.Equal(50, p.Advance(2.5).Speed, 6); // 2,5 s * 2 = 5 s
        Assert.Equal(5, p.Position, 6);
        p.Advance(100);
        Assert.True(p.Finished);
        Assert.Equal(20, p.Position);
    }

    [Fact]
    public void PlayerLoopsAround()
    {
        var p = new DrivePlayer(Simple()) { Loop = true };
        p.Advance(25);
        Assert.False(p.Finished);
        Assert.Equal(5, p.Position, 6);
        Assert.Equal(50, p.Current.Speed, 6);
    }

    [Fact]
    public void PlayerPauseAndSeek()
    {
        var p = new DrivePlayer(Simple());
        p.Paused = true;
        p.Advance(5);
        Assert.Equal(0, p.Position);
        p.Seek(15);
        Assert.Equal(75, p.Current.Speed, 6);
        p.Seek(999);
        Assert.Equal(20, p.Position);
        p.Seek(-3);
        Assert.Equal(0, p.Position);
    }

    [Fact]
    public void SameStepsGiveSameValues()
    {
        var a = new DrivePlayer(ExampleDrives.CityDrive());
        var b = new DrivePlayer(ExampleDrives.CityDrive());
        for (int i = 0; i < 200; i++)
            Assert.Equal(a.Advance(0.1), b.Advance(0.1));
    }

    [Fact]
    public void JsonRoundTrip()
    {
        var d = Simple();
        var back = Drive.FromJson(d.ToJson());
        Assert.Equal(d.Name, back.Name);
        Assert.Equal(d.Samples.Count, back.Samples.Count);
        Assert.Equal(Gear.D, back.Samples[1].Gear);
        Assert.Equal(["P0301"], back.Samples[1].Dtcs);
        Assert.Contains("\"Gear\":\"D\"", d.ToJson());
    }

    [Fact]
    public void ExampleDrivesMatchTheirDescriptions()
    {
        var city = ExampleDrives.CityDrive();
        Assert.Equal(300, city.Duration);
        Assert.Equal(50, city.MaxSpeed);
        Assert.Contains(city.Samples, s => s.Speed == 0 && s.T > 10); // stopp-och-kör

        var hw = ExampleDrives.HighwayDrive();
        Assert.Equal(600, hw.Duration);
        Assert.InRange(hw.At(200).Speed, 110, 120);
        Assert.InRange(hw.MaxSpeed, 119, 120);

        var cold = ExampleDrives.ColdStartDrive();
        Assert.Equal(5, cold.Samples[0].Coolant);
        Assert.InRange(cold.Samples[^1].Coolant, 88, 90);
        Assert.Equal(1300, cold.Samples[0].Rpm);
        Assert.True(cold.Samples[^1].Rpm < 850);
        Assert.All(cold.Samples, s => Assert.Equal(0, s.Speed));

        // Genereras likadant varje gång.
        Assert.Equal(ExampleDrives.CityDrive().ToJson().Replace(city.Created.ToString("o"), ""), city.ToJson().Replace(city.Created.ToString("o"), ""));
    }

    [Fact]
    public void RecorderCapturesTimestamps()
    {
        var s = new VehicleState { SpeedKmh = 10 };
        var r = new DriveRecorder();
        r.Capture(s, 0);
        s.SpeedKmh = 20;
        r.Capture(s, 0.1);
        s.SpeedKmh = 30;
        r.Capture(s, 0.1);
        var d = r.ToDrive("Min körning");
        Assert.Equal([0, 0.1, 0.2], d.Samples.Select(x => x.T).ToArray());
        Assert.Equal(30, d.MaxSpeed);
    }

    [Fact]
    public void ApplyToSetsVehicleState()
    {
        var s = new VehicleState();
        new DriveSample { Speed = 77, Rpm = 2500, Coolant = 60, Fuel = 40, Throttle = 20, Gear = Gear.D, Ignition = true, Dark = true, Dtcs = ["P0420"] }.ApplyTo(s);
        Assert.Equal(77, s.SpeedKmh);
        Assert.Equal(Gear.D, s.Gear);
        Assert.True(s.Dark);
        Assert.Equal(["P0420"], s.Dtcs);
    }

    [Fact]
    public void StoreSaveRenameImportDelete()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cd-drives-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new DriveStore(dir);
            store.EnsureExamples();
            Assert.Equal(3, store.LoadAll().Count);

            var d = Simple();
            store.Save(d);
            Assert.True(store.Rename(d, "Nytt namn: å/ä"));
            Assert.True(File.Exists(store.PathFor("Nytt namn: å/ä")));
            Assert.False(store.Exists("Test"));

            var export = Path.Combine(dir, "export.txt");
            store.Export(d, export);
            var imported = store.Import(export);
            Assert.Equal("Nytt namn: å/ä (2)", imported.Name);

            store.Delete(imported.Name);
            Assert.Equal(4, store.LoadAll().Count);
        }
        finally { Directory.Delete(dir, true); }
    }
}
