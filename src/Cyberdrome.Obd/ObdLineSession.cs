// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

using System.Text;
using Cyberdrome.Core;

namespace Cyberdrome.Obd;

/// <summary>
/// En anslutning till den virtuella ELM327:an, oavsett om den går via Wi-Fi (TCP) eller Bluetooth (SPP).
/// Delar upp inkommande tecken i rader (avslutas med \r), låter en egen Elm327Emulator svara
/// och rapporterar trafiken till loggen.
/// </summary>
public sealed class ObdLineSession
{
    private readonly Elm327Emulator _emu;
    private readonly StringBuilder _line = new();
    private readonly Action<ObdTrafficEntry>? _traffic;

    public ObdLineSession(VirtualCar car, string client, Action<ObdTrafficEntry>? traffic = null)
    {
        Client = client;
        _traffic = traffic;
        _emu = new Elm327Emulator(car);
        // Tips till OBD-loggen, t.ex. "Förväntad nyckel: …" efter ett seed (UDS 27).
        _emu.Hint += text => _traffic?.Invoke(new(DateTime.Now, "tips", false, "💡 " + text));
    }

    public string Client { get; }

    /// <summary>Tidpunkt för senaste kommandot från appen.</summary>
    public DateTime? LastRequest { get; private set; }

    /// <summary>
    /// Tar emot tecken från appen. Returnerar det som ska skickas tillbaka
    /// (tom sträng om ingen hel rad har kommit än).
    /// </summary>
    public string Feed(string chunk)
    {
        var reply = new StringBuilder();
        foreach (char c in chunk)
        {
            if (c == '\n') continue;
            if (c != '\r') { _line.Append(c); continue; }

            var cmd = _line.ToString();
            _line.Clear();
            LastRequest = DateTime.Now;
            var r = _emu.Handle(cmd);
            if (r.Length == 0) continue; // ignorerad rad (t.ex. tom rad direkt efter ATZ) – inget svar, ingen logg
            _traffic?.Invoke(new(DateTime.Now, Client, true, cmd));
            _traffic?.Invoke(new(DateTime.Now, Client, false, Clean(r, cmd)));
            reply.Append(r);
        }
        return reply.ToString();
    }

    /// <summary>Läser från strömmen och svarar tills anslutningen stängs.</summary>
    public async Task RunAsync(Stream stream, CancellationToken ct, Action? onRequest = null)
    {
        var buf = new byte[1024];
        while (!ct.IsCancellationRequested)
        {
            int n = await stream.ReadAsync(buf, ct).ConfigureAwait(false);
            if (n == 0) break;
            var reply = Feed(Encoding.ASCII.GetString(buf, 0, n));
            if (reply.Length == 0) continue;
            onRequest?.Invoke();
            await stream.WriteAsync(Encoding.ASCII.GetBytes(reply), ct).ConfigureAwait(false);
            await stream.FlushAsync(ct).ConfigureAwait(false);
        }
    }

    /// <summary>Gör svaret läsbart i loggen: utan eko, prompt och tomma rader.</summary>
    public static string Clean(string reply, string cmd)
    {
        var parts = reply.Replace("\n", "").Split('\r', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => p != ">").ToList();
        if (parts.Count > 0 && parts[0] == cmd) parts.RemoveAt(0);
        return string.Join(" | ", parts);
    }
}
