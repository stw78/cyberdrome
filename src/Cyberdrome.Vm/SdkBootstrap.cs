// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

using System.Formats.Tar;
using System.IO.Compression;

namespace Cyberdrome.Vm;

/// <summary>
/// Hämtar det som behövs för att installera Android-emulatorn när det saknas: Androids kommandoradsverktyg
/// (sdkmanager, avdmanager – från Google) och Java 17 (Eclipse Temurin – från Adoptium), för Windows eller Linux.
/// Allt hamnar i SdkRoot (C:\CyberdromeSDK eller ~/CyberdromeSDK). Inget installeras i systemet.
/// </summary>
public static class SdkBootstrap
{
    public const string CmdlineToolsVersion = "13114758";

    public static string CmdlineToolsUrl =>
        $"https://dl.google.com/android/repository/commandlinetools-{Platform.DownloadOs}-{CmdlineToolsVersion}_latest.zip";

    public static string JdkUrl =>
        $"https://api.adoptium.net/v3/binary/latest/17/ga/{(Platform.IsWindows ? "windows" : "linux")}/x64/jdk/hotspot/normal/eclipse";

    /// <summary>Hämtar och packar upp kommandoradsverktygen och Java om de saknas.</summary>
    public static async Task<bool> EnsureAsync(VmSettings s, Action<string> log, Action<double, string> progress, CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("CyberdromeCockpit");
        Directory.CreateDirectory(s.SdkRoot);
        if (!File.Exists(s.SdkManager))
        {
            log(L.F("Sdk_DownloadingCmdlineTools0", CmdlineToolsUrl));
            var zip = Path.Combine(Path.GetTempPath(), "cyberdrome-cmdline-tools.zip");
            await DownloadAsync(http, CmdlineToolsUrl, zip, p => progress(p * 0.5, L.F("Sdk_DownloadingTools0", Math.Round(p))), ct);
            var tmp = Path.Combine(s.SdkRoot, "cmdline-tools", "_tmp");
            if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
            ZipFile.ExtractToDirectory(zip, tmp, overwriteFiles: true);
            var latest = Path.Combine(s.SdkRoot, "cmdline-tools", "latest");
            if (Directory.Exists(latest)) Directory.Delete(latest, true);
            Directory.Move(Path.Combine(tmp, "cmdline-tools"), latest); // zippen har mappen cmdline-tools överst
            Directory.Delete(tmp, true);
            File.Delete(zip);
            MakeExecutable(Path.Combine(latest, "bin"));
        }
        if (s.JavaHome is null)
        {
            log(L.F("Sdk_DownloadingJava0", JdkUrl));
            var archive = Path.Combine(Path.GetTempPath(), Platform.IsWindows ? "cyberdrome-jdk.zip" : "cyberdrome-jdk.tar.gz");
            await DownloadAsync(http, JdkUrl, archive, p => progress(50 + p * 0.5, L.F("Sdk_DownloadingJava", Math.Round(p))), ct);
            var jdk = Path.Combine(s.SdkRoot, "jdk");
            Directory.CreateDirectory(jdk);
            if (Platform.IsWindows) ZipFile.ExtractToDirectory(archive, jdk, overwriteFiles: true);
            else
            {
                await using var fs = File.OpenRead(archive);
                await using var gz = new GZipStream(fs, CompressionMode.Decompress);
                await TarFile.ExtractToDirectoryAsync(gz, jdk, overwriteFiles: true, ct);
            }
            File.Delete(archive);
        }
        return File.Exists(s.SdkManager) && s.JavaHome is not null;
    }

    private static async Task DownloadAsync(HttpClient http, string url, string file, Action<double> progress, CancellationToken ct)
    {
        using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        long total = resp.Content.Headers.ContentLength ?? 0, done = 0;
        await using var src = await resp.Content.ReadAsStreamAsync(ct);
        await using var dst = File.Create(file);
        var buf = new byte[1 << 16];
        int n; double last = -1;
        while ((n = await src.ReadAsync(buf, ct)) > 0)
        {
            await dst.WriteAsync(buf.AsMemory(0, n), ct);
            done += n;
            if (total > 0) { var p = done * 100.0 / total; if (p - last >= 1) { last = p; progress(p); } }
        }
    }

    /// <summary>Skalskripten (sdkmanager m.fl.) ska vara körbara på Linux.</summary>
    private static void MakeExecutable(string dir)
    {
        if (OperatingSystem.IsWindows() || !Directory.Exists(dir)) return;
        foreach (var f in Directory.EnumerateFiles(dir))
            File.SetUnixFileMode(f, File.GetUnixFileMode(f) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
    }
}
