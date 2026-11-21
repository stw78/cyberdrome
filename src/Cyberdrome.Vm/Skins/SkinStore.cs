// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

using System.IO.Compression;

namespace Cyberdrome.Vm.Skins;

/// <summary>Egna skal i %AppData%\Cyberdrome\skal\&lt;id&gt;\ (skin.json + bild).</summary>
public sealed class SkinStore
{
    public SkinStore(string folder) => Folder = folder;

    public string Folder { get; }

    public static string DefaultFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create), "Cyberdrome", "skal");

    /// <summary>Läser alla giltiga egna skal (trasiga hoppas över).</summary>
    public IReadOnlyList<Skin> LoadAll()
    {
        if (!Directory.Exists(Folder)) return [];
        var list = new List<Skin>();
        foreach (var dir in Directory.GetDirectories(Folder))
        {
            try
            {
                var s = Load(dir);
                if (s.Validate().Count == 0) list.Add(s);
            }
            catch { }
        }
        return list.OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public static Skin Load(string dir)
    {
        var json = Path.Combine(dir, "skin.json");
        if (!File.Exists(json)) throw new FileNotFoundException(L.T("SkinStore_SkinJsonIsMissing"), json);
        var s = Skin.FromJson(File.ReadAllText(json));
        s.Folder = dir;
        s.Style = null; // egna skal ritas alltid från sin bild
        return s;
    }

    /// <summary>
    /// Importerar ett skal från en mapp eller zip-fil som innehåller skin.json och bilden.
    /// Kastar InvalidDataException med svenska felmeddelanden om skalet inte är giltigt.
    /// </summary>
    public Skin Import(string source)
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "cyberdrome-skal-" + Guid.NewGuid().ToString("N"));
        try
        {
            string dir;
            if (File.Exists(source) && source.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                ZipFile.ExtractToDirectory(source, tempDir);
                // skin.json kan ligga i roten eller i en undermapp
                var json = Directory.GetFiles(tempDir, "skin.json", SearchOption.AllDirectories).FirstOrDefault()
                           ?? throw new InvalidDataException(L.T("SkinStore_TheZipFileContainsNoSkin"));
                dir = Path.GetDirectoryName(json)!;
            }
            else if (Directory.Exists(source)) dir = source;
            else throw new InvalidDataException(L.T("SkinStore_ChooseAFolderOrAZip"));

            var skin = Load(dir);
            var errors = skin.Validate();
            if (errors.Count > 0) throw new InvalidDataException(L.T("SkinStore_TheSkinIsNotValid") + string.Join(" ", errors));

            var safeId = SafeId(skin.Id);
            if (BuiltInSkins.Find(safeId) is not null || safeId is BuiltInSkins.None or BuiltInSkins.CarDefault)
                safeId = "eget-" + safeId;
            var target = Path.Combine(Folder, safeId);
            if (Directory.Exists(target)) Directory.Delete(target, true);
            CopyDirectory(dir, target);

            var saved = Load(target);
            saved.Id = safeId;
            File.WriteAllText(Path.Combine(target, "skin.json"), saved.ToJson());
            return Load(target);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    /// <summary>Sparar ett skal som skapats från en enskild bild (skärmytan hittades automatiskt).</summary>
    public Skin SaveFromImage(string imagePath, Skin skin)
    {
        skin.Id = SafeId(string.IsNullOrWhiteSpace(skin.Id) ? Path.GetFileNameWithoutExtension(imagePath) : skin.Id);
        if (BuiltInSkins.Find(skin.Id) is not null) skin.Id = "eget-" + skin.Id;
        var target = Path.Combine(Folder, skin.Id);
        Directory.CreateDirectory(target);
        var imgName = "skal" + Path.GetExtension(imagePath).ToLowerInvariant();
        File.Copy(imagePath, Path.Combine(target, imgName), overwrite: true);
        skin.Image = imgName;
        skin.Style = null;
        File.WriteAllText(Path.Combine(target, "skin.json"), skin.ToJson());
        var loaded = Load(target);
        var errors = loaded.Validate();
        if (errors.Count > 0)
        {
            Directory.Delete(target, true);
            throw new InvalidDataException(L.T("SkinStore_TheSkinIsNotValid") + string.Join(" ", errors));
        }
        return loaded;
    }

    public void Delete(string id)
    {
        var dir = Path.Combine(Folder, SafeId(id));
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
    }

    public static string SafeId(string id)
    {
        var s = new string(id.Trim().ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-').ToArray()).Trim('-');
        return string.IsNullOrEmpty(s) ? "skal" : s;
    }

    private void CopyDirectory(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var f in Directory.GetFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)), true);
        foreach (var d in Directory.GetDirectories(from))
        {
            // Kopiera aldrig in skal-mappen i sig själv (om källmappen ligger ovanför den).
            var full = Path.GetFullPath(d).TrimEnd(Path.DirectorySeparatorChar);
            if (full.Equals(Path.GetFullPath(Folder).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)) continue;
            if (full.Equals(Path.GetFullPath(to).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)) continue;
            CopyDirectory(d, Path.Combine(to, Path.GetFileName(d)));
        }
    }

    /// <summary>
    /// Hittar skärmytan i en skalbild: den största rektangeln av genomskinliga pixlar (alfa &lt; 32)
    /// kring bildens mitt. <paramref name="alpha"/> är alfakanalen rad för rad.
    /// </summary>
    public static SkinRect? DetectScreen(byte[] alpha, int width, int height, byte threshold = 32)
    {
        bool T(int x, int y) => alpha[y * width + x] < threshold;
        // Utgå från närmaste genomskinliga pixel kring mitten.
        int cx = width / 2, cy = height / 2;
        if (!T(cx, cy))
        {
            bool found = false;
            for (int r = 1; r < Math.Min(width, height) / 2 && !found; r++)
                for (int dy = -r; dy <= r && !found; dy += r)
                    for (int dx = -r; dx <= r && !found; dx++)
                        if (cx + dx >= 0 && cx + dx < width && cy + dy >= 0 && cy + dy < height && T(cx + dx, cy + dy))
                        { cx += dx; cy += dy; found = true; }
            if (!found) return null;
        }
        int left = cx, right = cx, top = cy, bottom = cy;
        while (left > 0 && T(left - 1, cy)) left--;
        while (right < width - 1 && T(right + 1, cy)) right++;
        while (top > 0 && T(cx, top - 1)) top--;
        while (bottom < height - 1 && T(cx, bottom + 1)) bottom++;
        var rect = new SkinRect(left, top, right - left + 1, bottom - top + 1);
        return rect.W >= 100 && rect.H >= 100 ? rect : null;
    }
}
