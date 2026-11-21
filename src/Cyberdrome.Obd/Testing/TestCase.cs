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
    public static TestCase FromJson(string json) => JsonSerializer.Deserialize<TestCase>(json, Json) ?? throw new InvalidDataException("Tomt testfall.");

    public static readonly string[] CheckTypes = ["requestRate", "requestSeen", "responseContains", "noErrors"];
    public static readonly string[] StepActions = ["set", "addDtc", "removeDtc", "clearDtcs", "note"];

    public IReadOnlyList<string> Validate()
    {
        var e = new List<string>();
        if (string.IsNullOrWhiteSpace(Id)) e.Add("id saknas.");
        if (string.IsNullOrWhiteSpace(Name)) e.Add("name saknas.");
        if (DurationSeconds is <= 0 or > 3600) e.Add("durationSeconds ska vara 1–3600.");
        for (int i = 0; i < Steps.Count; i++)
        {
            var s = Steps[i];
            if (!StepActions.Contains(s.Action)) e.Add($"steg {i + 1}: okänd action \"{s.Action}\".");
            if (s.At < 0 || s.At > DurationSeconds) e.Add($"steg {i + 1}: at måste ligga inom testets tid.");
            if (s.Action is "addDtc" or "removeDtc" && string.IsNullOrWhiteSpace(s.Dtc)) e.Add($"steg {i + 1}: dtc saknas.");
        }
        foreach (var c in Checks)
        {
            if (!CheckTypes.Contains(c.Type)) e.Add($"kontroll \"{c.Name}\": okänd typ \"{c.Type}\".");
            if (c.Type != "noErrors" && string.IsNullOrWhiteSpace(c.Command)) e.Add($"kontroll \"{c.Name}\": command saknas.");
            if (c.Type == "responseContains" && string.IsNullOrWhiteSpace(c.Text)) e.Add($"kontroll \"{c.Name}\": text saknas.");
            if (c.AfterStep is { } a && (a < 0 || a >= Steps.Count)) e.Add($"kontroll \"{c.Name}\": afterStep finns inte.");
        }
        if (Checks.Count == 0) e.Add("minst en kontroll behövs.");
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
            Id = "grund-pid", Name = "Grundläggande PID:ar",
            Description = "Appen ska läsa hastighet och varvtal ofta (minst varannan sekund) utan felsvar. Värdena ändras under testet.",
            Values = new() { Speed = 50, Rpm = 2000, Coolant = 88, Fuel = 60, Ignition = true },
            DurationSeconds = 20,
            Steps =
            [
                new() { At = 0, Action = "note", Text = "Ha appen öppen och ansluten till OBD (t.ex. Cyberdrome Radio eller startskärmen)." },
                new() { At = 8, Action = "set", Values = new() { Speed = 90, Rpm = 3000 } },
                new() { At = 14, Action = "set", Values = new() { Speed = 30, Rpm = 1500 } },
            ],
            Checks =
            [
                new() { Type = "requestRate", Name = "Hastighet (01 0D) minst varannan sekund", Command = "010D", MaxGapSeconds = 2 },
                new() { Type = "requestRate", Name = "Varvtal (01 0C) minst varannan sekund", Command = "010C", MaxGapSeconds = 2 },
                new() { Type = "responseContains", Name = "Appen fick 90 km/h efter steg 2", Command = "010D", Text = "410D5A", AfterStep = 1 },
                new() { Type = "noErrors", Name = "Inga felsvar (? eller NO DATA)" },
            ],
        },
        new TestCase
        {
            Id = "felkoder", Name = "Felkoder: läs och radera",
            Description = "Cyberdrome tänder P0300. Appen ska läsa felkoderna (mode 03) och se P0300. Radera gärna i appen (mode 04).",
            Values = new() { Speed = 0, Rpm = 800, Ignition = true },
            DurationSeconds = 30,
            Steps =
            [
                new() { At = 0, Action = "clearDtcs" },
                new() { At = 3, Action = "addDtc", Dtc = "P0300", Text = "Felkod P0300 är nu aktiv – läs felkoder i appen." },
                new() { At = 20, Action = "note", Text = "Radera felkoderna i appen (valfritt)." },
            ],
            Checks =
            [
                new() { Type = "requestSeen", Name = "Appen läste felkoder (03) efter att P0300 tändes", Command = "03", AfterStep = 1 },
                new() { Type = "responseContains", Name = "Svaret innehöll P0300 (03 00)", Command = "03", Text = "0300", AfterStep = 1 },
                new() { Type = "requestSeen", Name = "Appen raderade felkoder (04)", Command = "04", Optional = true },
            ],
        },
        new TestCase
        {
            Id = "uds-kodning", Name = "Kodning via UDS 22",
            Description = "Appen ska välja instrumentet (ATSH714) och läsa kodningen med 22 06 00 (och gärna VIN med 22 F1 90).",
            Profile = "vw-golf8",
            DurationSeconds = 30,
            Steps = [new() { At = 0, Action = "note", Text = "Läs kodning i appen (ATSH714, 220600)." }],
            Checks =
            [
                new() { Type = "requestSeen", Name = "Appen valde styrdon med ATSH", Command = "ATSH714" },
                new() { Type = "responseContains", Name = "Kodningen lästes (62 06 00 …)", Command = "220600", Text = "620600" },
                new() { Type = "requestSeen", Name = "VIN lästes med 22 F1 90", Command = "22F190", Optional = true },
                new() { Type = "noErrors", Name = "Inga felsvar" },
            ],
        },
    ];
}
