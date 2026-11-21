// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

using System.Net;
using Cyberdrome.Core;
using Cyberdrome.Core.CarDb;
using Cyberdrome.Core.Profiles;
using Cyberdrome.Obd;

namespace Cyberdrome.Tests;

/// <summary>Bildatabasen: vPIC och OBDb med inspelade (egna, påhittade) svar – inget nätverk i testerna.</summary>
public class CarDbTests
{
    /// <summary>Svarar med fasta JSON-svar beroende på adressen och räknar anropen.</summary>
    private sealed class FakeHttp(Dictionary<string, string> routes) : HttpMessageHandler
    {
        public List<string> Calls { get; } = [];
        public int FailFirst { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri!.ToString();
            Calls.Add(url);
            if (FailFirst > 0) { FailFirst--; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)); }
            var hit = routes.FirstOrDefault(r => url.Contains(r.Key, StringComparison.OrdinalIgnoreCase));
            return Task.FromResult(hit.Key is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(hit.Value) });
        }
    }

    // vPIC-format (egna exempelvärden).
    private const string VpicMakes = """{"Count":2,"Results":[{"MakeId":448,"MakeName":"TOYOTA"},{"MakeId":482,"MakeName":"VOLKSWAGEN"}]}""";
    private const string VpicModels2021 = """{"Count":2,"Results":[{"Make_ID":448,"Make_Name":"TOYOTA","Model_ID":1,"Model_Name":"RAV4"},{"Make_ID":448,"Make_Name":"TOYOTA","Model_ID":2,"Model_Name":"Corolla"}]}""";
    private const string VpicVin = """{"Count":1,"Results":[{"Make":"TOYOTA","Model":"RAV4","ModelYear":"2021","Manufacturer":"TOYOTA MOTOR CORPORATION","BodyClass":"Sport Utility Vehicle (SUV)","FuelTypePrimary":"Gasoline","DisplacementL":"2.5","EngineCylinders":"4","EngineHP":"203","PlantCountry":"JAPAN","ErrorCode":"0","ErrorText":"0 - VIN decoded clean"}]}""";

    // OBDb-format: lista över arkiv och en egen, påhittad signalfil (inte OBDb:s data).
    private const string ObdbRepos = """[{"name":".github","archived":false},{"name":"Toyota-RAV4","archived":false},{"name":"Tesla-Model-3","archived":false},{"name":"Mercedes-Benz-C-Class","archived":false},{"name":"Old-Car","archived":true}]""";
    private const string ObdbSignals = """
        { "ecu": [ { "hdr": "7E0", "type": "engine" } ],
          "commands": [
            { "hdr": "7C0", "rax": "7C8", "cmd": {"22": "1234"}, "freq": 5,
              "signals": [ {"id": "TEST_BATT", "path": "Battery", "name": "Batterispänning", "fmt": { "len": 16, "max": 20, "div": 1000, "unit": "volts" } },
                           {"id": "TEST_TEMP", "path": "Engine", "name": "Oljetemperatur", "fmt": { "bix": 16, "len": 8, "min": -40, "max": 215, "add": -40, "unit": "celsius" } } ] },
            { "hdr": "7E0", "cmd": {"21": "05"}, "filter": { "to": 2018 },
              "signals": [ {"id": "OLD", "path": "Engine", "name": "Bara äldre", "fmt": { "len": 8, "unit": "scalar" } } ] },
            { "hdr": "750", "eax": "2A", "cmd": {"22": "2001"},
              "signals": [ {"id": "TPMS", "path": "Tires", "name": "Däcktryck", "fmt": { "len": 8, "unit": "kilopascal" } } ] },
            { "hdr": "7E0", "cmd": {"22": "4000"}, "filter": { "from": 2020 },
              "signals": [ {"id": "NEG", "path": "Engine", "name": "Signerad", "fmt": { "len": 16, "min": -100, "max": 100, "sign": true, "unit": "scalar" } } ] }
          ] }
        """;

    private static (LocalBilDatabas Db, FakeHttp Http, string Dir) NewDb()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cd-cardb-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var http = new FakeHttp(new()
        {
            ["GetMakesForVehicleType"] = VpicMakes,
            ["GetModelsForMakeYear/make/Toyota/modelyear/2021"] = VpicModels2021,
            ["GetModelsForMakeYear"] = """{"Count":0,"Results":[]}""",
            ["DecodeVinValues"] = VpicVin,
            ["orgs/OBDb/repos?per_page=100&page=1"] = ObdbRepos,
            ["Toyota-RAV4/main/signalsets"] = ObdbSignals,
        });
        var client = new HttpClient(http);
        var db = new LocalBilDatabas(new CarModelDb(Path.Combine(dir, "bildatabas.db")), new VpicClient(client), new ObdbClient(Path.Combine(dir, "obdb"), client));
        return (db, http, dir);
    }

    private static void Cleanup(LocalBilDatabas db, string dir)
    {
        db.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(dir, true); } catch { }
    }

    [Fact]
    public async Task VpicFillsDatabaseAndSearchWorksOffline()
    {
        var (db, http, dir) = NewDb();
        try
        {
            Assert.Equal(2, await db.RefreshMakesAsync());
            Assert.Equal(2, await db.FetchModelsAsync("Toyota", 2020, 2021));
            int calls = http.Calls.Count;
            Assert.Equal(0, await db.FetchModelsAsync("Toyota", 2020, 2021)); // redan hämtat → inga nya anrop
            Assert.Equal(calls, http.Calls.Count);

            var hits = db.Search("toyota rav");
            var rav = Assert.Single(hits);
            Assert.Equal("Toyota", rav.Make); // "TOYOTA" görs snyggare
            Assert.Equal("RAV4", rav.Model);
            Assert.Equal(2021, rav.FromYear);
            Assert.Contains("Volkswagen", db.Db.Makes());
        }
        finally { Cleanup(db, dir); }
    }

    [Fact]
    public async Task VinDecodeIsCached()
    {
        var (db, http, dir) = NewDb();
        try
        {
            var v = await db.DecodeVinAsync("jtmw1rfv0md000001");
            Assert.Equal(("Toyota", "RAV4", 2021), (v.Make, v.Model, v.Year));
            Assert.Equal("2.5 l 4 cyl 203 hk", v.Engine);
            Assert.Equal("", v.ErrorText);
            int calls = http.Calls.Count;
            var again = await db.DecodeVinAsync("JTMW1RFV0MD000001");
            Assert.Equal(calls, http.Calls.Count); // från cachen
            Assert.Equal("RAV4", again.Model);
            Assert.Contains(db.Search("rav4"), h => h.Sources.Contains("VIN"));
            await Assert.ThrowsAsync<ArgumentException>(() => db.DecodeVinAsync("KORT"));
        }
        finally { Cleanup(db, dir); }
    }

    [Fact]
    public async Task ObdbListAndSignalsAreParsedAndCached()
    {
        var (db, http, dir) = NewDb();
        try
        {
            Assert.Equal(3, await db.RefreshObdbAsync()); // ".github" och arkiverade hoppas över
            Assert.Contains(db.Db.ObdbVehicles(), v => v is { Make: "Mercedes-Benz", Model: "C Class" });
            Assert.Contains(db.Db.ObdbVehicles(), v => v is { Make: "Tesla", Model: "Model 3" });
            var hit = Assert.Single(db.Search("rav4"));
            Assert.NotNull(hit.Obdb);

            var set = await db.GetSignalsAsync("Toyota", "RAV4");
            Assert.NotNull(set);
            Assert.Equal(5, set!.SignalCount);
            Assert.True(File.Exists(Path.Combine(dir, "obdb", "Toyota-RAV4.json")));
            Assert.True(File.Exists(Path.Combine(dir, "obdb", "LICENS.txt")));
            int calls = http.Calls.Count;
            await db.GetSignalsAsync("Toyota", "RAV4"); // offline från cachen
            Assert.Equal(calls, http.Calls.Count);
            Assert.Null(await db.GetSignalsAsync("Volvo", "XC60"));
        }
        finally { Cleanup(db, dir); }
    }

    [Fact]
    public async Task RetriesWithBackoffOnServerError()
    {
        var (db, http, dir) = NewDb();
        try
        {
            http.FailFirst = 1; // första svaret 503, sedan OK
            Assert.Equal(2, await db.RefreshMakesAsync()); // vPIC väntar och försöker igen
        }
        finally { Cleanup(db, dir); }
        var (db2, http2, dir2) = NewDb();
        try
        {
            http2.FailFirst = 1;
            Assert.Equal(3, await db2.RefreshObdbAsync()); // ObdbClient väntar och försöker igen
        }
        finally { Cleanup(db2, dir2); }
    }

    [Fact]
    public async Task CreateProfileFromObdbIsAnsweredByEmulator()
    {
        var (db, _, dir) = NewDb();
        try
        {
            await db.RefreshObdbAsync();
            var set = await db.GetSignalsAsync("Toyota", "RAV4");
            var builtIns = ProfileLoader.LoadFolder(Path.Combine(AppContext.BaseDirectory, "profiles"));
            var store = new ProfileStore(Path.Combine(dir, "bilar"), builtIns.Select(b => b.Id));
            var notes = new List<string>();
            var p = CarProfileFactory.Create("Toyota", "RAV4", 2021, set, builtIns, store, null, notes, out var template);
            Assert.Equal("toyota-rav4", template.Id); // samma märke som en inbyggd modell
            Assert.Empty(ProfileLoader.Validate(p));
            Assert.Equal("Data från OBDb (github.com/OBDb), CC BY-SA 4.0", p.Attribution);
            Assert.Equal(2, p.ExtraCommands!.Count); // 21 05 (≤2018) och utökad adressering (750/2A) hoppas över
            Assert.Contains(p.Ecus, e => e is { RequestId: "7C0", ResponseId: "7C8" });
            Assert.Contains(notes, n => n.Contains("utökad adressering"));

            var car = new VirtualCar(p);
            car.State.Ignition = true;
            var e = new Elm327Emulator(car);
            e.Handle("ATZ"); e.Handle("ATE0"); e.Handle("ATSH7C0");
            var reply = e.Handle("221234");
            Assert.StartsWith("62 12 34 31 38 3C", reply); // 12,6 V = 12600 = 0x3138, 20 °C = 60 = 0x3C
            var bytes = Convert.FromHexString(reply.Split('\r')[0].Replace(" ", ""));
            var cmd = p.ExtraCommands.First(c => c.Request == "221234");
            Assert.Equal(12.6, cmd.Signals[0].Decode(bytes[3..]), 3);
            Assert.Equal(20, cmd.Signals[1].Decode(bytes[3..]));
            e.Handle("ATSH7E0");
            Assert.StartsWith("7F 22 31", e.Handle("221234")); // fel styrdon

            // Signerad signal: -50 kodas och avkodas rätt.
            var neg = new ExtraSignal { Len = 16, Signed = true, Value = -50 };
            var buf = new byte[2];
            neg.Write(buf);
            Assert.Equal(-50, neg.Decode(buf));
        }
        finally { Cleanup(db, dir); }
    }

    [Fact]
    public void RepoNamesAndFilters()
    {
        Assert.Equal(("Land Rover", "Range Rover"), (ObdbClient.ParseRepo("Land-Rover-Range-Rover")!.Make, ObdbClient.ParseRepo("Land-Rover-Range-Rover")!.Model));
        Assert.Null(ObdbClient.ParseRepo(".meta"));
        var c = new ObdbCommand { FilterTo = 2018 };
        Assert.True(c.AppliesTo(2018));
        Assert.False(c.AppliesTo(2019));
        var y = new ObdbCommand { FilterFrom = 2020, FilterYears = [2015] };
        Assert.True(y.AppliesTo(2015));
        Assert.False(y.AppliesTo(2016));
        Assert.True(y.AppliesTo(2024));
        Assert.Equal("Volkswagen", VpicClient.Pretty("VOLKSWAGEN"));
        Assert.Equal("BMW", VpicClient.Pretty("BMW"));
        Assert.Equal("Mercedes-Benz", VpicClient.Pretty("MERCEDES-BENZ"));
    }
}
