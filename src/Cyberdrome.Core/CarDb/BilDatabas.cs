// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

using Cyberdrome.Core.Profiles;

namespace Cyberdrome.Core.CarDb;

/// <summary>
/// Bildatabasen som programmet ansluter till. Idag en lokal SQLite-cache som fylls från öppna källor (NHTSA vPIC och
/// OBDb); samma gränssnitt kan senare implementeras av en egen Cyberdrome-server.
/// </summary>
public interface IBilDatabas : IDisposable
{
    string Name { get; }
    List<CarDbHit> Search(string? text, int? year = null);
    (int Makes, int Models, int Vins, int Obdb) Stats();
    Task<VinInfo> DecodeVinAsync(string vin, CancellationToken ct = default);
    Task<int> RefreshMakesAsync(CancellationToken ct = default);
    Task<int> FetchModelsAsync(string make, int fromYear, int toYear, IProgress<string>? progress = null, CancellationToken ct = default);
    Task<int> RefreshObdbAsync(IProgress<string>? progress = null, CancellationToken ct = default);
    /// <summary>Modellnamn (och år när de finns) för ett märke från Wikidata. Returnerar antal nya rader.</summary>
    Task<int> FetchWikidataAsync(string make, bool force = false, CancellationToken ct = default);
    /// <summary>Tillverkarsignaler för en modell (null om OBDb saknar modellen).</summary>
    Task<ObdbSignalSet?> GetSignalsAsync(string make, string model, CancellationToken ct = default);
}

/// <summary>Lokal bildatabas: SQLite-fil + vPIC + OBDb. Fungerar utan internet för det som redan hämtats.</summary>
public sealed class LocalBilDatabas(CarModelDb db, VpicClient vpic, ObdbClient obdb, WikidataClient? wikidata = null) : IBilDatabas
{
    public string Name => "Lokal bildatabas (" + db.FilePath + ")";
    public CarModelDb Db => db;
    public ObdbClient Obdb => obdb;

    public static LocalBilDatabas OpenDefault() => new(new CarModelDb(CarModelDb.DefaultPath), new VpicClient(), new ObdbClient(), new WikidataClient());

    public List<CarDbHit> Search(string? text, int? year = null) => db.SearchGrouped(text, year);

    public (int Makes, int Models, int Vins, int Obdb) Stats()
    {
        var (m, mo, v) = db.Counts();
        return (m, mo, v, db.ObdbVehicles().Count);
    }

    public async Task<VinInfo> DecodeVinAsync(string vin, CancellationToken ct = default)
    {
        vin = vin.Trim().ToUpperInvariant();
        if (vin.Length != 17) throw new ArgumentException("Ett VIN har 17 tecken.");
        if (db.FindVin(vin) is { } cached) return cached;
        var v = await vpic.DecodeVinAsync(vin, ct: ct).ConfigureAwait(false);
        db.SaveVin(v);
        if (v.HasModel) db.UpsertModels([new CarModelEntry(v.Make, v.Model, v.Year!.Value, "VIN")]);
        return v;
    }

    public async Task<int> RefreshMakesAsync(CancellationToken ct = default)
    {
        var makes = await vpic.GetCarMakesAsync(ct).ConfigureAwait(false);
        db.UpsertMakes(makes);
        return makes.Count;
    }

    public async Task<int> FetchModelsAsync(string make, int fromYear, int toYear, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        int added = 0;
        for (int y = toYear; y >= fromYear; y--)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report($"vPIC: {make} {y} …");
            if (db.IsFetched(make, y)) continue;
            added += db.UpsertModels(await vpic.GetModelsAsync(make, y, ct).ConfigureAwait(false));
            db.MarkFetched(make, y);
            await Task.Delay(150, ct).ConfigureAwait(false); // snällt mot den öppna tjänsten
        }
        return added;
    }

    public async Task<int> FetchWikidataAsync(string make, bool force = false, CancellationToken ct = default)
    {
        make = VpicClient.Pretty(make.Trim());
        var key = "wikidata:" + make.ToLowerInvariant();
        if (!force && DateTime.TryParse(db.GetMeta(key), null, System.Globalization.DateTimeStyles.RoundtripKind, out var when) && DateTime.UtcNow - when < TimeSpan.FromDays(30))
            return 0; // hämtat nyligen – använd cachen
        var models = await (wikidata ?? new WikidataClient()).GetModelsAsync(make, ct).ConfigureAwait(false);
        var added = db.UpsertModels(models);
        db.SetMeta(key, DateTime.UtcNow.ToString("o"));
        return added;
    }

    public async Task<int> RefreshObdbAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var list = await obdb.ListVehiclesAsync(progress, ct).ConfigureAwait(false);
        if (list.Count > 0) db.ReplaceObdbVehicles(list);
        return list.Count;
    }

    public async Task<ObdbSignalSet?> GetSignalsAsync(string make, string model, CancellationToken ct = default)
    {
        var v = db.FindObdb(make, model);
        var set = v is null ? null : await obdb.GetSignalSetAsync(v.Repo, false, ct).ConfigureAwait(false);
        if (set is { SignalCount: > 0 } || model == ObdbClient.MakeWide) return set;
        // Modellens fil saknas eller är tom ännu: använd märkets gemensamma signaler (t.ex. arkivet "Volvo") om de finns.
        var wide = db.FindObdb(make, ObdbClient.MakeWide);
        if (wide is null) return set;
        var w = await obdb.GetSignalSetAsync(wide.Repo, false, ct).ConfigureAwait(false);
        return w.SignalCount > 0 ? w : set;
    }

    public void Dispose() => db.Dispose();
}

