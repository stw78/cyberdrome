// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Cyberdrome.Obd.Testing;

public enum CheckStatus { Passed, Failed, NotDone }

public sealed record CheckResult(string Name, string Type, CheckStatus Status, string Detail, IReadOnlyList<string> Excerpt);

public sealed record StepResult(int Index, double At, string Action, string Description, DateTime? DoneAt);

public sealed record TestResult(
    string TestId, string TestName, string Description, DateTime Start, DateTime End,
    IReadOnlyList<StepResult> Steps, IReadOnlyList<CheckResult> Checks, int RequestCount, IReadOnlyList<string> Clients)
{
    /// <summary>Godkänt = inga underkända kontroller (valfria som inte gjordes räknas inte).</summary>
    public bool Passed => Checks.All(c => c.Status != CheckStatus.Failed);
    public int PassedCount => Checks.Count(c => c.Status == CheckStatus.Passed);
}

/// <summary>Bedömer ett testfall utifrån OBD-trafiken som spelades in under testet.</summary>
public static class TestEvaluator
{
    /// <summary>Hur länge appen får på sig att ansluta innan "requestRate" börjar räknas.</summary>
    public static readonly TimeSpan StartGrace = TimeSpan.FromSeconds(3);

    private sealed record Exchange(DateTime Time, string Client, string Command, string Raw, string Response);

    /// <summary>"01 0d1" → "010D" (mellanslag bort, versaler, svarsantal-siffran sist bort).</summary>
    public static string Normalize(string cmd)
    {
        var c = new string(cmd.Where(ch => !char.IsWhiteSpace(ch)).ToArray()).ToUpperInvariant();
        if (!c.StartsWith("AT", StringComparison.Ordinal) && c.Length is >= 3 and <= 15 && c.Length % 2 == 1 && c.All(Uri.IsHexDigit))
            c = c[..^1];
        return c;
    }

    /// <summary>Matchar en fråga mot kontrollens kommando. "010D" matchar även flera PID:ar i samma fråga (t.ex. 01050C0D).</summary>
    public static bool Matches(string normalizedRequest, string checkCommand)
    {
        var want = Normalize(checkCommand);
        if (normalizedRequest == want) return true;
        if (want.Length == 4 && want.StartsWith("01", StringComparison.Ordinal) && normalizedRequest.StartsWith("01", StringComparison.Ordinal)
            && normalizedRequest.Length % 2 == 0 && normalizedRequest.All(Uri.IsHexDigit))
        {
            var pid = want[2..];
            for (int i = 2; i + 2 <= normalizedRequest.Length; i += 2)
                if (normalizedRequest.Substring(i, 2) == pid) return true;
        }
        return false;
    }

    private static string Compact(string s) => new string(s.Where(ch => !char.IsWhiteSpace(ch) && ch != '|').ToArray()).ToUpperInvariant();

    /// <summary>Parar ihop varje fråga från en app med nästa svar till samma app.</summary>
    private static List<Exchange> Pair(IReadOnlyList<ObdTrafficEntry> traffic)
    {
        var list = new List<Exchange>();
        for (int i = 0; i < traffic.Count; i++)
        {
            var e = traffic[i];
            if (!e.FromApp) continue;
            string resp = "";
            for (int j = i + 1; j < traffic.Count; j++)
            {
                var r = traffic[j];
                if (r.Client != e.Client) continue;
                if (r.FromApp) break;
                resp = r.Text;
                break;
            }
            list.Add(new Exchange(e.Time, e.Client, Normalize(e.Text), e.Text, resp));
        }
        return list;
    }

    private static string Line(Exchange x) => $"{x.Time:HH:mm:ss.fff}  → {x.Raw}  ← {x.Response}";

    public static TestResult Evaluate(TestCase test, IReadOnlyList<ObdTrafficEntry> traffic, DateTime start, DateTime end,
        IReadOnlyList<DateTime?> stepTimes)
    {
        var exchanges = Pair(traffic.Where(t => t.Client != "tips" && t.Client != "program" && t.Time >= start && t.Time <= end).ToList());
        var checks = new List<CheckResult>();

        foreach (var c in test.Checks)
        {
            DateTime from = start;
            if (c.AfterStep is { } a)
            {
                if (a >= stepTimes.Count || stepTimes[a] is null)
                {
                    checks.Add(new(c.Name, c.Type, CheckStatus.Failed, L.F("TestEvaluator_Step0WasNeverRun", a + 1), []));
                    continue;
                }
                from = stepTimes[a]!.Value;
            }
            var relevant = exchanges.Where(x => x.Time >= from).ToList();
            checks.Add(c.Type switch
            {
                "requestRate" => RequestRate(c, relevant, from, end),
                "requestSeen" => RequestSeen(c, relevant),
                "responseContains" => ResponseContains(c, relevant),
                "noErrors" => NoErrors(c, relevant),
                _ => new CheckResult(c.Name, c.Type, CheckStatus.Failed, L.T("TestEvaluator_UnknownCheckType"), []),
            });
        }

        var steps = test.Steps.Select((s, i) => new StepResult(i, s.At, s.Action, Describe(s), i < stepTimes.Count ? stepTimes[i] : null)).ToList();
        return new TestResult(test.Id, test.Name, test.Description, start, end, steps, checks, exchanges.Count,
            exchanges.Select(x => x.Client).Distinct().ToList());
    }

