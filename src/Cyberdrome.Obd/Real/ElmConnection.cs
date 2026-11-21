// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

using System.IO.Ports;
using System.Net.Sockets;
using System.Text;

namespace Cyberdrome.Obd.Real;

/// <summary>Hur programmet når adaptern. Varje transport ger en ström (tecken in/ut).</summary>
public interface IAdapterTransport : IAsyncDisposable
{
    /// <summary>Visningsnamn, t.ex. "COM5 (38400)" eller "Wi-Fi 192.168.0.10:35000".</summary>
    string Name { get; }
    Task<Stream> OpenAsync(CancellationToken ct);
}

/// <summary>Wi-Fi-adapter (ELM327 Wi-Fi brukar vara 192.168.0.10:35000).</summary>
public sealed class TcpAdapterTransport(string host, int port) : IAdapterTransport
{
    private TcpClient? _client;
    public string Name => $"Wi-Fi {host}:{port}";

    public async Task<Stream> OpenAsync(CancellationToken ct)
    {
        _client = new TcpClient { NoDelay = true };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(6));
        await _client.ConnectAsync(host, port, timeout.Token).ConfigureAwait(false);
        return _client.GetStream();
    }

    public ValueTask DisposeAsync() { _client?.Dispose(); return ValueTask.CompletedTask; }
}

/// <summary>
/// USB-adapter eller parkopplad Bluetooth-adapter (klassisk SPP) som Windows visar som en COM-port.
/// </summary>
public sealed class SerialAdapterTransport(string portName, int baud = 38400) : IAdapterTransport
{
    private SerialPort? _port;
    public string Name => $"{portName} ({baud} baud)";

    public static string[] PortNames() => SerialPort.GetPortNames().Distinct().OrderBy(p => p.Length).ThenBy(p => p).ToArray();

    public Task<Stream> OpenAsync(CancellationToken ct)
    {
        _port = new SerialPort(portName, baud, Parity.None, 8, StopBits.One) { ReadTimeout = SerialPort.InfiniteTimeout, WriteTimeout = 3000, DtrEnable = true, RtsEnable = true };
        _port.Open();
        return Task.FromResult(_port.BaseStream);
    }

    public ValueTask DisposeAsync() { try { _port?.Close(); } catch { } _port?.Dispose(); return ValueTask.CompletedTask; }
}

/// <summary>
/// Textförbindelse med en ELM327-kompatibel adapter: skickar ett kommando och läser svaret fram till prompten '&gt;'.
/// Ett kommando i taget (adaptern delas av sidan, livevärden och vidarekopplingen).
/// </summary>
public sealed class ElmConnection : IAsyncDisposable
{
    private readonly IAdapterTransport _transport;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Stream? _stream;
    private readonly byte[] _buf = new byte[4096];
    private readonly StringBuilder _pending = new();

    public ElmConnection(IAdapterTransport transport) => _transport = transport;

    public string Name => _transport.Name;
    public bool IsOpen => _stream is not null;

    /// <summary>Allt som skickas och tas emot (för OBD-loggen). true = till adaptern.</summary>
    public event Action<bool, string>? Traffic;

    public async Task OpenAsync(CancellationToken ct = default) => _stream = await _transport.OpenAsync(ct).ConfigureAwait(false);

    /// <summary>Skickar ett kommando (utan \r) och returnerar svaret utan eko och prompt.</summary>
    public async Task<string> SendAsync(string cmd, int timeoutMs = 4000, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try { return await SendLockedAsync(cmd, timeoutMs, ct).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    /// <summary>Flera kommandon utan att någon annan kommer emellan (t.ex. ATSH + fråga).</summary>
    public async Task<T> ExclusiveAsync<T>(Func<Func<string, int, Task<string>>, Task<T>> work, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try { return await work((c, t) => SendLockedAsync(c, t, ct)).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    private async Task<string> SendLockedAsync(string cmd, int timeoutMs, CancellationToken ct)
    {
        var s = _stream ?? throw new InvalidOperationException(L.T("ElmConnection_TheAdapterIsNotConnected"));
        _pending.Clear();
        Traffic?.Invoke(true, cmd);
        var bytes = Encoding.ASCII.GetBytes(cmd + "\r");
        await s.WriteAsync(bytes, ct).ConfigureAwait(false);
        await s.FlushAsync(ct).ConfigureAwait(false);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(timeoutMs);
        try
        {
            while (true)
            {
                int n = await s.ReadAsync(_buf, timeout.Token).ConfigureAwait(false);
                if (n == 0) throw new IOException(L.T("ElmConnection_TheAdapterClosedTheConnection"));
                _pending.Append(Encoding.Latin1.GetString(_buf, 0, n));
                var text = _pending.ToString();
                int prompt = text.IndexOf('>');
                if (prompt >= 0)
                {
                    var reply = Clean(text[..prompt], cmd);
                    Traffic?.Invoke(false, reply.Replace("\n", " | "));
                    return reply;
                }
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            var partial = _pending.ToString();
            Traffic?.Invoke(false, partial.Length == 0 ? L.T("ElmConnection_NoReplyTimeout")
                : L.F("ElmConnection_ReplyWithoutELMPrompt0Hex", new string(partial.Select(c => c is >= ' ' and < '\x7f' ? c : '·').ToArray()), Convert.ToHexString(Encoding.Latin1.GetBytes(partial))));
            throw new TimeoutException(L.F("ElmConnection_TheAdapterDidNotReplyTo", cmd));
        }
    }

    /// <summary>Tar bort eko, "SEARCHING...", tomma rader. Rader skiljs med \n.</summary>
    public static string Clean(string raw, string cmd)
    {
        var lines = raw.Replace("\n", "\r").Split('\r', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => l.Length > 0 && !l.StartsWith("SEARCHING", StringComparison.OrdinalIgnoreCase) && l != "BUS INIT: ..." && !l.StartsWith("BUS INIT", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var bare = new string(cmd.Where(c => !char.IsWhiteSpace(c)).ToArray());
        if (lines.Count > 0 && string.Equals(new string(lines[0].Where(c => !char.IsWhiteSpace(c)).ToArray()), bare, StringComparison.OrdinalIgnoreCase))
            lines.RemoveAt(0);
        return string.Join("\n", lines);
    }

    public async ValueTask DisposeAsync()
    {
        _stream = null;
        await _transport.DisposeAsync().ConfigureAwait(false);
    }
}
