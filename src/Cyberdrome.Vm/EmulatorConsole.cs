// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

using System.Net.Sockets;
using System.Text;

namespace Cyberdrome.Vm;

/// <summary>
/// Snabba skärmbilder via emulatorns konsol (telnet på porten i "emulator-5554"). Bilden tas av emulatorn på
/// datorn i stället för av Android: cirka 30 ms per bild mot över 200 ms med "adb screencap -p".
/// Anslutningen hålls öppen mellan bilderna. Fungerar den inte används adb som förut.
/// </summary>
public sealed class EmulatorConsole : IDisposable
{
    private readonly int _port;
    private readonly string _dir;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private TcpClient? _client;
    private StreamReader? _reader;
    private StreamWriter? _writer;

    public EmulatorConsole(int port)
    {
        _port = port;
        _dir = Path.Combine(Path.GetTempPath(), "cyberdrome-screen-" + port);
    }

    /// <summary>Konsolporten ur ett adb-namn som "emulator-5554", annars null.</summary>
    public static int? PortFromSerial(string? serial) =>
        serial is not null && serial.StartsWith("emulator-", StringComparison.Ordinal) && int.TryParse(serial[9..], out var p) ? p : null;

    /// <summary>Emulatorns nyckel för konsolen (skapas av emulatorn i användarens hemmapp).</summary>
    public static string? AuthToken()
    {
        var f = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".emulator_console_auth_token");
        try { return File.Exists(f) ? File.ReadAllText(f).Trim() : null; } catch { return null; }
    }

    /// <summary>Läser konsolens svar fram till raden OK eller KO. True om OK.</summary>
    public static async Task<bool> ReadReplyAsync(TextReader reader, CancellationToken ct)
    {
        while (true)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line is null) throw new IOException("Emulator console closed");
            if (line.StartsWith("OK", StringComparison.Ordinal)) return true;
            if (line.StartsWith("KO", StringComparison.Ordinal)) return false;
        }
    }

    private async Task ConnectAsync(CancellationToken ct)
    {
        Close();
        var c = new TcpClient { NoDelay = true };
        using (var cts = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            cts.CancelAfter(2000);
            await c.ConnectAsync("127.0.0.1", _port, cts.Token);
        }
        var s = c.GetStream();
        _client = c;
        _reader = new StreamReader(s, Encoding.ASCII);
        _writer = new StreamWriter(s, Encoding.ASCII) { AutoFlush = true, NewLine = "\n" };
        await ReadReplyAsync(_reader, ct); // välkomsttext
        if (AuthToken() is { Length: > 0 } token)
        {
            await _writer.WriteLineAsync("auth " + token);
            if (!await ReadReplyAsync(_reader, ct)) throw new IOException("Emulator console rejected the auth token");
        }
        Directory.CreateDirectory(_dir);
    }

    /// <summary>En PNG av huvudskärmen, eller null om konsolen inte svarar.</summary>
    public async Task<byte[]?> CaptureAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    if (_writer is null) await ConnectAsync(ct);
                    foreach (var old in Directory.EnumerateFiles(_dir, "*.png")) TryDelete(old);
                    await _writer!.WriteLineAsync($"screenrecord screenshot {_dir}");
                    if (!await ReadReplyAsync(_reader!, ct)) return null;
                    var file = Directory.EnumerateFiles(_dir, "*.png").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
                    if (file is null) return null;
                    var png = await File.ReadAllBytesAsync(file, ct);
                    TryDelete(file);
                    return png.Length > 8 && png[0] == 0x89 && png[1] == (byte)'P' ? png : null;
                }
                catch (OperationCanceledException) { throw; }
                catch { Close(); } // försök ansluta en gång till (emulatorn kan ha startats om)
            }
            return null;
        }
        finally { _lock.Release(); }
    }

    private static void TryDelete(string f) { try { File.Delete(f); } catch { } }

    private void Close()
    {
        try { _client?.Dispose(); } catch { }
        _client = null; _reader = null; _writer = null;
    }

    public void Dispose() { Close(); _lock.Dispose(); }
}
