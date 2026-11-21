// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

#if WINDOWS
using Cyberdrome.Core;
using Cyberdrome.Obd;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;

namespace Cyberdrome.App;

/// <summary>
/// Virtuell Bluetooth OBD-dongel: en RFCOMM-tjänst med Serial Port Profile (SPP) som heter "OBDII",
/// precis som billiga ELM327-donglar. Varje anslutning får en egen Elm327Emulator mot samma bil,
/// på samma sätt som Wi-Fi-porten (ObdTcpServer). Använder Windows inbyggda Bluetooth (WinRT).
/// </summary>
public sealed class BluetoothObdServer : IAsyncDisposable
{
    public const string ServiceName = "OBDII";
    private const ushort SdpServiceNameAttributeId = 0x100;
    private const byte SdpTextType = (4 << 3) | 5; // text, längd i 1 byte

    private readonly VirtualCar _car;
    private RfcommServiceProvider? _provider;
    private StreamSocketListener? _listener;
    private CancellationTokenSource? _cts;
    private readonly List<StreamSocket> _sockets = new();

    public BluetoothObdServer(VirtualCar car) => _car = car;

    public bool IsRunning => _provider is not null;

    /// <summary>Namn på anslutna enheter.</summary>
    public IReadOnlyList<string> ConnectedDevices { get { lock (_connected) return _connected.ToList(); } }
    private readonly List<string> _connected = new();

    /// <summary>Datorns Bluetooth-namn (det telefonen ser), eller null om det inte gick att läsa.</summary>
    public string? AdapterName { get; private set; }

    public event EventHandler<ObdTrafficEntry>? Traffic;
    public event EventHandler? StatusChanged;

    /// <summary>Startar tjänsten. Kastar ett undantag med svensk text om Bluetooth inte går att använda.</summary>
    public async Task StartAsync()
    {
        if (IsRunning) return;
        var adapter = await BluetoothAdapter.GetDefaultAsync()
                      ?? throw new InvalidOperationException("Datorn har ingen Bluetooth-adapter.");
        if (!adapter.IsClassicSupported)
            throw new InvalidOperationException("Datorns Bluetooth stöder inte klassisk Bluetooth (SPP).");
        var radio = await adapter.GetRadioAsync();
        if (radio is not null && radio.State != Windows.Devices.Radios.RadioState.On)
            throw new InvalidOperationException("Bluetooth är avstängt i Windows. Slå på det i Inställningar → Bluetooth.");
        // Windows visar datorns namn (enhetsnamnet) för andra Bluetooth-enheter.
        AdapterName = Environment.MachineName;

        _cts = new CancellationTokenSource();
        _provider = await RfcommServiceProvider.CreateAsync(RfcommServiceId.SerialPort);
        _listener = new StreamSocketListener();
        _listener.ConnectionReceived += OnConnection;
        await _listener.BindServiceNameAsync(_provider.ServiceId.AsString(),
            SocketProtectionLevel.BluetoothEncryptionAllowNullAuthentication);

        // SDP: tjänstens namn "OBDII" så att OBD-appar känner igen den.
        var w = new DataWriter();
        var name = System.Text.Encoding.UTF8.GetBytes(ServiceName);
        w.WriteByte(SdpTextType);
        w.WriteByte((byte)name.Length);
        w.WriteBytes(name);
        _provider.SdpRawAttributes.Add(SdpServiceNameAttributeId, w.DetachBuffer());

        _provider.StartAdvertising(_listener, true);
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task StopAsync()
    {
        if (_provider is null) return;
        _cts?.Cancel();
        try { _provider.StopAdvertising(); } catch { }
        _provider = null;
        if (_listener is not null)
        {
            _listener.ConnectionReceived -= OnConnection;
            _listener.Dispose();
            _listener = null;
        }
        lock (_sockets)
        {
            foreach (var s in _sockets) { try { s.Dispose(); } catch { } }
            _sockets.Clear();
        }
        lock (_connected) _connected.Clear();
        StatusChanged?.Invoke(this, EventArgs.Empty);
        await Task.CompletedTask;
    }

    private async void OnConnection(StreamSocketListener sender, StreamSocketListenerConnectionReceivedEventArgs args)
    {
        var socket = args.Socket;
        var device = socket.Information.RemoteHostName?.DisplayName ?? "okänd enhet";
        var client = "Bluetooth: " + device;
        lock (_sockets) _sockets.Add(socket);
        lock (_connected) _connected.Add(device);
        StatusChanged?.Invoke(this, EventArgs.Empty);
        Traffic?.Invoke(this, new(DateTime.Now, client, false, "Ansluten via Bluetooth"));
        var ct = _cts?.Token ?? CancellationToken.None;
        try
        {
            using var stream = new DuplexStream(socket.InputStream.AsStreamForRead(0), socket.OutputStream.AsStreamForWrite(0));
            var session = new ObdLineSession(_car, client, e => Traffic?.Invoke(this, e));
            await session.RunAsync(stream, ct).ConfigureAwait(false);
        }
        catch (Exception) { /* anslutningen bröts */ }
        finally
        {
            lock (_sockets) _sockets.Remove(socket);
            lock (_connected) _connected.Remove(device);
            try { socket.Dispose(); } catch { }
            Traffic?.Invoke(this, new(DateTime.Now, client, false, "Bluetooth-anslutningen stängdes"));
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync();

    /// <summary>Slår ihop läs- och skrivström till en Stream.</summary>
    private sealed class DuplexStream(Stream read, Stream write) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => write.Flush();
        public override Task FlushAsync(CancellationToken ct) => write.FlushAsync(ct);
        public override int Read(byte[] buffer, int offset, int count) => read.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) => read.ReadAsync(buffer, ct);
        public override void Write(byte[] buffer, int offset, int count) => write.Write(buffer, offset, count);
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default) => write.WriteAsync(buffer, ct);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing) { read.Dispose(); write.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
#endif
