// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

using System.Text;
using Cyberdrome.Core;
using Cyberdrome.Core.Profiles;

namespace Cyberdrome.Obd.Real;

/// <summary>Ett styrdon som svarade vid avsökningen.</summary>
public sealed record FoundEcu(int RequestId, int ResponseId, bool AnswersObd, bool AnswersUds, string? Note);

/// <summary>Resultatet av "Läs riktig bil". Bara läsning har skickats till bilen.</summary>
public sealed class RealCarScan
{
    public AdapterInfo? Adapter { get; set; }
    public string? Vin { get; set; }
    public List<string> CalibrationIds { get; } = [];
    /// <summary>Stödda mode 01-PID:ar (hex) från motorstyrdonet.</summary>
    public List<string> SupportedPids { get; } = [];
    public List<string> Dtcs { get; } = [];
    public List<FoundEcu> Ecus { get; } = [];
    public Dictionary<string, double> Live { get; } = [];
    public List<string> Log { get; } = [];

    public string Summary()
    {
        var sb = new StringBuilder();
        if (Adapter is { } a)
        {
            sb.AppendLine($"Adapter: {a.Version}{(a.StnVersion is { } s ? " / " + s : "")}");
            sb.AppendLine(L.F("RealCarScanner_Protocol01", a.Protocol, a.ProtocolName));
            if (a.Voltage is { } v) sb.AppendLine(L.F("RealCarScanner_Voltage000V", v));
        }
        sb.AppendLine("VIN: " + (Vin ?? L.T("Scan_NoAnswer")));
        if (CalibrationIds.Count > 0) sb.AppendLine(L.T("Scan_CalibrationIds") + string.Join(", ", CalibrationIds));
        sb.AppendLine(L.F("Scan_Pids0", SupportedPids.Count) + string.Join(" ", SupportedPids));
        sb.AppendLine(L.T("Scan_Dtcs") + (Dtcs.Count == 0 ? L.T("Scan_None") : string.Join(", ", Dtcs)));
        sb.AppendLine(L.F("Scan_Ecus0", Ecus.Count));
        foreach (var e in Ecus)
            sb.AppendLine($"  {e.RequestId:X3} → {e.ResponseId:X3}  {(e.AnswersObd ? "OBD " : "")}{(e.AnswersUds ? "UDS " : "")}{e.Note}");
        if (Live.Count > 0) sb.AppendLine(L.T("RealCarScanner_Values") + string.Join(", ", Live.Select(kv => $"{kv.Key} {kv.Value:0.#}")));
        return sb.ToString().TrimEnd();
    }
}

/// <summary>Läser en riktig bil (bara lästjänster: 01, 03, 09, 22 F190).</summary>
public static class RealCarScanner
{
    /// <summary>Vanliga diagnosadresser (11 bit) utöver motor/växellåda 7E0–7E7.</summary>
    public static readonly int[] CommonUdsRequestIds = [0x6F1, 0x700, 0x70E, 0x710, 0x712, 0x713, 0x714, 0x715, 0x716, 0x720, 0x726, 0x740, 0x746, 0x74F, 0x750, 0x760, 0x765, 0x770, 0x773, 0x7A0, 0x7B0, 0x7C0, 0x7C6, 0x7D0];

