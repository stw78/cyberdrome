// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

using Microsoft.Data.Sqlite;

namespace Cyberdrome.Core.CarDb;

/// <summary>En bilmodell i databasen: märke, modell och årsmodell.</summary>
public sealed record CarModelEntry(string Make, string Model, int Year, string Source);

/// <summary>Resultat av en VIN-avkodning.</summary>
public sealed record VinInfo(string Vin, string Make, string Model, int? Year, string Manufacturer, string BodyClass,
    string FuelType, string Engine, string PlantCountry, string ErrorText)
{
    public bool HasModel => Make.Length > 0 && Model.Length > 0 && Year is > 0;
}

/// <summary>
/// Lokal bilmodelldatabas (SQLite-fil). Programmet ansluter till filen och söker i den; den fylls från
/// NHTSA vPIC (öppen data från amerikanska trafiksäkerhetsverket) och från egna/inbyggda bilmodeller.
/// Fungerar utan internet när den väl är fylld.
/// </summary>
public sealed class CarModelDb : IDisposable
{
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Cyberdrome", "bildatabas.db");

    private readonly SqliteConnection _db;
    private readonly object _lock = new();

    public string FilePath { get; }

    public CarModelDb(string path)
    {
        FilePath = path;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        _db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        _db.Open();
        Exec("""
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS makes (
                name TEXT PRIMARY KEY COLLATE NOCASE,
                vpic_id INTEGER,
                updated TEXT);
            CREATE TABLE IF NOT EXISTS models (
                make TEXT NOT NULL COLLATE NOCASE,
                model TEXT NOT NULL COLLATE NOCASE,
                year INTEGER NOT NULL,
                source TEXT NOT NULL,
                PRIMARY KEY (make, model, year));
            CREATE INDEX IF NOT EXISTS ix_models_model ON models(model);
            CREATE TABLE IF NOT EXISTS fetched (
                make TEXT NOT NULL COLLATE NOCASE,
                year INTEGER NOT NULL,
                updated TEXT NOT NULL,
                PRIMARY KEY (make, year));
            CREATE TABLE IF NOT EXISTS vins (
                vin TEXT PRIMARY KEY,
                make TEXT, model TEXT, year INTEGER, manufacturer TEXT, body TEXT, fuel TEXT, engine TEXT, country TEXT, error TEXT,
                updated TEXT);
            CREATE TABLE IF NOT EXISTS obdb_vehicles (
                repo TEXT PRIMARY KEY,
                make TEXT NOT NULL,
                model TEXT NOT NULL,
                updated TEXT);
            CREATE TABLE IF NOT EXISTS meta (key TEXT PRIMARY KEY, value TEXT);
            """);
    }

    private void Exec(string sql, params (string, object?)[] args)
    {
        lock (_lock)
        {
            using var c = _db.CreateCommand();
            c.CommandText = sql;
            foreach (var (n, v) in args) c.Parameters.AddWithValue(n, v ?? DBNull.Value);
            c.ExecuteNonQuery();
        }
    }

    private List<T> Query<T>(string sql, Func<SqliteDataReader, T> map, params (string, object?)[] args)
    {
        lock (_lock)
        {
            using var c = _db.CreateCommand();
            c.CommandText = sql;
            foreach (var (n, v) in args) c.Parameters.AddWithValue(n, v ?? DBNull.Value);
            using var r = c.ExecuteReader();
            var list = new List<T>();
            while (r.Read()) list.Add(map(r));
            return list;
        }
    }

    private static string Now => DateTime.UtcNow.ToString("o");

    public void UpsertMakes(IEnumerable<(string Name, int? VpicId)> makes)
    {
        lock (_lock)
        {
            using var tx = _db.BeginTransaction();
            using var c = _db.CreateCommand();
            c.Transaction = tx;
            c.CommandText = "INSERT INTO makes(name, vpic_id, updated) VALUES($n, $id, $u) ON CONFLICT(name) DO UPDATE SET vpic_id = coalesce(excluded.vpic_id, vpic_id), updated = excluded.updated";
            var pn = c.Parameters.Add("$n", SqliteType.Text);
            var pid = c.Parameters.Add("$id", SqliteType.Integer);
            c.Parameters.AddWithValue("$u", Now);
            foreach (var (name, id) in makes)
            {
                if (string.IsNullOrWhiteSpace(name)) continue;
                pn.Value = Tidy(name);
                pid.Value = (object?)id ?? DBNull.Value;
                c.ExecuteNonQuery();
            }
            tx.Commit();
        }
    }

