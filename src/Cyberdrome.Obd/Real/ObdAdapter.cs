// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

using System.Globalization;
using System.Text;
using Cyberdrome.Core;

namespace Cyberdrome.Obd.Real;

/// <summary>Uppgifter om adaptern efter anslutning.</summary>
public sealed record AdapterInfo(string Version, string? StnVersion, string Protocol, string ProtocolName, double? Voltage);

/// <summary>
/// En riktig ELM327-kompatibel adapter (även OBDLink/STN). Ställer adaptern i ett känt läge
/// (ATE0, ATL0, ATS1, ATH1) och tolkar CAN-ramar själv, så att flerramssvar (ISO-TP) blir hela svar.
/// Programmet ändrar aldrig adapterns sparade inställningar (ATPP).
/// </summary>
public sealed class ObdAdapter : IAsyncDisposable
{
    public const int FunctionalHeader = 0x7DF;
    private readonly ElmConnection _elm;
    private int? _header;

    public ObdAdapter(ElmConnection elm) => _elm = elm;

    public ElmConnection Connection => _elm;
    public AdapterInfo? Info { get; private set; }
    public double? LastVoltage { get; private set; }

    /// <summary>True för CAN-protokoll (6–9, A–C). Andra protokoll tolkas med 3 rubrikbyte + kontrollsumma.</summary>
    public bool IsCan => Info?.Protocol is "6" or "7" or "8" or "9" or "A" or "B" or "C";
    private bool Is29Bit => Info?.Protocol is "7" or "9";

    /// <summary>
    /// Återställer och ställer in adaptern. Kastar <see cref="NotElmException"/> om enheten inte svarar som en ELM327.
    /// </summary>
    public async Task<AdapterInfo> InitAsync(CancellationToken ct = default)
    {
        string z;
        try { z = await _elm.SendAsync("ATZ", 5000, ct).ConfigureAwait(false); }
        catch (TimeoutException) { throw new NotElmException("Inget svar på ATZ."); }
        await Task.Delay(300, ct).ConfigureAwait(false);
        var version = Last(await _elm.SendAsync("ATI", 2000, ct).ConfigureAwait(false));
        if (!LooksLikeElm(z + " " + version)) throw new NotElmException($"Svarade \"{(z + " " + version).Trim()}\" i stället för en ELM327-version.");
        foreach (var c in new[] { "ATE0", "ATL0", "ATS1", "ATH1", "ATCAF1", "ATSP0" })
            await _elm.SendAsync(c, 2000, ct).ConfigureAwait(false);
        string? stn = null;
        try
        {
            var s = Last(await _elm.SendAsync("STI", 1500, ct).ConfigureAwait(false));
            if (s.Length > 0 && s != "?") stn = s;
        }
        catch (TimeoutException) { }
        // Första frågan startar protokollsökningen (kan ta flera sekunder).
        try { await _elm.SendAsync("0100", 12000, ct).ConfigureAwait(false); } catch (TimeoutException) { }
        var dpn = Last(await _elm.SendAsync("ATDPN", 2000, ct).ConfigureAwait(false)).TrimStart('A');
        var dp = Last(await _elm.SendAsync("ATDP", 2000, ct).ConfigureAwait(false));
        _header = null;
        Info = new AdapterInfo(version, stn, dpn.Length == 1 ? dpn : "0", dp.StartsWith("AUTO, ", StringComparison.Ordinal) ? dp[6..] : dp, null);
        Info = Info with { Voltage = await ReadVoltageAsync(ct).ConfigureAwait(false) };
        return Info;
    }

    public static bool LooksLikeElm(string text) =>
        text.Contains("ELM327", StringComparison.OrdinalIgnoreCase) || text.Contains("OBDLINK", StringComparison.OrdinalIgnoreCase)
        || text.Contains("STN1", StringComparison.OrdinalIgnoreCase) || text.Contains("STN2", StringComparison.OrdinalIgnoreCase)
        || text.Contains("ELM329", StringComparison.OrdinalIgnoreCase) || text.Contains("vLinker", StringComparison.OrdinalIgnoreCase);

    private static string Last(string reply) => reply.Split('\n').LastOrDefault()?.Trim() ?? "";

