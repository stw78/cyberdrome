// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

using System.Reflection;

namespace Cyberdrome.Vm;

/// <summary>
/// Sköter Android Automotive i en virtuell maskin: Cuttlefish i en egen WSL2-distribution.
/// Allt körs via wsl.exe, så inga ändringar görs i användarens andra Linux-miljöer.
/// </summary>
public sealed class AndroidVm : IAndroidBackend, IScreenControl
{
    private const string Wsl = "wsl.exe";
    private bool _rebootNeeded;

    public AndroidVm(VmSettings settings) => Settings = settings;

    public VmSettings Settings { get; }
    public AndroidMode Mode => AndroidMode.Fast;
    /// <summary>Windows sett från WSL2 (standardgatewayen); Android i Cuttlefish når OBD-porten där. Läses av status.sh.</summary>
    public string? ObdHostFromAndroid => _obdHost;
    private string? _obdHost;

    public async Task<AndroidState> GetStateAsync(CancellationToken ct = default)
    {
        if (!OperatingSystem.IsWindows()) return AndroidState.NotWindows;
        if (_rebootNeeded) return AndroidState.RebootNeeded;

        var st = await ProcessRunner.RunAsync(Wsl, ["--status"], ct: ct);
        if (!st.Ok || st.Output.Contains("virtualiz", StringComparison.OrdinalIgnoreCase)
                   || st.Output.Contains("virtualiser", StringComparison.OrdinalIgnoreCase))
            return AndroidState.WslMissing;

        var list = await ProcessRunner.RunAsync(Wsl, ["--list", "--quiet"], ct: ct);
        if (!list.Output.Split('\n').Any(l => l.Trim() == Settings.DistroName))
            return AndroidState.DistroMissing;

        var s = await RunScriptAsync("status.sh", root: true, ct: ct);
        var kv = s.Output.Split('\n').Select(l => l.Trim().Split('=', 2)).Where(p => p.Length == 2)
            .ToDictionary(p => p[0], p => p[1]);
        string V(string k) => kv.GetValueOrDefault(k, "no");
        _obdHost = kv.GetValueOrDefault("gw") is { Length: > 0 } gw && System.Net.IPAddress.TryParse(gw, out _) ? gw : null;

        if (V("tools") != "yes") return AndroidState.ToolsMissing;
        if (V("kvm") != "yes") return AndroidState.NoKvm;
        if (V("image") != "yes") return AndroidState.ImageMissing;
        return V("running") == "yes" ? AndroidState.Running : AndroidState.Stopped;
    }

    // Hur stor del av förloppsstapeln (0–100) varje installationssteg får.
    private const double PctDistroStart = 5, PctToolsStart = 15, PctDownloadStart = 35;

    /// <summary>Texten som visas (med Fortsätt/Avbryt) innan WSL2 slås på.</summary>
    public static string RestartExplanation => L.T("Android_RestartExplanation");

