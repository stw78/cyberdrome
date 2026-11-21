// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

using System.Globalization;
using System.Text;
using Cyberdrome.Core;
using Cyberdrome.Core.Profiles;

namespace Cyberdrome.Obd;

/// <summary>
/// Låtsas vara en ELM327-adapter (v1.5) kopplad till en CAN-bil.
/// Tar emot en kommandorad (t.ex. "010D" eller "ATZ") och ger tillbaka exakt den text
/// en riktig adapter skulle skicka, inklusive eko och prompten "&gt;".
/// En instans per anslutning: inställningar (ATE, ATH, ATSH …) gäller bara den anslutningen.
/// </summary>
public sealed class Elm327Emulator
{
    public const string Version = "ELM327 v1.5";

    /// <summary>Utfyllnadsbyte i CAN-ramar som är kortare än 8 byte (syns bara med ATH1).</summary>
    public const byte PadByte = 0x00;
    private const int FunctionalHeader = 0x7DF;

    private readonly VirtualCar _car;
    private bool _echo = true;
    private bool _linefeeds;
    private bool _spaces = true;
    private bool _headers;
    private bool _dlc; // ATD1: visa antal databyte (DLC) efter id:t
    private DateTime _resetAt = DateTime.MinValue;

    /// <summary>Hur länge efter ATZ som tomma rader ignoreras (riktig ELM327 startar om under ca 1 s).</summary>
    public static readonly TimeSpan ResetBusyTime = TimeSpan.FromSeconds(1.5);
    private string _protocol = "0"; // 0 = automatiskt
    private int _header = FunctionalHeader;
    private string _lastCommand = "";
    private readonly Dictionary<int, byte> _sessions = new(); // ECU-svar-id -> UDS-session
    private readonly Dictionary<int, SecurityState> _security = new(); // ECU-svar-id -> låsläge (UDS 27)
    private readonly Func<DateTime> _clock;
    private readonly Random _random;

    /// <summary>Antal felaktiga nycklar innan styrdonet spärrar.</summary>
    public const int MaxKeyAttempts = 3;

    /// <summary>Hur länge styrdonet spärrar efter för många fel.</summary>
    public static readonly TimeSpan LockoutDelay = TimeSpan.FromSeconds(10);

    /// <summary>Tips till loggen, t.ex. vilken nyckel som förväntas efter ett seed.</summary>
    public event Action<string>? Hint;

    private sealed class SecurityState
    {
        public bool Unlocked;
        public byte[]? PendingSeed;
        public int FailedAttempts;
        public DateTime LockedUntil;
    }

    public Elm327Emulator(VirtualCar car, Func<DateTime>? clock = null, Random? random = null)
    {
        _car = car;
        _clock = clock ?? (() => DateTime.UtcNow);
        _random = random ?? Random.Shared;
    }

    /// <summary>Hanterar en rad (utan \r) och returnerar hela svaret till klienten.</summary>
    public string Handle(string rawLine)
    {
        var cmd = new string(rawLine.Where(c => !char.IsWhiteSpace(c)).ToArray()).ToUpperInvariant();
        if (cmd.Length == 0)
        {
            // En riktig ELM327 är upptagen en stund efter ATZ och ignorerar då tomma rader.
            // Appar (t.ex. Car Scanner) skickar flera tomma rader direkt efter ATZ för att "tömma" adaptern;
            // svarar vi på dem kommer appen att läsa fel svar för varje fråga efteråt.
            if (_clock() - _resetAt < ResetBusyTime) return "";
            cmd = _lastCommand; // tom rad = upprepa senaste (som riktig ELM)
        }
        else _lastCommand = cmd;

        var sb = new StringBuilder();
        if (_echo) sb.Append(rawLine).Append('\r');

        var lines = cmd.StartsWith("AT") ? HandleAt(cmd[2..]) : HandleObd(cmd);
        var nl = _linefeeds ? "\r\n" : "\r";
        foreach (var l in lines) sb.Append(l).Append(nl);
        sb.Append(nl).Append('>');
        return sb.ToString();
    }

    // ---------------- AT-kommandon ----------------

