// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Cyberdrome.Core.Profiles;

/// <summary>
/// COVESA Vehicle Signal Specification (VSS, öppen standard, MPL-2.0): standardnamn för fordonssignaler.
/// Varje värde i Cyberdrome har ett VSS-namn; export/import som platt JSON ("Vehicle.Speed": 54), samma form som
/// används av VISS/KUKSA-dataservrar.
/// </summary>
public static class Vss
{
    public sealed record Signal(string Path, string Unit, string Description, string? ObdPid);

    public static readonly IReadOnlyList<Signal> Signals =
    [
        new("Vehicle.Speed", "km/h", "Fordonets hastighet", "0D"),
        new("Vehicle.Powertrain.CombustionEngine.Speed", "rpm", "Motorvarvtal", "0C"),
        new("Vehicle.Powertrain.CombustionEngine.ECT", "celsius", "Kylvätsketemperatur", "05"),
        new("Vehicle.Powertrain.FuelSystem.RelativeLevel", "percent", "Bränslenivå", "2F"),
        new("Vehicle.OBD.ThrottlePosition", "percent", "Gasspjällsläge", "11"),
        new("Vehicle.OBD.IntakeTemp", "celsius", "Insugsluftens temperatur", "0F"),
        new("Vehicle.Exterior.AirTemperature", "celsius", "Utetemperatur", "46"),
        new("Vehicle.LowVoltageBattery.CurrentVoltage", "V", "Batterispänning", "42"),
        new("Vehicle.Powertrain.Transmission.SelectedGear", "", "Vald växel (126 = P, -1 = R, 0 = N, 127 = D)", null),
        new("Vehicle.LowVoltageSystemState", "", "Tändning (ON/OFF)", null),
        new("Vehicle.OBD.DTCList", "", "Aktiva felkoder", "03"),
        new("Vehicle.VehicleIdentification.VIN", "", "VIN", null),
        new("Vehicle.VehicleIdentification.Brand", "", "Märke", null),
        new("Vehicle.VehicleIdentification.Model", "", "Modell", null),
        new("Vehicle.VehicleIdentification.Year", "", "Årsmodell", null),
    ];

    public static int GearToVss(Gear g) => g switch { Gear.P => 126, Gear.R => -1, Gear.N => 0, _ => 127 };
    public static Gear GearFromVss(int v) => v switch { 126 => Gear.P, < 0 => Gear.R, 0 => Gear.N, _ => Gear.D };