    /// <summary>
    /// Kör alla installationssteg som behövs, från där vi är nu.
    /// Returnerar tillståndet efteråt (t.ex. RebootNeeded om Windows måste startas om).
    /// </summary>
    public async Task<AndroidState> InstallAsync(Action<string> log, IProgress<InstallProgress>? progress = null,
        Func<string, Task<bool>>? confirm = null, CancellationToken ct = default)
    {
        void Report(double? pct, string text, bool done = false) => progress?.Report(new InstallProgress(pct, text, done));

        Report(null, L.T("AndroidVm_CheckingWhatNeedsToBeInstalled"));
        // Fel ska aldrig vara tysta: allt som stoppar installationen loggas OCH visas i förloppsrutan.
        AndroidState Fail(AndroidState s, string why)
        {
            log(why);
            Report(0, L.F("AndroidVm_StepFailedSeeLog", why));
            return s;
        }
        var state = await GetStateAsync(ct);
        log(L.F("AndroidVm_Mode0", state));

        if (state == AndroidState.WslMissing)
        {
            var st = await ProcessRunner.RunAsync(Wsl, ["--status"], ct: ct);
            var detail = string.Join(" ", st.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(4));
            if (detail.Length > 0) log(L.F("AndroidVm_WslStatusDetail", detail));
            log(L.T("AndroidVm_WslNotWorkingHelp"));
            if (confirm is not null && !await confirm(RestartExplanation))
            {
                log(L.T("AndroidVm_CancelledNothingWasChanged"));
                Report(0, L.T("AndroidVm_CancelledNothingWasChanged2"));
                return state;
            }
            Report(null, L.T("AndroidVm_TurningOnWSL2AnswerYesTo"));
            log(L.T("AndroidVm_TurningOnWSL2WindowsAsksFor"));
            int code = await ProcessRunner.RunElevatedAsync(Wsl, "--install --no-distribution");
            if (code == -2) return Fail(state, L.T("AndroidVm_YouSaidNoToTheAdministrator"));
            if (code != 0) return Fail(state, L.F("AndroidVm_WSL2CouldNotBeTurnedOn", code));
            log(L.T("AndroidVm_WSL2IsTurnedOnRestartThe"));
            Report(PctDistroStart, L.T("AndroidVm_WSL2IsTurnedOnRestartThe2"));
            _rebootNeeded = true;
            return AndroidState.RebootNeeded;
        }
        if (state == AndroidState.RebootNeeded) return state;

        if (state == AndroidState.DistroMissing)
        {
            Report(null, L.F("AndroidVm_Step1Of3DownloadingAnd", Settings.BaseDistro));
            log(L.F("AndroidVm_CreatingTheLinuxEnvironment0", Settings.DistroName));
            var r = await ProcessRunner.RunAsync(Wsl,
                ["--install", Settings.BaseDistro, "--name", Settings.DistroName, "--no-launch"], onLine: log, ct: ct);
            if (!r.Ok) return Fail(state, L.T("AndroidVm_CouldNotCreateTheLinuxEnvironment"));
            state = AndroidState.ToolsMissing;
        }

        if (state == AndroidState.ToolsMissing)
        {
            Report(PctToolsStart, L.T("AndroidVm_Step2Of3InstallingThe"));
            log(L.T("AndroidVm_InstallingTheCuttlefishTools"));
            var r = await RunScriptAsync("setup.sh", root: true, log: Filter(log, line =>
            {
                if (ProgressParser.ParseStep(line) is { } s)
                    Report(PctToolsStart + s.Fraction * (PctDownloadStart - PctToolsStart), L.F("AndroidVm_Step2Of30", s.Text));
            }), ct: ct);
            if (!r.Ok) return Fail(state, L.T("AndroidVm_InstallingTheToolsFailed"));
            // Starta om Linux-miljön så att systemd och grupper gäller.
            await ProcessRunner.RunAsync(Wsl, ["--terminate", Settings.DistroName], ct: ct);
            state = await GetStateAsync(ct);
            // Tidigare fastnade vi här tyst: verktygen fanns men kontrollen letade efter fel program.
            if (state == AndroidState.ToolsMissing) return Fail(state, L.T("AndroidVm_ToolsStillMissing"));
        }

        if (state == AndroidState.NoKvm)
            return Fail(state, L.T("AndroidVm_VirtualisationKVMIsMissingInsideWSL") + " " + L.T("AndroidVm_NoKvmHelp"));

        if (state == AndroidState.ImageMissing)
        {
            Report(PctDownloadStart, L.T("AndroidVm_Step3Of3StartingThe"));
            long expected = Math.Max(1, Settings.ExpectedDownloadMb) * 1024 * 1024;
            long lastBytes = 0, finalBytes = 0;
            var lastTime = DateTime.UtcNow;
            double speed = 0; // byte/s, utjämnad
            var r = await RunScriptAsync("fetch.sh", root: false, log: Filter(log, line =>
            {
                if (ProgressParser.ParseBytes(line) is not { } bytes) return;
                finalBytes = bytes;
                var now = DateTime.UtcNow;
                double secs = (now - lastTime).TotalSeconds;
                if (secs > 0.5)
                {
                    double inst = Math.Max(0, bytes - lastBytes) / secs;
                    speed = speed == 0 ? inst : speed * 0.7 + inst * 0.3;
                    lastBytes = bytes; lastTime = now;
                }
                double f = ProgressParser.DownloadFraction(bytes, expected);
                string spd = speed > 0 ? $" – {ProgressParser.FormatMb((long)speed)}/s" : "";
                Report(PctDownloadStart + f * (100 - PctDownloadStart),
                    L.F("AndroidVm_Step3Of3DownloadingAndroid", ProgressParser.FormatMb(bytes), ProgressParser.FormatMb(expected), f * 100, spd));
            }), env: new() { ["BUILD"] = Settings.AndroidBuild }, ct: ct);
            if (!r.Ok) return Fail(state, L.T("AndroidVm_TheAndroidDownloadFailed"));
            // Kom ihåg verklig storlek så att stapeln stämmer bättre nästa gång.
            if (finalBytes > 0)
            {
                Settings.ExpectedDownloadMb = finalBytes / 1024 / 1024;
                Settings.Save();
            }
            log($"Nedladdat: {ProgressParser.FormatMb(finalBytes)}.");
            state = AndroidState.Stopped;
        }

        if (state is not (AndroidState.Stopped or AndroidState.Running))
            return Fail(state, L.F("AndroidVm_InstallStopped", DescribeState(state)));
        Report(100, L.T("AndroidVm_DoneAndroidIsDownloadedAndInstalled"), done: true);
        log(L.T("AndroidVm_AndroidIsInstalled"));
        return state;
    }

