// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

using System.IO.Compression;
using System.Text.Json;

namespace Cyberdrome.Core.Profiles;

/// <summary>Innehållet i en .cbs-fil: bilprofilen och valfria extrafiler (eget tema, skal, testfall).</summary>
public sealed class CarPackage
{
    public const string Extension = ".cbs";

    /// <summary>Äldre filändelse (läses fortfarande).</summary>
    public const string LegacyExtension = ".cyberbil";
    public const string ProfileEntry = "bilmodell.json";

    /// <summary>Sökväg till ODX-kopian i paketet.</summary>
    public static string OdxEntry(CarProfile p) => "odx/" + p.Id + ".odx-d";

    /// <summary>Är filen ett bilpaket (.cbs eller äldre .cyberbil)?</summary>
    public static bool IsPackage(string path) =>
        path.EndsWith(Extension, StringComparison.OrdinalIgnoreCase) || path.EndsWith(LegacyExtension, StringComparison.OrdinalIgnoreCase);

    public CarProfile Profile { get; set; } = new();

    /// <summary>Extrafiler: sökväg i paketet (t.ex. "tema/theme.json", "skal/skin.json", "testfall/x.json") → innehåll.</summary>
    public Dictionary<string, byte[]> Extras { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Sparar paketet som zip.</summary>
    public void Save(string path)
    {
        var errors = ProfileLoader.Validate(Profile);
        if (errors.Count > 0) throw new InvalidDataException(L.T("ProfileStore_TheCarModelIsNotValid") + string.Join(" ", errors));
        if (File.Exists(path)) File.Delete(path);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        using (var w = new StreamWriter(zip.CreateEntry(ProfileEntry).Open()))
            w.Write(JsonSerializer.Serialize(Profile, ProfileLoader.JsonOptions));
        // Samma bilmodell i standardformatet ASAM ODX 2.2, för andra diagnosverktyg.
        using (var w = new StreamWriter(zip.CreateEntry(OdxEntry(Profile)).Open(), new System.Text.UTF8Encoding(false)))
            w.Write(Odx.OdxExporter.ToOdxString(Profile));
        foreach (var (name, bytes) in Extras)
        {
            using var s = zip.CreateEntry(name.Replace('\\', '/')).Open();
            s.Write(bytes);
        }
    }

    /// <summary>Läser och kontrollerar ett paket. Kastar InvalidDataException med svenska fel.</summary>
    public static CarPackage Load(string path)
    {
        ZipArchive zip;
        try { zip = ZipFile.OpenRead(path); }
        catch (InvalidDataException) { throw new InvalidDataException(L.T("ProfileStore_TheFileIsNotACbs")); }
        using (zip)
        {
            var entry = zip.GetEntry(ProfileEntry) ?? throw new InvalidDataException(L.F("ProfileStore_TheFileIsMissing0", ProfileEntry));
            string json;
            using (var r = new StreamReader(entry.Open())) json = r.ReadToEnd();
            CarProfile profile;
            try { profile = ProfileLoader.Parse(json, ProfileEntry); }
            catch (JsonException ex) { throw new InvalidDataException(L.F("ProfileStore_0CannotBeRead1", ProfileEntry, ex.Message)); }
            var pkg = new CarPackage { Profile = profile };
            foreach (var e in zip.Entries)
            {
                if (e.FullName == ProfileEntry || e.FullName.EndsWith('/') || e.FullName.StartsWith("odx/", StringComparison.OrdinalIgnoreCase)) continue;
                if (e.FullName.Contains("..")) throw new InvalidDataException(L.T("ProfileStore_InvalidPathInThePackage"));
                if (e.Length > 20 * 1024 * 1024) throw new InvalidDataException(L.F("ProfileStore_0IsTooLarge", e.FullName));
                using var s = e.Open();
                using var ms = new MemoryStream();
                s.CopyTo(ms);
                pkg.Extras[e.FullName] = ms.ToArray();
            }
            return pkg;
        }
    }
}

/// <summary>Egna bilmodeller i %AppData%\Cyberdrome\bilar\. De inbyggda är skrivskyddade.</summary>
public sealed class ProfileStore
{
    public ProfileStore(string folder, IEnumerable<string> builtInIds)
    {
        Folder = folder;
        BuiltInIds = builtInIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public string Folder { get; }
    public IReadOnlySet<string> BuiltInIds { get; }

    public static string DefaultFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create), "Cyberdrome", "bilar");