    /// <summary>Bilens nuvarande värden och identitet som platt VSS-JSON.</summary>
    public static string Export(CarProfile p, VehicleState s)
    {
        var o = new JsonObject
        {
            ["Vehicle.VehicleIdentification.VIN"] = p.Vin,
            ["Vehicle.VehicleIdentification.Brand"] = p.Make,
            ["Vehicle.VehicleIdentification.Model"] = p.Model,
            ["Vehicle.VehicleIdentification.Year"] = p.Year,
            ["Vehicle.Speed"] = Math.Round(s.SpeedKmh, 1),
            ["Vehicle.Powertrain.CombustionEngine.Speed"] = Math.Round(s.Rpm),
            ["Vehicle.Powertrain.CombustionEngine.ECT"] = Math.Round(s.CoolantC, 1),
            ["Vehicle.Powertrain.FuelSystem.RelativeLevel"] = Math.Round(s.FuelPercent, 1),
            ["Vehicle.OBD.ThrottlePosition"] = Math.Round(s.ThrottlePercent, 1),
            ["Vehicle.OBD.IntakeTemp"] = Math.Round(s.IntakeC, 1),
            ["Vehicle.Exterior.AirTemperature"] = Math.Round(s.AmbientC, 1),
            ["Vehicle.LowVoltageBattery.CurrentVoltage"] = Math.Round(s.BatteryVolt, 2),
            ["Vehicle.Powertrain.Transmission.SelectedGear"] = GearToVss(s.Gear),
            ["Vehicle.LowVoltageSystemState"] = s.Ignition ? "ON" : "OFF",
            ["Vehicle.OBD.DTCList"] = new JsonArray(s.Dtcs.Select(d => (JsonNode)d!).ToArray()),
        };
        return o.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>Läser platt VSS-JSON (eller {"Vehicle.Speed": {"value": 54}}) och sätter bilens värden. Returnerar okända namn.</summary>
    public static IReadOnlyList<string> Import(string json, VehicleState s, CarProfile? profile = null)
    {
        var unknown = new List<string>();
        JsonObject obj;
        try { obj = JsonNode.Parse(json) as JsonObject ?? throw new InvalidDataException("VSS-filen måste vara ett JSON-objekt."); }
        catch (JsonException ex) { throw new InvalidDataException("VSS-filen går inte att läsa: " + ex.Message); }
        foreach (var (path, node) in obj)
        {
            var v = node is JsonObject wrap && wrap["value"] is { } inner ? inner : node;
            double D() => v!.GetValueKind() == JsonValueKind.String ? double.Parse(v.GetValue<string>(), CultureInfo.InvariantCulture) : v!.GetValue<double>();
            try
            {
                switch (path)
                {
                    case "Vehicle.Speed": s.SpeedKmh = D(); break;
                    case "Vehicle.Powertrain.CombustionEngine.Speed": s.Rpm = D(); break;
                    case "Vehicle.Powertrain.CombustionEngine.ECT": s.CoolantC = D(); break;
                    case "Vehicle.Powertrain.FuelSystem.RelativeLevel": s.FuelPercent = D(); break;
                    case "Vehicle.OBD.ThrottlePosition": s.ThrottlePercent = D(); break;
                    case "Vehicle.OBD.IntakeTemp": s.IntakeC = D(); break;
                    case "Vehicle.Exterior.AirTemperature": s.AmbientC = D(); break;
                    case "Vehicle.LowVoltageBattery.CurrentVoltage": s.BatteryVolt = D(); break;
                    case "Vehicle.Powertrain.Transmission.SelectedGear": s.Gear = GearFromVss((int)D()); break;
                    case "Vehicle.LowVoltageSystemState": s.Ignition = (v?.ToString() ?? "").ToUpperInvariant() is "ON" or "START"; break;
                    case "Vehicle.OBD.DTCList": s.SetDtcs((v as JsonArray)?.Select(x => x!.GetValue<string>().ToUpperInvariant()) ?? []); break;
                    case "Vehicle.VehicleIdentification.VIN": if (profile is not null) profile.Vin = v!.GetValue<string>(); break;
                    case "Vehicle.VehicleIdentification.Brand": if (profile is not null) profile.Make = v!.GetValue<string>(); break;
                    case "Vehicle.VehicleIdentification.Model": if (profile is not null) profile.Model = v!.GetValue<string>(); break;
                    case "Vehicle.VehicleIdentification.Year": if (profile is not null) profile.Year = (int)D(); break;
                    default: unknown.Add(path); break;
                }
            }
            catch (Exception) when (path.StartsWith("Vehicle.", StringComparison.Ordinal)) { unknown.Add(path + " (fel värde)"); }
        }
        return unknown;
    }
}

/// <summary>
/// Egna PID:ar i formatet som Torque och Car Scanner använder (CSV):
/// Name,ShortName,ModeAndPID,Equation,Min Value,Max Value,Units,Header
/// Simulatorn svarar på importerade PID:ar, så samma fil fungerar i appen och i Cyberdrome.
/// </summary>
public static class TorqueCsv
{
    public const string HeaderLine = "Name,ShortName,ModeAndPID,Equation,Min Value,Max Value,Units,Header";

    public static List<CustomPid> Parse(string csv, List<string>? problems = null)
    {
        var list = new List<CustomPid>();
        foreach (var raw in csv.Replace("\r", "").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith("Name", StringComparison.OrdinalIgnoreCase)) continue;
            var f = SplitCsv(line);
            if (f.Count < 4) { problems?.Add($"För få fält: {line}"); continue; }
            var req = f[2].Trim().Replace("0x", "", StringComparison.OrdinalIgnoreCase).Replace(" ", "").ToUpperInvariant();
            if (req.Length < 2 || req.Length % 2 != 0 || !req.All(Uri.IsHexDigit)) { problems?.Add($"Ogiltigt ModeAndPID \"{f[2]}\" ({f[0]})"); continue; }
            double Num(int i, double def) => f.Count > i && double.TryParse(f[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : def;
            var pid = new CustomPid
            {
                Name = f[0].Trim(), ShortName = f.Count > 1 ? f[1].Trim() : "", Request = req, Equation = f[3].Trim(),
                Min = Num(4, 0), Max = Num(5, 255), Unit = f.Count > 6 ? f[6].Trim() : "",
                Header = f.Count > 7 ? f[7].Trim().Replace("0x", "", StringComparison.OrdinalIgnoreCase).ToUpperInvariant() : null,
            };
            if (string.IsNullOrEmpty(pid.Header)) pid.Header = null;
            pid.Value = Math.Round((pid.Min + pid.Max) / 2, 2);
            list.Add(pid);
        }
        return list;
    }

    public static string Write(IEnumerable<CustomPid> pids)
    {
        var sb = new StringBuilder(HeaderLine).Append('\n');
        string Q(string s) => s.Contains(',') || s.Contains('"') ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
        foreach (var p in pids)
            sb.Append(string.Join(",", Q(p.Name), Q(p.ShortName), "0x" + p.Request, Q(p.Equation),
                p.Min.ToString(CultureInfo.InvariantCulture), p.Max.ToString(CultureInfo.InvariantCulture), Q(p.Unit), p.Header ?? "")).Append('\n');
        return sb.ToString();
    }

    private static List<string> SplitCsv(string line)
    {
        var res = new List<string>();
        var cur = new StringBuilder();
        bool q = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (q) { if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { cur.Append('"'); i++; } else if (c == '"') q = false; else cur.Append(c); }
            else if (c == '"') q = true;
            else if (c == ',') { res.Add(cur.ToString()); cur.Clear(); }
            else cur.Append(c);
        }
        res.Add(cur.ToString());
        return res;
    }

