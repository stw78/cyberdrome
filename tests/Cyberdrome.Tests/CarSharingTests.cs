// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

using System.Text;
using Cyberdrome.Core;
using Cyberdrome.Core.Profiles;
using Cyberdrome.Obd;

namespace Cyberdrome.Tests;

/// <summary>Egna bilmodeller och delning som .cbs.</summary>
public class CarSharingTests
{
    private static List<CarProfile> BuiltIns() => ProfileLoader.LoadFolder(Path.Combine(AppContext.BaseDirectory, "profiles"));
    private static CarProfile Golf() => BuiltIns().First(x => x.Id == "vw-golf8");

    private static (ProfileStore Store, string Dir) NewStore()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cd-cars-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return (new ProfileStore(Path.Combine(dir, "bilar"), BuiltIns().Select(p => p.Id)), dir);
    }

    [Fact]
    public void ExportImportRoundTripKeepsEverythingIncludingExtras()
    {
        var (store, dir) = NewStore();
        try
        {
            var copy = store.Duplicate(Golf());
            copy.Model = "Golf 8 testbil";
            copy.Vin = "WVWZZZCDZMW123456";
            copy.SupportedPids = ["0C", "0D", "05"];
            copy.DefaultValues = new ProfileDefaults { Speed = 30, Rpm = 1200 };
            copy.Ecus[0].Coding = "0A0B0C0D";
            var pkg = new CarPackage { Profile = copy };
            pkg.Extras["tema/theme.json"] = Encoding.UTF8.GetBytes("{\"id\":\"t\"}");
            pkg.Extras["testfall/a.json"] = Encoding.UTF8.GetBytes("{}");
            var file = Path.Combine(dir, "golf.cbs");
            pkg.Save(file);

            var (store2, dir2) = NewStore();
            try
            {
                var imported = store2.Import(file);
                var p = imported.Profile;
                Assert.Equal(copy.Id, p.Id);
                Assert.Equal("Golf 8 testbil", p.Model);
                Assert.Equal(["0C", "0D", "05"], p.SupportedPids);
                Assert.Equal(30, p.DefaultValues!.Speed);
                Assert.Equal("0A0B0C0D", p.Ecus[0].Coding);
                Assert.NotNull(p.SecurityAccess); // den påhittade formeln följer med
                Assert.Equal(2, imported.Extras.Count);
                var own = Assert.Single(store2.LoadOwn());
                Assert.True(own.IsOwn);
                Assert.EndsWith("(Egen)", own.ListName);
            }
            finally { Directory.Delete(dir2, true); }
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void NameClashGetsNewIdAndBuiltInIsNeverOverwritten()
    {
        var (store, dir) = NewStore();
        try
        {
            // Ett paket med samma id som en inbyggd bil
            var file = Path.Combine(dir, "golf.cbs");
            new CarPackage { Profile = Golf() }.Save(file);
            var a = store.Import(file).Profile;
            Assert.Equal("vw-golf8-2", a.Id);
            Assert.EndsWith("(importerad)", a.Model);
            var b = store.Import(file).Profile; // igen → krockar med a
            Assert.Equal("vw-golf8-3", b.Id);
            Assert.Equal(2, store.LoadOwn().Count);

            Assert.Throws<InvalidOperationException>(() => store.Save(Golf()));
            Assert.Throws<InvalidOperationException>(() => store.Delete(Golf()));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Theory]
    [InlineData("coding", "högst 4 byte")]
    [InlineData("vin", "VIN")]
    [InlineData("pid", "PID 99")]
    [InlineData("dupEcu", "samma nyckel")]
    [InlineData("dtc", "P30")]
    [InlineData("year", "Årsmodell")]
    public void ValidationCatchesMistakes(string what, string expected)
    {
        var p = ProfileStore.Clone(Golf());
        switch (what)
        {
            case "coding": p.Ecus[0].Coding = "0011223344"; break;
            case "vin": p.Vin = "KORT"; break;
            case "pid": p.SupportedPids = ["0D", "99"]; break;
            case "dupEcu": p.Ecus.Add(ProfileStore.Clone(p).Ecus[0]); break;
            case "dtc": p.StartDtcs = ["P30"]; break;
            case "year": p.Year = 1980; break;
        }
        Assert.Contains(ProfileLoader.Validate(p), e => e.Contains(expected));
    }

    [Fact]
    public void BrokenPackageIsRejectedWithSwedishMessage()
    {
        var (_, dir) = NewStore();
        try
        {
            var notZip = Path.Combine(dir, "x.cbs");
            File.WriteAllText(notZip, "hej");
            Assert.Contains("inte en zip", Assert.Throws<InvalidDataException>(() => CarPackage.Load(notZip)).Message);
            var bad = ProfileStore.Clone(Golf());
            bad.Vin = "X";
            Assert.Throws<InvalidDataException>(() => new CarPackage { Profile = bad }.Save(Path.Combine(dir, "bad.cbs")));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void DuplicateGetsUniqueId()
    {
        var (store, dir) = NewStore();
        try
        {
            var c1 = store.Duplicate(Golf());
            Assert.Equal("vw-golf8-kopia", c1.Id);
            store.Save(c1);
            Assert.Equal("vw-golf8-kopia-2", store.Duplicate(Golf()).Id);
            Assert.Equal("aao-bil", ProfileStore.SafeId("Åäö Bil!"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void EmulatorFollowsProfilePidList()
    {
        var p = ProfileStore.Clone(Golf());
        p.SupportedPids = ["0C", "0D"];
        var car = new VirtualCar(p);
        car.State.SpeedKmh = 54;
        var e = new Elm327Emulator(car);
        e.Handle("ATZ"); e.Handle("ATE0");
        Assert.Equal("41 0D 36\r\r>", e.Handle("010D"));
        Assert.Equal("NO DATA\r\r>", e.Handle("0105")); // kylvätska stöds inte av den här bilen
        // Stödtabell: bara 0C och 0D → bitar 12 och 13 → 00 18 00 00
        Assert.Equal("41 00 00 18 00 00\r\r>", e.Handle("0100"));
    }
}
