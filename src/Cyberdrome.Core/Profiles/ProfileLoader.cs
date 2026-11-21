// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

using System.Text.Json;

namespace Cyberdrome.Core.Profiles;

/// <summary>Läser bilprofiler (JSON) från en mapp och kontrollerar att de är rimliga.</summary>
public static class ProfileLoader
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
    };

    public static List<CarProfile> LoadFolder(string folder)
    {
        var list = new List<CarProfile>();
        if (!Directory.Exists(folder)) return list;
        foreach (var file in Directory.EnumerateFiles(folder, "*.json").Order())
            list.Add(LoadFile(file));
        return list;
    }

    public static CarProfile LoadFile(string path) => Parse(File.ReadAllText(path), Path.GetFileName(path));

    public static CarProfile Parse(string json, string source = "profil")
    {
        var p = JsonSerializer.Deserialize<CarProfile>(json, JsonOptions)
                ?? throw new InvalidDataException(L.F("ProfileLoader_0EmptyFile", source));
        var errors = Validate(p);
        if (errors.Count > 0)
            throw new InvalidDataException($"{source}: " + string.Join(" ", errors));
        return p;
    }

    /// <summary>Mode 01-PID:ar som simulatorn kan svara på (utöver stödtabellerna 00/20/40).</summary>
    public static readonly byte[] KnownPids = [0x01, 0x04, 0x05, 0x0C, 0x0D, 0x0F, 0x11, 0x1C, 0x1F, 0x2F, 0x42, 0x46, 0x51];

    /// <summary>Högst så många byte kodning per styrdon.</summary>
    public const int MaxCodingBytes = 4;

    /// <summary>Returnerar en lista med fel på svenska. Tom lista = profilen är ok.</summary>
    public static List<string> Validate(CarProfile p)
    {
        var e = new List<string>();
        if (string.IsNullOrWhiteSpace(p.Id)) e.Add(L.T("ProfileLoader_IdIsMissing"));
        else if (!p.Id.All(c => char.IsLetterOrDigit(c) || c is '-' or '_')) e.Add(L.T("ProfileLoader_TheIdMayOnlyContainLetters"));
        if (string.IsNullOrWhiteSpace(p.Make) || string.IsNullOrWhiteSpace(p.Model)) e.Add(L.T("ProfileLoader_MakeAndModelMustBeFilled"));
        if (p.Year is < 1996 or > 2100) e.Add(L.T("ProfileLoader_TheModelYearMustBe1996"));
        if (p.Vin.Length != 17) e.Add(L.T("ProfileLoader_TheVINMustBe17Characters"));
        else if (p.Vin.Any(c => !char.IsLetterOrDigit(c) || "IOQ".Contains(char.ToUpperInvariant(c)))) e.Add(L.T("ProfileLoader_TheVINMayOnlyContainA"));
        if (p.Ecus.Count == 0) e.Add(L.T("ProfileLoader_AtLeastOneECUIsNeeded"));
        if (p.Ecus.GroupBy(x => x.Key).Any(g => g.Count() > 1)) e.Add(L.T("ProfileLoader_TwoControlUnitsHaveTheSame"));
        if (p.Ecus.GroupBy(x => x.RequestId.ToUpperInvariant()).Any(g => g.Count() > 1)) e.Add(L.T("ProfileLoader_TwoControlUnitsHaveTheSame2"));
        foreach (var ecu in p.Ecus)
        {
            if (string.IsNullOrWhiteSpace(ecu.Key)) e.Add(L.T("ProfileLoader_AControlUnitHasNoKey"));
            if (!IsHex(ecu.RequestId) || !IsHex(ecu.ResponseId) || ecu.RequestId.Length > 3 || ecu.ResponseId.Length > 3)
                e.Add(L.F("ProfileLoader_ECU0InvalidCANId11", ecu.Key));
            if (ecu.Coding.Length % 2 != 0 || (ecu.Coding.Length > 0 && !IsHex(ecu.Coding))) e.Add(L.F("ProfileLoader_ECU0CodingMustBeHex", ecu.Key));
            else if (ecu.Coding.Length / 2 > MaxCodingBytes) e.Add(L.F("ProfileLoader_ECU0AtMost1Bytes", ecu.Key, MaxCodingBytes));
            if (!IsHex(ecu.CodingDid) || ecu.CodingDid.Length != 4) e.Add(L.F("ProfileLoader_ECU0TheCodingDIDMust", ecu.Key));
        }
        if (p.Features.GroupBy(f => f.Id).Any(g => g.Count() > 1)) e.Add(L.T("ProfileLoader_TwoHiddenFeaturesHaveTheSame"));
        foreach (var d in p.StartDtcs)
            if (d.Length != 5 || !"PCBU".Contains(char.ToUpperInvariant(d[0])) || !d[1..].All(Uri.IsHexDigit)) e.Add(L.F("ProfileLoader_TheTroubleCode0HasThe", d));
        if (p.SupportedPids is { } pids)
            foreach (var pid in pids)
                if (pid.Length != 2 || !IsHex(pid) || !KnownPids.Contains(Convert.ToByte(pid, 16))) e.Add(L.F("ProfileLoader_PID0IsNotSupportedBy", pid));
        if (p.DefaultValues is { } dv)
        {
            if (dv.Speed is < 0 or > 250) e.Add(L.T("ProfileLoader_TheDefaultSpeedMustBe0"));
            if (dv.Rpm is < 0 or > 8000) e.Add(L.T("ProfileLoader_TheDefaultEngineSpeedMustBe"));
            if (dv.Coolant is < -40 or > 130) e.Add(L.T("ProfileLoader_TheDefaultTemperatureMustBe40"));
            if (dv.Fuel is < 0 or > 100) e.Add(L.T("ProfileLoader_TheDefaultFuelLevelMustBe"));
        }
        foreach (var f in p.Features)
        {
            var ecu = p.Ecus.FirstOrDefault(x => x.Key == f.Ecu);
            if (ecu is null) { e.Add(L.F("ProfileLoader_Feature0ECU1DoesNot", f.Id, f.Ecu)); continue; }
            if (f.Byte < 0 || f.Byte >= ecu.Coding.Length / 2) e.Add(L.F("ProfileLoader_Feature0Byte1DoesNot", f.Id, f.Byte));
            if (f.Bit is < 0 or > 7) e.Add(L.F("ProfileLoader_Feature0BitMustBe0", f.Id));
        }
        foreach (var c in p.CustomPids ?? [])
        {
            if (c.Request.Length is < 2 or > 14 || c.Request.Length % 2 != 0 || !IsHex(c.Request)) e.Add(L.F("ProfileLoader_CustomPID0InvalidRequest1", c.Name, c.Request));
            if (!string.IsNullOrEmpty(c.Header) && (c.Header.Length > 3 || !IsHex(c.Header))) e.Add(L.F("ProfileLoader_CustomPID0InvalidHeader1", c.Name, c.Header));
        }
        foreach (var x in p.ExtraCommands ?? [])
        {
            if (x.Request.Length is < 2 or > 14 || x.Request.Length % 2 != 0 || !IsHex(x.Request)) e.Add(L.F("ProfileLoader_ManufacturerRequest01InvalidRequest", x.Header, x.Request));
            else if (p.Ecus.All(ecu => !ecu.RequestId.Equals(x.Header, StringComparison.OrdinalIgnoreCase))) e.Add(L.F("ProfileLoader_ManufacturerRequest0ControlUnit1", x.Request, x.Header));
            if (x.Signals.Any(s => s.Bix < 0 || s.Len is < 1 or > 63)) e.Add(L.F("ProfileLoader_ManufacturerRequest01InvalidSignal", x.Header, x.Request));
        }
        foreach (var code in p.DtcDescriptions?.Keys ?? Enumerable.Empty<string>())
            if (code.Length != 5 || !"PCBU".Contains(char.ToUpperInvariant(code[0])) || !IsHex(code[1..])) e.Add(L.F("ProfileLoader_TroubleCodeTextInvalidTroubleCode", code));
        return e;
    }

    private static bool IsHex(string s) => s.Length > 0 && s.All(Uri.IsHexDigit);
}
