// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

using System.Text.Json.Serialization;

namespace Cyberdrome.Core.Profiles;

/// <summary>
/// En virtuell bilmodell. Läses från en JSON-fil i mappen "profiles".
/// Funktionsnamnen är publika, men byte/bit-platserna är påhittade exempel.
/// </summary>
public sealed class CarProfile
{
    public string Id { get; set; } = "";
    public string Make { get; set; } = "";
    public string Model { get; set; } = "";
    public string Generation { get; set; } = "";
    public int Year { get; set; }
    public string Vin { get; set; } = "";

    /// <summary>ELM327-protokollnummer, t.ex. "6" = ISO 15765-4 CAN (11 bit, 500 kbaud).</summary>
    public string Protocol { get; set; } = "6";

    public List<EcuProfile> Ecus { get; set; } = new();
    public List<CodingFeature> Features { get; set; } = new();

    /// <summary>Felkoder som finns i bilen när den startas.</summary>
    public List<string> StartDtcs { get; set; } = new();

    /// <summary>Påhittad seed/key-formel för UDS 0x27 (valfri; standardformel om den saknas).</summary>
    public SecurityAccessConfig? SecurityAccess { get; set; }

    /// <summary>Värden bilen startar med när modellen väljs (valfritt).</summary>
    public ProfileDefaults? DefaultValues { get; set; }

    /// <summary>
    /// PID:ar (mode 01, hex, t.ex. "0D") som bilen svarar på. Saknas = alla som simulatorn kan.
    /// Påverkar även stödtabellerna 0100/0120/0140.
    /// </summary>
    public List<string>? SupportedPids { get; set; }

    /// <summary>Skärmförval som passar bilen (valfritt, t.ex. "10in-1280x800").</summary>
    public string? ScreenPreset { get; set; }

    /// <summary>Tema i Android (valfritt; id för ett inbyggt eller eget tema).</summary>
    public string? Theme { get; set; }

    /// <summary>Texter till felkoder, t.ex. "P0300" → "Slumpmässig feltändning" (från ODX eller CSV).</summary>
    public Dictionary<string, string>? DtcDescriptions { get; set; }

    /// <summary>Egna PID:ar (Torque/Car Scanner-format) som simulatorn svarar på.</summary>
    public List<CustomPid>? CustomPids { get; set; }

    /// <summary>Tillverkarspecifika frågor med signaler (t.ex. från OBDb) som simulatorn svarar på.</summary>
    public List<ExtraCommand>? ExtraCommands { get; set; }

    /// <summary>Källhänvisning för data i modellen (t.ex. "Data från OBDb, CC BY-SA 4.0").</summary>
    public string? Attribution { get; set; }

    /// <summary>True för egna (importerade/kopierade) bilmodeller. Sätts av programmet, sparas inte.</summary>
    [JsonIgnore] public bool IsOwn { get; set; }

    /// <summary>Filen modellen lästes från (egna modeller).</summary>
    [JsonIgnore] public string? FilePath { get; set; }

    [JsonIgnore]
    public string ListName => IsOwn ? DisplayName + "  (Egen)" : DisplayName;

    [JsonIgnore]
    public string DisplayName => $"{Make} {Model} · {Year}";

    public EcuProfile? FindEcuByRequestId(int requestId) => Ecus.FirstOrDefault(e => e.RequestIdValue == requestId);
}

/// <summary>Startvärden för en bilmodell.</summary>
public sealed class ProfileDefaults
{
    public double? Speed { get; set; }
    public double? Rpm { get; set; }
    public double? Coolant { get; set; }
    public double? Fuel { get; set; }
    public double? BatteryVolt { get; set; }
}

/// <summary>Ett styrdon (ECU) i bilen.</summary>
public sealed class EcuProfile
{
    /// <summary>Kort namn, t.ex. "motor" eller "info".</summary>
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";

    /// <summary>CAN-id för fråga, hex, t.ex. "7E0".</summary>
    public string RequestId { get; set; } = "7E0";

    /// <summary>CAN-id för svar, hex, t.ex. "7E8".</summary>
    public string ResponseId { get; set; } = "7E8";

    /// <summary>Kodningsdata i hex. Längden bestämmer hur många byte som finns.</summary>
    public string Coding { get; set; } = "";

    /// <summary>UDS-identifierare (DID) där kodningen läses/skrivs, hex.</summary>
    public string CodingDid { get; set; } = "0600";

    /// <summary>Egen seed/key-formel för just detta styrdon (valfri; annars bilens).</summary>
    public SecurityAccessConfig? SecurityAccess { get; set; }

    [JsonIgnore] public int RequestIdValue => Convert.ToInt32(RequestId, 16);
    [JsonIgnore] public int ResponseIdValue => Convert.ToInt32(ResponseId, 16);
    [JsonIgnore] public int CodingDidValue => Convert.ToInt32(CodingDid, 16);
}

/// <summary>En dold funktion som slås på/av med en bit i en ECU:s kodning.</summary>
public sealed class CodingFeature
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";

    /// <summary>Vilken ECU (Key) som håller funktionen.</summary>
    public string Ecu { get; set; } = "";
    public int Byte { get; set; }
    public int Bit { get; set; }
}
