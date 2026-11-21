// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace Cyberdrome.Vm;

/// <summary>
/// Läge "Utan omstart": Googles Android-emulator med Android Automotive, direkt i Windows.
/// Använder Windows Hypervisor Platform (WHPX) som redan finns, så ingen omstart behövs.
/// OBS: Googles emulatoravbilder får inte följa med i den sålda produkten – bara för utveckling.
/// </summary>
public sealed partial class EmulatorVm : IAndroidBackend, IScreenControl
{
    private bool _accelOk;
    private string? _serial;     // t.ex. emulator-5554, sparas mellan anropen
    private string? _displayId;  // huvudskärmens id (emulatorn har flera skärmar)
    private bool _locationEnabled;

    public EmulatorVm(VmSettings settings, bool googleApps = false)
    {
        Settings = settings;
        GoogleApps = googleApps;
    }

    /// <summary>Avbild med Googles appar (Play Store, Google Maps) – bara för utveckling.</summary>
    public bool GoogleApps { get; }

    public string SystemImage => GoogleApps ? Settings.GoogleSystemImage : Settings.SystemImage;
    public string AvdName => GoogleApps ? Settings.GoogleAvdName : Settings.AvdName;
    public string DeviceProfile => GoogleApps ? Settings.GoogleDeviceProfile : Settings.DeviceProfile;
    public string SystemImageDir => Path.Combine([Settings.SdkRoot, .. SystemImage.Split(';')]);

    public VmSettings Settings { get; }
    public AndroidMode Mode => GoogleApps ? AndroidMode.GoogleApps : AndroidMode.NoRestart;

    /// <summary>Inifrån emulatorn når man datorn på 10.0.2.2.</summary>
    public string? ObdHostFromAndroid => "10.0.2.2";

    private Dictionary<string, string> JavaEnv()
    {
        var env = new Dictionary<string, string> { ["ANDROID_SDK_ROOT"] = Settings.SdkRoot, ["ANDROID_HOME"] = Settings.SdkRoot };
        if (Settings.JavaHome is { } j) env["JAVA_HOME"] = j;
        return env;
    }

    private bool ImageInstalled => File.Exists(Path.Combine(SystemImageDir, "system.img"))
                                   || File.Exists(Path.Combine(SystemImageDir, "system.img.qcow2"));

    private bool AvdExists => File.Exists(Path.Combine(VmSettings.AvdHome, AvdName + ".ini"));

    public async Task<AndroidState> GetStateAsync(CancellationToken ct = default)
    {
        if (!OperatingSystem.IsWindows()) return AndroidState.NotWindows;
        if (!File.Exists(Settings.SdkManager) || Settings.JavaHome is null) return AndroidState.SdkMissing;
        if (!File.Exists(Settings.EmulatorExe) || !File.Exists(Settings.AdbExe)) return AndroidState.ToolsMissing;
        if (!_accelOk)
        {
            var a = await ProcessRunner.RunAsync(Settings.EmulatorExe, ["-accel-check"], ct: ct);
            if (!a.Ok) return AndroidState.NoAcceleration;
            _accelOk = true;
        }
        if (!ImageInstalled) return AndroidState.ImageMissing;
        if (!AvdExists) return AndroidState.AvdMissing;
        return await FindSerialAsync(ct) is not null ? AndroidState.Running : AndroidState.Stopped;
    }

    // Förloppsstapelns delar (0–100).
    private const double PctToolsEnd = 10, PctImageEnd = 90, PctAvdEnd = 100;

