// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cyberdrome.Obd.Testing;

/// <summary>Bilens värden som sätts i början av testet eller i ett steg.</summary>
public sealed class TestValues
{
    public double? Speed { get; set; }
    public double? Rpm { get; set; }
    public double? Coolant { get; set; }
    public double? Fuel { get; set; }
    public bool? Ignition { get; set; }
}

/// <summary>Ett steg som Cyberdrome utför under testet.</summary>
public sealed class TestStep
{
    /// <summary>Sekunder från teststart.</summary>
    public double At { get; set; }

    /// <summary>"set" (värden), "addDtc", "removeDtc", "clearDtcs" eller "note".</summary>
    public string Action { get; set; } = "note";

    public TestValues? Values { get; set; }
    public string? Dtc { get; set; }

    /// <summary>Text som visas för den som testar, t.ex. "Öppna felkoder i appen".</summary>
    public string? Text { get; set; }
}

/// <summary>
/// En kontroll som görs mot OBD-loggen när testet är klart.
/// Typer:
///  "requestRate"      – appen frågar efter <see cref="Command"/> minst en gång per <see cref="MaxGapSeconds"/> s.
///  "requestSeen"      – appen skickar <see cref="Command"/> (minst <see cref="MinCount"/> gånger), valfritt efter steg <see cref="AfterStep"/>.
///  "responseContains" – svaret på <see cref="Command"/> innehåller <see cref="Text"/> (mellanslag ignoreras), valfritt efter steg.
///  "noErrors"         – inga "?"-svar (okända kommandon) och inga "NO DATA" på mode 01.
/// </summary>
public sealed class TestCheck
{
    public string Type { get; set; } = "requestSeen";
    public string Name { get; set; } = "";
    public string? Command { get; set; }
    public string? Text { get; set; }
    public double MaxGapSeconds { get; set; } = 2;
    public int MinCount { get; set; } = 1;
    /// <summary>Index (0-baserat) på steget som måste ha skett först.</summary>
    public int? AfterStep { get; set; }
    /// <summary>Valfri kontroll: ger "Ej utförd" i stället för "Underkänd" om appen aldrig gjorde det.</summary>
    public bool Optional { get; set; }
}

/// <summary>Ett testfall (testfall.json).</summary>
public sealed class TestCase
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    /// <summary>Bilprofil (id), valfri.</summary>
    public string? Profile { get; set; }
    /// <summary>Körning som spelas upp under testet (namn), valfri.</summary>
    public string? Drive { get; set; }
    /// <summary>Fasta värden i början (om ingen körning).</summary>
    public TestValues? Values { get; set; }
    public double DurationSeconds { get; set; } = 30;
    public List<TestStep> Steps { get; set; } = new();
    public List<TestCheck> Checks { get; set; } = new();

    [JsonIgnore] public string? FilePath { get; set; }

    internal static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string ToJson() => JsonSerializer.Serialize(this, Json);
    public static TestCase FromJson(string json) => JsonSerializer.Deserialize<TestCase>(json, Json) ?? throw new InvalidDataException(L.T("TestCase_EmptyTestCase"));

    public static readonly string[] CheckTypes = ["requestRate", "requestSeen", "responseContains", "noErrors"];
    public static readonly string[] StepActions = ["set", "addDtc", "removeDtc", "clearDtcs", "note"];

    public IReadOnlyList<string> Validate()
    {
        var e = new List<string>();
        if (string.IsNullOrWhiteSpace(Id)) e.Add(L.T("TestCase_IdIsMissing"));
        if (string.IsNullOrWhiteSpace(Name)) e.Add(L.T("TestCase_NameIsMissing"));
        if (DurationSeconds is <= 0 or > 3600) e.Add(L.T("TestCase_DurationSecondsMustBe13600"));
        for (int i = 0; i < Steps.Count; i++)
        {
            var s = Steps[i];
            if (!StepActions.Contains(s.Action)) e.Add(L.F("TestCase_Step0UnknownAction1", i + 1, s.Action));
            if (s.At < 0 || s.At > DurationSeconds) e.Add(L.F("TestCase_Step0AtMustBeWithin", i + 1));
            if (s.Action is "addDtc" or "removeDtc" && string.IsNullOrWhiteSpace(s.Dtc)) e.Add(L.F("TestCase_Step0DtcIsMissing", i + 1));
        }
        foreach (var c in Checks)
        {
            if (!CheckTypes.Contains(c.Type)) e.Add(L.F("TestCase_Check0UnknownType1", c.Name, c.Type));
            if (c.Type != "noErrors" && string.IsNullOrWhiteSpace(c.Command)) e.Add(L.F("TestCase_Check0CommandIsMissing", c.Name));
            if (c.Type == "responseContains" && string.IsNullOrWhiteSpace(c.Text)) e.Add(L.F("TestCase_Check0TextIsMissing", c.Name));
            if (c.AfterStep is { } a && (a < 0 || a >= Steps.Count)) e.Add(L.F("TestCase_Check0AfterStepDoesNotExist", c.Name));
        }
        if (Checks.Count == 0) e.Add(L.T("TestCase_AtLeastOneCheckIsNeeded"));
        return e;
    }
}