    private IEnumerable<string> HandleAt(string a)
    {
        string On(Action act) { act(); return "OK"; }
        switch (a)
        {
            case "Z":
                ResetSettings();
                _resetAt = _clock();
                return ["", Version];
            case "WS":
                ResetSettings();
                _resetAt = _clock();
                return [Version];
            case "D":
                ResetSettings();
                return ["OK"];
            case "I": return [Version];
            case "@1": return ["Cyberdrome Virtual OBD"];
            case "E0": return [On(() => _echo = false)];
            case "E1": return [On(() => _echo = true)];
            case "L0": return [On(() => _linefeeds = false)];
            case "L1": return [On(() => _linefeeds = true)];
            case "S0": return [On(() => _spaces = false)];
            case "S1": return [On(() => _spaces = true)];
            case "H0": return [On(() => _headers = false)];
            case "H1": return [On(() => _headers = true)];
            case "D0": return [On(() => _dlc = false)];
            case "D1": return [On(() => _dlc = true)];
            case "IGN": return [_car.State.Ignition ? "ON" : "OFF"];
            case "CS": return ["T:00 R:00 ", "TOTAL:00"];
            case "RV":
                return [(_car.Bridge?.Voltage ?? _car.State.BatteryVolt).ToString("0.0", CultureInfo.InvariantCulture) + "V"];
            case "DP":
                return [(_protocol == "0" ? "AUTO, " : "") + ProtocolName(_car.Profile.Protocol)];
            case "DPN":
                return [(_protocol == "0" ? "A" : "") + _car.Profile.Protocol];
        }

        if (a.StartsWith("SP") || a.StartsWith("TP"))
        {
            var p = a[2..].TrimStart('A');
            if (p.Length == 1 && "0123456789ABC".Contains(p[0])) { _protocol = p; return ["OK"]; }
            return ["?"];
        }
        if (a.StartsWith("SH"))
        {
            var h = a[2..];
            if (h.Length is 3 or 6 && h.All(Uri.IsHexDigit))
            {
                _header = Convert.ToInt32(h[^3..], 16);
                return ["OK"];
            }
            return ["?"];
        }
        // Inställningar som en riktig app skickar men som inte påverkar simuleringen.
        string[] accepted = ["M0", "M1", "AT0", "AT1", "AT2", "CAF0", "CAF1", "AL", "NL", "PC", "R0", "R1", "CFC0", "CFC1", "DPN", "BI"];
        if (accepted.Contains(a) || a.StartsWith("ST") || a.StartsWith("CRA") || a.StartsWith("CF") || a.StartsWith("CM") || a.StartsWith("FC"))
            return ["OK"];

        return ["?"];
    }

    private void ResetSettings()
    {
        _echo = true; _linefeeds = false; _spaces = true; _headers = false; _dlc = false;
        _protocol = "0"; _header = FunctionalHeader; _sessions.Clear();
        // ATZ låser alla styrdon igen, men en pågående spärrtid (för många fel) finns kvar.
        foreach (var st in _security.Values) { st.Unlocked = false; st.PendingSeed = null; }
    }

    public static string ProtocolName(string p) => p switch
    {
        "6" => "ISO 15765-4 (CAN 11/500)",
        "7" => "ISO 15765-4 (CAN 29/500)",
        "8" => "ISO 15765-4 (CAN 11/250)",
        "9" => "ISO 15765-4 (CAN 29/250)",
        _ => "ISO 15765-4 (CAN 11/500)",
    };

    // ---------------- OBD / UDS ----------------

    private IEnumerable<string> HandleObd(string cmd)
    {
        // ELM327 v1.3+: en extra siffra sist anger hur många svar appen väntar på, t.ex. "010C1".
        // Den påverkar bara hur snabbt adaptern svarar, så vi tar bort den.
        if (cmd.Length is 3 or 5 or 7 or 9 or 11 or 13 or 15 && cmd.All(Uri.IsHexDigit) && cmd[^1] != '0')
            cmd = cmd[..^1];
        if (cmd.Length % 2 != 0 || !cmd.All(Uri.IsHexDigit) || cmd.Length > 14) // ELM327 skickar högst 7 byte (en CAN-ram)
            return ["?"];
        if (_car.Bridge is { } bridge)
            return Bridged(bridge, Convert.FromHexString(cmd));
        if (!_car.State.Ignition)
            return ["NO DATA"];

        var req = Convert.FromHexString(cmd);
        var coding = _car.Coding;
        var profile = coding.Profile;

        EcuProfile? ecu;
        if (_header == FunctionalHeader)
            ecu = profile.Ecus.FirstOrDefault(); // motorstyrdonet svarar på allmänna OBD-frågor
        else
            ecu = profile.FindEcuByRequestId(_header);

        if (ecu is null) return ["NO DATA"];

        var resp = Respond(req, ecu, coding, physical: _header != FunctionalHeader);
        if (resp is null) return ["NO DATA"];
        return FormatIsoTp(ecu.ResponseIdValue, resp);
    }

