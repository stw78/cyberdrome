// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

using System.Globalization;
using System.Net;
using System.Text.Json;

namespace Cyberdrome.Core.CarDb;

/// <summary>
/// OBDb (https://github.com/OBDb): öppen samling tillverkarsignaler (mode 21/22 med formler), ett arkiv per
/// märke-modell. Licens CC BY-SA 4.0 – data hämtas av användaren, sparas som ett separat datapaket i
/// %AppData%\Cyberdrome\obdb och blandas aldrig in i programkoden. Källan anges i programmet och README.
/// </summary>
public sealed class ObdbClient
{
    public static string Attribution => L.T("Obdb_Attribution");
    /// <summary>Modellnamn för arkiv med märkesgemensamma signaler (arkivnamnet är bara märket).</summary>
    public const string MakeWide = "(alla modeller)";
    public const string ListUrl = "https://api.github.com/orgs/OBDb/repos?per_page=100&page=";
    public const string RawUrl = "https://raw.githubusercontent.com/OBDb/{0}/main/signalsets/v3/default.json";

    private readonly HttpClient _http;
    public string Folder { get; }

    public static string DefaultFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Cyberdrome", "obdb");

    public ObdbClient(string? folder = null, HttpClient? http = null)
    {
        Folder = folder ?? DefaultFolder;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        if (_http.DefaultRequestHeaders.UserAgent.Count == 0) _http.DefaultRequestHeaders.UserAgent.ParseAdd("CyberdromeCockpit/0.2");
    }

    /// <summary>Märken med bindestreck i namnet (resten av arkivnamnet är modellen).</summary>
    private static readonly string[] HyphenMakes = ["Mercedes-Benz", "Land-Rover", "Alfa-Romeo", "Aston-Martin", "Rolls-Royce", "Lynk-Co", "Lynk-&-Co"];

    /// <summary>"Tesla-Model-3" → (Tesla, Model 3). Null för arkiv som inte är fordon (".github", ".meta" …).</summary>
    public static ObdbVehicle? ParseRepo(string repo)
    {
        if (repo.StartsWith('.') || repo.Length < 2) return null;
        if (!repo.Contains('-')) return char.IsUpper(repo[0]) ? new ObdbVehicle(repo, repo, MakeWide) : null; // t.ex. "Saab": gemensamma signaler för märket
        var make = HyphenMakes.FirstOrDefault(m => repo.StartsWith(m + "-", StringComparison.OrdinalIgnoreCase));
        string rest;
        if (make is not null) { rest = repo[(make.Length + 1)..]; make = make.Replace('-', ' ').Replace("Mercedes Benz", "Mercedes-Benz"); }
        else { int i = repo.IndexOf('-'); make = repo[..i]; rest = repo[(i + 1)..]; }
        if (rest.Length == 0) return null;
        return new ObdbVehicle(repo, make, rest.Replace('-', ' '));
    }

    /// <summary>Alla fordon i OBDb (GitHub-listan, 100 per sida).</summary>
    public async Task<List<ObdbVehicle>> ListVehiclesAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var list = new List<ObdbVehicle>();
        for (int page = 1; page <= 30; page++)
        {
            progress?.Report(L.F("Obdb_OBDbGettingListPage0", page));
            var json = await Http.GetStringAsync(_http, ListUrl + page, ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            int n = 0;
            foreach (var r in doc.RootElement.EnumerateArray())
            {
                n++;
                if (r.TryGetProperty("archived", out var a) && a.ValueKind == JsonValueKind.True) continue;
                if (r.GetProperty("name").GetString() is { } name && ParseRepo(name) is { } v) list.Add(v);
            }
            if (n < 100) break;
        }
        return list;
    }

    public string CachePath(string repo) => Path.Combine(Folder, repo + ".json");

