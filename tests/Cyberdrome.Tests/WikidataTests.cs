// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

using System.Net;
using Cyberdrome.Core.CarDb;
using Cyberdrome.Core.Profiles;

namespace Cyberdrome.Tests;

/// <summary>Wikidata som tredje källa (inspelat SPARQL-svar i Wikidatas format, egna exempelrader).</summary>
public class WikidataTests
{
    private const string Sparql = """
        {"head":{"vars":["m","mLabel","from","to"]},"results":{"bindings":[
          {"m":{"type":"uri","value":"http://www.wikidata.org/entity/Q1"},"mLabel":{"type":"literal","value":"Saab 9-3"}},
          {"m":{"type":"uri","value":"http://www.wikidata.org/entity/Q2"},"mLabel":{"type":"literal","value":"Saab 900"},"from":{"type":"literal","value":"1978"},"to":{"type":"literal","value":"1998"}},
          {"m":{"type":"uri","value":"http://www.wikidata.org/entity/Q3"},"mLabel":{"type":"literal","value":"Saab 95"},"from":{"type":"literal","value":"1959"}},
          {"m":{"type":"uri","value":"http://www.wikidata.org/entity/Q4"},"mLabel":{"type":"literal","value":"Q4"}},
          {"m":{"type":"uri","value":"http://www.wikidata.org/entity/Q5"},"mLabel":{"type":"literal","value":"Saab 9-3"}}
        ]}}
        """;

    private sealed class Fake(string body) : HttpMessageHandler
    {
        public List<string> Calls { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Calls.Add(r.RequestUri!.ToString());
            Assert.DoesNotContain("@", r.Headers.UserAgent.ToString()); // ingen e-post eller personlig info
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }

    [Fact]
    public void ParsesModelsYearsAndStripsMake()
    {
        var list = WikidataClient.Parse(Sparql, "Saab");
        Assert.Single(list, m => m.Model == "9-3" && m.Year == 0); // dubbletter slås ihop, år okänt = 0
        Assert.Equal(21, list.Count(m => m.Model == "900")); // 1978–1998
        Assert.Contains(list, m => m.Model == "95" && m.Year == 1959);
        Assert.DoesNotContain(list, m => m.Model.StartsWith('Q')); // saknar namn
        Assert.All(list, m => Assert.Equal("Wikidata", m.Source));
    }

    [Fact]
    public async Task FetchStoresInDatabaseAndCaches()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cd-wd-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var fake = new Fake(Sparql);
        var db = new LocalBilDatabas(new CarModelDb(Path.Combine(dir, "b.db")), new VpicClient(new HttpClient(fake)), new ObdbClient(dir, new HttpClient(fake)), new WikidataClient(new HttpClient(fake)));
        try
        {
            Assert.True(await db.FetchWikidataAsync("saab") > 0);
            Assert.Contains(fake.Calls, c => c.Contains("query.wikidata.org/sparql") && c.Contains("Q48957")); // känd tillverkare, ingen sökning
            int calls = fake.Calls.Count;
            Assert.Equal(0, await db.FetchWikidataAsync("Saab")); // cachat i 30 dagar
            Assert.Equal(calls, fake.Calls.Count);
            var hits = db.Search("saab");
            var s900 = Assert.Single(hits, h => h.Model == "900");
            Assert.Equal((1978, 1998), (s900.FromYear, s900.ToYear));
            var s93 = Assert.Single(hits, h => h.Model == "9-3");
            Assert.Null(s93.FromYear);
        }
        finally { db.Dispose(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void MakeWideObdbRepoAndObd2Warning()
    {
        var saab = ObdbClient.ParseRepo("Saab")!;
        Assert.Equal(("Saab", ObdbClient.MakeWide), (saab.Make, saab.Model));
        Assert.Equal(("Saab", "9 3"), (ObdbClient.ParseRepo("Saab-9-3")!.Make, ObdbClient.ParseRepo("Saab-9-3")!.Model));
        Assert.Equal(CarModelDb.Key("Saab", "9-3"), CarModelDb.Key("Saab", "9 3")); // OBDb "9 3" och Wikidata "9-3" matchar

        var builtIns = ProfileLoader.LoadFolder(Path.Combine(AppContext.BaseDirectory, "profiles"));
        var store = new ProfileStore(Path.Combine(Path.GetTempPath(), "cd-wd-" + Guid.NewGuid().ToString("N")), builtIns.Select(b => b.Id));
        var notes = new List<string>();
        var p = CarProfileFactory.Create("Saab", "900", 1990, null, builtIns, store, null, notes, out _);
        Assert.Equal(1996, p.Year);
        Assert.Contains(notes, n => n.Contains("äldre än OBD-II"));
        Assert.Empty(ProfileLoader.Validate(p));
        notes.Clear();
        CarProfileFactory.Create("Saab", "9-3", 2002, null, builtIns, store, null, notes, out _);
        Assert.Contains(notes, n => n.Contains("EU-bensinbilar från 2001"));
        Assert.Equal("YS3ZZZ00080000001", CarProfileFactory.PlaceholderVin("Saab", 2008));
        Assert.Equal("YV1ZZZ000T0000001", CarProfileFactory.PlaceholderVin("Volvo", 1996));
        Assert.Equal("YS3ZZZ000T0000001", p.Vin);
    }
}

public class ObdbFallbackTests
{
    private sealed class Fake(Dictionary<string, string> routes) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            var url = r.RequestUri!.ToString();
            var hit = routes.FirstOrDefault(x => url.Contains(x.Key));
            return Task.FromResult(hit.Key is null ? new HttpResponseMessage(HttpStatusCode.NotFound) : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(hit.Value) });
        }
    }