    /// <summary>Vidarekopplad fråga: den riktiga bilen svarar, formaterat efter appens AT-inställningar.</summary>
    private IEnumerable<string> Bridged(IObdBridge bridge, byte[] req)
    {
        BridgeReply r;
        try { r = bridge.Request(_header, req); }
        catch (Exception ex) { r = new BridgeReply([], "CAN ERROR", "Adapterfel: " + ex.Message); }
        if (r.Hint is not null) Hint?.Invoke(r.Hint);
        if (r.Error is not null && r.Replies.Count == 0) return [r.Error];
        if (r.Replies.Count == 0) return ["NO DATA"];
        return r.Replies.SelectMany(x => FormatIsoTp(x.ResponseId, x.Data)).ToList();
    }

    private byte[]? Respond(byte[] req, EcuProfile ecu, CodingStore coding, bool physical)
    {
        var s = _car.State;
        byte sid = req[0];

        // Egna PID:ar (Torque/Car Scanner-CSV) går före de inbyggda.
        if (coding.Profile.CustomPids is { Count: > 0 } custom)
        {
            var hex = Convert.ToHexString(req);
            var hit = custom.FirstOrDefault(c => string.Equals(c.Request, hex, StringComparison.OrdinalIgnoreCase)
                && (string.IsNullOrEmpty(c.Header) || string.Equals(c.Header, ecu.RequestId, StringComparison.OrdinalIgnoreCase)));
            if (hit is not null) return [(byte)(sid + 0x40), .. req[1..], .. hit.Encode()];
        }
        // Tillverkarsignaler (t.ex. OBDb, mode 21/22) – bara till rätt styrdon.
        if (coding.Profile.ExtraCommands is { Count: > 0 } extra)
        {
            var hex = Convert.ToHexString(req);
            var hit = extra.FirstOrDefault(c => string.Equals(c.Request, hex, StringComparison.OrdinalIgnoreCase)
                && string.Equals(c.Header, ecu.RequestId, StringComparison.OrdinalIgnoreCase));
            if (hit is not null) return hit.BuildResponse();
        }

        switch (sid)
        {
            case 0x01:
                if (req.Length < 2) return null;
                var outBytes = new List<byte> { 0x41 };
                var supported = SupportedFor(coding.Profile);
                foreach (var pid in req.Skip(1))
                {
                    var data = pid is 0x00 or 0x20 or 0x40 ? SupportBitmap(pid, supported) : supported.Contains(pid) ? Mode01(pid, s) : null;
                    if (data is null) continue;
                    outBytes.Add(pid);
                    outBytes.AddRange(data);
                }
                return outBytes.Count > 1 ? outBytes.ToArray() : null;

            case 0x03:
            {
                var codes = s.Dtcs;
                var r = new List<byte> { 0x43, (byte)codes.Count };
                foreach (var c in codes) r.AddRange(EncodeDtc(c));
                return r.ToArray();
            }
            case 0x04:
                s.ClearDtcs();
                return [0x44];

            case 0x09:
                if (req.Length < 2) return null;
                return req[1] switch
                {
                    0x00 => [0x49, 0x00, 0x40, 0x00, 0x00, 0x00], // stöd för PID 02 (VIN)
                    0x02 => [0x49, 0x02, 0x01, .. Encoding.ASCII.GetBytes(coding.Profile.Vin)],
                    _ => null,
                };
        }

        // UDS-tjänster: bara när en ECU adresseras direkt (ATSH).
        if (!physical) return null;
        return Uds(req, ecu, coding);
    }

    private static byte[]? Mode01(byte pid, VehicleState s)
    {
        int dtcCount = s.Dtcs.Count;
        return pid switch
        {



            0x01 => [(byte)((dtcCount > 0 ? 0x80 : 0) | Math.Min(dtcCount, 0x7F)), 0x07, 0xE5, 0x00],
            0x04 => [Pct(s.ThrottlePercent * 0.8 + (s.Rpm > 0 ? 15 : 0))],
            0x05 => [(byte)(s.CoolantC + 40)],
            0x0C => U16(s.Rpm * 4),
            0x0D => [(byte)Math.Round(s.SpeedKmh)],
            0x0F => [(byte)(s.IntakeC + 40)],
            0x11 => [Pct(s.ThrottlePercent)],
            0x1C => [0x06], // EOBD
            0x1F => U16(600),
            0x2F => [Pct(s.FuelPercent)],
            0x42 => U16(s.BatteryVolt * 1000),
            0x46 => [(byte)(s.AmbientC + 40)],
            0x51 => [0x01], // bensin
            _ => null,
        };
    }

