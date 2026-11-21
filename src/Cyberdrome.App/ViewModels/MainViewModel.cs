// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

using System.Collections.ObjectModel;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cyberdrome.Core;
using Cyberdrome.Core.Profiles;
using Cyberdrome.Obd;
using Cyberdrome.Vm;

namespace Cyberdrome.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private const int MaxLogLines = 500;
    private readonly VirtualCar? _car;
    private readonly ObdTcpServer? _server;
    private readonly DispatcherTimer _timer;

    public MainViewModel(string profileFolder)
    {
        _android = AndroidBackends.Create(_vmSettings);
        try
        {
            foreach (var p in ProfileLoader.LoadFolder(profileFolder)) Profiles.Add(p);
        }
        catch (Exception ex)
        {
            ErrorText = L.T("Main_CouldNotReadTheCarModels") + ex.Message;
        }

        if (Profiles.Count == 0)
        {
            ErrorText ??= L.F("Main_NoCarModelsFoundIn0", profileFolder);
            _timer = new DispatcherTimer();
            return;
        }

        InitCars(Profiles.Select(p => p.Id).ToList()); // egna bilmodeller från %AppData%\Cyberdrome\bilar
        InitUpdates();
        _car = new VirtualCar(Profiles[0]);
        _selectedProfile = Profiles[0];
        _server = new ObdTcpServer(_car);
        _server.Traffic += (_, e) => Dispatcher.UIThread.Post(() => AddLog(e));
        _server.StatusChanged += (_, _) => Dispatcher.UIThread.Post(UpdateObdStatus);
        _car.State.Changed += (_, _) => Dispatcher.UIThread.Post(UpdateDtcs);

        // Startvärden som i skissen
        Speed = 54;
        Rpm = 1850;
        Coolant = 88;
        Fuel = 62;
        Gear = Core.Gear.D;
        Ignition = true;

        LoadFeatures();
        UpdateDtcs();
        StartObd();
        if (_appSettings.BluetoothObd) BluetoothEnabled = true; // startar Bluetooth om det var påslaget förra gången
        if (_appSettings.BleObd) BleEnabled = true;
        InitDrives();
        InitMap();
        InitLook();
        InitTests();
        _ = RefreshAndroid();

        // Uppdatera "för x sekunder sedan" en gång per sekund.
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => UpdateObdStatus());
        _timer.Start();

        // Android kan startas eller stängas utanför programmet (eller adb var inte redo vid start): kontrollera var 5:e sekund.
        _androidWatch = new DispatcherTimer(TimeSpan.FromSeconds(5), DispatcherPriority.Background, (_, _) => _ = WatchAndroidAsync());
        _androidWatch.Start();
    }

    public ObservableCollection<CarProfile> Profiles { get; } = new();
    public string? ErrorText { get; }
    public bool HasError => ErrorText is not null;

    // ---------------- Navigering ----------------

    /// <summary>Sidorna i menyn (översatta, byts när språket ändras).</summary>
    public PageItem[] PageKeys { get; } = new[] { "Nav_Overview", "Nav_HiddenFeatures", "Nav_ObdLog", "Nav_Android", "Nav_Drives", "Nav_Map", "Nav_TestCases", "Nav_CarModels", "Nav_ObdSource", "Nav_CarDatabase", "Nav_Settings" }.Select(k => new PageItem(k)).ToArray();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOverview), nameof(IsFeatures), nameof(IsLog), nameof(IsAndroid), nameof(IsDrives), nameof(IsMap), nameof(IsTests), nameof(IsCars), nameof(IsSource), nameof(IsCarDb), nameof(IsSettings))]
    private int _pageIndex;

    public bool IsDrives => PageIndex == 4;
    public bool IsMap => PageIndex == 5;
    public bool IsTests => PageIndex == 6;
    public bool IsCars => PageIndex == 7;
    public bool IsSource => PageIndex == 8;
    public bool IsCarDb => PageIndex == 9;
    public bool IsSettings => PageIndex == 10;

    public bool IsOverview => PageIndex == 0;
    public bool IsFeatures => PageIndex == 1;
    public bool IsLog => PageIndex == 2;
    public bool IsAndroid => PageIndex == 3;

    // Läs av Android-läget på nytt när man går till Android-sidan, så att knapparna
    // alltid stämmer utan att programmet behöver startas om.
    partial void OnPageIndexChanged(int value)
    {
        if (value == 3 && !AndroidBusy) _ = RefreshAndroid();
        UpdateScreenLoop(); // skärmströmmen går bara när Översikt visas
        if (value == 8 && ComPorts.Count == 0) RefreshComPorts();
        if (value == 9 && CarDbHits.Count == 0) SearchCarDb();
    }

    // ---------------- Bilmodell ----------------

    [ObservableProperty]
    private CarProfile? _selectedProfile;

    partial void OnSelectedProfileChanged(CarProfile? value)
    {
        if (value is null || _car is null) return;
        _car.LoadProfile(value);
        ApplyProfileDefaults(value);
        OnPropertyChanged(nameof(SelectedIsOwn));
        ApplySecurityChoice();
        LoadFeatures();
        OnPropertyChanged(nameof(SelectedThemeOption));
        OnPropertyChanged(nameof(SelectedSkinOption));
        _ = ApplyCarLookAsync(); // bilens eget tema, skal och skärmformat
        AddLog(new ObdTrafficEntry(DateTime.Now, "program", false, L.F("Main_CarModelChangedTo0", value.DisplayName)));
    }

    /// <summary>Bilmodellens egna startvärden (egna modeller kan ha det).</summary>
    private void ApplyProfileDefaults(CarProfile p)
    {
        if (p.DefaultValues is not { } d || IsPlayingDrive) return;
        if (d.Speed is { } s) Speed = s;
        if (d.Rpm is { } r) Rpm = r;
        if (d.Coolant is { } c) Coolant = c;
        if (d.Fuel is { } f) Fuel = f;
        if (d.BatteryVolt is { } b && _car is not null) _car.State.BatteryVolt = b;
    }

    public string CarInfo => SelectedProfile is null ? "" :
        $"VIN {SelectedProfile.Vin} · {Elm327Emulator.ProtocolName(SelectedProfile.Protocol)} · {SelectedProfile.Ecus.Count} styrdon";

    // ---------------- Bilen just nu ----------------

    [ObservableProperty] private double _speed;
    [ObservableProperty] private double _rpm;
    [ObservableProperty] private double _coolant;
    [ObservableProperty] private double _fuel;
    [ObservableProperty] private bool _ignition;
    [ObservableProperty] private bool _dark;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GearP), nameof(GearR), nameof(GearN), nameof(GearD))]
    private Gear _gear;

    partial void OnSpeedChanged(double value) { if (_car is not null) _car.State.SpeedKmh = value; }
    partial void OnRpmChanged(double value) { if (_car is not null) _car.State.Rpm = value; }
    partial void OnCoolantChanged(double value) { if (_car is not null) _car.State.CoolantC = value; }
    partial void OnFuelChanged(double value) { if (_car is not null) _car.State.FuelPercent = value; }
    partial void OnIgnitionChanged(bool value) { if (_car is not null) _car.State.Ignition = value; OnPropertyChanged(nameof(CarRunningText)); }
    partial void OnDarkChanged(bool value) { if (_car is not null) _car.State.Dark = value; }
    partial void OnGearChanged(Gear value) { if (_car is not null) _car.State.Gear = value; }

    public bool GearP { get => Gear == Core.Gear.P; set { if (value) Gear = Core.Gear.P; } }
    public bool GearR { get => Gear == Core.Gear.R; set { if (value) Gear = Core.Gear.R; } }
    public bool GearN { get => Gear == Core.Gear.N; set { if (value) Gear = Core.Gear.N; } }
    public bool GearD { get => Gear == Core.Gear.D; set { if (value) Gear = Core.Gear.D; } }

    public string CarRunningText => Ignition ? L.T("Main_TheCarIsRunning") : L.T("Main_TheIgnitionIsOff");

    // ---------------- Felkoder ----------------

    [ObservableProperty] private string _dtcText = "";
    [ObservableProperty] private string _newDtc = "";

    /// <summary>Snabbval för vanliga felkoder (av/på).</summary>
    public ObservableCollection<DtcToggleViewModel> DtcToggles { get; } = new();

    private void UpdateDtcs()
    {
        var d = _car?.State.Dtcs ?? [];
        var texts = _car?.Coding.Profile.DtcDescriptions;
        DtcText = d.Count == 0 ? L.T("Main_NoTroubleCodes") : string.Join(", ", d.Select(c => texts?.GetValueOrDefault(c) is { Length: > 0 } t ? $"{c} ({t})" : c));
        if (_car is not null && DtcToggles.Count == 0)
            foreach (var (code, desc) in DtcToggleViewModel.Common)
                DtcToggles.Add(new DtcToggleViewModel(code, desc, _car.State));
        foreach (var t in DtcToggles) t.Refresh();
    }

    [RelayCommand]
    private void AddDtc()
    {
        var code = NewDtc.Trim().ToUpperInvariant();
        if (code.Length == 5 && "PCBU".Contains(code[0]) && code[1..].All(Uri.IsHexDigit))
        {
            _car?.State.AddDtc(code);
            NewDtc = "";
        }
    }

    [RelayCommand]
    private void ClearDtcs() => _car?.State.ClearDtcs();

    // ---------------- Dolda funktioner ----------------

    public ObservableCollection<FeatureViewModel> Features { get; } = new();

    public string FeatureSummary => SelectedProfile is null ? "" :
        $"{SelectedProfile.Model} har {Features.Count} exempelfunktioner.";

    public string FeatureOnText => L.F("Main_0TurnedOn", Features.Count(f => f.IsOn));

    private void LoadFeatures()
    {
        if (_car is null) return;
        foreach (var f in Features) f.PropertyChanged -= OnFeatureChanged;
        Features.Clear();
        foreach (var f in _car.Profile.Features)
        {
            var vm = new FeatureViewModel(f, _car.Coding);
            vm.PropertyChanged += OnFeatureChanged;
            Features.Add(vm);
        }
        _car.Coding.Changed += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            foreach (var f in Features) f.Refresh();
        });
        OnPropertyChanged(nameof(FeatureSummary));
        OnPropertyChanged(nameof(FeatureOnText));
        OnPropertyChanged(nameof(CarInfo));
    }

    private void OnFeatureChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) =>
        OnPropertyChanged(nameof(FeatureOnText));

    [RelayCommand]
    private void ResetCoding() => _car?.Coding.Reset();

    // Valet "Kräv säkerhetsnyckel" kom ihåg per bilmodell medan programmet körs.
    private readonly Dictionary<string, bool> _requireSecurity = new();

    /// <summary>Om skrivning av kodning (UDS 2E) kräver säkerhetsnyckel (UDS 27) för vald bilmodell.</summary>
    public bool RequireSecurityAccess
    {
        get => _car?.Coding.RequireSecurityAccess ?? true;
        set
        {
            if (_car is null || _car.Coding.RequireSecurityAccess == value) return;
            _car.Coding.RequireSecurityAccess = value;
            _requireSecurity[_car.Profile.Id] = value;
            OnPropertyChanged();
            AddLog(new ObdTrafficEntry(DateTime.Now, "program", false,
                value ? L.T("Main_ASecurityKeyUDS27Is") : L.T("Main_ASecurityKeyUDS27Is2")));
        }
    }

    private void ApplySecurityChoice()
    {
        if (_car is null) return;
        _car.Coding.RequireSecurityAccess = _requireSecurity.GetValueOrDefault(_car.Profile.Id, true);
        OnPropertyChanged(nameof(RequireSecurityAccess));
    }

    // ---------------- Virtuell OBD ----------------

    [ObservableProperty] private bool _obdRunning;
    [ObservableProperty] private string _obdStatus = "";
    [ObservableProperty] private string _obdClients = "";
    [ObservableProperty] private string _obdLastAnswer = "";

    public string ObdAddress => string.Join("  " + L.T("Ui_Or") + "  ", LocalAddresses().Select(a => $"{a}:{ObdTcpServer.DefaultPort}"));

    [RelayCommand]
    private void StartObd()
    {
        if (_server is null) return;
        try
        {
            _server.Start();
        }
        catch (SocketException ex)
        {
            ObdStatus = L.F("Main_CouldNotStartOnPort0", ObdTcpServer.DefaultPort, ex.Message);
        }
        UpdateObdStatus();
    }

    [RelayCommand]
    private async Task StopObd()
    {
        if (_server is not null) await _server.StopAsync();
        UpdateObdStatus();
    }

    // ----- Bluetooth OBD-dongel ("OBDII") -----

    private readonly AppSettings _appSettings = AppSettings.Load();