    public async Task<AndroidState> InstallAsync(Action<string> log, IProgress<InstallProgress>? progress = null,
        Func<string, Task<bool>>? confirm = null, CancellationToken ct = default)
    {
        void Report(double? pct, string text, bool done = false) => progress?.Report(new InstallProgress(pct, text, done));

        Report(null, L.T("AndroidVm_CheckingWhatNeedsToBeInstalled"));
        var state = await GetStateAsync(ct);
        log(L.F("AndroidVm_Mode0", state));

        if (state == AndroidState.SdkMissing)
        {
            log(L.F("EmulatorVm_AndroidSDKSdkmanagerAndJavaWas", Settings.SdkRoot, VmSettings.FilePath));
            return state;
        }
        if (state == AndroidState.NoAcceleration)
        {
            log(L.T("EmulatorVm_WindowsHypervisorPlatformCannotBeUsed"));
            return state;
        }

        if (state == AndroidState.ToolsMissing)
        {
            Report(0, L.T("EmulatorVm_Step1Of3DownloadingThe"));
            var r = await SdkManagerAsync(["emulator", "platform-tools"], log, pct =>
                Report(pct / 100 * PctToolsEnd, L.F("EmulatorVm_Step1Of3DownloadingThe2", pct)), ct);
            if (!r.Ok) { log(L.T("EmulatorVm_CouldNotInstallTheEmulator")); return state; }
            state = await GetStateAsync(ct);
        }

        if (state == AndroidState.ImageMissing)
        {
            Report(PctToolsEnd, L.T("EmulatorVm_Step2Of3DownloadingAndroid"));
            log(L.F("EmulatorVm_Downloading0", SystemImage));
            var r = await SdkManagerAsync([SystemImage], log, pct =>
                Report(PctToolsEnd + pct / 100 * (PctImageEnd - PctToolsEnd),
                    L.F("EmulatorVm_Step2Of3DownloadingAndroid2", pct)), ct);
            if (!r.Ok || !ImageInstalled) { log(L.T("AndroidVm_TheAndroidDownloadFailed")); return AndroidState.ImageMissing; }
            log(L.T("EmulatorVm_AndroidAutomotiveIsDownloaded"));
            state = AndroidState.AvdMissing;
        }

        if (state == AndroidState.AvdMissing)
        {
            Report(PctImageEnd, L.T("EmulatorVm_Step3Of3CreatingThe"));
            log(L.F("EmulatorVm_CreatingTheVirtualCar0", AvdName));
            var r = await ProcessRunner.RunAsync(Settings.AvdManager,
                ["create", "avd", "--force", "-n", AvdName, "-k", SystemImage, "-d", DeviceProfile],
                stdin: "no\n", onLine: log, ct: ct, env: JavaEnv());
            if (!r.Ok || !AvdExists) { log(L.T("EmulatorVm_CouldNotCreateTheVirtualCar")); return AndroidState.AvdMissing; }
            TuneAvd();
            state = AndroidState.Stopped;
        }

        if (state is AndroidState.Stopped or AndroidState.Running)
        {
            Report(PctAvdEnd, L.T("AndroidVm_DoneAndroidIsDownloadedAndInstalled"), done: true);
            log(L.T("AndroidVm_AndroidIsInstalled"));
        }
        return state;
    }

    /// <summary>Ställer in minne och tangentbord i den virtuella bilen.</summary>
    private void TuneAvd()
    {
        try
        {
            var cfg = Path.Combine(VmSettings.AvdHome, AvdName + ".avd", "config.ini");
            if (!File.Exists(cfg)) return;
            var lines = File.ReadAllLines(cfg).ToList();
            void Set(string key, string value)
            {
                int i = lines.FindIndex(l => l.StartsWith(key + "=", StringComparison.Ordinal) || l.StartsWith(key + " =", StringComparison.Ordinal));
                if (i >= 0) lines[i] = $"{key}={value}"; else lines.Add($"{key}={value}");
            }
            Set("hw.ramSize", Settings.EmulatorMemoryMb.ToString());
            Set("hw.cpu.ncore", Settings.Cpus.ToString());
            Set("hw.keyboard", "yes");
            File.WriteAllLines(cfg, lines);
        }
        catch { /* standardvärden duger */ }
    }

    /// <summary>Kör sdkmanager och läser procent ur dess utskrift.</summary>
    private async Task<ProcessResult> SdkManagerAsync(string[] packages, Action<string> log, Action<double> onPercent, CancellationToken ct)
    {
        double last = -1;
        var args = new List<string> { $"--sdk_root={Settings.SdkRoot}" };
        args.AddRange(packages);
        // Licenserna för SDK-paketen godkänns med "y" (samma som i Android Studio).
        return await ProcessRunner.RunAsync(Settings.SdkManager, args, stdin: string.Concat(Enumerable.Repeat("y\n", 20)),
            onLine: line =>
            {
                if (ParsePercent(line) is { } p)
                {
                    if (Math.Abs(p - last) >= 1) { last = p; onPercent(p); }
                    return; // procentrader skräpar annars ner loggen
                }
                log(line);
            }, ct: ct, env: JavaEnv());
    }