    /// <summary>PID:ar som bilmodellen svarar på (profilens lista, annars alla som simulatorn kan).</summary>
    public static HashSet<byte> SupportedFor(CarProfile profile) =>
        profile.SupportedPids is { Count: > 0 } list
            ? list.Select(p => Convert.ToByte(p, 16)).ToHashSet()
            : ProfileLoader.KnownPids.ToHashSet();

    private static byte[] SupportBitmap(byte start, HashSet<byte> supported)
    {
        uint bits = 0;
        // 20 och 40 räknas som "stödd" i tabellen före om det finns PID:ar efter dem (som en riktig bil).
        var all = new HashSet<byte>(supported);
        if (all.Any(p => p > 0x20)) all.Add(0x20);
        if (all.Any(p => p > 0x40)) all.Add(0x40);
        foreach (var p in all)
            if (p > start && p <= start + 0x20)
                bits |= 1u << (32 - (p - start));
        return [(byte)(bits >> 24), (byte)(bits >> 16), (byte)(bits >> 8), (byte)bits];
    }

    private static byte Pct(double v) => (byte)Math.Round(Math.Clamp(v, 0, 100) * 255 / 100);

    private static byte[] U16(double v)
    {
        var x = (int)Math.Clamp(Math.Round(v), 0, 0xFFFF);
        return [(byte)(x >> 8), (byte)x];
    }

    /// <summary>"P0301" -> 03 01. Bokstaven ger de två översta bitarna.</summary>
    public static byte[] EncodeDtc(string code)
    {
        int letter = "PCBU".IndexOf(char.ToUpperInvariant(code[0]));
        int rest = Convert.ToInt32(code[1..], 16);
        int v = (letter << 14) | (rest & 0x3FFF);
        return [(byte)(v >> 8), (byte)v];
    }

    private SecurityState Security(EcuProfile ecu)
    {
        if (!_security.TryGetValue(ecu.ResponseIdValue, out var st))
            _security[ecu.ResponseIdValue] = st = new SecurityState();
        return st;
    }

    /// <summary>Är styrdonet upplåst (eller krävs ingen nyckel)?</summary>
    public bool IsUnlocked(int responseId) =>
        !_car.Coding.RequireSecurityAccess || (_security.TryGetValue(responseId, out var st) && st.Unlocked);

    // UDS: 10 (session), 27 (säkerhetsnyckel), 3E (tester present), 22 (läs DID), 2E (skriv DID).
    private byte[] Uds(byte[] req, EcuProfile ecu, CodingStore coding)
    {
        byte sid = req[0];
        byte[] Neg(byte nrc) => [0x7F, sid, nrc];
        byte session = _sessions.GetValueOrDefault(ecu.ResponseIdValue, (byte)0x01);

        switch (sid)
        {
            case 0x10:
                if (req.Length != 2) return Neg(0x13);
                if (req[1] is not (0x01 or 0x03)) return Neg(0x12);
                _sessions[ecu.ResponseIdValue] = req[1];
                if (req[1] == 0x01)
                {
                    // Tillbaka till standardsession: styrdonet låses igen.
                    var st = Security(ecu);
                    st.Unlocked = false;
                    st.PendingSeed = null;
                }
                return [0x50, req[1], 0x00, 0x32, 0x01, 0xF4];

            case 0x27:
                return SecurityAccess(req, ecu, coding, session);

            case 0x3E:
                return [0x7E, 0x00];

            case 0x22:
            {
                if (req.Length != 3) return Neg(0x13);
                int did = (req[1] << 8) | req[2];
                if (did == 0xF190)
                    return [0x62, req[1], req[2], .. Encoding.ASCII.GetBytes(coding.Profile.Vin)];
                if (did == ecu.CodingDidValue)
                    return [0x62, req[1], req[2], .. coding.Get(ecu.Key)];
                return Neg(0x31);
            }
            case 0x2E:
            {
                if (req.Length < 4) return Neg(0x13);
                int did = (req[1] << 8) | req[2];
                if (did != ecu.CodingDidValue) return Neg(0x31);
                if (session != 0x03) return Neg(0x7F); // kräver utökad session (10 03)
                if (!IsUnlocked(ecu.ResponseIdValue)) return Neg(0x33); // securityAccessDenied
                if (!coding.TrySet(ecu.Key, req[3..])) return Neg(0x13);
                return [0x6E, req[1], req[2]];
            }
        }
        return Neg(0x11);
    }

