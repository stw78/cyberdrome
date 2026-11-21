// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

using System.Text.Json;

namespace Cyberdrome.Core.CarDb;

/// <summary>
/// Wikidata (https://www.wikidata.org): fri kunskapsbas, data under CC0 (fri att använda). Ger bilmodellnamn per
/// tillverkare även för europeiska modeller som vPIC saknar (t.ex. Saab 9000, Volvo V70). Årsmodeller finns bara ibland.
/// </summary>
public sealed class WikidataClient
{
    public const string SparqlUrl = "https://query.wikidata.org/sparql";
    public const string SearchUrl = "https://www.wikidata.org/w/api.php?action=wbsearchentities&format=json&language=en&type=item&limit=10&search=";
    public const string Attribution = "Wikidata (CC0)";

    /// <summary>Kända tillverkare (Wikidata-id). Andra märken slås upp med sökning.</summary>
    public static readonly Dictionary<string, string> KnownMakers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Volvo"] = "Q215293",  // Volvo Personvagnar
        ["Saab"] = "Q48957",    // Saab Automobile
    };

    private readonly HttpClient _http;

    public WikidataClient(HttpClient? http = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        if (_http.DefaultRequestHeaders.UserAgent.Count == 0) _http.DefaultRequestHeaders.UserAgent.ParseAdd("CyberdromeCockpit/0.2");
    }

    /// <summary>Wikidata-id för en biltillverkare: kända id först, annars sökning ("… car manufacturer").</summary>
    public async Task<string?> FindMakerAsync(string make, CancellationToken ct = default)
    {
        if (KnownMakers.TryGetValue(make.Trim(), out var q)) return q;
        var json = await Http.GetStringAsync(_http, SearchUrl + Uri.EscapeDataString(make.Trim()), ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("search", out var arr)) return null;
        foreach (var r in arr.EnumerateArray())
        {
            var desc = r.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "";
            if (new[] { "car manufacturer", "automobile manufacturer", "automaker", "car brand", "automobile marque", "vehicle manufacturer", "motor vehicle" }
                .Any(k => desc.Contains(k, StringComparison.OrdinalIgnoreCase)))
                return r.GetProperty("id").GetString();
        }
        return null;
    }

    /// <summary>Bilmodeller från en tillverkare (tillverkare eller varumärke). År 0 = okänt år.</summary>
    public async Task<List<CarModelEntry>> GetModelsAsync(string make, CancellationToken ct = default)
    {
        var maker = await FindMakerAsync(make, ct).ConfigureAwait(false)
                    ?? throw new InvalidOperationException(L.F("WikidataClient_NoCarManufacturer0FoundIn", make));
        var query = $$"""
            SELECT ?m ?mLabel (MIN(YEAR(?s)) AS ?from) (MAX(YEAR(?e)) AS ?to) WHERE {
              ?m wdt:P31/wdt:P279* wd:Q3231690 .
              { ?m wdt:P176 wd:{{maker}} } UNION { ?m wdt:P1716 wd:{{maker}} }
              OPTIONAL { ?m wdt:P2031|wdt:P577|wdt:P571 ?s }
              OPTIONAL { ?m wdt:P2032|wdt:P576 ?e }
              SERVICE wikibase:label { bd:serviceParam wikibase:language "sv,en". }
            } GROUP BY ?m ?mLabel
            """;
        var json = await Http.GetStringAsync(_http, SparqlUrl + "?format=json&query=" + Uri.EscapeDataString(query), ct).ConfigureAwait(false);
        return Parse(json, make);
    }

    /// <summary>Tolkar SPARQL-svaret. Märket tas bort ur modellnamnet ("Saab 9-3" → "9-3").</summary>
    public static List<CarModelEntry> Parse(string json, string make)
    {
        using var doc = JsonDocument.Parse(json);
        var list = new List<CarModelEntry>();
        var seen = new HashSet<string>();
        int now = DateTime.Now.Year;
        foreach (var b in doc.RootElement.GetProperty("results").GetProperty("bindings").EnumerateArray())
        {
            var label = Val(b, "mLabel");
            if (label is null || (label.Length > 1 && label[0] == 'Q' && label[1..].All(char.IsDigit))) continue; // saknar namn
            var model = label.StartsWith(make + " ", StringComparison.OrdinalIgnoreCase) ? label[(make.Length + 1)..].Trim() : label.Trim();
            if (model.Length == 0) continue;
            int? from = int.TryParse(Val(b, "from"), out var f) ? f : null;
            int? to = int.TryParse(Val(b, "to"), out var t) ? t : null;
            var years = from is { } a && to is { } z && z >= a && z - a <= 60 ? Enumerable.Range(a, z - a + 1)
                : from is { } only ? [only] : [0];
            foreach (var y in years.Where(y => y == 0 || (y >= 1900 && y <= now + 1)))
                if (seen.Add(CarModelDb.Key(make, model) + "|" + y)) list.Add(new CarModelEntry(make, model, y, "Wikidata"));
        }
        return list;
    }

    private static string? Val(JsonElement b, string name) =>
        b.TryGetProperty(name, out var v) && v.TryGetProperty("value", out var s) ? s.GetString() : null;
}
