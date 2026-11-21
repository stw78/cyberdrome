// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

using System.Reflection;

namespace Cyberdrome.Vm;

/// <summary>
/// Sköter Android Automotive i en virtuell maskin: Cuttlefish i en egen WSL2-distribution.
/// Allt körs via wsl.exe, så inga ändringar görs i användarens andra Linux-miljöer.
/// </summary>
public sealed class AndroidVm : IAndroidBackend
{
    private const string Wsl = "wsl.exe";
    private bool _rebootNeeded;

    public AndroidVm(VmSettings settings) => Settings = settings;

    public VmSettings Settings { get; }
    public AndroidMode Mode => AndroidMode.Fast;
    public string? ObdHostFromAndroid => null;

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
        var state = await GetStateAsync(ct);
        log(L.F("AndroidVm_Mode0", state));

        if (state == AndroidState.WslMissing)
        {
            if (confirm is not null && !await confirm(RestartExplanation))
            {
                log(L.T("AndroidVm_CancelledNothingWasChanged"));
                Report(0, L.T("AndroidVm_CancelledNothingWasChanged2"));
                return state;
            }
            Report(null, L.T("AndroidVm_TurningOnWSL2AnswerYesTo"));
            log(L.T("AndroidVm_TurningOnWSL2WindowsAsksFor"));
            int code = await ProcessRunner.RunElevatedAsync(Wsl, "--install --no-distribution");
            if (code == -2) { log(L.T("AndroidVm_YouSaidNoToTheAdministrator")); return state; }
            if (code != 0) { log(L.F("AndroidVm_WSL2CouldNotBeTurnedOn", code)); return state; }
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
            if (!r.Ok) { log(L.T("AndroidVm_CouldNotCreateTheLinuxEnvironment")); return state; }
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
            if (!r.Ok) { log(L.T("AndroidVm_InstallingTheToolsFailed")); return state; }
            // Starta om Linux-miljön så att systemd och grupper gäller.
            await ProcessRunner.RunAsync(Wsl, ["--terminate", Settings.DistroName], ct: ct);
            state = await GetStateAsync(ct);
        }

        if (state == AndroidState.NoKvm)
        {
            log(L.T("AndroidVm_VirtualisationKVMIsMissingInsideWSL"));
            return state;
        }

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
            if (!r.Ok) { log(L.T("AndroidVm_TheAndroidDownloadFailed")); return state; }
            // Kom ihåg verklig storlek så att stapeln stämmer bättre nästa gång.
            if (finalBytes > 0)
            {
                Settings.ExpectedDownloadMb = finalBytes / 1024 / 1024;
                Settings.Save();
            }
            log($"Nedladdat: {ProgressParser.FormatMb(finalBytes)}.");
            state = AndroidState.Stopped;
        }

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

    public Task<ProcessResult> StartAsync(Action<string> log, IProgress<InstallProgress>? progress = null, CancellationToken ct = default) =>
        RunScriptAsync("start.sh", root: false, log: log, ct: ct, env: new()
        {
            ["CPUS"] = Settings.Cpus.ToString(),
            ["MEM"] = Settings.MemoryMb.ToString(),
        });

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
        AndroidState.WslMissing => L.T("AndroidVm_WSL2IsNotTurnedOnPress"),
        AndroidState.RebootNeeded => L.T("AndroidVm_RestartTheComputerAndThenPress"),
        AndroidState.DistroMissing => L.T("AndroidVm_AndroidIsNotInstalledYet"),
        AndroidState.ToolsMissing => L.T("AndroidVm_AndroidIsPartlyInstalledPressInstall"),
        AndroidState.NoKvm => L.T("AndroidVm_VirtualisationIsMissingInWSLCheck"),
        AndroidState.ImageMissing => L.T("AndroidVm_TheAndroidImageIsNotDownloaded"),
        AndroidState.Stopped => L.T("AndroidVm_AndroidIsInstalledAndStopped"),
        AndroidState.Running => L.T("AndroidVm_AndroidIsRunning"),
        _ => L.T("AndroidVm_UnknownStatePressCheckAgain"),
    };
}
