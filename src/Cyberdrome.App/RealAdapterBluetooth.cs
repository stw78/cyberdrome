// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

#if WINDOWS
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading.Channels;
using Cyberdrome.Obd.Real;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Devices.Enumeration;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;

namespace Cyberdrome.App;


/// <summary>Söker Bluetooth-adaptrar: parkopplade SPP-enheter (klassisk Bluetooth) och BLE-enheter i närheten.</summary>
public static class AdapterDiscovery
{
    public static async Task<List<AdapterDevice>> FindSppAsync()
    {
        var list = new List<AdapterDevice>();
        var found = await DeviceInformation.FindAllAsync(RfcommDeviceService.GetDeviceSelector(RfcommServiceId.SerialPort));
        foreach (var d in found)
        {
            // Hoppa över Cyberdromes egen virtuella dongel.
            if (d.Name.Contains("Cyberdrome", StringComparison.OrdinalIgnoreCase)) continue;
            list.Add(new AdapterDevice(d.Name.Length > 0 ? d.Name : L.T("RealAdapterBluetooth_UnknownSPPDevice"), AdapterDeviceKind.Spp, d.Id, 0, null));
        }
        return list;
    }

    /// <summary>Lyssnar på BLE-annonser en stund. Visar namngivna enheter (OBD-namn först).</summary>
    public static async Task<List<AdapterDevice>> ScanBleAsync(TimeSpan time, CancellationToken ct = default)
    {
        var seen = new Dictionary<ulong, AdapterDevice>();
        var watcher = new BluetoothLEAdvertisementWatcher { ScanningMode = BluetoothLEScanningMode.Active };
        watcher.Received += (_, a) =>
        {
            var name = a.Advertisement.LocalName;
            lock (seen)
            {
                if (string.IsNullOrWhiteSpace(name) && seen.TryGetValue(a.BluetoothAddress, out var old)) name = old.Name;
                if (string.IsNullOrWhiteSpace(name)) return;
                seen[a.BluetoothAddress] = new AdapterDevice(name, AdapterDeviceKind.Ble, "", a.BluetoothAddress, a.RawSignalStrengthInDBm);
            }
        };
        watcher.Start();
        try { await Task.Delay(time, ct); } catch (OperationCanceledException) { }
        watcher.Stop();
        lock (seen)
            return seen.Values.OrderByDescending(d => LooksLikeObd(d.Name)).ThenByDescending(d => d.Rssi).ToList();
    }