    /// <summary>Skickar förloppsrader (@@…) till <paramref name="onProgress"/> och allt annat till loggen.</summary>
    private static Action<string> Filter(Action<string> log, Action<string> onProgress) => line =>
    {
        if (ProgressParser.IsProgressLine(line)) onProgress(line);
        else log(line);
    };

    public async Task<ProcessResult> StartAsync(Action<string> log, IProgress<InstallProgress>? progress = null, CancellationToken ct = default)
    {
        _adbConnected = false;
        _displayId = null;
        var r = await RunScriptAsync("start.sh", root: false, log: log, ct: ct, env: StartEnv(Settings));
        if (r.Ok)
        {
            var scale = AndroidTuning.AnimationScale(Settings.FastAnimations);
            foreach (var key in AndroidTuning.AnimationKeys) await ShellAsync(ct, "settings", "put", "global", key, scale);
        }
        return r;
    }

    /// <summary>Raden "FEL: …" från startskriptet (den sista), utan "FEL:", eller null.</summary>
    public static string? StartFailureReason(string output) =>
        output.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.StartsWith("FEL:", StringComparison.Ordinal)) is { } line
            ? line[4..].Trim()
            : null;

    /// <summary>Miljövariabler till start.sh: kärnor, minne, skärmstorlek (förvalet i Inställningar) och tidsgräns.</summary>
    public static Dictionary<string, string> StartEnv(VmSettings s)
    {
        var (w, h, dpi) = s.EffectiveScreen();
        return new()
        {
            ["CPUS"] = s.Cpus.ToString(),
            ["MEM"] = s.MemoryMb.ToString(),
            ["X_RES"] = w.ToString(),
            ["Y_RES"] = h.ToString(),
            ["DPI"] = dpi.ToString(),
            ["ADB_PORT"] = s.AdbPort.ToString(),
            ["BOOT_TIMEOUT"] = Math.Max(60, s.BootTimeoutSeconds).ToString(),
        };
    }

    // ---------------- Skärmen i programfönstret (adb i Linux-miljön) ----------------

    private const string CvdAdb = "/home/cvd/cf/bin/adb";
    private string Serial => $"127.0.0.1:{Settings.AdbPort}";
    private bool _adbConnected;
    private string? _displayId;

    /// <summary>
    /// Windows egen adb (från Android SDK) når Cuttlefish direkt på 127.0.0.1 via WSL2:s portvidarebefordran och är
    /// mycket snabbare än att starta wsl.exe för varje bild. Finns den inte används adb inne i Linux-miljön.
    /// </summary>
    private bool WindowsAdb => !_windowsAdbFailed && File.Exists(Settings.AdbExe);
    private bool _windowsAdbFailed;

    private (string File, List<string> Args) Adb(params string[] args)
    {
        if (WindowsAdb) return (Settings.AdbExe, ["-s", Serial, .. args]);
        var a = new List<string> { "-d", Settings.DistroName, "-u", "cvd", "--", CvdAdb, "-s", Serial };
        a.AddRange(args);
        return (Wsl, a);
    }

    private async Task EnsureAdbAsync(CancellationToken ct)
    {
        if (_adbConnected) return;
        if (WindowsAdb)
        {
            var w = await ProcessRunner.RunAsync(Settings.AdbExe, ["connect", Serial], ct: ct);
            _adbConnected = w.Ok && w.Output.Contains("connected", StringComparison.OrdinalIgnoreCase) && !w.Output.Contains("cannot", StringComparison.OrdinalIgnoreCase);
            if (_adbConnected) return;
            _windowsAdbFailed = true;
        }
        var r = await ProcessRunner.RunAsync(Wsl, ["-d", Settings.DistroName, "-u", "cvd", "--", CvdAdb, "connect", Serial], ct: ct);
        _adbConnected = r.Ok && !r.Output.Contains("failed", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<ProcessResult> ShellAsync(CancellationToken ct, params string[] args)
    {
        await EnsureAdbAsync(ct);
        var (file, a) = Adb(["shell", .. args]);
        return await ProcessRunner.RunAsync(file, a, ct: ct);
    }

    public async Task<byte[]?> CaptureScreenAsync(CancellationToken ct = default)
    {
        await EnsureAdbAsync(ct);
        // Bilen har flera skärmar (huvudskärm, instrumentkluster): välj huvudskärmen (den första fysiska).
        if (_displayId is null)
        {
            var d = await ShellAsync(ct, "dumpsys", "SurfaceFlinger", "--display-id");
            var m = System.Text.RegularExpressions.Regex.Match(d.Output, @"Display (\d+)");
            _displayId = m.Success ? m.Groups[1].Value : "";
        }
        var (file, args) = _displayId.Length > 0 ? Adb("exec-out", "screencap", "-p", "-d", _displayId) : Adb("exec-out", "screencap", "-p");
        var png = await ProcessRunner.RunBinaryAsync(file, args, ct);
        if (png is null || png.Length < 8 || png[0] != 0x89 || png[1] != (byte)'P')
        {
            _adbConnected = false; // Android kan ha startats om – anslut och leta skärm igen nästa gång
            _displayId = null;
            return null;
        }
        return png;
    }

    public Task TapAsync(int x, int y, CancellationToken ct = default) =>
        ShellAsync(ct, "input", "tap", x.ToString(), y.ToString());

    public Task SwipeAsync(int x1, int y1, int x2, int y2, int durationMs, CancellationToken ct = default) =>
        ShellAsync(ct, "input", "swipe", x1.ToString(), y1.ToString(), x2.ToString(), y2.ToString(), durationMs.ToString());

    public Task KeyEventAsync(int keyCode, CancellationToken ct = default) =>
        ShellAsync(ct, "input", "keyevent", keyCode.ToString());

    public async Task<string?> RestartForegroundAppAsync(Action<string> log, CancellationToken ct = default)
    {
        var dump = await ShellAsync(ct, "dumpsys", "activity", "activities");
        var component = ActivityParser.TopComponent(dump.Output);
        if (component is null) { log(L.T("EmulatorVm_NoAppFoundInTheForeground")); return null; }
        var pkg = component.Split('/')[0];
        log(L.F("EmulatorVm_Restarting0", pkg));
        await ShellAsync(ct, "am", "force-stop", pkg);
        var r = await ShellAsync(ct, "am", "start", "-n", component);
        log(r.Ok ? L.F("EmulatorVm_0HasBeenRestarted", pkg) : L.F("EmulatorVm_CouldNotStart0Again", pkg));
        return pkg;
    }

    public async Task<bool> LaunchAppAsync(string packageName, CancellationToken ct = default) =>
        (await ShellAsync(ct, "monkey", "-p", packageName, "-c", "android.intent.category.LAUNCHER", "1")).Ok;

    public Task<ProcessResult> StopAsync(Action<string> log, CancellationToken ct = default) =>
        RunScriptAsync("stop.sh", root: false, log: log, ct: ct);

    public void OpenScreen(Action<string> log)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Settings.ScreenUrl) { UseShellExecute = true });
        }
        catch (Exception ex) { log(L.T("AndroidVm_CouldNotOpenTheScreen") + ex.Message); }
    }

    /// <summary>Installerar en APK-fil i Android.</summary>
    public async Task<ProcessResult> InstallApkAsync(string windowsPath, Action<string> log, CancellationToken ct = default)
    {
        var conv = await ProcessRunner.RunAsync(Wsl, ["-d", Settings.DistroName, "-u", "cvd", "--", "wslpath", "-a", windowsPath], ct: ct);
        if (!conv.Ok) return conv;
        return await RunScriptAsync("install-apk.sh", root: false, log: log, ct: ct, env: new()
        {
            ["APK"] = conv.Output.Trim(),
            ["ADB_PORT"] = Settings.AdbPort.ToString(),
        });
    }

    /// <summary>Kör ett av våra inbyggda skript i Linux-miljön via stdin.</summary>
    private Task<ProcessResult> RunScriptAsync(string name, bool root, Action<string>? log = null,
        Dictionary<string, string>? env = null, CancellationToken ct = default)
    {
        var args = new List<string> { "-d", Settings.DistroName, "-u", root ? "root" : "cvd", "--", "env" };
        if (!root) args.Add("HOME=/home/cvd");
        foreach (var (k, v) in env ?? new()) args.Add($"{k}={v}");
        args.AddRange(["bash", "-s"]);
        return ProcessRunner.RunAsync(Wsl, args, stdin: ReadScript(name), onLine: log, ct: ct);
    }

    public static string ReadScript(string name)
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Cyberdrome.Vm.Scripts.{name}")
                      ?? throw new FileNotFoundException(name);
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }

    public string Describe(AndroidState s) => DescribeState(s);

    public static string DescribeState(AndroidState s) => s switch
    {
        AndroidState.NotWindows => L.T("AndroidVm_TheAndroidPartOnlyWorksOn"),
        AndroidState.WslMissing => L.T("AndroidVm_WSL2IsNotTurnedOnPress") + " " + L.T("AndroidVm_WslNotWorkingHelp"),
        AndroidState.RebootNeeded => L.T("AndroidVm_RestartTheComputerAndThenPress"),
        AndroidState.DistroMissing => L.T("AndroidVm_AndroidIsNotInstalledYet"),
        AndroidState.ToolsMissing => L.T("AndroidVm_AndroidIsPartlyInstalledPressInstall"),
        AndroidState.NoKvm => L.T("AndroidVm_NoKvmHelp"),
        AndroidState.ImageMissing => L.T("AndroidVm_TheAndroidImageIsNotDownloaded"),
        AndroidState.Stopped => L.T("AndroidVm_AndroidIsInstalledAndStopped"),
        AndroidState.Running => L.T("AndroidVm_AndroidIsRunning"),
        _ => L.T("AndroidVm_UnknownStatePressCheckAgain"),
    };
}