    [Fact]
    public async Task EmptyModelFileFallsBackToMakeWideSignals()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cd-fb-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var http = new HttpClient(new Fake(new()
        {
            ["repos?per_page=100&page=1"] = """[{"name":"Volvo"},{"name":"Volvo-V70"},{"name":"Saab-9-3"}]""",
            ["/Volvo-V70/"] = """{ "commands": [] }""",
            ["/Saab-9-3/"] = """{ "commands": [] }""",
            ["/Volvo/main"] = """{ "commands": [ { "hdr": "7E0", "cmd": {"22": "1234"}, "signals": [ {"id": "X", "name": "Gemensam", "fmt": {"len": 8, "unit": "scalar"}} ] } ] }""",
        }));
        var db = new LocalBilDatabas(new CarModelDb(Path.Combine(dir, "b.db")), new VpicClient(http), new ObdbClient(Path.Combine(dir, "obdb"), http));
        try
        {
            await db.RefreshObdbAsync();
            var v70 = await db.GetSignalsAsync("Volvo", "V70");
            Assert.Equal("Volvo", v70!.Repo); // tom modellfil → märkets gemensamma signaler
            Assert.Equal(1, v70.SignalCount);
            var saab = await db.GetSignalsAsync("Saab", "9-3");
            Assert.Equal(0, saab!.SignalCount); // inget märkesarkiv → tom fil

            var builtIns = ProfileLoader.LoadFolder(Path.Combine(AppContext.BaseDirectory, "profiles"));
            var store = new ProfileStore(Path.Combine(dir, "bilar"), builtIns.Select(b => b.Id));
            var notes = new List<string>();
            var p = CarProfileFactory.Create("Saab", "9-3", 2008, saab, builtIns, store, null, notes, out _);
            Assert.Null(p.ExtraCommands);
            Assert.Contains(notes, n => n.Contains("inga signaler ännu"));
            notes.Clear();
            var v = CarProfileFactory.Create("Volvo", "V70", 2008, v70, builtIns, store, null, notes, out var template);
            Assert.Equal("volvo-xc60", template.Id);
            Assert.Single(v.ExtraCommands!);
            Assert.Contains(notes, n => n.Contains("märkets gemensamma signaler"));
            Assert.Empty(ProfileLoader.Validate(v));
        }
        finally { db.Dispose(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); try { Directory.Delete(dir, true); } catch { } }
    }
}

public class StandardCarTests
{
    [Fact]
    public void StandardCarThenAddSignalsFromObdbJson()
    {
        var builtIns = ProfileLoader.LoadFolder(Path.Combine(AppContext.BaseDirectory, "profiles"));
        var store = new ProfileStore(Path.Combine(Path.GetTempPath(), "cd-std-" + Guid.NewGuid().ToString("N")), builtIns.Select(b => b.Id));
        var notes = new List<string>();
        var p = CarProfileFactory.Create("Lada", "Niva", 2010, null, builtIns, store, null, notes, out _);
        Assert.Empty(ProfileLoader.Validate(p));
        Assert.Null(p.ExtraCommands);
        Assert.Equal("CDX00000000000001", p.Vin); // okänt märke

        // Senare: egna signaler i OBDb-format läggs till; samma fråga ersätts, andra behålls.
        var set1 = ObdbSignalSet.Parse("""{ "commands": [ { "hdr": "7E0", "cmd": {"22": "1111"}, "signals": [ {"id": "A", "name": "A", "fmt": {"len": 8, "unit": "scalar"}} ] } ] }""", "egen");
        var set2 = ObdbSignalSet.Parse("""{ "commands": [ { "hdr": "7E0", "cmd": {"22": "1111"}, "signals": [ {"id": "B", "name": "B", "fmt": {"len": 16, "unit": "scalar"}} ] }, { "hdr": "7A0", "cmd": {"21": "05"}, "signals": [ {"id": "C", "name": "C", "fmt": {"len": 8, "unit": "scalar"}} ] } ] }""", "egen2");
        Assert.Equal(1, CarProfileFactory.ApplySignals(p, set1, notes, "test", "egen.json"));
        Assert.Equal(2, CarProfileFactory.ApplySignals(p, set2, notes, "test", "egen2.json"));
        Assert.Equal(2, p.ExtraCommands!.Count);
        Assert.Equal("B", p.ExtraCommands.Single(c => c.Request == "221111").Signals[0].Id);
        Assert.Contains(p.Ecus, e => e.RequestId == "7A0");
        Assert.Empty(ProfileLoader.Validate(p));
    }
}