    /// <summary>Signalerna för ett fordon: från cachen, annars hämtas de (och sparas).</summary>
    public async Task<ObdbSignalSet> GetSignalSetAsync(string repo, bool refresh = false, CancellationToken ct = default)
    {
        var file = CachePath(repo);
        if (!refresh && File.Exists(file)) return ObdbSignalSet.Parse(await File.ReadAllTextAsync(file, ct).ConfigureAwait(false), repo);
        var json = await Http.GetStringAsync(_http, string.Format(RawUrl, Uri.EscapeDataString(repo)), ct).ConfigureAwait(false);
        var set = ObdbSignalSet.Parse(json, repo); // kontrollera innan den sparas
        Directory.CreateDirectory(Folder);
        await File.WriteAllTextAsync(file, json, ct).ConfigureAwait(false);
        var notice = Path.Combine(Folder, "LICENS.txt");
        if (!File.Exists(notice))
            await File.WriteAllTextAsync(notice,
                "Filerna i den här mappen är hämtade från OBDb (https://github.com/OBDb). / The files in this folder are downloaded from OBDb.\r\n" +
                "Licens: Creative Commons Attribution-ShareAlike 4.0 International (CC BY-SA 4.0).\r\n" +
                "https://creativecommons.org/licenses/by-sa/4.0/\r\n" +
                "Ändrade versioner av datat måste delas under samma licens. Datat är inte en del av Cyberdromes programkod.\r\n", ct).ConfigureAwait(false);
        return set;
    }
}

/// <summary>HTTP med enkel backoff: väntar och försöker igen vid 429/5xx, tydligt fel vid GitHubs gräns.</summary>
internal static class Http
{
    public static async Task<string> GetStringAsync(HttpClient http, string url, CancellationToken ct)
    {
        for (int attempt = 0; ; attempt++)
        {
            using var resp = await http.GetAsync(url, ct).ConfigureAwait(false);
            if (resp.IsSuccessStatusCode) return await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (resp.StatusCode == HttpStatusCode.NotFound) throw new HttpRequestException(L.T("Obdb_NotFound404") + url, null, resp.StatusCode);
            if (resp.StatusCode == HttpStatusCode.Forbidden && resp.Headers.TryGetValues("X-RateLimit-Remaining", out var rem) && rem.FirstOrDefault() == "0")
            {
                var reset = resp.Headers.TryGetValues("X-RateLimit-Reset", out var r) && long.TryParse(r.FirstOrDefault(), out var s)
                    ? DateTimeOffset.FromUnixTimeSeconds(s).ToLocalTime().ToString("HH:mm") : L.T("Obdb_InAWhile");
                throw new HttpRequestException(L.F("Obdb_GitHubSRequestLimitHasBeen", reset), null, resp.StatusCode);
            }
            bool retry = resp.StatusCode is HttpStatusCode.TooManyRequests or >= HttpStatusCode.InternalServerError;
            if (!retry || attempt >= 3) throw new HttpRequestException(L.F("Obdb_Error0From1", (int)resp.StatusCode, new Uri(url).Host), null, resp.StatusCode);
            var wait = resp.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(Math.Pow(2, attempt + 1));
            await Task.Delay(wait, ct).ConfigureAwait(false);
        }
    }
}

/// <summary>Signaluppsättning (OBDb "signalsets/v3").</summary>
public sealed class ObdbSignalSet
{
    public string Repo { get; init; } = "";
    public List<ObdbCommand> Commands { get; } = [];
    public int SignalCount => Commands.Sum(c => c.Signals.Count);