    public static string Describe(TestStep s) => s.Action switch
    {
        "set" => L.T("TestEvaluator_Set") + string.Join(", ", new[]
        {
            s.Values?.Speed is { } sp ? $"hastighet {sp:0} km/h" : null,
            s.Values?.Rpm is { } r ? $"varvtal {r:0}" : null,
            s.Values?.Coolant is { } c ? L.F("TestEvaluator_Coolant00C", c) : null,
            s.Values?.Fuel is { } f ? L.F("TestEvaluator_Fuel00", f) : null,
            s.Values?.Ignition is { } ig ? (ig ? L.T("TestEvaluator_IgnitionOn") : L.T("TestEvaluator_IgnitionOff")) : null,
        }.Where(x => x is not null)),
        "addDtc" => L.F("TestEvaluator_TurnOnTroubleCode0", s.Dtc),
        "removeDtc" => L.F("TestEvaluator_TurnOffTroubleCode0", s.Dtc),
        "clearDtcs" => L.T("TestEvaluator_ClearAllTroubleCodes"),
        _ => s.Text ?? "",
    } + (s.Action != "note" && !string.IsNullOrEmpty(s.Text) ? " – " + s.Text : "");

    private static CheckResult NotSeen(TestCheck c, string detail) =>
        new(c.Name, c.Type, c.Optional ? CheckStatus.NotDone : CheckStatus.Failed, detail, []);

    private static CheckResult RequestRate(TestCheck c, List<Exchange> xs, DateTime from, DateTime end)
    {
        var hits = xs.Where(x => Matches(x.Command, c.Command!)).Select(x => x.Time).ToList();
        if (hits.Count == 0) return NotSeen(c, L.F("TestEvaluator_TheAppNeverAskedFor0", c.Command));
        var windowStart = from + StartGrace;
        var times = hits.Where(t => t >= windowStart).ToList();
        var points = new List<DateTime> { windowStart };
        points.AddRange(times);
        points.Add(end);
        double maxGap = 0;
        DateTime gapAt = windowStart;
        for (int i = 1; i < points.Count; i++)
        {
            var g = (points[i] - points[i - 1]).TotalSeconds;
            if (g > maxGap) { maxGap = g; gapAt = points[i - 1]; }
        }
        var excerpt = xs.Where(x => Matches(x.Command, c.Command!)).Take(5).Select(Line).ToList();
        var inv = CultureInfo.GetCultureInfo("sv-SE");
        double perSec = hits.Count / Math.Max(1, (end - from).TotalSeconds);
        return maxGap <= c.MaxGapSeconds
            ? new(c.Name, c.Type, CheckStatus.Passed, string.Format(inv, L.T("TestEvaluator_0Requests100S"), hits.Count, perSec, maxGap), excerpt)
            : new(c.Name, c.Type, CheckStatus.Failed, string.Format(inv, L.T("TestEvaluator_LongestPause000S"), maxGap, gapAt, c.MaxGapSeconds, hits.Count), excerpt);
    }

    private static CheckResult RequestSeen(TestCheck c, List<Exchange> xs)
    {
        var hits = xs.Where(x => Matches(x.Command, c.Command!)).ToList();
        return hits.Count >= c.MinCount
            ? new(c.Name, c.Type, CheckStatus.Passed, L.F("TestEvaluator_0TimesFirst1HHMm", hits.Count, hits[0].Time), hits.Take(3).Select(Line).ToList())
            : NotSeen(c, hits.Count == 0 ? L.F("TestEvaluator_TheAppNeverSent0", c.Command) : L.F("TestEvaluator_Only0Of1Times", hits.Count, c.MinCount));
    }

    private static CheckResult ResponseContains(TestCheck c, List<Exchange> xs)
    {
        var hits = xs.Where(x => Matches(x.Command, c.Command!)).ToList();
        if (hits.Count == 0) return NotSeen(c, L.F("TestEvaluator_TheAppNeverSent0", c.Command));
        var want = Compact(c.Text!);
        var ok = hits.FirstOrDefault(x => Compact(x.Response).Contains(want, StringComparison.Ordinal));
        return ok is not null
            ? new(c.Name, c.Type, CheckStatus.Passed, L.F("TestEvaluator_TheReplyContained01HH", c.Text, ok.Time), [Line(ok)])
            : new(c.Name, c.Type, CheckStatus.Failed, L.F("TestEvaluator_NoneOf0RepliesContained1", hits.Count, c.Text), hits.TakeLast(3).Select(Line).ToList());
    }

