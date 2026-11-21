// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

using Cyberdrome.Obd;
using Cyberdrome.Obd.Testing;

namespace Cyberdrome.Tests;

public class TestCaseTests
{
    private static readonly DateTime T0 = new(2026, 11, 20, 12, 0, 0);

    /// <summary>Bygger trafik: (sekunder, fråga, svar) för klienten "app".</summary>
    private static List<ObdTrafficEntry> Traffic(params (double S, string Req, string Resp)[] xs)
    {
        var list = new List<ObdTrafficEntry>();
        foreach (var (s, req, resp) in xs)
        {
            list.Add(new(T0.AddSeconds(s), "app", true, req));
            list.Add(new(T0.AddSeconds(s + 0.01), "app", false, resp));
        }
        return list;
    }

    private static TestCase Simple(params TestCheck[] checks) => new() { Id = "t", Name = "T", DurationSeconds = 10, Checks = checks.ToList() };

    [Fact]
    public void BuiltInTestCasesAreValid()
    {
        Assert.Equal(3, BuiltInTestCases.All.Count);
        Assert.All(BuiltInTestCases.All, t => Assert.Empty(t.Validate()));
        var json = BuiltInTestCases.All[1].ToJson();
        Assert.Equal(BuiltInTestCases.All[1].Checks.Count, TestCase.FromJson(json).Checks.Count);
    }

    [Theory]
    [InlineData("010D", "010D", true)]
    [InlineData("01 0d 1", "010D", true)]        // svarsantal-siffran sist
    [InlineData("01050C0D", "010D", true)]       // flera PID:ar i samma fråga
    [InlineData("01050C", "010D", false)]
    [InlineData("0902", "010D", false)]
    [InlineData("ATSH714", "ATSH714", true)]
    [InlineData("03", "03", true)]
    public void CommandMatching(string request, string check, bool expected) =>
        Assert.Equal(expected, TestEvaluator.Matches(TestEvaluator.Normalize(request), check));

    [Fact]
    public void RequestRatePassesWhenAppAsksOften()
    {
        var tr = Traffic(Enumerable.Range(0, 20).Select(i => (i * 0.5, "010D", "41 0D 36")).ToArray());
        var r = TestEvaluator.Evaluate(Simple(new TestCheck { Type = "requestRate", Name = "fart", Command = "010D", MaxGapSeconds = 2 }),
            tr, T0, T0.AddSeconds(10), []);
        Assert.Equal(CheckStatus.Passed, r.Checks[0].Status);
        Assert.True(r.Passed);
    }

    [Fact]
    public void RequestRateFailsOnLongPause()
    {
        var tr = Traffic((1, "010D", "41 0D 36"), (4, "010D", "41 0D 36"), (9.5, "010D", "41 0D 36"));
        var r = TestEvaluator.Evaluate(Simple(new TestCheck { Type = "requestRate", Name = "fart", Command = "010D", MaxGapSeconds = 2 }),
            tr, T0, T0.AddSeconds(10), []);
        Assert.Equal(CheckStatus.Failed, r.Checks[0].Status);
        Assert.Contains("Längsta paus", r.Checks[0].Detail);
        Assert.False(r.Passed);
    }

    [Fact]
    public void ResponseContainsWorksWithHeadersAndAfterStep()
    {
        var test = new TestCase
        {
            Id = "d", Name = "D", DurationSeconds = 10,
            Steps = [new() { At = 0, Action = "clearDtcs" }, new() { At = 3, Action = "addDtc", Dtc = "P0300" }],
            Checks = [new() { Type = "responseContains", Name = "P0300", Command = "03", Text = "0300", AfterStep = 1 }],
        };
        // Före steget: inga felkoder. Efter steget: P0300 (med rubriker och utan mellanslag).
        var tr = Traffic((1, "03", "43 00"), (5, "03", "7E80443010300000000"));
        var r = TestEvaluator.Evaluate(test, tr, T0, T0.AddSeconds(10), [T0, T0.AddSeconds(3)]);
        Assert.Equal(CheckStatus.Passed, r.Checks[0].Status);

        // Läste bara före steget → underkänt
        var r2 = TestEvaluator.Evaluate(test, Traffic((1, "03", "43 01 03 00")), T0, T0.AddSeconds(10), [T0, T0.AddSeconds(3)]);
        Assert.Equal(CheckStatus.Failed, r2.Checks[0].Status);

        // Steget kördes aldrig → underkänt med förklaring
        var r3 = TestEvaluator.Evaluate(test, tr, T0, T0.AddSeconds(10), [T0, null]);
        Assert.Contains("kördes aldrig", r3.Checks[0].Detail);
    }

