// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

#if WINDOWS
using System.Collections.Concurrent;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using Cyberdrome.Core;
using Cyberdrome.Obd;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

namespace Cyberdrome.App;

/// <summary>
/// Virtuell Bluetooth LE OBD-dongel – för iPhone, som inte kan klassisk Bluetooth (SPP).
/// Beter sig som vanliga BLE-ELM327-donglar (t.ex. Vgate iCar Pro BLE):
///   tjänst FFF0, FFF1 = svar (notify), FFF2 = kommandon (write).
/// Varje telefon får en egen Elm327Emulator mot samma virtuella bil.
/// </summary>
public sealed class BleObdServer : IAsyncDisposable
{
    public static readonly Guid ServiceUuid = BluetoothUuidHelper.FromShortId(0xFFF0);
    public static readonly Guid NotifyUuid = BluetoothUuidHelper.FromShortId(0xFFF1);
    public static readonly Guid WriteUuid = BluetoothUuidHelper.FromShortId(0xFFF2);

    private readonly VirtualCar _car;
    private GattServiceProvider? _provider;
    private GattLocalCharacteristic? _notify;
    private GattLocalCharacteristic? _write;
    private readonly ConcurrentDictionary<string, ObdLineSession> _sessions = new();
    private readonly ConcurrentDictionary<string, string> _names = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    public BleObdServer(VirtualCar car) => _car = car;

    public bool IsRunning => _provider is not null;

    /// <summary>Windows läge för annonseringen (Started = syns för telefoner).</summary>
    public string AdvertisementStatus => _provider?.AdvertisementStatus.ToString() ?? "Stopped";

    /// <summary>Telefoner som prenumererar på svaren (dvs. är anslutna i OBD-appen).</summary>
    public IReadOnlyList<string> ConnectedDevices =>
        _notify?.SubscribedClients.Select(c => _names.GetValueOrDefault(c.Session.DeviceId.Id, "telefon")).Distinct().ToList()
        ?? (IReadOnlyList<string>)[];

    public event EventHandler<ObdTrafficEntry>? Traffic;
    public event EventHandler? StatusChanged;

    public async Task StartAsync()
    {
        if (IsRunning) return;
        var adapter = await BluetoothAdapter.GetDefaultAsync()
                      ?? throw new InvalidOperationException("Datorn har ingen Bluetooth-adapter.");
        if (!adapter.IsLowEnergySupported || !adapter.IsPeripheralRoleSupported)
            throw new InvalidOperationException("Datorns Bluetooth kan inte agera Bluetooth LE-enhet (peripheral).");

        var r = await GattServiceProvider.CreateAsync(ServiceUuid);
        if (r.Error != BluetoothError.Success)
            throw new InvalidOperationException("Kunde inte skapa Bluetooth LE-tjänsten: " + r.Error);
        var provider = r.ServiceProvider;

        // FFF1: svar till telefonen (notify). Skrivning tillåts också, eftersom vissa appar skickar hit.
        var n = await provider.Service.CreateCharacteristicAsync(NotifyUuid, new GattLocalCharacteristicParameters
        {
            CharacteristicProperties = GattCharacteristicProperties.Notify | GattCharacteristicProperties.Read
                                       | GattCharacteristicProperties.Write | GattCharacteristicProperties.WriteWithoutResponse,
            ReadProtectionLevel = GattProtectionLevel.Plain,
            WriteProtectionLevel = GattProtectionLevel.Plain,
            UserDescription = "OBD svar",
        });
        // FFF2: kommandon från telefonen (write).
        var w = await provider.Service.CreateCharacteristicAsync(WriteUuid, new GattLocalCharacteristicParameters
        {
            CharacteristicProperties = GattCharacteristicProperties.Write | GattCharacteristicProperties.WriteWithoutResponse,
            WriteProtectionLevel = GattProtectionLevel.Plain,
            UserDescription = "OBD kommandon",
        });
        if (n.Error != BluetoothError.Success || w.Error != BluetoothError.Success)
            throw new InvalidOperationException("Kunde inte skapa Bluetooth LE-egenskaperna.");

        _notify = n.Characteristic;
        _write = w.Characteristic;
        _notify.ReadRequested += OnRead;
        _notify.WriteRequested += OnWrite;
        _write.WriteRequested += OnWrite;
        _notify.SubscribedClientsChanged += OnSubscribersChanged;

        provider.AdvertisementStatusChanged += (_, _) => StatusChanged?.Invoke(this, EventArgs.Empty);
        provider.StartAdvertising(new GattServiceProviderAdvertisingParameters { IsDiscoverable = true, IsConnectable = true });
        _provider = provider;
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    public Task StopAsync()
    {
        if (_provider is null) return Task.CompletedTask;
        try { _provider.StopAdvertising(); } catch { }
        if (_notify is not null)
        {
            _notify.ReadRequested -= OnRead;
            _notify.WriteRequested -= OnWrite;
            _notify.SubscribedClientsChanged -= OnSubscribersChanged;
        }
        if (_write is not null) _write.WriteRequested -= OnWrite;
        _provider = null; _notify = null; _write = null;
        _sessions.Clear();
        StatusChanged?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    private async void OnSubscribersChanged(GattLocalCharacteristic sender, object args)
    {
        foreach (var c in sender.SubscribedClients)
        {
            var id = c.Session.DeviceId.Id;
            if (_names.ContainsKey(id)) continue;
            var name = "telefon";
            try
            {
                using var dev = await BluetoothLEDevice.FromIdAsync(id);
                if (!string.IsNullOrWhiteSpace(dev?.Name)) name = dev.Name;
            }
            catch { }
            _names[id] = name;
            Traffic?.Invoke(this, new(DateTime.Now, "Bluetooth LE: " + name, false, "Ansluten via Bluetooth LE"));
        }
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    private async void OnRead(GattLocalCharacteristic sender, GattReadRequestedEventArgs args)
    {
        using var deferral = args.GetDeferral();
        var req = await args.GetRequestAsync();
        req?.RespondWithValue(Encoding.ASCII.GetBytes(">").AsBuffer());
    }

    private async void OnWrite(GattLocalCharacteristic sender, GattWriteRequestedEventArgs args)
    {
        using var deferral = args.GetDeferral();
        var req = await args.GetRequestAsync();
        if (req is null) return;
        var data = req.Value.ToArray();
        if (req.Option == GattWriteOption.WriteWithResponse) req.Respond();

        var id = args.Session.DeviceId.Id;
        var name = _names.GetValueOrDefault(id, "telefon");
        var session = _sessions.GetOrAdd(id, _ => new ObdLineSession(_car, "Bluetooth LE: " + name, e => Traffic?.Invoke(this, e)));
        var reply = session.Feed(Encoding.ASCII.GetString(data));
        if (reply.Length == 0 || _notify is null) return;

        var client = _notify.SubscribedClients.FirstOrDefault(c => c.Session.DeviceId.Id == id);
        if (client is null) return; // appen lyssnar inte på svar
        await _sendLock.WaitAsync();
        try
        {
            foreach (var chunk in BleChunker.Split(reply, client.Session.MaxPduSize))
                await _notify.NotifyValueAsync(chunk.AsBuffer(), client);
        }
        catch { /* telefonen kopplade ner */ }
        finally { _sendLock.Release(); }
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}
#endif
