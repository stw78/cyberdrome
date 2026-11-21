// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

using System.Text.Json;

namespace Cyberdrome.Core.CarDb;

/// <summary>
/// Klient för NHTSA vPIC (https://vpic.nhtsa.dot.gov/api/), en öppen och gratis databas över bilmärken,
/// modeller per årsmodell och VIN-avkodning. Ingen registrering krävs. Data gäller i första hand modeller som
/// sålts i USA (årsmodell 1981 och senare); europeiska modeller kan saknas.
/// Bara märke/år eller VIN skickas – inga personuppgifter.
/// </summary>
public sealed class VpicClient
{
    public const string BaseUrl = "https://vpic.nhtsa.dot.gov/api/vehicles/";
    private readonly HttpClient _http;

    public VpicClient(HttpClient? http = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        if (_http.DefaultRequestHeaders.UserAgent.Count == 0) _http.DefaultRequestHeaders.UserAgent.ParseAdd("CyberdromeCockpit/0.2");
    }

    private async Task<JsonElement[]> GetResultsAsync(string path, CancellationToken ct)
    {
        var json = await Http.GetStringAsync(_http, BaseUrl + path, ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty("Results", out var res) && res.ValueKind == JsonValueKind.Array
            ? res.EnumerateArray().Select(e => e.Clone()).ToArray()
            : [];
    }

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) ? v.ValueKind switch
        {
            JsonValueKind.String => v.GetString()?.Trim() ?? "",
            JsonValueKind.Number => v.ToString(),
            _ => "",
        } : "";

    /// <summary>Alla märken som har personbilar (passenger car + MPV/SUV).</summary>
    public async Task<List<(string Name, int? Id)>> GetCarMakesAsync(CancellationToken ct = default)
    {
        var list = new Dictionary<string, int?>(StringComparer.OrdinalIgnoreCase);
        foreach (var type in new[] { "car", "multipurpose%20passenger%20vehicle%20(mpv)" })
            foreach (var e in await GetResultsAsync($"GetMakesForVehicleType/{type}?format=json", ct).ConfigureAwait(false))
            {
                var name = Str(e, "MakeName");
                if (name.Length > 0) list[Pretty(name)] = int.TryParse(Str(e, "MakeId"), out var id) ? id : null;
            }
        return list.Select(kv => (kv.Key, kv.Value)).OrderBy(x => x.Key).ToList();
    }

    /// <summary>Personbilsmodeller för ett märke och en årsmodell.</summary>
    public async Task<List<CarModelEntry>> GetModelsAsync(string make, int year, CancellationToken ct = default)
    {
        var res = new Dictionary<string, CarModelEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var type in new[] { "car", "multipurpose%20passenger%20vehicle%20(mpv)" })
        {
            var path = $"GetModelsForMakeYear/make/{Uri.EscapeDataString(make.Trim())}/modelyear/{year}/vehicletype/{type}?format=json";
            foreach (var e in await GetResultsAsync(path, ct).ConfigureAwait(false))
            {
                var model = Str(e, "Model_Name");
                var mk = Str(e, "Make_Name");
                if (model.Length > 0) res[model] = new CarModelEntry(Pretty(mk.Length > 0 ? mk : make), model, year, "NHTSA vPIC");
            }
        }
        return res.Values.OrderBy(m => m.Model).ToList();
    }

    /// <summary>Avkodar ett VIN (17 tecken).</summary>
    public async Task<VinInfo> DecodeVinAsync(string vin, int? modelYear = null, CancellationToken ct = default)
    {
        var path = $"DecodeVinValues/{Uri.EscapeDataString(vin.Trim().ToUpperInvariant())}?format=json" + (modelYear is { } y ? $"&modelyear={y}" : "");
        var r = (await GetResultsAsync(path, ct).ConfigureAwait(false)).FirstOrDefault();
        return r.ValueKind == JsonValueKind.Undefined ? new VinInfo(vin, "", "", null, "", "", "", "", "", L.T("VpicClient_NoReply")) : Parse(vin, r);
    }

    /// <summary>Tolkar ett DecodeVinValues-resultat (platta fält).</summary>
    public static VinInfo Parse(string vin, JsonElement r)
    {
        var engine = string.Join(" ", new[]
        {
            Str(r, "DisplacementL") is { Length: > 0 } l && double.TryParse(l, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var dl) ? $"{dl:0.0} l" : "",
            Str(r, "EngineCylinders") is { Length: > 0 } c ? $"{c} cyl" : "",
            Str(r, "EngineHP") is { Length: > 0 } hp ? $"{hp} hk" : "",
        }.Where(s => s.Length > 0)).Replace(',', '.');
        var err = Str(r, "ErrorCode") is "0" or "" ? "" : Str(r, "ErrorText");
        return new VinInfo(vin.ToUpperInvariant(), Pretty(Str(r, "Make")), Str(r, "Model"),
            int.TryParse(Str(r, "ModelYear"), out var y) ? y : null, Str(r, "Manufacturer"), Str(r, "BodyClass"),
            Str(r, "FuelTypePrimary"), engine, Str(r, "PlantCountry"), err);
    }

    /// <summary>"VOLKSWAGEN" → "Volkswagen", "BMW" behålls.</summary>
    public static string Pretty(string make)
    {
        make = make.Trim();
        if (make.Length <= 3 || make.Any(char.IsLower)) return make;
        return string.Join(' ', make.Split(' ').Select(w => w.Length <= 3 && !w.Any(char.IsDigit) && w is not ("KIA" or "SEAT") ? w
            : w.Contains('-') ? string.Join('-', w.Split('-').Select(Cap)) : Cap(w)));
        static string Cap(string w) => w.Length == 0 ? w : char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant();
    }
}