    public async Task<double?> ReadVoltageAsync(CancellationToken ct = default)
    {
        var rv = Last(await _elm.SendAsync("ATRV", 2000, ct).ConfigureAwait(false)).TrimEnd('V', 'v');
        LastVoltage = double.TryParse(rv, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
        return LastVoltage;
    }

    /// <summary>Skickar en fråga till ett CAN-id och returnerar svaren per styrdon (ihopsatta).</summary>
    public async Task<(List<EcuReply> Replies, string? Error)> RequestAsync(int header, byte[] request, int timeoutMs = 3000, CancellationToken ct = default)
    {
        var hex = Convert.ToHexString(request);
        return await _elm.ExclusiveAsync(async send =>
        {
            if (!Is29Bit && _header != header)
            {
                var ok = await send($"ATSH{header:X3}", 2000).ConfigureAwait(false);
                if (!ok.Contains("OK")) return (new List<EcuReply>(), (string?)"?");
                _header = header;
            }
            var text = await send(hex, timeoutMs).ConfigureAwait(false);
            return ParseReply(text, IsCan || Info is null, Is29Bit);
        }, ct).ConfigureAwait(false);
    }

    private static readonly string[] Errors =
        ["NO DATA", "CAN ERROR", "UNABLE TO CONNECT", "BUS ERROR", "BUS BUSY", "FB ERROR", "DATA ERROR", "STOPPED", "?", "ERR", "LV RESET", "BUFFER FULL", "ACT ALERT"];

    /// <summary>
    /// Tolkar svaret från en adapter med rubriker på (ATH1, ATS1). CAN: "7E8 10 14 49 02 01 ..." (PCI-byte först).
    /// Andra protokoll: "48 6B 10 41 0D 00 xx" (3 rubrikbyte, data, kontrollsumma).
    /// </summary>
    public static (List<EcuReply> Replies, string? Error) ParseReply(string text, bool can, bool is29Bit = false)
    {
        string? error = null;
        var order = new List<int>();
        var buffers = new Dictionary<int, List<byte>>();
        var expected = new Dictionary<int, int>();
        foreach (var raw in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Errors.Any(e => raw.StartsWith(e, StringComparison.OrdinalIgnoreCase))) { error ??= raw; continue; }
            var tokens = Tokens(raw);
            if (tokens is null) continue;
            int id; List<byte> data;
            if (can)
            {
                if (is29Bit)
                {
                    if (tokens.Count < 5) continue;
                    id = (tokens[0] << 24) | (tokens[1] << 16) | (tokens[2] << 8) | tokens[3];
                    data = tokens.Skip(4).Select(t => (byte)t).ToList();
                }
                else
                {
                    if (tokens.Count < 2 || tokens[0] > 0x7FF) continue;
                    id = tokens[0];
                    data = tokens.Skip(1).Select(t => (byte)t).ToList();
                }
                if (data.Count == 0) continue;
                int pci = data[0] >> 4;
                if (!buffers.ContainsKey(id)) { buffers[id] = []; order.Add(id); }
                switch (pci)
                {
                    case 0: // en ram
                        int len = data[0] & 0x0F;
                        buffers[id] = data.Skip(1).Take(len).ToList();
                        expected[id] = len;
                        break;
                    case 1: // första ramen
                        if (data.Count < 2) continue;
                        expected[id] = ((data[0] & 0x0F) << 8) | data[1];
                        buffers[id] = data.Skip(2).ToList();
                        break;
                    case 2: // följande ramar
                        buffers[id].AddRange(data.Skip(1));
                        break;
                }
            }
            else
            {
                if (tokens.Count < 5) continue;
                id = tokens[2]; // avsändare
                if (!buffers.ContainsKey(id)) { buffers[id] = []; order.Add(id); }
                buffers[id].AddRange(tokens.Skip(3).Take(tokens.Count - 4).Select(t => (byte)t));
            }
        }
        var replies = order.Select(id => new EcuReply(id,
            (expected.TryGetValue(id, out var n) ? buffers[id].Take(n) : buffers[id]).ToArray())).Where(r => r.Data.Length > 0).ToList();
        return (replies, replies.Count == 0 ? error ?? "NO DATA" : null);
    }

    /// <summary>"7E8 06 41 00 BE" → [0x7E8, 0x06, 0x41, 0x00, 0xBE]. Null om raden inte är hex.</summary>
    private static List<int>? Tokens(string line)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1 && parts[0].Length > 3 && parts[0].Length % 2 == 1) // utan mellanslag: "7E8064100BE..."
        {
            var s = parts[0];
            parts = [s[..3], .. Enumerable.Range(0, (s.Length - 3) / 2).Select(i => s.Substring(3 + i * 2, 2))];
        }
        var res = new List<int>();
        foreach (var p in parts)
        {
            if (p.Length is < 1 or > 3 || !p.All(Uri.IsHexDigit)) return null;
            res.Add(Convert.ToInt32(p, 16));
        }
        return res;
    }

    public async ValueTask DisposeAsync()
    {
        try { if (_elm.IsOpen) await _elm.SendAsync("ATPC", 1000).ConfigureAwait(false); } catch { } // stäng protokollet (adaptern somnar)
        await _elm.DisposeAsync().ConfigureAwait(false);
    }
}

/// <summary>Enheten svarar inte som en ELM327 (t.ex. en adapter som är låst till tillverkarens app).</summary>
public sealed class NotElmException(string message) : Exception(message);
