// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

namespace Cyberdrome.Core.Drives;

/// <summary>Sparade körningar som JSON-filer i en mapp (standard %AppData%\Cyberdrome\korningar).</summary>
public sealed class DriveStore
{
    public DriveStore(string folder) => Folder = folder;

    public string Folder { get; }

    public static string DefaultFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create), "Cyberdrome", "korningar");

    /// <summary>Gör ett namn till ett giltigt filnamn (behåller å, ä, ö).</summary>
    public static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var s = new string(name.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return string.IsNullOrWhiteSpace(s) ? L.T("DriveStore_Drive") : s;
    }

    public string PathFor(string name) => Path.Combine(Folder, SafeFileName(name) + ".json");

    public IReadOnlyList<Drive> LoadAll()
    {
        if (!Directory.Exists(Folder)) return [];
        var list = new List<Drive>();
        foreach (var f in Directory.GetFiles(Folder, "*.json"))
        {
            try { list.Add(Drive.FromJson(File.ReadAllText(f))); }
            catch { /* trasig fil hoppas över */ }
        }
        return list.OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public void Save(Drive d)
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(PathFor(d.Name), d.ToJson());
    }

    public bool Exists(string name) => File.Exists(PathFor(name));

    public void Delete(string name)
    {
        var p = PathFor(name);
        if (File.Exists(p)) File.Delete(p);
    }

    /// <summary>Byter namn. Falskt om det nya namnet redan finns.</summary>
    public bool Rename(Drive d, string newName)
    {
        newName = newName.Trim();
        if (newName.Length == 0 || newName == d.Name) return false;
        if (Exists(newName)) return false;
        var old = d.Name;
        d.Name = newName;
        Save(d);
        Delete(old);
        return true;
    }

    public void Export(Drive d, string targetPath) => File.WriteAllText(targetPath, d.ToJson());

    /// <summary>Läser in en körning från en fil. Får ett nytt namn om namnet redan finns.</summary>
    public Drive Import(string sourcePath)
    {
        var d = Drive.FromJson(File.ReadAllText(sourcePath));
        if (string.IsNullOrWhiteSpace(d.Name)) d.Name = Path.GetFileNameWithoutExtension(sourcePath);
        var baseName = d.Name;
        for (int i = 2; Exists(d.Name); i++) d.Name = $"{baseName} ({i})";
        Save(d);
        return d;
    }

    /// <summary>Lägger in (och uppdaterar) de inbyggda exemplen. De genereras alltid likadant.</summary>
    public void EnsureExamples()
    {
        foreach (var d in ExampleDrives.All) Save(d);
    }
}