    /// <summary>Lägger till modeller. Returnerar hur många som var nya.</summary>
    public int UpsertModels(IEnumerable<CarModelEntry> models)
    {
        int added = 0;
        lock (_lock)
        {
            using var tx = _db.BeginTransaction();
            using var c = _db.CreateCommand();
            c.Transaction = tx;
            c.CommandText = "INSERT OR IGNORE INTO models(make, model, year, source) VALUES($m, $mo, $y, $s)";
            var pm = c.Parameters.Add("$m", SqliteType.Text);
            var pmo = c.Parameters.Add("$mo", SqliteType.Text);
            var py = c.Parameters.Add("$y", SqliteType.Integer);
            var ps = c.Parameters.Add("$s", SqliteType.Text);
            using var mk = _db.CreateCommand();
            mk.Transaction = tx;
            mk.CommandText = "INSERT OR IGNORE INTO makes(name, updated) VALUES($n, $u)";
            var mn = mk.Parameters.Add("$n", SqliteType.Text);
            mk.Parameters.AddWithValue("$u", Now);
            foreach (var e in models)
            {
                if (string.IsNullOrWhiteSpace(e.Make) || string.IsNullOrWhiteSpace(e.Model) || (e.Year != 0 && e.Year < 1900)) continue; // 0 = okänt år
                mn.Value = Tidy(e.Make); mk.ExecuteNonQuery();
                pm.Value = Tidy(e.Make); pmo.Value = Tidy(e.Model); py.Value = e.Year; ps.Value = e.Source;
                added += c.ExecuteNonQuery();
            }
            tx.Commit();
        }
        return added;
    }

    public void MarkFetched(string make, int year) =>
        Exec("INSERT INTO fetched(make, year, updated) VALUES($m, $y, $u) ON CONFLICT(make, year) DO UPDATE SET updated = excluded.updated",
            ("$m", Tidy(make)), ("$y", year), ("$u", Now));

    public bool IsFetched(string make, int year) =>
        Query("SELECT 1 FROM fetched WHERE make = $m AND year = $y", _ => 1, ("$m", Tidy(make)), ("$y", year)).Count > 0;

    public List<string> Makes(string? filter = null, int limit = 500) =>
        Query("SELECT name FROM makes WHERE $f IS NULL OR name LIKE $f ORDER BY name LIMIT $l", r => r.GetString(0),
            ("$f", string.IsNullOrWhiteSpace(filter) ? null : "%" + filter.Trim() + "%"), ("$l", limit));