    public static async Task<RealCarScan> ScanAsync(ObdAdapter a, bool probeUds = true, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var scan = new RealCarScan { Adapter = a.Info };
        void Step(string s) { scan.Log.Add(s); progress?.Report(s); }

        Step(L.T("RealCarScanner_ReadingSupportedPIDs"));
        var supported = new SortedSet<byte>();
        var obdEcus = new HashSet<int>();
        for (byte basePid = 0x00; basePid <= 0xC0; basePid += 0x20)
        {
            var (r, _) = await a.RequestAsync(ObdAdapter.FunctionalHeader, [0x01, basePid], 4000, ct).ConfigureAwait(false);
            var main = r.Where(x => x.Data.Length >= 6 && x.Data[0] == 0x41 && x.Data[1] == basePid).OrderBy(x => x.ResponseId).ToList();
            foreach (var x in main) obdEcus.Add(x.ResponseId);
            var first = main.FirstOrDefault();
            if (first is null) break;
            uint bits = (uint)(first.Data[2] << 24 | first.Data[3] << 16 | first.Data[4] << 8 | first.Data[5]);
            for (int i = 0; i < 32; i++)
                if ((bits & (1u << (31 - i))) != 0) supported.Add((byte)(basePid + i + 1));
            if ((bits & 1) == 0) break; // nästa tabell stöds inte
        }
        scan.SupportedPids.AddRange(supported.Where(p => p % 0x20 != 0).Select(p => p.ToString("X2")));

        Step(L.T("RealCarScanner_ReadingVIN"));
        var (vin, _) = await a.RequestAsync(ObdAdapter.FunctionalHeader, [0x09, 0x02], 5000, ct).ConfigureAwait(false);
        var v = vin.FirstOrDefault(x => x.Data.Length > 3 && x.Data[0] == 0x49);
        if (v is not null) scan.Vin = Ascii(v.Data.Skip(3)).Trim();

        Step(L.T("RealCarScanner_ReadingCalibrationIds"));
        var (cal, _) = await a.RequestAsync(ObdAdapter.FunctionalHeader, [0x09, 0x04], 5000, ct).ConfigureAwait(false);
        foreach (var c in cal.Where(x => x.Data.Length > 3 && x.Data[0] == 0x49))
            foreach (var chunk in c.Data.Skip(3).Chunk(16))
                if (Ascii(chunk).Trim() is { Length: > 0 } s) scan.CalibrationIds.Add($"{c.ResponseId:X3}: {s}");

        Step(L.T("RealCarScanner_ReadingTroubleCodes"));
        var (dtc, _) = await a.RequestAsync(ObdAdapter.FunctionalHeader, [0x03], 4000, ct).ConfigureAwait(false);
        foreach (var d in dtc.Where(x => x.Data.Length >= 1 && x.Data[0] == 0x43))
        {
            var bytes = d.Data.Skip(a.IsCan ? 2 : 1).ToArray(); // CAN: antal först
            for (int i = 0; i + 1 < bytes.Length; i += 2)
                if (bytes[i] != 0 || bytes[i + 1] != 0) scan.Dtcs.Add(DecodeDtc(bytes[i], bytes[i + 1]));
        }

        Step(L.T("RealCarScanner_ReadingValues"));
        foreach (var (pid, name) in LivePids)
        {
            if (!supported.Contains(pid)) continue;
            var (r, _) = await a.RequestAsync(ObdAdapter.FunctionalHeader, [0x01, pid], 2000, ct).ConfigureAwait(false);
            if (r.FirstOrDefault(x => x.Data.Length > 2 && x.Data[0] == 0x41 && x.Data[1] == pid) is { } x && Decode(pid, x.Data[2..]) is { } val)
                scan.Live[name] = val;
        }

        if (a.IsCan)
        {
            Step(L.T("RealCarScanner_SearchingForControlUnits"));
            var ids = Enumerable.Range(0x7E0, 8).Concat(probeUds ? CommonUdsRequestIds : []).Distinct().ToList();
            foreach (var req in ids)
            {
                ct.ThrowIfCancellationRequested();
                bool obd = false, uds = false; int resp = req + 8; string? note = null;
                if (req is >= 0x7E0 and <= 0x7E7)
                {
                    var (r, _) = await a.RequestAsync(req, [0x01, 0x00], 600, ct).ConfigureAwait(false);
                    if (r.FirstOrDefault(x => x.Data.Length > 0 && x.Data[0] == 0x41) is { } x) { obd = true; resp = x.ResponseId; }
                }
                if (probeUds)
                {
                    var (u, _) = await a.RequestAsync(req, [0x22, 0xF1, 0x90], 600, ct).ConfigureAwait(false);
                    if (u.FirstOrDefault() is { } y)
                    {
                        uds = true; resp = y.ResponseId;
                        if (y.Data.Length > 3 && y.Data[0] == 0x62) note = "VIN " + Ascii(y.Data.Skip(3)).Trim();
                        else if (y.Data.Length >= 3 && y.Data[0] == 0x7F) note = $"svarar (NRC {y.Data[2]:X2})";
                    }
                }
                if (obd || uds) scan.Ecus.Add(new FoundEcu(req, resp, obd, uds, note));
            }
        }
        foreach (var id in obdEcus.Where(id => scan.Ecus.All(e => e.ResponseId != id)))
            scan.Ecus.Add(new FoundEcu(id >= 0x7E8 && id <= 0x7EF ? id - 8 : 0x7DF, id, true, false, L.T("RealCarScanner_AnsweredOn7DF")));
        Step("Klar.");
        return scan;
    }