    /// <summary>
    /// UDS 0x27 SecurityAccess, nivå 01/02: 27 01 = begär seed, 27 02 &lt;nyckel&gt; = skicka nyckel.
    /// Bara för de simulerade styrdonen, med vår egen påhittade formel (se SecurityAccessConfig).
    /// </summary>
    private byte[] SecurityAccess(byte[] req, EcuProfile ecu, CodingStore coding, byte session)
    {
        byte[] Neg(byte nrc) => [0x7F, 0x27, nrc];
        if (req.Length < 2) return Neg(0x13);
        if (session != 0x03) return Neg(0x7F); // serviceNotSupportedInActiveSession: kräver 10 03
        var st = Security(ecu);
        var now = _clock();

        switch (req[1])
        {
            case 0x01: // requestSeed
            {
                if (req.Length != 2) return Neg(0x13);
                if (now < st.LockedUntil) return Neg(0x37); // requiredTimeDelayNotExpired
                if (st.Unlocked || !coding.RequireSecurityAccess)
                    return [0x67, 0x01, 0x00, 0x00, 0x00, 0x00]; // redan upplåst
                var seed = new byte[4];
                do _random.NextBytes(seed); while (seed.All(b => b == 0));
                st.PendingSeed = seed;
                var key = coding.SecurityFor(ecu).ComputeKey(seed);
                Hint?.Invoke(L.F("Elm327Emulator_0ExpectedKey1Send27", ecu.Name, Convert.ToHexString(key).Insert(6, " ").Insert(4, " ").Insert(2, " "), Convert.ToHexString(key)));
                return [0x67, 0x01, .. seed];
            }
            case 0x02: // sendKey
            {
                if (req.Length != 6) return Neg(0x13);
                if (now < st.LockedUntil) return Neg(0x37);
                if (st.PendingSeed is null) return Neg(0x24); // requestSequenceError: begär seed först
                var expected = coding.SecurityFor(ecu).ComputeKey(st.PendingSeed);
                st.PendingSeed = null;
                if (req.AsSpan(2).SequenceEqual(expected))
                {
                    st.Unlocked = true;
                    st.FailedAttempts = 0;
                    Hint?.Invoke(L.F("Elm327Emulator_0UnlockedWithTheCorrectKey", ecu.Name));
                    return [0x67, 0x02];
                }
                st.FailedAttempts++;
                if (st.FailedAttempts >= MaxKeyAttempts)
                {
                    st.FailedAttempts = 0;
                    st.LockedUntil = now + LockoutDelay;
                    Hint?.Invoke(L.F("Elm327Emulator_0TooManyWrongKeysLocked", ecu.Name, LockoutDelay.TotalSeconds));
                    return Neg(0x36); // exceededNumberOfAttempts
                }
                return Neg(0x35); // invalidKey
            }
        }
        return Neg(0x12); // subFunctionNotSupported
    }

    // ---------------- Formatering som en riktig ELM327 ----------------

    private IEnumerable<string> FormatIsoTp(int canId, byte[] data)
    {
        string Hex(IEnumerable<byte> b) => string.Join(_spaces ? " " : "", b.Select(x => x.ToString("X2")));
        string Id() => canId.ToString("X3") + (_spaces ? " " : "") + (_dlc ? "8" + (_spaces ? " " : "") : "");

        // Med rubriker (ATH1) visar en riktig ELM327 hela CAN-ramen: 8 databyte efter id:t,
        // dvs. PCI-byte + data + utfyllnad. Utfyllnaden är det som styrdonet skickar (här 00).
        string Frame(IEnumerable<byte> bytes)
        {
            var f = bytes.ToList();
            while (f.Count < 8) f.Add(PadByte);
            return Id() + Hex(f);
        }

        if (data.Length <= 7)
        {
            yield return _headers
                ? Frame([(byte)data.Length, .. data])
                : Hex(data);
            yield break;
        }

        // Flera ramar (ISO-TP): första ramen 6 byte, sedan 7 byte per ram.
        if (_headers)
        {
            yield return Frame([(byte)(0x10 | (data.Length >> 8)), (byte)data.Length, .. data.Take(6)]);
            int seq = 1;
            for (int i = 6; i < data.Length; i += 7, seq++)
                yield return Frame([(byte)(0x20 | (seq & 0xF)), .. data.Skip(i).Take(7)]);
        }
        else
        {
            yield return data.Length.ToString("X3");
            yield return "0:" + (_spaces ? " " : "") + Hex(data.Take(6));
            int seq = 1;
            for (int i = 6; i < data.Length; i += 7, seq++)
                yield return $"{seq & 0xF:X}:" + (_spaces ? " " : "") + Hex(data.Skip(i).Take(7));
        }
    }
}
