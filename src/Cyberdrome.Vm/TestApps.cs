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
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Cyberdrome", "apps");

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
        log($"Frågar F-Droid efter senaste {DisplayName} …");
        var info = await http.GetFromJsonAsync<FdroidPackage>(ApiUrl, ct)
                   ?? throw new InvalidOperationException("F-Droid svarade inte.");
        if (info.SuggestedVersionCode <= 0) throw new InvalidOperationException("F-Droid gav ingen version.");

        var path = CachePath(info.SuggestedVersionCode);
        if (File.Exists(path) && IsValidApk(path))
        {
            log($"{DisplayName} ({info.SuggestedVersionCode}) finns redan nedladdad.");
            onPercent(100);
            return path;
        }

        Directory.CreateDirectory(CacheFolder);
        var url = ApkUrl(info.SuggestedVersionCode);
        log("Laddar ner " + url);
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
            throw new InvalidDataException($"Filen från F-Droid är inte en giltig APK ({DisplayName}).");
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

    public const string HelpText =
        "Så ansluter du AndrOBD till den virtuella bilen: öppna AndrOBD → meny (⋮) → Inställningar → " +
        "Anslutningstyp: Nätverk (Network/WiFi). Ange IP-adress 10.0.2.2 och port 35000. " +
        "Gå tillbaka och tryck Anslut (Connect). Välj sedan t.ex. Live-data (OBD Data) – hastighet och varvtal " +
        "följer reglagen i Cyberdrome. AndrOBD är GPLv3 och laddas ner från F-Droid; den följer inte med Cyberdrome.";

    public static readonly FdroidApp App = new(PackageName, DisplayName, HelpText);

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
    public static readonly FdroidApp App = new("app.organicmaps", "Organic Maps",
        "Organic Maps visar bilens position från Cyberdrome (GPS skickas från sidan Karta). Första gången: tillåt platsåtkomst. " +
        "Kartdata för t.ex. Stockholm laddas ner inne i appen när du zoomar in. Organic Maps laddas ner från F-Droid och följer inte med Cyberdrome.");
}