    private static readonly (byte Pid, string Name)[] LivePids =
        [(0x0D, "km/h"), (0x0C, "rpm"), (0x05, "kylv. °C"), (0x2F, L.T("Ui_Fuel2")), (0x11, L.T("Source_Throttle")), (0x0F, "insug °C"), (0x46, "ute °C"), (0x42, "volt")];

    public static double? Decode(byte pid, byte[] d) => pid switch
    {
        0x0D when d.Length >= 1 => d[0],
        0x0C when d.Length >= 2 => (d[0] * 256 + d[1]) / 4.0,
        0x05 or 0x0F or 0x46 when d.Length >= 1 => d[0] - 40,
        0x2F or 0x11 when d.Length >= 1 => d[0] * 100.0 / 255,
        0x42 when d.Length >= 2 => (d[0] * 256 + d[1]) / 1000.0,
        _ => null,
    };

    public static string DecodeDtc(byte a, byte b) => $"{"PCBU"[a >> 6]}{(a >> 4) & 0x3:X}{a & 0xF:X}{b:X2}";

    private static string Ascii(IEnumerable<byte> b) => new(b.Where(x => x is >= 0x20 and < 0x7F).Select(x => (char)x).ToArray());

    /// <summary>Märke från VIN:ets tillverkarkod (WMI). Grov tabell; bildatabasen ger mer.</summary>
    public static string MakeFromVin(string? vin)
    {
        if (vin is not { Length: >= 3 }) return L.T("RealCarScanner_UnknownMake");
        string[] pairs = ["WVW:Volkswagen", "WVG:Volkswagen", "WV1:Volkswagen", "WV2:Volkswagen", "3VW:Volkswagen", "WAU:Audi", "WUA:Audi", "TMB:Škoda",
            "VSS:Seat", "WBA:BMW", "WBS:BMW", "WBY:BMW", "WMW:Mini", "WDD:Mercedes-Benz", "WDB:Mercedes-Benz", "W1K:Mercedes-Benz", "YV1:Volvo", "YV4:Volvo",
            "LVY:Volvo", "YS3:Saab", "JT:Toyota", "SB1:Toyota", "NMT:Toyota", "WF0:Ford", "1FA:Ford", "1FM:Ford", "VF1:Renault", "VF3:Peugeot", "VF7:Citroën",
            "ZFA:Fiat", "KMH:Hyundai", "TMA:Hyundai", "KNA:Kia", "U5Y:Kia", "JN:Nissan", "SJN:Nissan", "JHM:Honda", "JM:Mazda", "JF:Subaru", "WP0:Porsche",
            "5YJ:Tesla", "LRW:Tesla", "XP7:Tesla", "W0L:Opel", "W0V:Opel", "SAL:Land Rover", "SAJ:Jaguar", "JS:Suzuki", "TSM:Suzuki", "JMB:Mitsubishi", "LBV:BMW"];
        var v = vin.ToUpperInvariant();
        foreach (var p in pairs)
        {
            var (code, make) = (p[..p.IndexOf(':')], p[(p.IndexOf(':') + 1)..]);
            if (v.StartsWith(code, StringComparison.Ordinal)) return make;
        }
        return L.T("RealCarScanner_UnknownMake");
    }

