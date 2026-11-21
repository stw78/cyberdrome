// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

using System.Net;
using System.Net.Sockets;
using System.Text;
using Cyberdrome.Core;

namespace Cyberdrome.Obd;

/// <summary>En rad i OBD-trafikloggen.</summary>
public sealed record ObdTrafficEntry(DateTime Time, string Client, bool FromApp, string Text);

/// <summary>
/// Virtuell Wi-Fi OBD-dongel: en TCP-server (standard port 35000) som beter sig som en ELM327.
/// Android-appar ansluter till den precis som till en riktig Wi-Fi-dongel.
/// </summary>
public sealed class ObdTcpServer : IAsyncDisposable
{
    public const int DefaultPort = 35000;

    private readonly VirtualCar _car;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private int _clientCount;

    public ObdTcpServer(VirtualCar car) => _car = car;

    public int Port { get; private set; }
    public bool IsRunning => _listener is not null;
    public int ClientCount => Volatile.Read(ref _clientCount);
    public DateTime? LastRequest { get; private set; }

    public event EventHandler<ObdTrafficEntry>? Traffic;
    public event EventHandler? StatusChanged;

    public void Start(int port = DefaultPort, IPAddress? address = null)
    {
        if (IsRunning) return;
        _cts = new CancellationTokenSource();
        _listener = new TcpListener(address ?? IPAddress.Any, port);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        // Körs på trådpoolen (inte UI-tråden), så att servern alltid svarar även om fönstret är upptaget.
        var l = _listener;
        var token = _cts.Token;
        _ = Task.Run(() => AcceptLoop(l, token));
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    public Task StopAsync()
    {
        if (_listener is null) return Task.CompletedTask;
        _cts!.Cancel();
        _listener.Stop();
        _listener = null;
        StatusChanged?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    private async Task AcceptLoop(TcpListener listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await listener.AcceptTcpClientAsync(ct).ConfigureAwait(false); }
            catch { break; }
            _ = HandleClient(client, ct);
        }
    }

    private async Task HandleClient(TcpClient client, CancellationToken ct)
    {
        var name = client.Client.RemoteEndPoint?.ToString() ?? L.T("ObdTcpServer_Unknown");
        Interlocked.Increment(ref _clientCount);
        StatusChanged?.Invoke(this, EventArgs.Empty);
        var session = new ObdLineSession(_car, name, e => Traffic?.Invoke(this, e));
        try
        {
            using (client)
            {
                client.NoDelay = true;
                await session.RunAsync(client.GetStream(), ct, () => LastRequest = DateTime.Now).ConfigureAwait(false);
            }
        }
        catch (Exception) when (ct.IsCancellationRequested) { }
        catch (IOException) { }
        catch (SocketException) { }
        finally
        {
            Interlocked.Decrement(ref _clientCount);
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}
