// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

using System.IO.Compression;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Cyberdrome.Vm;

/// <summary>
/// En fri app från F-Droid som laddas ner när användaren trycker på en knapp.
/// Apparna följer INTE med Cyberdrome, så vår egen produkts licens påverkas inte.
/// </summary>
public sealed class FdroidApp
{
    public FdroidApp(string packageName, string displayName, string helpText)
    {
        PackageName = packageName;
        DisplayName = displayName;
        HelpText = helpText;
    }

    public string PackageName { get; }
    public string DisplayName { get; }
    public string HelpText { get; }

    public string ApiUrl => $"https://f-droid.org/api/v1/packages/{PackageName}";
    public string ApkUrl(long versionCode) => $"https://f-droid.org/repo/{PackageName}_{versionCode}.apk";

    public static string CacheFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create), "Cyberdrome", "apps");

    public string CachePath(long versionCode) => Path.Combine(CacheFolder, $"{PackageName}_{versionCode}.apk");

    private sealed class FdroidPackage
    {
        [JsonPropertyName("suggestedVersionCode")] public long SuggestedVersionCode { get; set; }
    }

    /// <summary>En APK är en zip-fil med AndroidManifest.xml och minst en classes.dex.</summary>
    public static bool IsValidApk(string path)
    {
        try
        {
            using var zip = ZipFile.OpenRead(path);
            return zip.GetEntry("AndroidManifest.xml") is not null &&
                   zip.Entries.Any(e => e.FullName.StartsWith("classes", StringComparison.Ordinal) && e.FullName.EndsWith(".dex", StringComparison.Ordinal));
        }
        catch { return false; }
    }

    /// <summary>
    /// Hämtar senaste versionen från F-Droid (eller använder den som redan ligger i cachen).
    /// <paramref name="onPercent"/> får 0–100 under nedladdningen.
    /// </summary>
    public async Task<string> DownloadAsync(HttpClient http, Action<string> log, Action<double> onPercent, CancellationToken ct = default)
    {
        log(L.F("TestApps_AskingFDroidForTheLatest", DisplayName));
        var info = await http.GetFromJsonAsync<FdroidPackage>(ApiUrl, ct)
                   ?? throw new InvalidOperationException(L.T("TestApps_FDroidDidNotAnswer"));
        if (info.SuggestedVersionCode <= 0) throw new InvalidOperationException(L.T("TestApps_FDroidGaveNoVersion"));

        var path = CachePath(info.SuggestedVersionCode);
        if (File.Exists(path) && IsValidApk(path))
        {
            log(L.F("TestApps_01IsAlreadyDownloaded", DisplayName, info.SuggestedVersionCode));
            onPercent(100);
            return path;
        }

        Directory.CreateDirectory(CacheFolder);
        var url = ApkUrl(info.SuggestedVersionCode);
        log(L.T("TestApps_Downloading") + url);
        using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        long total = resp.Content.Headers.ContentLength ?? 0;
        var tmp = path + ".part";
        await using (var src = await resp.Content.ReadAsStreamAsync(ct))
        await using (var dst = File.Create(tmp))
        {
            var buf = new byte[81920];
            long done = 0;
            int n;
            double last = -1;
            while ((n = await src.ReadAsync(buf, ct)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n), ct);
                done += n;
                if (total > 0)
                {
                    double pct = done * 100.0 / total;
                    if (pct - last >= 1) { last = pct; onPercent(pct); }
                }
            }
        }
        if (!IsValidApk(tmp))
        {
            File.Delete(tmp);
            throw new InvalidDataException(L.F("TestApps_TheFileFromFDroidIs", DisplayName));
        }
        File.Move(tmp, path, overwrite: true);
        onPercent(100);
        log($"Nedladdad: {path} ({new FileInfo(path).Length / 1024} kB)");
        return path;
    }
}

/// <summary>Gratis OBD-testapp: AndrOBD (öppen källkod, GPLv3) från F-Droid.</summary>
public static class AndrObd
{
    public const string PackageName = "com.fr3ts0n.ecu.gui.androbd";
    public const string DisplayName = "AndrOBD";

    public static string HelpText => L.T("AndrObd_Help");

    public static FdroidApp App => new(PackageName, DisplayName, HelpText);

    public static string ApiUrl => App.ApiUrl;
    public static string ApkUrl(long versionCode) => App.ApkUrl(versionCode);
    public static string CacheFolder => FdroidApp.CacheFolder;
    public static string CachePath(long versionCode) => App.CachePath(versionCode);
    public static bool IsValidApk(string path) => FdroidApp.IsValidApk(path);

    public static Task<string> DownloadAsync(HttpClient http, Action<string> log, Action<double> onPercent, CancellationToken ct = default) =>
        App.DownloadAsync(http, log, onPercent, ct);
}

/// <summary>Kartapp: Organic Maps (öppen källkod, Apache-2.0) från F-Droid, för att se den emulerade GPS:en.</summary>
public static class OrganicMaps
{
    public static FdroidApp App => new("app.organicmaps", "Organic Maps", L.T("OrganicMaps_Help"));
}
