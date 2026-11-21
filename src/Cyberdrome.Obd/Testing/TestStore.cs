// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

namespace Cyberdrome.Obd.Testing;

/// <summary>Egna testfall: JSON-filer i %AppData%\Cyberdrome\testfall\.</summary>
public sealed class TestStore
{
    public TestStore(string folder) => Folder = folder;
    public string Folder { get; }

    public static string DefaultFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Cyberdrome", "testfall");

    /// <summary>Inbyggda + egna testfall (trasiga egna hoppas över).</summary>
    public IReadOnlyList<TestCase> LoadAll()
    {
        var list = BuiltInTestCases.All.ToList();
        if (Directory.Exists(Folder))
            foreach (var f in Directory.GetFiles(Folder, "*.json").OrderBy(f => f))
            {
                try
                {
                    var t = TestCase.FromJson(File.ReadAllText(f));
                    t.FilePath = f;
                    if (t.Validate().Count == 0 && list.All(x => x.Id != t.Id)) list.Add(t);
                }
                catch { }
            }
        return list;
    }

    /// <summary>Importerar ett testfall (JSON). Kastar InvalidDataException med svenska fel.</summary>
    public TestCase Import(string path)
    {
        TestCase t;
        try { t = TestCase.FromJson(File.ReadAllText(path)); }
        catch (System.Text.Json.JsonException ex) { throw new InvalidDataException(L.T("TestStore_TheFileCannotBeRead") + ex.Message); }
        var errors = t.Validate();
        if (errors.Count > 0) throw new InvalidDataException(L.T("TestStore_TheTestCaseIsNotValid") + string.Join(" ", errors));
        if (BuiltInTestCases.All.Any(b => b.Id == t.Id)) t.Id = "eget-" + t.Id;
        Directory.CreateDirectory(Folder);
        var target = Path.Combine(Folder, string.Concat(t.Id.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-')) + ".json");
        File.WriteAllText(target, t.ToJson());
        t.FilePath = target;
        return t;
    }

    /// <summary>Exporterar (t.ex. ett inbyggt testfall som mall att utgå från).</summary>
    public static void Export(TestCase t, string path) => File.WriteAllText(path, t.ToJson());
}