    [Fact]
    public void OptionalCheckGivesNotDoneAndStillPasses()
    {
        var r = TestEvaluator.Evaluate(Simple(new TestCheck { Type = "requestSeen", Name = "radera", Command = "04", Optional = true }),
            Traffic((1, "010D", "41 0D 36")), T0, T0.AddSeconds(10), []);
        Assert.Equal(CheckStatus.NotDone, r.Checks[0].Status);
        Assert.True(r.Passed);
    }

    [Fact]
    public void NoErrorsIgnoresAdapterProbingButCatchesObdErrors()
    {
        var check = new TestCheck { Type = "noErrors", Name = "fel" };
        var ok = TestEvaluator.Evaluate(Simple(check), Traffic((1, "STI", "?"), (2, "ATZ", "ELM327 v1.5"), (3, "010D", "41 0D 36")), T0, T0.AddSeconds(10), []);
        Assert.Equal(CheckStatus.Passed, ok.Checks[0].Status);
        var bad = TestEvaluator.Evaluate(Simple(check), Traffic((1, "010D1", "?"), (2, "0199", "NO DATA")), T0, T0.AddSeconds(10), []);
        Assert.Equal(CheckStatus.Failed, bad.Checks[0].Status);
        Assert.Equal(2, bad.Checks[0].Excerpt.Count);
    }

    [Fact]
    public void ProgramAndTipsEntriesAreIgnored()
    {
        var tr = new List<ObdTrafficEntry>
        {
            new(T0.AddSeconds(1), "program", false, "Teststeg 1"),
            new(T0.AddSeconds(2), "tips", false, "💡 förväntad nyckel"),
        };
        var r = TestEvaluator.Evaluate(Simple(new TestCheck { Type = "noErrors", Name = "fel" }), tr, T0, T0.AddSeconds(10), []);
        Assert.Equal(0, r.RequestCount);
        Assert.Empty(r.Clients);
    }

    [Fact]
    public void ValidationFindsMistakes()
    {
        var t = new TestCase
        {
            Id = "x", Name = "X", DurationSeconds = 10,
            Steps = [new() { At = 20, Action = "explode" }, new() { At = 1, Action = "addDtc" }],
            Checks = [new() { Type = "magic", Name = "m" }, new() { Type = "responseContains", Name = "r", Command = "03" }, new() { Type = "requestSeen", Name = "s", Command = "03", AfterStep = 5 }],
        };
        var e = t.Validate();
        Assert.Contains(e, x => x.Contains("okänd action"));
        Assert.Contains(e, x => x.Contains("inom testets tid"));
        Assert.Contains(e, x => x.Contains("dtc saknas"));
        Assert.Contains(e, x => x.Contains("okänd typ"));
        Assert.Contains(e, x => x.Contains("text saknas"));
        Assert.Contains(e, x => x.Contains("afterStep"));
    }

    [Fact]
    public void ReportIsSavedAsHtmlAndJson()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cd-rep-" + Guid.NewGuid().ToString("N"));
        try
        {
            var test = BuiltInTestCases.All[0];
            var tr = Traffic(Enumerable.Range(0, 40).Select(i => (i * 0.5, i % 2 == 0 ? "010D" : "010C", i % 2 == 0 ? "41 0D 5A" : "41 0C 1F 40")).ToArray());
            var r = TestEvaluator.Evaluate(test, tr, T0, T0.AddSeconds(20), [T0, T0.AddSeconds(8), T0.AddSeconds(14)]);
            var html = TestEvaluator.SaveReport(r, dir);
            Assert.True(File.Exists(html));
            Assert.True(File.Exists(Path.ChangeExtension(html, ".json")));
            var text = File.ReadAllText(html);
            Assert.Contains("Grundläggande PID:ar", text);
            Assert.Contains("GODKÄNT", text);
            Assert.Contains("\"Status\": \"Passed\"", File.ReadAllText(Path.ChangeExtension(html, ".json")));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void ImportRejectsInvalidAndRenamesBuiltInIds()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cd-tf-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            var store = new TestStore(Path.Combine(dir, "testfall"));
            var f = Path.Combine(dir, "a.json");
            File.WriteAllText(f, BuiltInTestCases.All[0].ToJson());
            Assert.Equal("eget-grund-pid", store.Import(f).Id);
            Assert.Equal(4, store.LoadAll().Count);
            File.WriteAllText(f, "{ \"id\": \"x\", \"name\": \"X\", \"checks\": [] }");
            Assert.Throws<InvalidDataException>(() => store.Import(f));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