    /// <summary>Årsmodell från VIN-tecken 10 (senaste möjliga år som inte ligger i framtiden).</summary>
    public static int? YearFromVin(string? vin, int? now = null)
    {
        if (vin is not { Length: 17 }) return null;
        const string codes = "ABCDEFGHJKLMNPRSTVWXY123456789"; // 1980/2010 = A
        int i = codes.IndexOf(char.ToUpperInvariant(vin[9]));
        if (i < 0) return null;
        int limit = (now ?? DateTime.Now.Year) + 1;
        int y = 1980 + i;
        while (y + 30 <= limit) y += 30;
        return y;
    }

    /// <summary>
    /// Gör en egen bilmodell av avsökningen. Om <paramref name="hideVinSerial"/> ersätts VIN:ets serienummer
    /// (tecken 12–17) med 000001 så att filen kan delas utan att peka ut bilen.
    /// </summary>
    public static CarProfile ToProfile(RealCarScan scan, ProfileStore store, bool hideVinSerial = true, string? make = null, string? model = null, int? year = null)
    {
        var vin = scan.Vin is { Length: 17 } v ? v.ToUpperInvariant() : "CDX00000000000001";
        if (hideVinSerial && scan.Vin is { Length: 17 }) vin = vin[..11] + "000001";
        make ??= MakeFromVin(scan.Vin);
        var y = year ?? YearFromVin(scan.Vin) ?? DateTime.Now.Year;
        y = Math.Clamp(y, 1996, 2100);
        model ??= L.T("RealCarScanner_RealCar") + DateTime.Now.ToString("yyyy-MM-dd");
        var p = new CarProfile
        {
            Make = make, Model = model, Generation = "", Year = y, Vin = vin,
            Protocol = scan.Adapter?.Protocol is "6" or "7" or "8" or "9" ? scan.Adapter.Protocol : "6",
            StartDtcs = scan.Dtcs.Distinct().ToList(),
            SupportedPids = scan.SupportedPids.Where(s => ProfileLoader.KnownPids.Contains(Convert.ToByte(s, 16))).ToList(),
        };
        int n = 0;
        foreach (var e in scan.Ecus.Where(e => e.RequestId != 0x7DF).OrderBy(e => e.RequestId == 0x7E0 ? 0 : 1).ThenBy(e => e.RequestId))
        {
            if (e.RequestId > 0x7FF || e.ResponseId > 0x7FF) continue;
            p.Ecus.Add(new EcuProfile
            {
                Key = n == 0 && e.RequestId == 0x7E0 ? "motor" : $"ecu{e.RequestId:x3}",
                Name = (e.RequestId == 0x7E0 ? "Motor" : e.RequestId == 0x7E1 ? L.T("RealCarScanner_Transmission") : "Styrdon") + $" {e.RequestId:X3}",
                RequestId = $"{e.RequestId:X3}", ResponseId = $"{e.ResponseId:X3}", Coding = "00000000", CodingDid = "0600",
            });
            n++;
        }
        if (p.Ecus.Count == 0) p.Ecus.Add(new EcuProfile { Key = "motor", Name = "Motor 7E0", RequestId = "7E0", ResponseId = "7E8", Coding = "00000000" });
        if (p.SupportedPids.Count == 0) p.SupportedPids = null;
        static double? Lv(RealCarScan s, string k) => s.Live.TryGetValue(k, out var x) ? x : null;
        p.DefaultValues = new ProfileDefaults
        {
            Speed = Lv(scan, "km/h"), Rpm = Lv(scan, "rpm"), Coolant = Lv(scan, "kylv. °C") is { } c ? Math.Clamp(c, -40, 130) : null,
            Fuel = Lv(scan, L.T("Ui_Fuel2")), BatteryVolt = Lv(scan, "volt"),
        };
        p.Id = store.UniqueId(ProfileStore.SafeId($"{make}-{model}"));
        return p;
    }
}