    public bool IsBuiltIn(string id) => BuiltInIds.Contains(id);

    /// <summary>Egna bilmodeller (trasiga filer hoppas över).</summary>
    public List<CarProfile> LoadOwn()
    {
        var list = new List<CarProfile>();
        if (!Directory.Exists(Folder)) return list;
        foreach (var f in Directory.EnumerateFiles(Folder, "*.json").Order())
        {
            try
            {
                var p = ProfileLoader.LoadFile(f);
                if (IsBuiltIn(p.Id)) continue;
                p.IsOwn = true;
                p.FilePath = f;
                list.Add(p);
            }
            catch { }
        }
        return list;
    }

    /// <summary>Ett id som inte krockar med inbyggda eller egna modeller ("golf8-kopia", "golf8-kopia-2" …).</summary>
    public string UniqueId(string wanted, string? except = null)
    {
        var baseId = SafeId(wanted);
        var taken = LoadOwn().Where(p => !string.Equals(p.Id, except, StringComparison.OrdinalIgnoreCase))
            .Select(p => p.Id).Concat(BuiltInIds).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var id = baseId;
        for (int i = 2; taken.Contains(id); i++) id = $"{baseId}-{i}";
        return id;
    }

    public static string SafeId(string s)
    {
        // Svenska och vanliga europeiska bokstäver blir närmaste a–z (å/ä → a, ö → o …).
        var t = s.Trim().ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
        var plain = new string(t.Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray());
        var id = new string(plain.Replace("æ", "ae").Replace("ø", "o").Replace("ß", "ss").Select(c => char.IsLetterOrDigit(c) && c < 128 ? c : '-').ToArray());
        while (id.Contains("--")) id = id.Replace("--", "-");
        id = id.Trim('-');
        return id.Length == 0 ? "bilmodell" : id;
    }

    /// <summary>Sparar en egen modell. Inbyggda kan inte skrivas över.</summary>
    public CarProfile Save(CarProfile p)
    {
        if (IsBuiltIn(p.Id)) throw new InvalidOperationException(L.T("Cars_TheBuiltInCarModelsAre"));
        var errors = ProfileLoader.Validate(p);
        if (errors.Count > 0) throw new InvalidDataException(L.T("ProfileStore_TheCarModelIsNotValid") + string.Join(" ", errors));
        Directory.CreateDirectory(Folder);
        var path = Path.Combine(Folder, p.Id + ".json");
        File.WriteAllText(path, JsonSerializer.Serialize(p, ProfileLoader.JsonOptions));
        p.IsOwn = true;
        p.FilePath = path;
        return p;
    }

    public void Delete(CarProfile p)
    {
        if (IsBuiltIn(p.Id) || !p.IsOwn) throw new InvalidOperationException(L.T("Cars_OnlyYourOwnCarModelsCan"));
        var path = Path.Combine(Folder, p.Id + ".json");
        if (File.Exists(path)) File.Delete(path);
    }

    /// <summary>Djup kopia (via JSON).</summary>
    public static CarProfile Clone(CarProfile p) =>
        JsonSerializer.Deserialize<CarProfile>(JsonSerializer.Serialize(p, ProfileLoader.JsonOptions), ProfileLoader.JsonOptions)!;

    /// <summary>Kopia av en modell med nytt, unikt id och namn.</summary>
    public CarProfile Duplicate(CarProfile source)
    {
        var c = Clone(source);
        c.Model = source.Model + " (kopia)";
        c.Id = UniqueId(source.Id + "-kopia");
        return c;
    }

    /// <summary>Importerar en .cbs. Id byts om det krockar. Returnerar paketet (med extrafiler) och sparad profil.</summary>
    public CarPackage Import(string path)
    {
        var pkg = CarPackage.Load(path);
        var p = pkg.Profile;
        if (IsBuiltIn(p.Id) || LoadOwn().Any(o => string.Equals(o.Id, p.Id, StringComparison.OrdinalIgnoreCase)))
        {
            p.Id = UniqueId(p.Id);
            if (!p.Model.EndsWith("(importerad)", StringComparison.Ordinal)) p.Model += " (importerad)";
        }
        Save(p);
        return pkg;
    }
}
