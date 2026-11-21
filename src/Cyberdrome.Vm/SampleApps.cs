// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

namespace Cyberdrome.Vm;

/// <summary>
/// Vår egen exempelapp "Cyberdrome Radio" (källkod i android\CyberdromeRadio).
/// Byggs med build.ps1 utan Gradle och installeras i Android-emulatorn.
/// </summary>
public static class CyberdromeRadio
{
    public const string PackageName = "se.cyberdrome.radio";
    public const string DisplayName = "Cyberdrome Radio";
    public const string RelativeProjectDir = @"android\CyberdromeRadio";
    public const string ApkName = "CyberdromeRadio.apk";
    public static readonly SampleApp App = new(PackageName, DisplayName, RelativeProjectDir, ApkName);

    /// <summary>
    /// Letar uppåt från programmets mapp efter android\CyberdromeRadio (källkodsmappen).
    /// Null om den inte finns (t.ex. i en installerad version utan källkod).
    /// </summary>
    public static string? FindProjectDir(string startDir)
    {
        var dir = new DirectoryInfo(startDir);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, RelativeProjectDir.Replace('\\', Path.DirectorySeparatorChar));
            if (File.Exists(Path.Combine(candidate, "build.ps1"))) return candidate;
            dir = dir.Parent;
        }
        return null;
    }

    public static string ApkPath(string projectDir) => Path.Combine(projectDir, "bin", ApkName);

    /// <summary>Kör build.ps1 och returnerar sökvägen till APK:n, eller null om bygget misslyckades.</summary>
    public static async Task<string?> BuildAsync(string projectDir, string sdkRoot, Action<string> log, CancellationToken ct = default)
    {
        var script = Path.Combine(projectDir, "build.ps1");
        var r = await ProcessRunner.RunAsync("powershell.exe",
            ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script, "-Sdk", sdkRoot], onLine: log, ct: ct);
        var apk = ApkPath(projectDir);
        return r.Ok && File.Exists(apk) ? apk : null;
    }
}

/// <summary>En av våra egna Android-appar (källkod under android\), som byggs med build.ps1 och installeras i emulatorn.</summary>
public sealed record SampleApp(string PackageName, string DisplayName, string RelativeProjectDir, string ApkName)
{
    public string? FindProjectDir(string startDir)
    {
        var dir = new DirectoryInfo(startDir);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, RelativeProjectDir.Replace('\\', Path.DirectorySeparatorChar));
            if (File.Exists(Path.Combine(candidate, "build.ps1"))) return candidate;
            dir = dir.Parent;
        }
        return null;
    }

    public string ApkPath(string projectDir) => Path.Combine(projectDir, "bin", ApkName);

    /// <summary>Färdigbyggd APK som följer med installationen (programmappenpps). Null om den saknas.</summary>
    public static string? Bundled(string apkName)
    {
        var p = Path.Combine(AppContext.BaseDirectory, "apps", apkName);
        return File.Exists(p) ? p : null;
    }

    /// <summary>True om APK saknas eller om källkoden är nyare än APK:n.</summary>
    public bool NeedsBuild(string projectDir)
    {
        var apk = ApkPath(projectDir);
        if (!File.Exists(apk)) return true;
        var sep = Path.DirectorySeparatorChar;
        var newest = Directory.GetFiles(projectDir, "*.*", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{sep}bin{sep}") && !f.Contains($"{sep}obj{sep}"))
            .Select(File.GetLastWriteTimeUtc).DefaultIfEmpty(DateTime.MinValue).Max();
        return File.GetLastWriteTimeUtc(apk) < newest;
    }

    public async Task<string?> BuildAsync(string projectDir, string sdkRoot, Action<string> log, CancellationToken ct = default)
    {
        var script = Path.Combine(projectDir, "build.ps1");
        var r = await ProcessRunner.RunAsync("powershell.exe",
            ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script, "-Sdk", sdkRoot], onLine: log, ct: ct);
        var apk = ApkPath(projectDir);
        return r.Ok && File.Exists(apk) ? apk : null;
    }
}

/// <summary>Vår startskärm med märkesteman (källkod i android\CyberdromeLauncher).</summary>
public static class CyberdromeLauncher
{
    public const string PackageName = "se.cyberdrome.launcher";
    public const string HomeActivity = PackageName + "/.HomeActivity";
    public const string ThemeReceiver = PackageName + "/.ThemeReceiver";
    public const string ThemeAction = "se.cyberdrome.launcher.SET_THEME";
    public static readonly SampleApp App = new(PackageName, "Cyberdrome Launcher", @"android\CyberdromeLauncher", "CyberdromeLauncher.apk");

    /// <summary>Argument till "adb shell am broadcast" för ett inbyggt tema.</summary>
    public static string[] BroadcastBuiltIn(string themeId, string? obd = null) =>
        [.. Base, "--es", "id", themeId, .. Obd(obd)];

    /// <summary>Argument till "adb shell am broadcast" för ett eget tema (JSON som base64 – inga specialtecken).</summary>
    public static string[] BroadcastCustom(Themes.LauncherTheme theme, string? obd = null) =>
        [.. Base, "--es", "json64", theme.ToBase64(), .. Obd(obd)];

    private static readonly string[] Base = ["am", "broadcast", "--user", "current", "-n", ThemeReceiver, "-a", ThemeAction];

    /// <summary>OBD-adressen sett inifrån Android ("värd:port"): 10.0.2.2 i emulatorn, WSL2-gatewayen i Cuttlefish.</summary>
    private static string[] Obd(string? hostPort) => string.IsNullOrEmpty(hostPort) ? [] : ["--es", "obd", hostPort];
}