    private static CheckResult NoErrors(TestCheck c, List<Exchange> xs)
    {
        // "?" räknas bara för OBD/UDS-frågor (appar provar ofta okända AT/ST-kommandon för att känna igen adaptern).
        var bad = xs.Where(x =>
            (x.Command.All(Uri.IsHexDigit) && Compact(x.Response) == "?")
            || (x.Command.StartsWith("01", StringComparison.Ordinal) && x.Command.All(Uri.IsHexDigit) && Compact(x.Response).Contains("NODATA"))).ToList();
        return bad.Count == 0
            ? new(c.Name, c.Type, CheckStatus.Passed, L.F("TestEvaluator_0RequestsWithoutErrorReplies", xs.Count), [])
            : new(c.Name, c.Type, CheckStatus.Failed, $"{bad.Count} felsvar.", bad.Take(5).Select(Line).ToList());
    }

    // ---------------- Rapport ----------------

    public static string ToJson(TestResult r) => JsonSerializer.Serialize(r, new JsonSerializerOptions
    {
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    });

    public static string ToHtml(TestResult r)
    {
        // Koda bara det som måste kodas (å, ä, ö blir kvar som läsbar text; filen är UTF-8).
        string E(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        string Status(CheckStatus s) => s switch
        {
            CheckStatus.Passed => L.T("TestEvaluator_SpanClassOkPassedSpan"),
            CheckStatus.NotDone => L.T("TestEvaluator_SpanClassNdNotPerformedOptional"),
            _ => L.T("TestEvaluator_SpanClassFailFailedSpan"),
        };
        var sb = new StringBuilder();
        sb.Append(L.T("TestEvaluator_DoctypeHtmlHtmlHeadMetaCharset")).Append(E(r.TestName)).Append("</title><style>")
          .Append("body{font-family:Segoe UI,Arial,sans-serif;margin:24px;color:#0f172a}h1{margin:0 0 4px}.muted{color:#64748b}")
          .Append("table{border-collapse:collapse;width:100%;margin:12px 0}td,th{border-bottom:1px solid #e3e7ef;padding:8px;text-align:left;vertical-align:top}")
          .Append(".ok{color:#16a34a;font-weight:600}.fail{color:#dc2626;font-weight:600}.nd{color:#b45309;font-weight:600}")
          .Append("pre{background:#f8fafc;padding:6px;margin:4px 0 0;font-size:12px;white-space:pre-wrap}.big{font-size:20px;font-weight:700}")
          .Append("</style></head><body>");
        sb.Append("<h1>").Append(E(r.TestName)).Append("</h1><div class=muted>").Append(E(r.Description)).Append("</div>");
        sb.Append("<p class=big>").Append(r.Passed ? L.T("TestEvaluator_BigPassed") : L.T("TestEvaluator_BigFailed"))
          .Append(L.F("TestEvaluator_0Of1ChecksPassedP", r.PassedCount, r.Checks.Count));
        sb.Append(L.F("TestEvaluator_PClassMuted0YyyyMM", r.Start, r.End, r.RequestCount, E(string.Join(", ", r.Clients))));
        sb.Append("<h2>Kontroller</h2><table><tr><th>Kontroll</th><th>Resultat</th><th>Detaljer</th></tr>");
        foreach (var c in r.Checks)
        {
            sb.Append("<tr><td>").Append(E(c.Name)).Append("</td><td>").Append(Status(c.Status)).Append("</td><td>").Append(E(c.Detail));
            if (c.Excerpt.Count > 0) sb.Append("<pre>").Append(E(string.Join("\n", c.Excerpt))).Append("</pre>");
            sb.Append("</td></tr>");
        }
        sb.Append(L.T("TestEvaluator_TableH2StepsH2TableTr"));
        foreach (var s in r.Steps)
            sb.Append($"<tr><td>{s.Index + 1}</td><td>{s.At:0} s</td><td>{E(s.Description)}</td><td>{(s.DoneAt is { } d ? d.ToString("HH:mm:ss") : "–")}</td></tr>");
        sb.Append(L.T("TestEvaluator_TablePClassMutedCyberdromeSimulator"));
        return sb.ToString();
    }

    public static string ReportFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create), "Cyberdrome", "testresultat");

    /// <summary>Sparar rapporten som HTML och JSON. Returnerar sökvägen till HTML-filen.</summary>
    public static string SaveReport(TestResult r, string? folder = null)
    {
        folder ??= ReportFolder;
        Directory.CreateDirectory(folder);
        var baseName = $"{r.Start:yyyy-MM-dd_HH-mm-ss}_{r.TestId}";
        File.WriteAllText(Path.Combine(folder, baseName + ".json"), ToJson(r));
        var html = Path.Combine(folder, baseName + ".html");
        File.WriteAllText(html, ToHtml(r));
        return html;
    }
}