/// <summary>Gör en egen bilmodell av en träff i bildatabasen.</summary>
public static class CarProfileFactory
{
    /// <summary>
    /// Bygger en bilmodell: grund från en inbyggd modell av samma märke (annars Golf 8 med bara motorstyrdonet),
    /// märke/modell/år från databasen, och OBDb-signalerna (mode 21/22) som extra frågor som simulatorn svarar på.
    /// </summary>
    public static CarProfile Create(string make, string model, int year, ObdbSignalSet? signals, IReadOnlyList<CarProfile> builtIns,
        ProfileStore store, VinInfo? vin, List<string> notes, out CarProfile template)
    {
        var sameMake = builtIns.FirstOrDefault(b => CarModelDb.Key(b.Make, "") == CarModelDb.Key(make, ""));
        template = sameMake ?? builtIns.First(b => b.Id == "vw-golf8");
        var p = ProfileStore.Clone(template);
        p.Make = make; p.Model = model; p.Generation = ""; p.Year = Math.Clamp(year, 1996, 2100);
        if (year < 1996)
            notes.Add($"⚠ Årsmodell {year} är äldre än OBD-II (USA 1996, EU bensin 2001, EU diesel 2004) – bilen har troligen ingen OBD-II-port. Modellen sparas som 1996 och simulerar OBD-II ändå.");
        else if (year < 2004)
            notes.Add("⚠ OBD-II gäller USA från 1996, EU-bensinbilar från 2001 och EU-dieslar från 2004. En europeisk bil av den här årsmodellen kan sakna OBD-II.");
        p.StartDtcs = [];
        p.CustomPids = null; p.ExtraCommands = null; p.DtcDescriptions = null; p.Attribution = null;
        if (sameMake is null)
        {
            p.Features = [];
            p.Ecus = p.Ecus.Take(1).ToList();
            p.Ecus[0].Key = "motor"; p.Ecus[0].Name = "Motor"; p.Ecus[0].Coding = "00000000";
            p.SecurityAccess = null;
            notes.Add($"Ingen inbyggd {make} – grunden (skärm, motorstyrdon) kommer från {template.DisplayName}.");
        }
        if (vin is { Vin.Length: 17 }) p.Vin = vin.Vin[..11] + "000001"; // serienumret döljs
        else if (sameMake is null) p.Vin = PlaceholderVin(make, p.Year);
        p.Id = store.UniqueId(ProfileStore.SafeId($"{make}-{model}-{p.Year}"));

        if (signals is { SignalCount: 0 })
        {
            notes.Add($"OBDb har arkivet {signals.Repo} men det innehåller inga signaler ännu – bara standard-PID:ar.");
            signals = null;
            p.ExtraCommands = null;
            return p;
        }
        if (signals is not null && !signals.Repo.Contains('-'))
            notes.Add($"Modellen saknar egna signaler i OBDb – märkets gemensamma signaler ({signals.Repo}) används.");
        if (signals is not null) ApplySignals(p, signals, notes);
        else notes.Add("OBDb saknar modellen – bara standard-PID:ar.");
        return p;
    }