    /// <summary>Standard-PID:ar som också kan behöva egna rader (VIN, kodning per styrdon).</summary>
    public static List<CustomPid> FromProfile(CarProfile p)
    {
        var list = new List<CustomPid>(p.CustomPids ?? []);
        foreach (var e in p.Ecus)
            if (list.All(x => !(x.Request == "22" + e.CodingDid && x.Header == e.RequestId)))
                list.Add(new CustomPid { Name = $"Kodning {e.Name}", ShortName = "KOD_" + e.Key.ToUpperInvariant(), Request = "22" + e.CodingDid, Equation = "A", Min = 0, Max = 255, Header = e.RequestId });
        return list;
    }
}

/// <summary>En egen PID (Torque/Car Scanner-format) som simulatorn svarar på.</summary>
public sealed class CustomPid
{
    public string Name { get; set; } = "";
    public string ShortName { get; set; } = "";
    /// <summary>Mode + PID i hex, t.ex. "221234" eller "015C".</summary>
    public string Request { get; set; } = "";
    public string Equation { get; set; } = "A";
    public double Min { get; set; }
    public double Max { get; set; } = 255;
    public string Unit { get; set; } = "";
    /// <summary>Styrdonets CAN-id (ATSH), t.ex. "7E0". Tomt = svarar på allmänna frågor.</summary>
    public string? Header { get; set; }
    /// <summary>Värdet simulatorn rapporterar (i formelns enhet).</summary>
    public double Value { get; set; }

    /// <summary>
    /// Räknar ut databyten för <see cref="Value"/> genom att vända enkla formler:
    /// A, A*k, A/k, A+k, A-k, (A*256+B), (A*256+B)/k, A*100/255. Andra formler: värdet som en byte.
    /// </summary>
    public byte[] Encode()
    {
        var eq = Equation.Replace(" ", "").ToUpperInvariant();
        double v = Value;
        static byte B(double x) => (byte)Math.Clamp(Math.Round(x), 0, 255);
        static byte[] U16(double x) { var n = (int)Math.Clamp(Math.Round(x), 0, 65535); return [(byte)(n >> 8), (byte)n]; }
        static double? K(string s) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;
        if (eq is "A") return [B(v)];
        if (eq is "(A*256+B)" or "A*256+B" or "(A*256)+B") return U16(v);
        if (eq.StartsWith("(A*256+B)/", StringComparison.Ordinal) && K(eq[10..]) is { } div16) return U16(v * div16);
        if (eq.StartsWith("((A*256)+B)/", StringComparison.Ordinal) && K(eq[12..]) is { } div16b) return U16(v * div16b);
        if (eq is "A*100/255" or "(A*100)/255") return [B(v * 255 / 100)];
        if (eq.StartsWith("A-", StringComparison.Ordinal) && K(eq[2..]) is { } sub) return [B(v + sub)];
        if (eq.StartsWith("A+", StringComparison.Ordinal) && K(eq[2..]) is { } add) return [B(v - add)];
        if (eq.StartsWith("A*", StringComparison.Ordinal) && K(eq[2..]) is { } mul && mul != 0) return [B(v / mul)];
        if (eq.StartsWith("A/", StringComparison.Ordinal) && K(eq[2..]) is { } dv) return [B(v * dv)];
        return [B(v)];
    }
}

/// <summary>Felkodstexter från CSV i SAE J2012-stil: "P0300;Slumpmässig feltändning" (semikolon eller komma).</summary>
public static class DtcCsv
{
    public static Dictionary<string, string> Parse(string csv, List<string>? problems = null)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in csv.Replace("\r", "").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            int sep = line.IndexOfAny([';', ',', '\t']);
            if (sep < 0) { problems?.Add("Saknar avgränsare: " + line); continue; }
            var code = line[..sep].Trim().Trim('"').ToUpperInvariant();
            var text = line[(sep + 1)..].Trim().Trim('"');
            if (code is "CODE" or "KOD" or "DTC") continue; // rubrikrad
            if (code.Length != 5 || !"PCBU".Contains(code[0]) || !code[1..].All(Uri.IsHexDigit)) { problems?.Add("Ogiltig felkod: " + code); continue; }
            d[code] = text;
        }
        return d;
    }
}