    public static bool LooksLikeObd(string name) =>
        new[] { "OBD", "ELM", "vLink", "V-LINK", "VLINK", "OBDLink", "Vgate", "iCar", "Carly", "KONNWEI", "Veepeak", "BAFX", "Viecar", "LELink", "Kiwi", "IOS-Vlink", "Android-Vlink" }
            .Any(k => name.Contains(k, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Klassisk Bluetooth (SPP/RFCOMM) direkt, utan COM-port.</summary>
public sealed class RfcommAdapterTransport(AdapterDevice device) : IAdapterTransport
{
    private StreamSocket? _socket;
    public string Name => "Bluetooth " + device.Name;

    public async Task<Stream> OpenAsync(CancellationToken ct)
    {
        var svc = await RfcommDeviceService.FromIdAsync(device.Id) ?? throw new IOException(L.T("RealAdapterBluetooth_TheBluetoothServiceWasNotFound"));
        _socket = new StreamSocket();
        await _socket.ConnectAsync(svc.ConnectionHostName, svc.ConnectionServiceName, SocketProtectionLevel.BluetoothEncryptionAllowNullAuthentication).AsTask(ct);
        return new DuplexStream(_socket.InputStream.AsStreamForRead(0), _socket.OutputStream.AsStreamForWrite(0));
    }

    public ValueTask DisposeAsync() { _socket?.Dispose(); return ValueTask.CompletedTask; }
}

/// <summary>
/// BLE-adapter. Kända varianter: FFF0 (FFF1 notify / FFF2 write, t.ex. Vgate iCar Pro BLE), FFE0 (FFE1 för båda,
/// HM-10-typ), 18F0 (2AF0 notify / 2AF1 write, t.ex. vLinker) och OBDLink CX. Annars används första tjänst som har
/// ett notify- och ett skrivbart kännetecken.
/// </summary>
public sealed class BleAdapterTransport(AdapterDevice device, Action<string>? log = null) : IAdapterTransport
{
    private static readonly Guid[] Preferred =
    [
        BluetoothUuidHelper.FromShortId(0xFFF0), BluetoothUuidHelper.FromShortId(0xFFE0), BluetoothUuidHelper.FromShortId(0x18F0),
        Guid.Parse("e7810a71-73ae-499d-8c15-faa9aef0c3f2"), // OBDLink CX
    ];

    private BluetoothLEDevice? _dev;
    private BleStream? _stream;
    public string Name => "Bluetooth LE " + device.Name;

    /// <summary>Alla tjänster och kännetecken (för "Identifiera adapter").</summary>
    public List<string> Services { get; } = [];

    public async Task<Stream> OpenAsync(CancellationToken ct)
    {
        _dev = await BluetoothLEDevice.FromBluetoothAddressAsync(device.Address).AsTask(ct) ?? throw new IOException(L.T("RealAdapterBluetooth_TheBLEDeviceDoesNotAnswer"));
        var services = await _dev.GetGattServicesAsync(BluetoothCacheMode.Uncached).AsTask(ct);
        if (services.Status != GattCommunicationStatus.Success) throw new IOException(L.T("RealAdapterBluetooth_CouldNotReadTheBLEServices") + services.Status);
        (GattCharacteristic Notify, GattCharacteristic Write)? pick = null;
        int bestRank = int.MaxValue;
        foreach (var s in services.Services)
        {
            var chars = await s.GetCharacteristicsAsync(BluetoothCacheMode.Uncached).AsTask(ct);
            if (chars.Status != GattCommunicationStatus.Success) { Services.Add(L.F("RealAdapterBluetooth_Service0CharacteristicsCannotBeRead", Short(s.Uuid), chars.Status)); continue; }
            Services.Add(L.F("RealAdapterBluetooth_Service0", Short(s.Uuid)) + string.Join(", ", chars.Characteristics.Select(c => $"{Short(c.Uuid)} [{Props(c.CharacteristicProperties)}]")));
            var notify = chars.Characteristics.FirstOrDefault(c => c.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Notify) || c.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Indicate));
            var write = chars.Characteristics.FirstOrDefault(c => c.CharacteristicProperties.HasFlag(GattCharacteristicProperties.WriteWithoutResponse) || c.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Write));
            if (notify is null || write is null) continue;
            if (BluetoothUuidHelper.TryGetShortId(s.Uuid) is uint sid && sid is >= 0x1800 and <= 0x18FF && sid != 0x18F0)
                continue; // standardtjänster (batteri, enhetsinfo …) är inte OBD
            int rank = Array.IndexOf(Preferred, s.Uuid) is var i and >= 0 ? i : 100;
            if (rank < bestRank) { bestRank = rank; pick = (notify, write); }
        }
        foreach (var l in Services) log?.Invoke(l);
        if (pick is null) throw new NotElmException(L.T("RealAdapterBluetooth_NoServiceWithBothNotifyAnd"));
        var (n, w) = pick.Value;
        log?.Invoke(L.F("RealAdapterBluetooth_Using0Replies1Commands2", Short(n.Service.Uuid), Short(n.Uuid), Short(w.Uuid)));
        _stream = new BleStream(w);
        n.ValueChanged += (_, e) => _stream.Push(e.CharacteristicValue.ToArray());
        var cccd = n.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Notify)
            ? GattClientCharacteristicConfigurationDescriptorValue.Notify : GattClientCharacteristicConfigurationDescriptorValue.Indicate;
        var st = await n.WriteClientCharacteristicConfigurationDescriptorAsync(cccd).AsTask(ct);
        if (st != GattCommunicationStatus.Success) throw new IOException(L.T("RealAdapterBluetooth_CouldNotTurnOnNotifications") + st);
        return _stream;
    }

    private static string Short(Guid g)
    {
        var s = g.ToString();
        return s.EndsWith("-0000-1000-8000-00805f9b34fb", StringComparison.OrdinalIgnoreCase) && s.StartsWith("0000") ? s[4..8].ToUpperInvariant() : s;
    }

    private static string Props(GattCharacteristicProperties p)
    {
        var l = new List<string>();
        if (p.HasFlag(GattCharacteristicProperties.Read)) l.Add(L.T("RealAdapterBluetooth_Read"));
        if (p.HasFlag(GattCharacteristicProperties.Write)) l.Add(L.T("Ble_PropWrite"));
        if (p.HasFlag(GattCharacteristicProperties.WriteWithoutResponse)) l.Add(L.T("Ble_PropWriteNoResponse"));
        if (p.HasFlag(GattCharacteristicProperties.Notify)) l.Add("notify");
        if (p.HasFlag(GattCharacteristicProperties.Indicate)) l.Add("indicate");
        return string.Join("/", l);
    }

    public ValueTask DisposeAsync() { _stream?.Complete(); _dev?.Dispose(); return ValueTask.CompletedTask; }

    /// <summary>Ström över två BLE-kännetecken (skriver i bitar om 20 byte).</summary>
    private sealed class BleStream(GattCharacteristic write) : Stream
    {
        private readonly Channel<byte[]> _in = Channel.CreateUnbounded<byte[]>();
        private byte[] _left = [];
        private int _pos;

        public void Push(byte[] b) => _in.Writer.TryWrite(b);
        public void Complete() => _in.Writer.TryComplete();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (_pos >= _left.Length)
            {
                if (!await _in.Reader.WaitToReadAsync(ct)) return 0;
                _left = await _in.Reader.ReadAsync(ct);
                _pos = 0;
            }
            int n = Math.Min(buffer.Length, _left.Length - _pos);
            _left.AsMemory(_pos, n).CopyTo(buffer);
            _pos += n;
            return n;
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
        {
            var opt = write.CharacteristicProperties.HasFlag(GattCharacteristicProperties.WriteWithoutResponse) ? GattWriteOption.WriteWithoutResponse : GattWriteOption.WriteWithResponse;
            for (int i = 0; i < buffer.Length; i += 20)
            {
                var chunk = buffer.Slice(i, Math.Min(20, buffer.Length - i)).ToArray();
                var st = await write.WriteValueAsync(chunk.AsBuffer(), opt).AsTask(ct);
                if (st != GattCommunicationStatus.Success) throw new IOException(L.T("RealAdapterBluetooth_BLEWriteFailed") + st);
            }
        }

        public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
        public override void Write(byte[] buffer, int offset, int count) => WriteAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
        public override void Flush() { }
        public override bool CanRead => true;
        public override bool CanWrite => true;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
        public override void SetLength(long v) => throw new NotSupportedException();
    }
}

/// <summary>Läs från en ström, skriv till en annan.</summary>
public sealed class DuplexStream(Stream read, Stream write) : Stream
{
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) => read.ReadAsync(buffer, ct);
    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default) { await write.WriteAsync(buffer, ct); await write.FlushAsync(ct); }
    public override int Read(byte[] b, int o, int c) => read.Read(b, o, c);
    public override void Write(byte[] b, int o, int c) { write.Write(b, o, c); write.Flush(); }
    public override Task FlushAsync(CancellationToken ct) => write.FlushAsync(ct);
    public override void Flush() => write.Flush();
    public override bool CanRead => true;
    public override bool CanWrite => true;
    public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
    public override void SetLength(long v) => throw new NotSupportedException();
    protected override void Dispose(bool disposing) { if (disposing) { read.Dispose(); write.Dispose(); } base.Dispose(disposing); }
}
#endif