    public static ObdbSignalSet Parse(string json, string repo)
    {
        using var doc = JsonDocument.Parse(json);
        var set = new ObdbSignalSet { Repo = repo };
        if (!doc.RootElement.TryGetProperty("commands", out var cmds) || cmds.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException(L.T("Obdb_NoCommandsListNotAnOBDb"));
        foreach (var c in cmds.EnumerateArray())
        {
            if (!c.TryGetProperty("cmd", out var cmd) || cmd.ValueKind != JsonValueKind.Object) continue;
            var kv = cmd.EnumerateObject().FirstOrDefault();
            if (kv.Value.ValueKind != JsonValueKind.String) continue;
            var oc = new ObdbCommand
            {
                Header = Str(c, "hdr") ?? "7DF", ResponseHeader = Str(c, "rax"), ExtendedAddress = Str(c, "eax"),
                Service = kv.Name.ToUpperInvariant(), Pid = kv.Value.GetString()!.ToUpperInvariant(),
                FilterFrom = Int(c, "filter", "from"), FilterTo = Int(c, "filter", "to"),
                FilterYears = c.TryGetProperty("filter", out var f) && f.TryGetProperty("years", out var ys) && ys.ValueKind == JsonValueKind.Array
                    ? ys.EnumerateArray().Where(y => y.ValueKind == JsonValueKind.Number).Select(y => y.GetInt32()).ToList() : [],
            };
            if (c.TryGetProperty("signals", out var sigs) && sigs.ValueKind == JsonValueKind.Array)
                foreach (var s in sigs.EnumerateArray())
                {
                    if (!s.TryGetProperty("fmt", out var fmt)) continue;
                    oc.Signals.Add(new ObdbSignal
                    {
                        Id = Str(s, "id") ?? "", Name = Str(s, "name") ?? Str(s, "id") ?? "", Path = Str(s, "path") ?? "",
                        Bix = (int)(Num(fmt, "bix") ?? 0), Len = (int)(Num(fmt, "len") ?? 8),
                        Mul = Num(fmt, "mul") ?? 1, Div = Num(fmt, "div") ?? 1, Add = Num(fmt, "add") ?? 0,
                        Min = Num(fmt, "min") ?? 0, Max = Num(fmt, "max") ?? 255, Unit = Str(fmt, "unit") ?? "",
                        Signed = fmt.TryGetProperty("sign", out var sg) && sg.ValueKind == JsonValueKind.True,
                        IsEnum = fmt.TryGetProperty("map", out _),
                    });
                }
            if (oc.Signals.Count > 0) set.Commands.Add(oc);
        }
        return set;
    }

    private static string? Str(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    private static double? Num(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
    private static int? Int(JsonElement e, string o, string n) => e.TryGetProperty(o, out var x) && x.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;
}

public sealed class ObdbCommand
{
    public string Header { get; init; } = "7E0";
    public string? ResponseHeader { get; init; }
    public string? ExtendedAddress { get; init; }
    public string Service { get; init; } = "22";
    public string Pid { get; init; } = "";
    public int? FilterFrom { get; init; }
    public int? FilterTo { get; init; }
    public List<int> FilterYears { get; init; } = [];
    public List<ObdbSignal> Signals { get; } = [];

    public string Request => Service + Pid;

    /// <summary>Gäller kommandot årsmodellen? (filter: from/to/years; "to: 2018" = till och med 2018).</summary>
    public bool AppliesTo(int year)
    {
        if (FilterFrom is null && FilterTo is null && FilterYears.Count == 0) return true;
        if (FilterYears.Contains(year)) return true;
        if (FilterFrom is { } f && FilterTo is { } t) return year >= f && year <= t;
        if (FilterFrom is { } f2) return year >= f2;
        if (FilterTo is { } t2) return year <= t2;
        return false;
    }
}

public sealed class ObdbSignal
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Path { get; init; } = "";
    public int Bix { get; init; }
    public int Len { get; init; } = 8;
    public double Mul { get; init; } = 1;
    public double Div { get; init; } = 1;
    public double Add { get; init; }
    public double Min { get; init; }
    public double Max { get; init; } = 255;
    public string Unit { get; init; } = "";
    public bool Signed { get; init; }
    public bool IsEnum { get; init; }

    public override string ToString() =>
        $"{Name} ({Unit}, {Min.ToString(CultureInfo.InvariantCulture)}–{Max.ToString(CultureInfo.InvariantCulture)})";
}