#if WINDOWS
    private BluetoothObdServer? _bt;
#endif

    public bool BluetoothSupported => OperatingSystem.IsWindows();

    [ObservableProperty] private string _bluetoothStatus = L.T("Main_BluetoothIsOff");
    [ObservableProperty] private string _bluetoothName = Environment.MachineName;

    private bool _bluetoothEnabled;
    public bool BluetoothEnabled
    {
        get => _bluetoothEnabled;
        set
        {
            if (_bluetoothEnabled == value) return;
            _bluetoothEnabled = value;
            OnPropertyChanged();
            _appSettings.BluetoothObd = value;
            _appSettings.Save();
            _ = ApplyBluetoothAsync();
        }
    }

    private async Task ApplyBluetoothAsync()
    {
#if WINDOWS
        if (_car is null) return;
        if (_bluetoothEnabled)
        {
            if (_bt is null)
            {
                _bt = new BluetoothObdServer(_car);
                _bt.Traffic += (_, e) => Dispatcher.UIThread.Post(() => AddLog(e));
                _bt.StatusChanged += (_, _) => Dispatcher.UIThread.Post(UpdateBluetoothStatus);
            }
            BluetoothStatus = L.T("Main_StartingBluetooth");
            try
            {
                await _bt.StartAsync();
                if (_bt.AdapterName is { } n) BluetoothName = n;
            }
            catch (Exception ex)
            {
                BluetoothStatus = L.T("Main_BluetoothCouldNotBeStarted") + ex.Message;
                return;
            }
        }
        else if (_bt is not null)
        {
            await _bt.StopAsync();
        }
        UpdateBluetoothStatus();
#else
        BluetoothStatus = L.T("Main_BluetoothOBDIsOnlyAvailableOn");
        await Task.CompletedTask;
#endif
    }

    // ----- Bluetooth LE OBD-dongel (iPhone) -----