/// <summary>Inbyggda exempel på testfall.</summary>
public static class BuiltInTestCases
{
    public static IReadOnlyList<TestCase> All =>
    [
        new TestCase
        {
            Id = "grund-pid", Name = L.T("TestCase_BasicPIDs"),
            Description = L.T("TestCase_TheAppShouldReadSpeedAnd"),
            Values = new() { Speed = 50, Rpm = 2000, Coolant = 88, Fuel = 60, Ignition = true },
            DurationSeconds = 20,
            Steps =
            [
                new() { At = 0, Action = "note", Text = L.T("TestCase_KeepTheAppOpenAndConnected") },
                new() { At = 8, Action = "set", Values = new() { Speed = 90, Rpm = 3000 } },
                new() { At = 14, Action = "set", Values = new() { Speed = 30, Rpm = 1500 } },
            ],
            Checks =
            [
                new() { Type = "requestRate", Name = L.T("TestCase_Speed010DAtLeastEvery"), Command = "010D", MaxGapSeconds = 2 },
                new() { Type = "requestRate", Name = L.T("TestCase_EngineSpeed010CAtLeast"), Command = "010C", MaxGapSeconds = 2 },
                new() { Type = "responseContains", Name = L.T("TestCase_TheAppGot90KmH"), Command = "010D", Text = "410D5A", AfterStep = 1 },
                new() { Type = "noErrors", Name = L.T("TestCase_NoErrorRepliesOrNODATA") },
            ],
        },
        new TestCase
        {
            Id = "felkoder", Name = L.T("TestCase_TroubleCodesReadAndClear"),
            Description = L.T("TestCase_CyberdromeTurnsOnP0300TheApp"),
            Values = new() { Speed = 0, Rpm = 800, Ignition = true },
            DurationSeconds = 30,
            Steps =
            [
                new() { At = 0, Action = "clearDtcs" },
                new() { At = 3, Action = "addDtc", Dtc = "P0300", Text = L.T("TestCase_TroubleCodeP0300IsNowActive") },
                new() { At = 20, Action = "note", Text = L.T("TestCase_ClearTheTroubleCodesInThe") },
            ],
            Checks =
            [
                new() { Type = "requestSeen", Name = L.T("TestCase_TheAppReadTroubleCodes03"), Command = "03", AfterStep = 1 },
                new() { Type = "responseContains", Name = L.T("TestCase_TheReplyContainedP03000300"), Command = "03", Text = "0300", AfterStep = 1 },
                new() { Type = "requestSeen", Name = L.T("TestCase_TheAppClearedTroubleCodes04"), Command = "04", Optional = true },
            ],
        },
        new TestCase
        {
            Id = "uds-kodning", Name = L.T("TestCase_CodingViaUDS22"),
            Description = L.T("TestCase_TheAppShouldSelectTheInstrument"),
            Profile = "vw-golf8",
            DurationSeconds = 30,
            Steps = [new() { At = 0, Action = "note", Text = L.T("TestCase_ReadTheCodingInTheApp") }],
            Checks =
            [
                new() { Type = "requestSeen", Name = L.T("TestCase_TheAppSelectedAControlUnit"), Command = "ATSH714" },
                new() { Type = "responseContains", Name = L.T("TestCase_TheCodingWasRead6206"), Command = "220600", Text = "620600" },
                new() { Type = "requestSeen", Name = L.T("TestCase_TheVINWasReadWith22"), Command = "22F190", Optional = true },
                new() { Type = "noErrors", Name = L.T("TestCase_NoErrorReplies") },
            ],
        },
    ];
}
