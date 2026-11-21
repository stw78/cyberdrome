// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

namespace Cyberdrome.Vm;

/// <summary>Var installationen är just nu.</summary>
public enum AndroidState
{
    NotWindows,      // bara Windows stöds i MVP
    WslMissing,      // (Snabbt) WSL2 / virtualiseringsplattformen är inte påslagen
    RebootNeeded,    // (Snabbt) WSL har slagits på, Windows måste startas om
    DistroMissing,   // (Snabbt) vår Linux-miljö finns inte än
    ToolsMissing,    // Cuttlefish / emulatorn är inte installerad
    NoKvm,           // (Snabbt) ingen hårdvaruvirtualisering inne i WSL
    SdkMissing,      // (Utan omstart) Android SDK (sdkmanager + Java) saknas
    NoAcceleration,  // (Utan omstart) Windows Hypervisor Platform går inte att använda
    ImageMissing,    // Android-avbilden är inte nedladdad
    AvdMissing,      // (Utan omstart) den virtuella bilen är inte skapad
    Stopped,         // klart att starta
    Running,         // Android är igång
}

/// <summary>Ett sätt att köra Android (WSL2 + Cuttlefish, eller Googles emulator).</summary>
public interface IAndroidBackend
{
    VmSettings Settings { get; }
    AndroidMode Mode { get; }

    Task<AndroidState> GetStateAsync(CancellationToken ct = default);

    /// <summary>
    /// Kör alla installationssteg som behövs. <paramref name="confirm"/> anropas med en förklarande text
    /// innan något stort görs (t.ex. ett steg som kräver omstart); svarar den false avbryts installationen.
    /// </summary>
    Task<AndroidState> InstallAsync(Action<string> log, IProgress<InstallProgress>? progress,
        Func<string, Task<bool>>? confirm, CancellationToken ct = default);

    Task<ProcessResult> StartAsync(Action<string> log, IProgress<InstallProgress>? progress = null, CancellationToken ct = default);
    Task<ProcessResult> StopAsync(Action<string> log, CancellationToken ct = default);
    Task<ProcessResult> InstallApkAsync(string windowsPath, Action<string> log, CancellationToken ct = default);

    /// <summary>Visar Android-skärmen (webbläsare eller emulatorns fönster).</summary>
    void OpenScreen(Action<string> log);

    /// <summary>Text för användaren som beskriver läget.</summary>
    string Describe(AndroidState state);

    /// <summary>OBD-adressen sett inifrån Android, eller null om den är samma som datorns.</summary>
    string? ObdHostFromAndroid { get; }
}

public static class AndroidBackends
{
    public static IAndroidBackend Create(VmSettings settings) => settings.Mode switch
    {
        AndroidMode.NoRestart => new EmulatorVm(settings),
        AndroidMode.GoogleApps => new EmulatorVm(settings, googleApps: true),
        _ => new AndroidVm(settings),
    };

    public static string ModeTitle(AndroidMode m) => m switch
    {
        AndroidMode.NoRestart => L.T("Ui_NoRestartGoogleSEmulatorFor"),
        AndroidMode.GoogleApps => L.T("Ui_AndroidWithGoogleAppsPlayStore"),
        _ => L.T("Ui_FastWSL2CuttlefishRealAOSP"),
    };
}