#if WINDOWS
    private BleObdServer? _ble;
#endif

    [ObservableProperty] private string _bleStatus = L.T("Main_BluetoothLEIsOff");

    private bool _bleEnabled;
    public bool BleEnabled
    {
        get => _bleEnabled;
        set
        {
            if (_bleEnabled == value) return;
            _bleEnabled = value;
            OnPropertyChanged();
            _appSettings.BleObd = value;
            _appSettings.Save();
            _ = ApplyBleAsync();
        }
    }

    private async Task ApplyBleAsync()
    {
#if WINDOWS
        if (_car is null) return;
        if (_bleEnabled)
        {
            if (_ble is null)
            {
                _ble = new BleObdServer(_car);
                _ble.Traffic += (_, e) => Dispatcher.UIThread.Post(() => AddLog(e));
                _ble.StatusChanged += (_, _) => Dispatcher.UIThread.Post(UpdateBleStatus);
            }
            BleStatus = L.T("Main_StartingBluetoothLE");
            try { await _ble.StartAsync(); }
            catch (Exception ex) { BleStatus = L.T("Main_BluetoothLECouldNotBeStarted") + ex.Message; return; }
        }
        else if (_ble is not null) await _ble.StopAsync();
        UpdateBleStatus();
#else
        BleStatus = L.T("Main_BluetoothLEOBDIsOnlyAvailable");
        await Task.CompletedTask;
#endif
    }

    private void UpdateBleStatus()
    {
#if WINDOWS
        if (_ble is null || !_ble.IsRunning) { BleStatus = L.T("Main_BluetoothLEIsOff"); return; }
        if (_ble.AdvertisementStatus is "Aborted" or "Stopped")
        {
            BleStatus = L.F("Main_WindowsStoppedTheBluetoothLEAdvertising", _ble.AdvertisementStatus);
            return;
        }
        var devices = _ble.ConnectedDevices;
        BleStatus = devices.Count == 0
            ? L.F("Main_WaitingForAnIPhoneVisibleAs", BluetoothName)
            : L.T("Main_ConnectedPrefix") + string.Join(", ", devices);
#endif
    }

    private void UpdateBluetoothStatus()
    {
#if WINDOWS
        if (_bt is null || !_bt.IsRunning) { BluetoothStatus = L.T("Main_BluetoothIsOff"); return; }
        var devices = _bt.ConnectedDevices;
        BluetoothStatus = devices.Count == 0
            ? L.F("Main_WaitingForPairingConnectionVisibleAs", BluetoothName)
            : L.T("Main_ConnectedPrefix") + string.Join(", ", devices);
#endif
    }

    private void UpdateObdStatus()
    {
        if (_server is null) return;
        ObdRunning = _server.IsRunning;
        if (_server.IsRunning) ObdStatus = L.T("Main_VirtualOBDAdapterIsOn");
        else if (!ObdStatus.StartsWith("Kunde")) ObdStatus = L.T("Main_VirtualOBDAdapterIsOff");
        ObdClients = _server.ClientCount switch
        {
            0 => L.T("Main_NoAppConnectedYet"),
            1 => L.T("Main_1AppConnected"),
            var n => L.F("Main_0AppsConnected", n),
        };
        if (_server.LastRequest is { } t)
        {
            var s = (int)(DateTime.Now - t).TotalSeconds;
            ObdLastAnswer = s < 2 ? L.T("Main_ReplySentJustNow") : L.F("Main_LastReply0SecondsAgo", s);
        }
        else ObdLastAnswer = L.T("Main_WaitingForTheFirstRequest");
    }

    private static IEnumerable<IPAddress> LocalAddresses()
    {
        var list = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Select(a => a.Address)
            .Where(a => a.AddressFamily == AddressFamily.InterNetwork)
            .ToList();
        return list.Count > 0 ? list : [IPAddress.Loopback];
    }

    // ---------------- OBD-logg ----------------

    public ObservableCollection<string> Log { get; } = new();

    private void AddLog(ObdTrafficEntry e)
    {
        var arrow = e.FromApp ? "→" : "←";
        Log.Add($"{e.Time:HH:mm:ss.fff}  {arrow}  {e.Text}");
        while (Log.Count > MaxLogLines) Log.RemoveAt(0);
        RecordTestTraffic(e);
        // Hela trafiken sparas även i en fil, så att man kan felsöka en apps uppstart i efterhand.
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ObdLogFile)!);
            File.AppendAllText(ObdLogFile, $"{e.Time:yyyy-MM-dd HH:mm:ss.fff}  [{e.Client}]  {arrow}  {e.Text}{Environment.NewLine}");
        }
        catch { /* loggfilen är inte kritisk */ }
    }

    /// <summary>OBD-trafiken som textfil: %LocalAppData%\Cyberdrome\obd-logg.txt</summary>
    public static string ObdLogFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create), "Cyberdrome", "obd-logg.txt");

    [RelayCommand]
    private void ClearLog() => Log.Clear();

    // ---------------- Android ----------------

    private readonly VmSettings _vmSettings = VmSettings.Load();
    private IAndroidBackend _android;

    public ObservableCollection<string> AndroidLog { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AndroidRunning), nameof(AndroidReady), nameof(AndroidCanInstall), nameof(AndroidStatusShort))]
    private AndroidState _androidState = AndroidState.DistroMissing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AndroidCanInstall), nameof(AndroidReady))]
    private bool _androidBusy;

    [ObservableProperty]
    private string _androidStatus = L.T("Main_CheckingAndroid");

    public bool AndroidRunning => AndroidState == AndroidState.Running;
    public bool AndroidReady => !AndroidBusy && AndroidState is AndroidState.Stopped or AndroidState.Running;
    public bool AndroidCanInstall => !AndroidBusy && AndroidState is not (AndroidState.Stopped or AndroidState.Running or AndroidState.NotWindows);
    public string AndroidStatusShort => AndroidState switch
    {
        AndroidState.Running => L.T("Main_AndroidRunning"),
        AndroidState.Stopped => L.T("Main_AndroidStopped"),
        _ => L.T("Main_AndroidNotInstalled"),
    };

    private void AndroidLogLine(string line) => Dispatcher.UIThread.Post(() =>
    {
        AndroidLog.Add($"{DateTime.Now:HH:mm:ss}  {line}");
        while (AndroidLog.Count > MaxLogLines) AndroidLog.RemoveAt(0);
    });

    private async Task RunAndroid(Func<Task> work)
    {
        if (AndroidBusy)
        {
            // Inget tyst "ingenting händer": säg att något redan pågår och visa var.
            AndroidLogLine(L.T("Main_AndroidStepAlreadyRunning"));
            AndroidStatus = L.T("Main_AndroidStepAlreadyRunning");
            PageIndex = 3;
            return;
        }
        AndroidBusy = true;
        try { await work(); }
        catch (Exception ex)
        {
            AndroidLogLine(L.T("Main_ErrorPrefix") + ex);
            InstallProgressVisible = true;
            InstallProgressIndeterminate = false;
            InstallProgressText = L.F("Main_AndroidError0", ex.Message);
            AndroidStatus = InstallProgressText;
        }
        finally
        {
            AndroidBusy = false;
            await RefreshAndroid();
        }
    }

    [RelayCommand]
    private async Task RefreshAndroid()
    {
        try { AndroidState = await _android.GetStateAsync(); }
        catch (Exception ex) { AndroidLogLine(L.T("Main_CouldNotReadTheAndroidMode") + ex.Message); }
        AndroidStatus = _android.Describe(AndroidState);
    }

    private bool _watching;

    /// <summary>
    /// Bakgrundskontroll (emulatorlägena): märker när Android startas eller stängs utanför programmet. Ändrar bara
    /// statustexten när läget faktiskt ändras, så att felmeddelanden inte skrivs över. WSL2 kontrolleras inte här
    /// (wsl.exe var 5:e sekund skulle hålla Linux-miljön vaken).
    /// </summary>
    private async Task WatchAndroidAsync()
    {
        if (_watching || AndroidBusy || _android is not EmulatorVm) return;
        _watching = true;
        try
        {
            var s = await _android.GetStateAsync();
            if (s != AndroidState && !AndroidBusy) { AndroidState = s; AndroidStatus = _android.Describe(s); }
        }
        catch { /* nästa gång */ }
        finally { _watching = false; }
    }

    // ----- Förloppsstapel för installationen -----

    /// <summary>Syns medan installationen pågår och ligger kvar som "Klart" efteråt.</summary>
    [ObservableProperty]
    private bool _installProgressVisible;

    /// <summary>0–100.</summary>
    [ObservableProperty]
    private double _installProgressValue;

    /// <summary>True när vi inte vet hur långt det kommit (rullande stapel).</summary>
    [ObservableProperty]
    private bool _installProgressIndeterminate;

    [ObservableProperty]
    private string _installProgressText = "";

    [ObservableProperty]
    private bool _installDone;

    public string InstallProgressPercentText => InstallProgressIndeterminate ? "" : $"{InstallProgressValue:0} %";

    partial void OnInstallProgressValueChanged(double value) => OnPropertyChanged(nameof(InstallProgressPercentText));
    partial void OnInstallProgressIndeterminateChanged(bool value) => OnPropertyChanged(nameof(InstallProgressPercentText));

    private void ApplyProgress(InstallProgress p)
    {
        InstallProgressVisible = true;
        InstallProgressIndeterminate = p.Percent is null;
        if (p.Percent is { } pct) InstallProgressValue = pct;
        InstallProgressText = p.Text;
        InstallDone = p.Done;
    }

    /// <summary>Anropas när programmet startats automatiskt efter omstarten (se AutoResume).</summary>
    public async Task ContinueInstallAfterReboot()
    {
        AndroidLogLine(L.T("Main_TheComputerHasRestartedContinuingThe"));
        PageIndex = 3;
        await RefreshAndroid();
        if (AndroidState == AndroidState.WslMissing)
        {
            // Undvik en slinga av administratörsfrågor och omstarter om WSL2 ändå inte fungerar.
            AndroidLogLine(L.T("Main_WSL2StillDoesnTWorkAfter"));
            return;
        }
        await InstallAndroid();
    }

    [RelayCommand]
    private Task InstallAndroid() => RunAndroid(async () =>
    {
        PageIndex = 3;
        AndroidStatus = L.T("Main_InstallingThisMayTakeAWhile");
        InstallDone = false;
        ApplyProgress(new InstallProgress(0, L.T("Main_PreparingTheInstallation")));
        // Progress<T> skickar uppdateringarna till UI-tråden åt oss.
        var progress = new Progress<InstallProgress>(ApplyProgress);
        try
        {
            var state = await _android.InstallAsync(AndroidLogLine, progress, AskConfirmAsync);
            if (state == AndroidState.RebootNeeded)
            {
                // Låt programmet starta av sig självt efter omstarten och fortsätta installationen.
                if (AutoResume.Register(AndroidLogLine))
                    AndroidLogLine(L.T("Main_AfterTheRestartCyberdromeOpensAutomatically"));
            }
            else if (state == AndroidState.Stopped)
            {
                // Installationen är klar: starta Android direkt, utan extra knapptryck eller omstart av programmet.
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                ApplyProgress(new InstallProgress(100, L.T("Main_DoneAndroidIsInstalledStartingAndroid"), true));
                AndroidStatus = L.T("Main_StartingAndroid");
                var r = await _android.StartAsync(AndroidLogLine, new Progress<InstallProgress>(p =>
                    ApplyProgress(p with { Percent = 100, Done = true, Text = L.T("Main_DonePrefix") + p.Text })));
                if (r.Ok)
                {
                    ApplyProgress(new InstallProgress(100, L.T("Main_DoneAndroidIsInstalledAndRunning"), true));
                    OpenAndroidScreen();
                }
                else
                {
                    ApplyProgress(new InstallProgress(100, L.T("Main_AndroidIsInstalledButCouldNot"), true));
                }
            }
            else
            {
                // Installationen stannade (t.ex. KVM saknas): visa orsaken och nästa steg i stället för att tiga.
                AndroidStatus = _android.Describe(state);
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                var failHint = L.F("AndroidVm_StepFailedSeeLog", "").Trim();
                if (state != AndroidState.RebootNeeded && !InstallProgressText.EndsWith(failHint))
                    ApplyProgress(new InstallProgress(0, L.F("AndroidVm_InstallStopped", _android.Describe(state))));
            }
        }
        finally
        {
            // Progress<T> postar asynkront; vänta in sista uppdateringen innan vi kollar resultatet.
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            if (!InstallDone)
            {
                InstallProgressIndeterminate = false;
                InstallProgressText = AndroidState == AndroidState.RebootNeeded || InstallProgressText.Contains(L.T("Main_Restart"))
                    ? InstallProgressText
                    : L.T("Main_TheInstallationWasCancelledOrFailed");
            }
        }
    });

    [RelayCommand]
    private Task StartAndroid() => RunAndroid(async () =>
    {
        AndroidStatus = L.T("Main_StartingAndroid");
        InstallDone = false;
        AndroidStartFailed = false;
        AndroidPhase = AndroidPhase.Starting;
        try
        {
            var r = await _android.StartAsync(AndroidLogLine, new Progress<InstallProgress>(ApplyProgress));
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            if (r.Ok) { OpenAndroidScreen(); InstallProgressVisible = false; }
            else
            {
                // Visa orsaken (raden "FEL: …" från startskriptet) i stället för bara "se loggen".
                var why = AndroidVm.StartFailureReason(r.Output);
                AndroidStartFailed = true;
                ApplyProgress(new InstallProgress(0, why is null ? L.T("Main_AndroidCouldNotBeStartedSee") : L.F("Main_AndroidCouldNotStart0", why)));
                AndroidStatus = InstallProgressText;
            }
        }
        finally { AndroidPhase = AndroidPhase.None; }
    });

    [RelayCommand]
    private Task StopAndroid() => RunAndroid(async () =>
    {
        AndroidStatus = L.T("Main_ShuttingDownAndroid");
        AndroidPhase = AndroidPhase.Stopping;
        try { await _android.StopAsync(AndroidLogLine); }
        finally { AndroidPhase = AndroidPhase.None; }
    });

    /// <summary>
    /// Visar Android-skärmen: i emulatorläget inne i programmet (Översikt), annars i webbläsaren (Cuttlefish).
    /// </summary>
    [RelayCommand]
    private void OpenAndroidScreen()
    {
        if (_android is IScreenControl) PageIndex = 0;
        else _android.OpenScreen(AndroidLogLine);
    }

    // ---------------- Android-skärmen i programmet ----------------

    private CancellationTokenSource? _screenCts;

    /// <summary>Senaste bilden av Android-skärmen.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasScreenFrame), nameof(ShowScreenPlaceholder))]
    private Avalonia.Media.Imaging.Bitmap? _screenFrame;

    public bool HasScreenFrame => ScreenFrame is not null;
    public bool ShowScreenPlaceholder => ScreenFrame is null && !HasWebScreen;

    /// <summary>True när skärmen kan visas och styras inne i programmet (emulatorläget, Android igång).</summary>
    public bool CanControlScreen => _android is IScreenControl && AndroidRunning;

    partial void OnAndroidStateChanged(AndroidState oldValue, AndroidState newValue)
    {
        OnPropertyChanged(nameof(CanControlScreen));
        _ = UpdateWebScreenAsync();
        UpdateScreenLoop();
        // När Android just har startat: sätt vald skärmstorlek.
        if (newValue == AndroidState.Running && oldValue != AndroidState.Running) _ = ApplyCarLookAsync();
    }

    /// <summary>Startar eller stoppar skärmströmmen beroende på läge och sida.</summary>
    private void UpdateScreenLoop()
    {
        bool want = CanControlScreen && IsOverview && !HasWebScreen; // WebRTC-strömmen ersätter skärmdumparna
        if (want && _screenCts is null)
        {
            _screenCts = new CancellationTokenSource();
            _ = ScreenLoopAsync((IScreenControl)_android, _screenCts.Token);
        }
        else if (!want && _screenCts is not null)
        {
            _screenCts.Cancel();
            _screenCts = null;
        }
        if (!CanControlScreen) SetFrame(null);
    }

    private void SetFrame(Avalonia.Media.Imaging.Bitmap? frame)
    {
        var old = ScreenFrame;
        ScreenFrame = frame;
        old?.Dispose();
    }

    /// <summary>Hämtar skärmbilder i en slinga (ca 4–6 bilder/s) och visar dem.</summary>
    private async Task ScreenLoopAsync(IScreenControl screen, CancellationToken ct)
    {
        // Bäst: en kontinuerlig bildström (emulatorns gRPC, ~60 bilder/s). Annars skärmdumpar som förut.
        if (screen is IScreenStream stream)
        {
            while (!ct.IsCancellationRequested)
            {
                LiveScreen = true;
                bool ok = await stream.StreamScreenAsync(OnRawFrame, ct);
                LiveScreen = false;
                if (!ok || ct.IsCancellationRequested) break;
                try { await Task.Delay(1000, ct); } catch { return; } // strömmen bröts (Android startades om) – försök igen
            }
            if (ct.IsCancellationRequested) return;
        }
        int failures = 0;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var png = await screen.CaptureScreenAsync(ct);
                if (png is not null)
                {
                    failures = 0;
                    var bmp = new Avalonia.Media.Imaging.Bitmap(new MemoryStream(png));
                    if (ct.IsCancellationRequested) { bmp.Dispose(); break; }
                    SetFrame(bmp);
                }
                else if (++failures == 5)
                {
                    _ = RefreshAndroid(); // Android kan ha stängts
                }
                // Sikta på cirka 15 bilder/s: vänta bara det som är kvar av 66 ms efter att bilden hämtats.
                await Task.Delay(png is null ? 1000 : ScreenPacing.DelayAfter(sw.ElapsedMilliseconds), ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                AndroidLogLine(L.T("Main_Screen") + ex.Message);
                try { await Task.Delay(1000, ct); } catch { break; }
            }
        }
    }

    /// <summary>Klick eller svep på skärmen i programmet, i Android-skärmens egna pixlar.</summary>
    public async Task ScreenPointerAsync(int x1, int y1, int x2, int y2, TimeSpan held)
    {
        if (_android is not IScreenControl s || !AndroidRunning) return;
        bool moved = Math.Abs(x2 - x1) + Math.Abs(y2 - y1) > 12;
        if (moved)
            await s.SwipeAsync(x1, y1, x2, y2, (int)Math.Clamp(held.TotalMilliseconds, 100, 1500));
        else if (held.TotalMilliseconds > 600)
            await s.SwipeAsync(x1, y1, x1, y1, (int)held.TotalMilliseconds); // långt tryck
        else
            await s.TapAsync(x1, y1);
    }

    [RelayCommand]
    private async Task SteeringKey(SteeringButton button)
    {
        if (_android is not IScreenControl s || !AndroidRunning) return;
        await s.KeyEventAsync(SteeringKeys.KeyCode(button));
        AndroidLogLine($"Rattknapp: {SteeringKeys.Label(button)}");
    }

    [RelayCommand]
    private async Task RestartForegroundApp()
    {
        if (_android is not IScreenControl s || !AndroidRunning) return;
        await s.RestartForegroundAppAsync(AndroidLogLine);
    }

    // ----- OBD-testapp (AndrOBD från F-Droid) -----

    private static readonly HttpClient Http = CreateHttp();

    private static HttpClient CreateHttp()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("CyberdromeCockpit/0.4");
        return h;
    }

    /// <summary>Hjälptext som visas efter att testappen installerats.</summary>
    [ObservableProperty]
    private bool _testAppHelpVisible;

    /// <summary>Hjälptexten för den senast installerade F-Droid-appen.</summary>
    [ObservableProperty]
    private string _testAppHelp = AndrObd.HelpText;

    [RelayCommand]
    private Task InstallTestApp() => InstallFdroidAppAsync(AndrObd.App);

    [RelayCommand]
    private Task InstallMapApp() => InstallFdroidAppAsync(OrganicMaps.App);

    /// <summary>Laddar ner en fri app från F-Droid (vid användarens tryck), installerar och startar den.</summary>
    private Task InstallFdroidAppAsync(FdroidApp app) => RunAndroid(async () =>
    {
        if (_android is not IScreenControl screen)
        {
            AndroidLogLine(L.F("Main_0CanBeInstalledInThe", app.DisplayName));
            return;
        }
        if (!AndroidRunning)
        {
            AndroidLogLine(L.T("Main_StartAndroidFirst"));
            return;
        }
        InstallDone = false;
        ApplyProgress(new InstallProgress(0, L.F("Main_Downloading0FromFDroid", app.DisplayName)));
        try
        {
            var path = await app.DownloadAsync(Http, AndroidLogLine, pct =>
                Dispatcher.UIThread.Post(() => ApplyProgress(new InstallProgress(pct * 0.8, L.F("Main_Downloading0FromFDroid1", app.DisplayName, pct)))));
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            ApplyProgress(new InstallProgress(85, L.F("Main_Installing0InAndroid", app.DisplayName)));
            var r = await _android.InstallApkAsync(path, AndroidLogLine);
            if (!r.Ok)
            {
                ApplyProgress(new InstallProgress(85, L.F("Main_0CouldNotBeInstalledSee", app.DisplayName)));
                return;
            }
            await screen.LaunchAppAsync(app.PackageName);
            ApplyProgress(new InstallProgress(100, L.F("Main_Done0IsInstalledAndStarted", app.DisplayName), true));
            AndroidLogLine(L.F("Main_0IsInstalled", app.DisplayName) + app.HelpText);
            TestAppHelp = app.HelpText;
            TestAppHelpVisible = true;
        }
        catch (Exception ex)
        {
            ApplyProgress(new InstallProgress(0, L.F("Main_CouldNotGet0", app.DisplayName) + ex.Message));
            AndroidLogLine(L.F("Main_CouldNotGet0", app.DisplayName) + ex.Message);
        }
    });

    // ----- Vår exempelapp: Cyberdrome Radio -----

    [RelayCommand]
    private Task InstallRadioApp() => RunAndroid(async () =>
    {
        if (_android is not IScreenControl screen || !AndroidRunning)
        {
            AndroidLogLine(L.T("Main_StartAndroidNoRestartModeFirst"));
            return;
        }
        var project = CyberdromeRadio.FindProjectDir(AppContext.BaseDirectory);
        if (project is null)
        {
            // Installerad version: färdigbyggd APK i programmappen (apps\).
            var bundled = SampleApp.Bundled(CyberdromeRadio.ApkName);
            if (bundled is null) { AndroidLogLine(L.F("Main_CyberdromeRadioNotFoundApps0", CyberdromeRadio.ApkName)); return; }
            InstallDone = false;
            ApplyProgress(new InstallProgress(70, L.T("Main_InstallingCyberdromeRadio")));
            var br = await _android.InstallApkAsync(bundled, AndroidLogLine);
            if (!br.Ok) { ApplyProgress(new InstallProgress(70, L.T("Main_CyberdromeRadioCouldNotBeInstalled"))); return; }
            await screen.LaunchAppAsync(CyberdromeRadio.PackageName);
            ApplyProgress(new InstallProgress(100, L.T("Main_DoneCyberdromeRadioIsInstalledAnd"), true));
            return;
        }
        InstallDone = false;
        var apk = CyberdromeRadio.ApkPath(project);
        // Bygg om ifall APK saknas eller källkoden är nyare.
        var newest = Directory.GetFiles(project, "*.*", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Select(File.GetLastWriteTimeUtc).DefaultIfEmpty(DateTime.MinValue).Max();
        if (!File.Exists(apk) || File.GetLastWriteTimeUtc(apk) < newest)
        {
            ApplyProgress(new InstallProgress(null, L.T("Main_BuildingCyberdromeRadio")));
            var built = await CyberdromeRadio.BuildAsync(project, _vmSettings.SdkRoot, AndroidLogLine);
            if (built is null)
            {
                ApplyProgress(new InstallProgress(0, L.T("Main_BuildingCyberdromeRadioFailedSeeThe")));
                return;
            }
        }
        ApplyProgress(new InstallProgress(70, L.T("Main_InstallingCyberdromeRadio")));
        var r = await _android.InstallApkAsync(apk, AndroidLogLine);
        if (!r.Ok)
        {
            ApplyProgress(new InstallProgress(70, L.T("Main_CyberdromeRadioCouldNotBeInstalled")));
            return;
        }
        await screen.LaunchAppAsync(CyberdromeRadio.PackageName);
        ApplyProgress(new InstallProgress(100, L.T("Main_DoneCyberdromeRadioIsInstalledAnd"), true));
    });

    [ObservableProperty]
    private string _screenshotStatus = "";

    [RelayCommand]
    private async Task TakeScreenshot()
    {
        if (_android is not IScreenControl s || !AndroidRunning) return;
        var png = await s.CaptureScreenAsync();
        if (png is null) { ScreenshotStatus = L.T("Main_CouldNotTakeAScreenshot"); return; }
        try
        {
            Directory.CreateDirectory(_vmSettings.ScreenshotFolder);
            var path = ScreenshotNaming.UniquePath(_vmSettings.ScreenshotFolder, DateTime.Now);
            await File.WriteAllBytesAsync(path, png);
            ScreenshotStatus = "Sparad: " + path;
            AndroidLogLine(L.T("Main_ScreenshotSaved") + path);
        }
        catch (Exception ex) { ScreenshotStatus = L.T("Main_CouldNotSave") + ex.Message; }
    }

    // ----- Bekräftelse i programmet (ingen blockerande Windows-dialog) -----

    private TaskCompletionSource<bool>? _confirm;

    [ObservableProperty]
    private bool _confirmVisible;

    [ObservableProperty]
    private string _confirmText = "";

    private Task<bool> AskConfirmAsync(string text)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(() =>
        {
            _confirm?.TrySetResult(false);
            _confirm = tcs;
            ConfirmText = text;
            ConfirmVisible = true;
            PageIndex = 3;
        });
        return tcs.Task;
    }

    private void AnswerConfirm(bool ok)
    {
        ConfirmVisible = false;
        _confirm?.TrySetResult(ok);
        _confirm = null;
    }

    [RelayCommand]
    private void ConfirmContinue() => AnswerConfirm(true);

    [RelayCommand]
    private void ConfirmCancel() => AnswerConfirm(false);

    // ----- Inställningar: Android-läge -----

    public string AndroidModeText => L.T("Main_AndroidMode") + AndroidBackends.ModeTitle(_vmSettings.Mode);

    public bool ModeFast
    {
        get => _vmSettings.Mode == AndroidMode.Fast;
        set { if (value) SetMode(AndroidMode.Fast); }
    }

    public bool ModeNoRestart
    {
        get => _vmSettings.Mode == AndroidMode.NoRestart;
        set { if (value) SetMode(AndroidMode.NoRestart); }
    }

    public bool ModeGoogle
    {
        get => _vmSettings.Mode == AndroidMode.GoogleApps;
        set { if (value) SetMode(AndroidMode.GoogleApps); }
    }

    /// <summary>Byte av läge går inte medan något installeras eller startas.</summary>
    public bool CanChangeMode => !AndroidBusy;

    partial void OnAndroidBusyChanged(bool value) { OnPropertyChanged(nameof(CanChangeMode)); RaiseAndroidSwitch(); }

    private void SetMode(AndroidMode mode)
    {
        if (_vmSettings.Mode == mode || AndroidBusy) return;
        _vmSettings.Mode = mode;
        _vmSettings.Save();
        _android = AndroidBackends.Create(_vmSettings);
        _screenCts?.Cancel();
        _screenCts = null;
        OnPropertyChanged(nameof(CanControlScreen));
        UpdateScreenLoop();
        AndroidLogLine(L.T("Main_AndroidModeChangedTo") + AndroidBackends.ModeTitle(mode));
        InstallProgressVisible = false;
        OnPropertyChanged(nameof(ModeFast));
        OnPropertyChanged(nameof(ModeNoRestart));
        OnPropertyChanged(nameof(ModeGoogle));
        RaiseModeStatus();
        OnPropertyChanged(nameof(AndroidModeText));
        OnPropertyChanged(nameof(AndroidObdAddress));
        OnPropertyChanged(nameof(HasAndroidObdAddress));
        _ = RefreshAndroid();
    }

    /// <summary>OBD-adressen sett inifrån Android (t.ex. 10.0.2.2:35000 i emulatorn).</summary>
    public string? AndroidObdAddress => _android.ObdHostFromAndroid is { } h ? $"{h}:{ObdTcpServer.DefaultPort}" : null;
    public bool HasAndroidObdAddress => AndroidObdAddress is not null;

    /// <summary>Anropas från fönstret när användaren valt en APK-fil.</summary>
    public Task InstallApk(string path) => RunAndroid(async () =>
    {
        AndroidStatus = L.T("Main_InstallingApp");
        var r = await _android.InstallApkAsync(path, AndroidLogLine);
        AndroidLogLine(r.Ok ? L.T("Main_TheAppIsInstalled") : L.T("Main_TheAppCouldNotBeInstalled"));
    });

    [RelayCommand]
    private void ClearAndroidLog() => AndroidLog.Clear();

    public void Shutdown()
    {
        _screenCts?.Cancel();
        _timer.Stop();
        // Stoppa servrarna på trådpoolen med tidsgräns, så att programmet aldrig hänger sig när det stängs
        // (att vänta på async-kod direkt på UI-tråden kan låsa sig).
        var stop = Task.Run(async () =>
        {
#if WINDOWS
            try { if (_bt is not null) await _bt.StopAsync().ConfigureAwait(false); } catch { }
            try { if (_ble is not null) await _ble.StopAsync().ConfigureAwait(false); } catch { }
#endif
            try { if (_server is not null) await _server.StopAsync().ConfigureAwait(false); } catch { }
        });
        stop.Wait(TimeSpan.FromSeconds(3));
    }
}