    /// <summary>
    /// Lägger till signaler i OBDb-format (mode 21/22) i en bilmodell: styrdon skapas per CAN-id, frågor med samma
    /// styrdon och fråga ersätts. Används både när en modell skapas och vid import av en OBDb-JSON-fil.
    /// </summary>
    public static int ApplySignals(CarProfile p, ObdbSignalSet signals, List<string> notes, string? attribution = ObdbClient.Attribution, string source = "OBDb")
    {
            int used = 0, skippedExt = 0, skippedOther = 0, skippedYear = 0;
            var commands = new List<ExtraCommand>();
            foreach (var c in signals.Commands)
            {
                if (!c.AppliesTo(p.Year)) { skippedYear++; continue; }
                if (c.ExtendedAddress is not null) { skippedExt++; continue; }
                if (c.Header.Length != 3 || !c.Header.All(Uri.IsHexDigit) || c.Header.Equals("7DF", StringComparison.OrdinalIgnoreCase)
                    || c.Service is not ("21" or "22") || c.Pid.Length % 2 != 0 || !c.Pid.All(Uri.IsHexDigit)) { skippedOther++; continue; }
                var hdr = c.Header.ToUpperInvariant();
                if (commands.Any(x => x.Header == hdr && x.Request == c.Request)) continue;
                var ecu = p.Ecus.FirstOrDefault(e => e.RequestId.Equals(hdr, StringComparison.OrdinalIgnoreCase));
                if (ecu is null)
                {
                    var resp = c.ResponseHeader is { Length: 3 } r && r.All(Uri.IsHexDigit) ? r.ToUpperInvariant() : (Convert.ToInt32(hdr, 16) + 8).ToString("X3");
                    p.Ecus.Add(ecu = new EcuProfile { Key = "obdb" + hdr.ToLowerInvariant(), Name = $"Styrdon {hdr} (OBDb)", RequestId = hdr, ResponseId = resp, Coding = "00000000", CodingDid = "0600" });
                }
                if (c.Service == "22" && (c.Pid == "F190" || c.Pid.Equals(ecu.CodingDid, StringComparison.OrdinalIgnoreCase))) { skippedOther++; continue; }
                commands.Add(new ExtraCommand
                {
                    Header = hdr, Request = c.Request.ToUpperInvariant(), Source = source,
                    Signals = c.Signals.Select(s => new ExtraSignal
                    {
                        Id = s.Id, Name = s.Name, Unit = s.Unit, Bix = s.Bix, Len = s.Len, Mul = s.Mul, Div = s.Div, Add = s.Add,
                        Min = s.Min, Max = s.Max, Signed = s.Signed, Value = DefaultValue(s),
                    }).ToList(),
                });
                used += c.Signals.Count;
            }
            // Behåll tidigare frågor (t.ex. från en annan fil) som inte ersätts.
            var kept = (p.ExtraCommands ?? []).Where(o => !commands.Any(n => n.Header == o.Header && n.Request == o.Request));
            p.ExtraCommands = kept.Concat(commands).ToList();
            if (attribution is not null) p.Attribution = attribution;
            notes.Add($"{used} tillverkarsignaler i {commands.Count} frågor från {source}.");
            if (skippedYear > 0) notes.Add($"{skippedYear} frågor gäller andra årsmodeller.");
            if (skippedExt > 0) notes.Add($"{skippedExt} frågor med utökad adressering hoppades över (simuleras inte).");
            if (skippedOther > 0) notes.Add($"{skippedOther} frågor av annan typ hoppades över.");
        return used;
    }

    private static readonly Dictionary<string, string> Wmi = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Saab"] = "YS3", ["Volvo"] = "YV1", ["Volkswagen"] = "WVW", ["Audi"] = "WAU", ["Škoda"] = "TMB", ["Skoda"] = "TMB", ["Seat"] = "VSS",
        ["BMW"] = "WBA", ["Mini"] = "WMW", ["Mercedes-Benz"] = "WDD", ["Toyota"] = "JTM", ["Ford"] = "WF0", ["Opel"] = "W0L", ["Tesla"] = "5YJ",
        ["Kia"] = "KNA", ["Hyundai"] = "KMH", ["Peugeot"] = "VF3", ["Renault"] = "VF1", ["Citroën"] = "VF7", ["Fiat"] = "ZFA", ["Porsche"] = "WP0",
        ["Nissan"] = "SJN", ["Honda"] = "JHM", ["Mazda"] = "JMZ", ["Subaru"] = "JF1", ["Polestar"] = "LPS",
    };

    /// <summary>
    /// Påhittat VIN med rätt tillverkarkod (WMI) och årsmodelltecken (position 10), så att appar ser rätt märke och år:
    /// t.ex. Saab 2008 → "YS3ZZZ00080000001". Serienumret är alltid 000001. Okänt märke → CDX….
    /// </summary>
    public static string PlaceholderVin(string make, int year)
    {
        const string codes = "ABCDEFGHJKLMNPRSTVWXY123456789"; // 1980/2010 = A
        if (!Wmi.TryGetValue(make, out var wmi)) return "CDX00000000000001";
        char y = codes[((year - 1980) % 30 + 30) % 30];
        return $"{wmi}ZZZ000{y}0000001";
    }

    /// <summary>Rimligt startvärde: mitt i intervallet (spänning 12,6 V, temperatur 20 °C om det ryms).</summary>
    public static double DefaultValue(ObdbSignal s)
    {
        double lo = Math.Min(s.Min, s.Max), hi = Math.Max(s.Min, s.Max);
        if (s.IsEnum) return Math.Max(lo, Math.Min(hi, 1));
        double v = s.Unit switch
        {
            "volts" when lo <= 12.6 && hi >= 12.6 => 12.6,
            "celsius" when lo <= 20 && hi >= 20 => 20,
            _ => Math.Round((lo + hi) / 2, 2),
        };
        return v;
    }
}