    /// <summary>Hittar procent i en sdkmanager-rad, t.ex. "[=====    ] 45% Downloading x.zip".</summary>
    public static double? ParsePercent(string line)
    {
        var m = PercentRegex().Match(line);
        return m.Success && double.TryParse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture, out var p)
            ? Math.Clamp(p, 0, 100) : null;
    }

    [GeneratedRegex(@"\]\s*(\d{1,3})%")]
    private static partial Regex PercentRegex();

    // ---------------- Köra ----------------

    /// <summary>Serienumret (t.ex. emulator-5554) för vår körande bil, eller null.</summary>
    private async Task<string?> FindSerialAsync(CancellationToken ct)
    {
        if (!File.Exists(Settings.AdbExe)) return null;
        var d = await ProcessRunner.RunAsync(Settings.AdbExe, ["devices"], ct: ct);
        foreach (var line in d.Output.Split('\n'))
        {
            var parts = line.Trim().Split('\t');
            if (parts.Length < 2 || !parts[0].StartsWith("emulator-", StringComparison.Ordinal)) continue;
            var name = await ProcessRunner.RunAsync(Settings.AdbExe, ["-s", parts[0], "emu", "avd", "name"], ct: ct);
            if (name.Output.Split('\n').Any(l => l.Trim() == AvdName)) return parts[0];
        }
        return null;
    }

    public async Task<ProcessResult> StartAsync(Action<string> log, IProgress<InstallProgress>? progress = null, CancellationToken ct = default)
    {
        void Report(double? pct, string text, bool done = false) => progress?.Report(new InstallProgress(pct, text, done));

        if (await FindSerialAsync(ct) is not null)
        {
            log(L.T("EmulatorVm_AndroidIsAlreadyRunning"));
            OpenScreen(log);
            return new ProcessResult(0, "");
        }

        log(L.T("EmulatorVm_StartingTheAndroidEmulator"));
        Report(null, L.T("Main_StartingAndroid"));
        _serial = null;
        // Arbetsmapp utanför programmappen: emulatorn lever kvar efter programmet och får inte låsa mappen (uppdatering).
        var psi = new ProcessStartInfo(Settings.EmulatorExe) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = ProcessRunner.NeutralDir(Settings.EmulatorExe) };
        foreach (var a in new[] { "-avd", AvdName, "-gpu", "auto", "-memory", Settings.EmulatorMemoryMb.ToString(),
                     "-cores", Settings.Cpus.ToString(), "-no-boot-anim", "-netdelay", "none", "-netspeed", "full" })
            psi.ArgumentList.Add(a);
        // Skärmen visas inne i Cyberdrome, så emulatorns eget fönster behövs inte.
        if (!Settings.ShowEmulatorWindow) psi.ArgumentList.Add("-no-window");
        psi.Environment["ANDROID_SDK_ROOT"] = Settings.SdkRoot;
        Process? proc;
        try { proc = Process.Start(psi); }
        catch (Exception ex) { log(L.T("EmulatorVm_CouldNotStartTheEmulator") + ex.Message); return new ProcessResult(-1, ex.Message); }

        // Vänta tills Android har startat klart.
        var started = DateTime.UtcNow;
        string? serial = null;
        while ((DateTime.UtcNow - started).TotalSeconds < Settings.BootTimeoutSeconds)
        {
            ct.ThrowIfCancellationRequested();
            if (proc is { HasExited: true })
            {
                log(L.F("EmulatorVm_TheEmulatorClosedImmediatelyCode0", proc.ExitCode));
                return new ProcessResult(proc.ExitCode == 0 ? -1 : proc.ExitCode, "");
            }
            serial ??= await FindSerialAsync(ct);
            if (serial is not null)
            {
                var b = await ProcessRunner.RunAsync(Settings.AdbExe, ["-s", serial, "shell", "getprop", "sys.boot_completed"], ct: ct);
                if (b.Output.Trim() == "1")
                {
                    log(L.T("AndroidVm_AndroidIsRunning"));
                    Report(100, L.T("AndroidVm_AndroidIsRunning"), done: true);
                    OpenScreen(log);
                    return new ProcessResult(0, "");
                }
            }
            int secs = (int)(DateTime.UtcNow - started).TotalSeconds;
            Report(null, L.F("EmulatorVm_StartingAndroid0STheFirst", secs));
            await Task.Delay(2000, ct);
        }
        log(L.T("EmulatorVm_AndroidDidNotStartWithinThe"));
        return new ProcessResult(-1, "timeout");
    }

    public async Task<ProcessResult> StopAsync(Action<string> log, CancellationToken ct = default)
    {
        var serial = await FindSerialAsync(ct);
        if (serial is null) { log(L.T("EmulatorVm_AndroidIsNotRunning")); return new ProcessResult(0, ""); }
        log(L.T("Main_ShuttingDownAndroid"));
        return await ProcessRunner.RunAsync(Settings.AdbExe, ["-s", serial, "emu", "kill"], onLine: log, ct: ct);
    }

    public async Task<ProcessResult> InstallApkAsync(string windowsPath, Action<string> log, CancellationToken ct = default)
    {
        var serial = await FindSerialAsync(ct);
        if (serial is null) { log(L.T("EmulatorVm_AndroidIsNotRunning")); return new ProcessResult(-1, ""); }
        // -r: ersätt om den finns, -g: ge appen alla behörigheter direkt (smidigare vid test).
        return await ProcessRunner.RunAsync(Settings.AdbExe, ["-s", serial, "install", "-r", "-g", windowsPath], onLine: log, ct: ct);
    }

    /// <summary>
    /// Skickar GPS till emulatorn som NMEA-meningar (adb emu geo nmea …). RMC innehåller fart och kurs,
    /// så Androids position får samma fart som OBD.
    /// </summary>
    public async Task<bool> SendNmeaAsync(IEnumerable<string> sentences, CancellationToken ct = default)
    {
        var serial = await SerialAsync(ct);
        if (serial is null) return false;
        if (!_locationEnabled)
        {
            // Platstjänster är avstängda från början i emulatorn: slå på dem (bara i test-Android).
            await ProcessRunner.RunAsync(Settings.AdbExe, ["-s", serial, "shell", "cmd", "location", "set-location-enabled", "true", "--user", "current"], ct: ct);
            _locationEnabled = true;
        }
        bool ok = true;
        foreach (var s in sentences)
        {
            var r = await ProcessRunner.RunAsync(Settings.AdbExe, ["-s", serial, "emu", "geo", "nmea", s], ct: ct);
            ok &= r.Ok;
        }
        return ok;
    }

    /// <summary>
    /// Skickar en GPS-position med fart till emulatorn: adb emu geo fix &lt;lon&gt; &lt;lat&gt; &lt;höjd&gt; &lt;satelliter&gt; &lt;fart i knop&gt;.
    /// (Emulatorns "geo nmea" tas emot men används inte av nyare emulatorer, därför geo fix.)
    /// Android räknar själv ut kursen mellan positionerna; farten blir samma som OBD-farten.
    /// </summary>
    public async Task<bool> SendGpsFixAsync(double lat, double lon, double altitudeM, double speedKmh, CancellationToken ct = default)
    {
        var serial = await SerialAsync(ct);
        if (serial is null) return false;
        if (!_locationEnabled)
        {
            await ProcessRunner.RunAsync(Settings.AdbExe, ["-s", serial, "shell", "cmd", "location", "set-location-enabled", "true", "--user", "current"], ct: ct);
            _locationEnabled = true;
        }
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var r = await ProcessRunner.RunAsync(Settings.AdbExe, ["-s", serial, "emu", "geo", "fix",
            lon.ToString("0.000000", inv), lat.ToString("0.000000", inv), altitudeM.ToString("0.0", inv), "10",
            (speedKmh / 1.852).ToString("0.00", inv)], ct: ct); // km/h → knop
        return r.Ok && r.Output.Contains("OK");
    }

    /// <summary>
    /// Byter skärmstorlek direkt (utan omstart): wm size / wm density, och låser rotationen.
    /// Sparar även storleken i den virtuella bilens config.ini så att nästa kallstart får samma skärm.
    /// </summary>
    public async Task<bool> ApplyScreenAsync(int width, int height, int dpi, Action<string> log, CancellationToken ct = default)
    {
        UpdateAvdScreen(width, height, dpi);
        var serial = await SerialAsync(ct);
        if (serial is null) return false; // gäller vid nästa start
        async Task<bool> Sh(params string[] a) => (await ProcessRunner.RunAsync(Settings.AdbExe, ["-s", serial, "shell", .. a], ct: ct)).Ok;
        bool ok = await Sh("wm", "size", $"{width}x{height}");
        ok &= await Sh("wm", "density", dpi.ToString());
        // Lås rotationen så att Android inte vänder skärmen själv.
        await Sh("settings", "put", "system", "accelerometer_rotation", "0");
        await Sh("settings", "put", "system", "user_rotation", "0");
        log(ok ? L.F("EmulatorVm_Screen012Dpi", width, height, dpi) : L.T("EmulatorVm_CouldNotChangeTheScreenSize"));
        return ok;
    }

    /// <summary>Skriver hw.lcd.* i AVD:ns config.ini (läses vid nästa kallstart).</summary>
    private void UpdateAvdScreen(int width, int height, int dpi)
    {
        try
        {
            var cfg = Path.Combine(VmSettings.AvdHome, AvdName + ".avd", "config.ini");
            if (!File.Exists(cfg)) return;
            var lines = File.ReadAllLines(cfg).ToList();
            void Set(string key, string value)
            {
                int i = lines.FindIndex(l => l.StartsWith(key + "=", StringComparison.Ordinal) || l.StartsWith(key + " =", StringComparison.Ordinal));
                if (i >= 0) lines[i] = $"{key}={value}"; else lines.Add($"{key}={value}");
            }
            Set("hw.lcd.width", width.ToString());
            Set("hw.lcd.height", height.ToString());
            // Emulatorn godtar bara standardtätheter i config.ini; närmaste används (wm density ger exakt värde).
            int[] std = [120, 140, 160, 180, 213, 240, 280, 320, 340, 360, 400, 420, 440, 480, 560, 640];
            Set("hw.lcd.density", std.OrderBy(d => Math.Abs(d - dpi)).First().ToString());
            File.WriteAllLines(cfg, lines);
        }
        catch { /* inte kritiskt – wm size gäller ändå */ }
    }

    /// <summary>Kör ett skalkommando i Android och returnerar resultatet.</summary>
    public async Task<ProcessResult> RunShellAsync(string[] args, CancellationToken ct = default)
    {
        var serial = await SerialAsync(ct);
        if (serial is null) return new ProcessResult(-1, L.T("EmulatorVm_AndroidIsNotRunning"));
        return await ProcessRunner.RunAsync(Settings.AdbExe, ["-s", serial, "shell", .. args], ct: ct);
    }

    public async Task<bool> IsInstalledAsync(string packageName, CancellationToken ct = default)
    {
        var r = await RunShellAsync(["pm", "path", packageName], ct);
        return r.Ok && r.Output.Contains("package:");
    }

    /// <summary>Sätter vilken app som är startskärm (HOME), t.ex. se.cyberdrome.launcher/.HomeActivity.</summary>
    public async Task<bool> SetHomeAsync(string component, CancellationToken ct = default)
    {
        var r = await RunShellAsync(["cmd", "package", "set-home-activity", "--user", "current", component], ct);
        await RunShellAsync(["input", "keyevent", "KEYCODE_HOME"], ct);
        return r.Ok;
    }

    /// <summary>Tillbaka till Android Automotives vanliga startskärm.</summary>
    public Task<bool> ResetHomeAsync(CancellationToken ct = default) =>
        SetHomeAsync("com.android.car.carlauncher/.CarLauncher", ct);

    /// <summary>Startar en installerad app med dess startaktivitet.</summary>
    public async Task<bool> LaunchAppAsync(string packageName, CancellationToken ct = default)
    {
        var serial = await SerialAsync(ct);
        if (serial is null) return false;
        var r = await ProcessRunner.RunAsync(Settings.AdbExe,
            ["-s", serial, "shell", "monkey", "-p", packageName, "-c", "android.intent.category.LAUNCHER", "1"], ct: ct);
        return r.Ok;
    }

    // ---------------- Skärmen i programfönstret ----------------

    private async Task<string?> SerialAsync(CancellationToken ct)
    {
        if (_serial is not null) return _serial;
        _serial = await FindSerialAsync(ct);
        _displayId = null;
        return _serial;
    }

    private async Task<string?> DisplayIdAsync(string serial, CancellationToken ct)
    {
        if (_displayId is not null) return _displayId;
        var r = await ProcessRunner.RunAsync(Settings.AdbExe, ["-s", serial, "shell", "dumpsys", "SurfaceFlinger", "--display-id"], ct: ct);
        var m = Regex.Match(r.Output, @"Display (\d+)");
        _displayId = m.Success ? m.Groups[1].Value : "";
        return _displayId;
    }

    public async Task<byte[]?> CaptureScreenAsync(CancellationToken ct = default)
    {
        var serial = await SerialAsync(ct);
        if (serial is null) return null;
        var display = await DisplayIdAsync(serial, ct);
        var args = new List<string> { "-s", serial, "exec-out", "screencap", "-p" };
        if (!string.IsNullOrEmpty(display)) args.AddRange(["-d", display]);
        var png = await ProcessRunner.RunBinaryAsync(Settings.AdbExe, args, ct);
        if (png is null || png.Length < 8 || png[0] != 0x89 || png[1] != (byte)'P')
        {
            _serial = null; // emulatorn kan ha startats om – leta upp den igen nästa gång
            return null;
        }
        return png;
    }

    private async Task ShellAsync(CancellationToken ct, params string[] cmd)
    {
        var serial = await SerialAsync(ct);
        if (serial is null) return;
        var r = await ProcessRunner.RunAsync(Settings.AdbExe, ["-s", serial, "shell", .. cmd], ct: ct);
        if (!r.Ok) _serial = null;
    }

    public Task TapAsync(int x, int y, CancellationToken ct = default) =>
        ShellAsync(ct, "input", "tap", x.ToString(), y.ToString());

    public Task SwipeAsync(int x1, int y1, int x2, int y2, int durationMs, CancellationToken ct = default) =>
        ShellAsync(ct, "input", "swipe", x1.ToString(), y1.ToString(), x2.ToString(), y2.ToString(), durationMs.ToString());

    public Task KeyEventAsync(int keyCode, CancellationToken ct = default) =>
        ShellAsync(ct, "input", "keyevent", keyCode.ToString());

    public async Task<string?> RestartForegroundAppAsync(Action<string> log, CancellationToken ct = default)
    {
        var serial = await SerialAsync(ct);
        if (serial is null) { log(L.T("EmulatorVm_AndroidIsNotRunning")); return null; }
        var dump = await ProcessRunner.RunAsync(Settings.AdbExe, ["-s", serial, "shell", "dumpsys", "activity", "activities"], ct: ct);
        var component = ActivityParser.TopComponent(dump.Output);
        if (component is null) { log(L.T("EmulatorVm_NoAppFoundInTheForeground")); return null; }
        var pkg = component.Split('/')[0];
        log(L.F("EmulatorVm_Restarting0", pkg));
        await ProcessRunner.RunAsync(Settings.AdbExe, ["-s", serial, "shell", "am", "force-stop", pkg], ct: ct);
        var r = await ProcessRunner.RunAsync(Settings.AdbExe, ["-s", serial, "shell", "am", "start", "-n", component], ct: ct);
        log(r.Ok ? L.F("EmulatorVm_0HasBeenRestarted", pkg) : L.F("EmulatorVm_CouldNotStart0Again", pkg));
        return pkg;
    }

    /// <summary>Emulatorn har ett eget fönster: lägg det överst.</summary>
    public void OpenScreen(Action<string> log)
    {
        if (!OperatingSystem.IsWindows()) return;
        foreach (var p in Process.GetProcesses().Where(p => p.ProcessName.StartsWith("qemu-system", StringComparison.OrdinalIgnoreCase)))
        {
            var h = p.MainWindowHandle;
            if (h == IntPtr.Zero) continue;
            ShowWindow(h, 9); // SW_RESTORE
            SetForegroundWindow(h);
            return;
        }
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    public string Describe(AndroidState s) => s switch
    {
        AndroidState.NotWindows => L.T("AndroidVm_TheAndroidPartOnlyWorksOn"),
        AndroidState.SdkMissing => L.F("EmulatorVm_AndroidSDKWasNotFoundIn", Settings.SdkRoot),
        AndroidState.ToolsMissing => L.T("EmulatorVm_TheEmulatorIsNotInstalledPress"),
        AndroidState.NoAcceleration => L.T("EmulatorVm_WindowsHypervisorPlatformCannotBeUsed2"),
        AndroidState.ImageMissing => L.T("EmulatorVm_AndroidIsNotDownloadedYetPress"),
        AndroidState.AvdMissing => L.T("EmulatorVm_AndroidIsDownloadedButTheVirtual"),
        AndroidState.Stopped => L.T("AndroidVm_AndroidIsInstalledAndStopped"),
        AndroidState.Running => L.T("AndroidVm_AndroidIsRunning"),
        _ => L.T("EmulatorVm_PressInstallAndroid"),
    };
}