    /// <summary>
    /// Söker modeller. Texten matchas mot "märke modell" (alla ord måste finnas), t.ex. "volvo xc" eller "golf".
    /// </summary>
    public List<CarModelEntry> Search(string? text, int? year = null, int limit = 300)
    {
        var words = (text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(5).ToArray();
        var where = new List<string>();
        var args = new List<(string, object?)>();
        for (int i = 0; i < words.Length; i++)
        {
            where.Add($"(make || ' ' || model) LIKE $w{i}");
            args.Add(($"$w{i}", "%" + words[i] + "%"));
        }
        if (year is not null) { where.Add("year = $y"); args.Add(("$y", year)); }
        args.Add(("$l", limit));
        var sql = "SELECT make, model, year, source FROM models" + (where.Count > 0 ? " WHERE " + string.Join(" AND ", where) : "") +
                  " ORDER BY make, model, year DESC LIMIT $l";
        return Query(sql, r => new CarModelEntry(r.GetString(0), r.GetString(1), r.GetInt32(2), r.GetString(3)), args.ToArray());
    }

    public (int Makes, int Models, int Vins) Counts()
    {
        int C(string t) => Query($"SELECT count(*) FROM {t}", r => r.GetInt32(0))[0];
        return (C("makes"), C("models"), C("vins"));
    }

    public void SaveVin(VinInfo v) =>
        Exec("""
            INSERT INTO vins(vin, make, model, year, manufacturer, body, fuel, engine, country, error, updated)
            VALUES($v, $m, $mo, $y, $ma, $b, $f, $e, $c, $er, $u)
            ON CONFLICT(vin) DO UPDATE SET make=excluded.make, model=excluded.model, year=excluded.year, manufacturer=excluded.manufacturer,
              body=excluded.body, fuel=excluded.fuel, engine=excluded.engine, country=excluded.country, error=excluded.error, updated=excluded.updated
            """, ("$v", v.Vin.ToUpperInvariant()), ("$m", v.Make), ("$mo", v.Model), ("$y", v.Year), ("$ma", v.Manufacturer), ("$b", v.BodyClass),
            ("$f", v.FuelType), ("$e", v.Engine), ("$c", v.PlantCountry), ("$er", v.ErrorText), ("$u", Now));

    public VinInfo? FindVin(string vin) =>
        Query("SELECT vin, make, model, year, manufacturer, body, fuel, engine, country, error FROM vins WHERE vin = $v",
            r => new VinInfo(r.GetString(0), S(r, 1), S(r, 2), r.IsDBNull(3) ? null : r.GetInt32(3), S(r, 4), S(r, 5), S(r, 6), S(r, 7), S(r, 8), S(r, 9)),
            ("$v", vin.ToUpperInvariant())).FirstOrDefault();

    private static string S(SqliteDataReader r, int i) => r.IsDBNull(i) ? "" : r.GetString(i);

    // ---------------- OBDb (fordon med tillverkarsignaler) ----------------

    public void ReplaceObdbVehicles(IEnumerable<ObdbVehicle> vehicles)
    {
        lock (_lock)
        {
            using var tx = _db.BeginTransaction();
            using (var del = _db.CreateCommand()) { del.Transaction = tx; del.CommandText = "DELETE FROM obdb_vehicles"; del.ExecuteNonQuery(); }
            using var c = _db.CreateCommand();
            c.Transaction = tx;
            c.CommandText = "INSERT OR REPLACE INTO obdb_vehicles(repo, make, model, updated) VALUES($r, $m, $mo, $u)";
            var pr = c.Parameters.Add("$r", SqliteType.Text);
            var pm = c.Parameters.Add("$m", SqliteType.Text);
            var pmo = c.Parameters.Add("$mo", SqliteType.Text);
            c.Parameters.AddWithValue("$u", Now);
            foreach (var v in vehicles) { pr.Value = v.Repo; pm.Value = v.Make; pmo.Value = v.Model; c.ExecuteNonQuery(); }
            tx.Commit();
        }
        SetMeta("obdb_updated", Now);
    }

    public List<ObdbVehicle> ObdbVehicles() =>
        Query("SELECT repo, make, model FROM obdb_vehicles ORDER BY make, model", r => new ObdbVehicle(r.GetString(0), r.GetString(1), r.GetString(2)));

    public ObdbVehicle? FindObdb(string make, string model)
    {
        var key = Key(make, model);
        return ObdbVehicles().FirstOrDefault(v => Key(v.Make, v.Model) == key);
    }

    public void SetMeta(string key, string value) =>
        Exec("INSERT INTO meta(key, value) VALUES($k, $v) ON CONFLICT(key) DO UPDATE SET value = excluded.value", ("$k", key), ("$v", value));

    public string? GetMeta(string key) => Query("SELECT value FROM meta WHERE key = $k", r => S(r, 0), ("$k", key)).FirstOrDefault();

    /// <summary>Jämförelsenyckel: "Tesla Model-3" och "TESLA Model 3" blir samma.</summary>
    public static string Key(string make, string model) =>
        new string((make + model).ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray())
            .Replace("ë", "e").Replace("é", "e").Replace("š", "s");

    /// <summary>
    /// Söker och slår ihop källorna: en rad per märke+modell med årsmodeller (vPIC/VIN/egna) och om OBDb har signaler.
    /// </summary>
    public List<CarDbHit> SearchGrouped(string? text, int? year = null, int limit = 300)
    {
        var words = (text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(5).ToArray();
        var hits = new Dictionary<string, CarDbHit>();
        foreach (var g in Search(text, year, 5000).GroupBy(m => Key(m.Make, m.Model)))
        {
            var first = g.First();
            var years = g.Where(x => x.Year > 0).Select(x => x.Year).ToList();
            hits[g.Key] = new CarDbHit(first.Make, first.Model, years.Count > 0 ? years.Min() : null, years.Count > 0 ? years.Max() : null,
                g.Select(x => x.Source).Distinct().OrderBy(s => s).ToList(), null);
        }
        foreach (var v in ObdbVehicles())
        {
            var full = v.Make + " " + v.Model;
            if (!words.All(w => full.Contains(w, StringComparison.OrdinalIgnoreCase) || v.Repo.Contains(w, StringComparison.OrdinalIgnoreCase))) continue;
            var k = Key(v.Make, v.Model);
            if (hits.TryGetValue(k, out var h)) hits[k] = h with { Obdb = v, Sources = [.. h.Sources, "OBDb"] };
            else if (year is null) hits[k] = new CarDbHit(v.Make, v.Model, null, null, ["OBDb"], v);
        }
        return hits.Values.OrderBy(h => h.Make).ThenBy(h => h.Model).Take(limit).ToList();
    }

    private static string Tidy(string s) => string.Join(' ', s.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    public void Dispose()
    {
        lock (_lock) _db.Dispose();
    }
}

/// <summary>Ett fordon i OBDb (ett GitHub-arkiv per märke-modell, t.ex. "Toyota-RAV4").</summary>
public sealed record ObdbVehicle(string Repo, string Make, string Model);

/// <summary>En sökträff: märke, modell, årsmodeller och källor.</summary>
public sealed record CarDbHit(string Make, string Model, int? FromYear, int? ToYear, List<string> Sources, ObdbVehicle? Obdb)
{
    public string Years => FromYear is null ? "–" : FromYear == ToYear ? $"{FromYear}" : $"{FromYear}–{ToYear}";
    public string SourceText => string.Join(", ", Sources);
    public string Label => $"{Make} {Model}   ·   {Years}   ·   {SourceText}{(Obdb is not null ? L.T("CarDb_ManufacturerSignalsStar") : "")}";
}
