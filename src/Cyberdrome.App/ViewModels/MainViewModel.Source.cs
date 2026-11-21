// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cyberdrome.Obd;
using Cyberdrome.Obd.Real;

namespace Cyberdrome.App.ViewModels;

/// <summary>Sidan OBD-källa: virtuell bil eller riktig adapter (läsläge som standard).</summary>
public sealed partial class MainViewModel
{
    public string[] AdapterKinds { get; } = ["USB eller Bluetooth via COM-port", "Bluetooth (parkopplad)", "Bluetooth LE", "Wi-Fi"];
    public string[] BaudRates { get; } = ["38400", "9600", "115200", "230400", "500000"];

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsComKind), nameof(IsBtKind), nameof(IsWifiKind))] private int _adapterKindIndex;
    public bool IsComKind => AdapterKindIndex == 0;
    public bool IsBtKind => AdapterKindIndex is 1 or 2;
    public bool IsWifiKind => AdapterKindIndex == 3;

    public ObservableCollection<string> ComPorts { get; } = new();
    [ObservableProperty] private string? _selectedComPort;
    [ObservableProperty] private string _selectedBaud = "38400";
    public ObservableCollection<AdapterDevice> BtDevices { get; } = new();
    [ObservableProperty] private AdapterDevice? _selectedBtDevice;
    [ObservableProperty] private string _wifiHost = "192.168.0.10";
    [ObservableProperty] private string _wifiPort = "35000";

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanUseAdapter))] private bool _adapterConnected;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanUseAdapter))] private bool _adapterBusy;
    public bool CanUseAdapter => AdapterConnected && !AdapterBusy;
    [ObservableProperty] private string _adapterStatus = "Ingen adapter ansluten. Simulatorn använder den virtuella bilen.";
    [ObservableProperty] private string _scanText = "";
    [ObservableProperty] private bool _hideVinSerial = true;
    [ObservableProperty] private bool _liveValues;
    [ObservableProperty] private string _liveText = "";

    /// <summary>Riktig adapter som källa: apparna i Android pratar med den riktiga bilen.</summary>
    [ObservableProperty] private bool _sourceIsReal;
    /// <summary>Tillåt skrivning för den här sessionen (nollställs när adaptern kopplas från).</summary>
    [ObservableProperty] private bool _allowWrites;

    private ObdAdapter? _adapter;
    private RealCarBridge? _bridge;
    private RealCarScan? _lastScan;
    private CancellationTokenSource? _liveCts;

    /// <summary>Sätts av fönstret: visar bekräftelsedialogen för en skrivning.</summary>
    public Func<WriteRequest, Task<bool>>? ConfirmWrite { get; set; }

    private void AdapterLog(string text, bool toAdapter = false) =>
        Dispatcher.UIThread.Post(() => AddLog(new ObdTrafficEntry(DateTime.Now, "adapter", toAdapter, text)));

    [RelayCommand]
    private void RefreshComPorts()
    {
        ComPorts.Clear();
        foreach (var p in SerialAdapterTransport.PortNames()) ComPorts.Add(p);
        SelectedComPort ??= ComPorts.FirstOrDefault();
        AdapterStatus = ComPorts.Count == 0 ? "Inga COM-portar hittades. Anslut USB-adaptern eller parkoppla Bluetooth-adaptern i Windows." : $"{ComPorts.Count} COM-portar.";
    }

    [RelayCommand]
    private async Task ScanBluetooth()
    {
#if WINDOWS
        AdapterBusy = true;
        AdapterStatus = "Söker Bluetooth-adaptrar (8 s) …";
        try
        {
            BtDevices.Clear();
            var spp = await AdapterDiscovery.FindSppAsync();
            var ble = await AdapterDiscovery.ScanBleAsync(TimeSpan.FromSeconds(8));
            foreach (var d in spp.Concat(ble)) BtDevices.Add(d);
            SelectedBtDevice = BtDevices.FirstOrDefault(d => AdapterDiscovery.LooksLikeObd(d.Name)) ?? BtDevices.FirstOrDefault();
            AdapterStatus = $"Hittade {spp.Count} parkopplade och {ble.Count} BLE-enheter. Klassisk Bluetooth måste parkopplas i Windows först (kod ofta 1234 eller 0000).";
        }
        catch (Exception ex) { AdapterStatus = "Bluetooth-sökningen misslyckades: " + ex.Message; }
        finally { AdapterBusy = false; }
#else
        AdapterStatus = "Bluetooth stöds bara på Windows.";
        await Task.CompletedTask;
#endif
    }

    private IAdapterTransport? MakeTransport(Action<string>? log = null)
    {
        switch (AdapterKindIndex)
        {
            case 0:
                if (SelectedComPort is null) { AdapterStatus = "Välj en COM-port."; return null; }
                return new SerialAdapterTransport(SelectedComPort, int.Parse(SelectedBaud));
            case 3:
                if (!int.TryParse(WifiPort, out var port)) { AdapterStatus = "Ogiltig port."; return null; }
                return new TcpAdapterTransport(WifiHost.Trim(), port);
#if WINDOWS
            case 1 or 2:
                if (SelectedBtDevice is not { } d) { AdapterStatus = "Sök och välj en Bluetooth-adapter."; return null; }
                return d.Kind == AdapterDeviceKind.Spp ? new RfcommAdapterTransport(d) : new BleAdapterTransport(d, log);
#endif
        }
        AdapterStatus = "Den här anslutningstypen stöds inte här.";
        return null;
    }

    [RelayCommand]
    private async Task ConnectAdapter()
    {
        if (AdapterConnected) { await DisconnectAdapter(); return; }
        var t = MakeTransport(s => AdapterLog(s));
        if (t is null) return;
        AdapterBusy = true;
        AdapterStatus = $"Ansluter till {t.Name} …";
        var elm = new ElmConnection(t);
        elm.Traffic += (to, text) => AdapterLog(text, to);
        try
        {
            await elm.OpenAsync();
            var a = new ObdAdapter(elm);
            var info = await a.InitAsync();
            _adapter = a;
            _bridge = new RealCarBridge(a, RealCarBridge.DefaultBackupFolder, req => ConfirmWrite?.Invoke(req) ?? Task.FromResult(false));
            AdapterConnected = true;
            AdapterStatus = $"Ansluten: {t.Name} · {info.Version}{(info.StnVersion is { } s ? " (" + s + ")" : "")} · protokoll {info.Protocol} {info.ProtocolName}" +
                            (info.Voltage is { } v ? $" · {v:0.0} V" : "") + " · läsläge";
            ApplySource();
        }
        catch (NotElmException ex)
        {
            await elm.DisposeAsync();
            AdapterStatus = "Adaptern stöds inte (låst till tillverkarens app eller inte ELM327-kompatibel). " + ex.Message +
                            " Använd en ELM327-kompatibel adapter, t.ex. vLinker eller OBDLink.";
        }
        catch (Exception ex)
        {
            await elm.DisposeAsync();
            AdapterStatus = "Kunde inte ansluta: " + ex.Message;
        }
        finally { AdapterBusy = false; }
    }

    [RelayCommand]
    private async Task DisconnectAdapter()
    {
        LiveValues = false;
        AllowWrites = false;
        if (_car is not null) _car.Bridge = null;
        var a = _adapter;
        _adapter = null; _bridge = null;
        AdapterConnected = false;
        if (a is not null) await a.DisposeAsync();
        AdapterStatus = "Adaptern är frånkopplad. Simulatorn använder den virtuella bilen.";
    }

    partial void OnSourceIsRealChanged(bool value) => ApplySource();

    private void ApplySource()
    {
        if (_car is null) return;
        _car.Bridge = SourceIsReal && _bridge is not null ? _bridge : null;
        AdapterLog(_car.Bridge is not null
            ? "🔌 Vidarekoppling PÅ: appar (Wi-Fi, Bluetooth, BLE) pratar nu med den riktiga bilen via adaptern" + (AllowWrites ? " – SKRIVNING TILLÅTEN" : " – läsläge")
            : "🔌 Källa: virtuell bil");
    }

    partial void OnAllowWritesChanged(bool value)
    {
        if (_bridge is not null) _bridge.AllowWrites = value;
        if (value) AdapterLog("⚠ Skrivning tillåten för den här sessionen. Varje skrivning måste bekräftas och originalvärdet sparas först.");
        if (AdapterConnected) AdapterStatus = AdapterStatus.Replace(value ? "läsläge" : "SKRIVNING TILLÅTEN", value ? "SKRIVNING TILLÅTEN" : "läsläge");
    }

    [RelayCommand]
    private async Task ScanRealCar()
    {
        if (_adapter is null) return;
        AdapterBusy = true;
        ScanText = "";
        try
        {
            var progress = new Progress<string>(s => AdapterStatus = "Läser riktig bil: " + s);
            _lastScan = await Task.Run(() => RealCarScanner.ScanAsync(_adapter, true, progress));
            ScanText = _lastScan.Summary();
            AdapterStatus = "Avläsningen är klar (bara lästjänster användes).";
        }
        catch (Exception ex) { AdapterStatus = "Avläsningen misslyckades: " + ex.Message; }
        finally { AdapterBusy = false; }
    }

    [RelayCommand]
    private void CreateCarFromScan()
    {
        if (_lastScan is null || _profileStore is null) { AdapterStatus = "Läs bilen först."; return; }
        try
        {
            var p = RealCarScanner.ToProfile(_lastScan, _profileStore, HideVinSerial);
            var errors = Cyberdrome.Core.Profiles.ProfileLoader.Validate(p);
            if (errors.Count > 0) throw new InvalidDataException(string.Join(" ", errors));
            _profileStore.Save(p);
            ReloadOwnProfiles(p.Id);
            AdapterStatus = $"Ny bilmodell: {p.DisplayName} ({p.Ecus.Count} styrdon, {p.SupportedPids?.Count ?? 0} PID:ar). Ändra namn/modell på sidan Bilmodeller.";
        }
        catch (Exception ex) { AdapterStatus = "Kunde inte skapa bilmodellen: " + ex.Message; }
    }

    partial void OnLiveValuesChanged(bool value)
    {
        _liveCts?.Cancel();
        _liveCts = null;
        if (!value || _adapter is null) { LiveText = ""; return; }
        var cts = _liveCts = new CancellationTokenSource();
        var a = _adapter;
        _ = Task.Run(async () =>
        {
            byte[] pids = [0x0D, 0x0C, 0x05, 0x2F, 0x11, 0x42];
            string[] names = ["km/h", "rpm", "kylv. °C", "bränsle %", "gasspjäll %", "V"];
            while (!cts.IsCancellationRequested)
            {
                var parts = new List<string>();
                for (int i = 0; i < pids.Length && !cts.IsCancellationRequested; i++)
                {
                    try
                    {
                        var (r, _) = await a.RequestAsync(ObdAdapter.FunctionalHeader, [0x01, pids[i]], 1500, cts.Token);
                        if (r.FirstOrDefault(x => x.Data.Length > 2 && x.Data[0] == 0x41) is { } x && RealCarScanner.Decode(pids[i], x.Data[2..]) is { } v)
                            parts.Add($"{v:0.#} {names[i]}");
                    }
                    catch (OperationCanceledException) { return; }
                    catch (Exception ex) { parts.Add("fel: " + ex.Message); break; }
                }
                var text = parts.Count > 0 ? string.Join("   ", parts) : "inga värden";
                Dispatcher.UIThread.Post(() => { if (!cts.IsCancellationRequested) LiveText = text; });
                try { await Task.Delay(1000, cts.Token); } catch { return; }
            }
        });
    }

    /// <summary>
    /// "Identifiera adapter": ansluter, listar tjänster (BLE) och provar ATZ/ATI/ATE0/0100. Allt hamnar i OBD-loggen.
    /// Proprietära protokoll (t.ex. adaptrar låsta till en tillverkares app) analyseras inte.
    /// </summary>
    [RelayCommand]
    private async Task IdentifyAdapter()
    {
        if (AdapterConnected) { AdapterStatus = "Koppla från adaptern först."; return; }
        var t = MakeTransport(s => AdapterLog("🔎 " + s));
        if (t is null) return;
        AdapterBusy = true;
        AdapterLog($"🔎 Identifierar {t.Name} …");
        AdapterStatus = $"Identifierar {t.Name} …";
        var elm = new ElmConnection(t);
        elm.Traffic += (to, text) => AdapterLog(text, to);
        string verdict;
        try
        {
            await elm.OpenAsync();
            var answers = new List<string>();
            foreach (var cmd in new[] { "ATZ", "ATI", "ATE0", "0100" })
            {
                try { answers.Add(await elm.SendAsync(cmd, cmd == "0100" ? 8000 : 2500)); }
                catch (TimeoutException) { answers.Add(""); }
            }
            var all = string.Join(" ", answers);
            if (ObdAdapter.LooksLikeElm(all))
                verdict = $"✅ ELM327-kompatibel: {answers[1].Split('\n').Last()}" +
                          (answers[3].Contains("41 00") || answers[3].Contains("4100") ? " – bilen svarar på OBD." : " – ingen bil svarade på 0100 (tändningen på?).");
            else if (answers.All(a => a.Length == 0))
                verdict = "❌ Adaptern stöds inte (låst till tillverkarens app): den svarar inte på ELM327-kommandon. Använd en ELM327-kompatibel adapter, t.ex. vLinker eller OBDLink.";
            else
                verdict = "❌ Adaptern stöds inte (låst till tillverkarens app): den svarar, men inte som en ELM327. Använd en ELM327-kompatibel adapter, t.ex. vLinker eller OBDLink.";
        }
        catch (NotElmException ex) { verdict = "❌ Adaptern stöds inte (låst till tillverkarens app): " + ex.Message + " Använd en ELM327-kompatibel adapter, t.ex. vLinker eller OBDLink."; }
        catch (Exception ex) { verdict = "❌ Kunde inte ansluta: " + ex.Message; }
        finally { await elm.DisposeAsync(); AdapterBusy = false; }
        AdapterLog("🔎 Resultat: " + verdict);
        AdapterStatus = verdict;
    }
}
