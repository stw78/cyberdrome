// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

using Android.Emulation.Control;
using Grpc.Core;
using Grpc.Net.Client;

namespace Cyberdrome.Vm;

/// <summary>En skärmbild som rå RGBA (4 byte per bildpunkt, rad för rad).</summary>
public sealed record RawFrame(int Width, int Height, ReadOnlyMemory<byte> Rgba);

/// <summary>
/// Android-läge som kan strömma skärmen kontinuerligt och ta emot tryck direkt (i stället för skärmdumpar och adb).
/// </summary>
public interface IScreenStream
{
    /// <summary>
    /// Strömmar skärmen tills <paramref name="ct"/> avbryts. Returnerar false direkt om strömmen inte finns
    /// (då används skärmdumpar som förut).
    /// </summary>
    Task<bool> StreamScreenAsync(Action<RawFrame> onFrame, CancellationToken ct);

    /// <summary>Pekare i Android-skärmens bildpunkter: ned, flytta och upp (som en riktig pekskärm).</summary>
    Task<bool> PointerAsync(int x, int y, bool down, CancellationToken ct = default);
}

/// <summary>
/// Klient för Android-emulatorns gRPC-API (startas med -grpc PORT -grpc-use-token). Nyckeln läses ur emulatorns
/// upptäcktsfil (%LOCALAPPDATA%\Temp\avd\running\pid_*.ini) och skickas som "authorization: Bearer …".
/// Mätt på en Ryzen 7 9800X3D + RX 9070: ~57 bilder/s och ~1 ms per tryck, mot ~3 bilder/s med adb screencap.
/// </summary>
public sealed class EmulatorGrpc : IDisposable
{
    private readonly GrpcChannel _channel;
    private readonly EmulatorController.EmulatorControllerClient _client;
    private readonly Metadata _headers;

    public EmulatorGrpc(int port, string? token)
    {
        _channel = GrpcChannel.ForAddress($"http://127.0.0.1:{port}", new GrpcChannelOptions { MaxReceiveMessageSize = 128 * 1024 * 1024 });
        _client = new EmulatorController.EmulatorControllerClient(_channel);
        _headers = [];
        if (!string.IsNullOrEmpty(token)) _headers.Add("authorization", "Bearer " + token);
    }

    /// <summary>Port och nyckel för den emulator som kör <paramref name="avdName"/>, ur emulatorns upptäcktsfiler.</summary>
    public static (int Port, string? Token)? Discover(string avdName)
    {
        foreach (var dir in DiscoveryDirs())
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var file in Directory.EnumerateFiles(dir, "pid_*.ini"))
            {
                try
                {
                    var info = ParseDiscovery(File.ReadAllText(file));
                    if (info.Cmdline.Contains($"\"{avdName}\"", StringComparison.Ordinal) && info.Port is { } p) return (p, info.Token);
                }
                catch { /* filen skrivs just nu – prova nästa */ }
            }
        }
        return null;
    }

    private static IEnumerable<string> DiscoveryDirs()
    {
        var local = System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData);
        yield return Path.Combine(local, "Temp", "avd", "running");
        yield return Path.Combine(Path.GetTempPath(), "avd", "running");
    }

    /// <summary>Läser grpc.port, grpc.token och cmdline ur en upptäcktsfil.</summary>
    public static (int? Port, string? Token, string Cmdline) ParseDiscovery(string ini)
    {
        int? port = null; string? token = null; var cmd = "";
        foreach (var raw in ini.Split('\n'))
        {
            var line = raw.Trim();
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            var key = line[..eq];
            var value = line[(eq + 1)..];
            if (key == "grpc.port" && int.TryParse(value, out var p)) port = p;
            else if (key == "grpc.token") token = value;
            else if (key == "cmdline") cmd = value;
        }
        return (port, token, cmd);
    }

    /// <summary>Strömmar huvudskärmen som RGBA tills den avbryts eller emulatorn försvinner.</summary>
    public async Task StreamAsync(Action<RawFrame> onFrame, CancellationToken ct)
    {
        using var call = _client.streamScreenshot(new ImageFormat { Format = ImageFormat.Types.ImgFormat.Rgba8888, Display = 0 },
            headers: _headers, cancellationToken: ct);
        while (await call.ResponseStream.MoveNext(ct))
        {
            var img = call.ResponseStream.Current;
            int w = (int)img.Format.Width, h = (int)img.Format.Height;
            if (w <= 0 || h <= 0 || img.Image_.Length < w * h * 4) continue;
            // Ingen kopia här: bildens bytes används direkt (kopieras en gång, in i skärmens bitmapp).
            onFrame(new RawFrame(w, h, img.Image_.Memory));
        }
    }

    /// <summary>Musknapp ned/upp på huvudskärmen (emulatorn gör om det till tryck i Android).</summary>
    public async Task MouseAsync(int x, int y, bool down, CancellationToken ct = default) =>
        await _client.sendMouseAsync(new MouseEvent { X = x, Y = y, Buttons = down ? 1 : 0, Display = 0 }, _headers, cancellationToken: ct);

    public void Dispose() => _channel.Dispose();
}
