// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

using System.Globalization;
using System.Text.RegularExpressions;

namespace Cyberdrome.Tests;

/// <summary>Språkfilerna: samma nycklar på alla språk, giltiga format, inga hårdkodade texter i gränssnittet.</summary>
public class LocalizationTests
{
    private static string Root
    {
        get
        {
            var d = new DirectoryInfo(AppContext.BaseDirectory);
            while (d is not null && !File.Exists(Path.Combine(d.FullName, "Cyberdrome.sln"))) d = d.Parent;
            return d!.FullName;
        }
    }

    [Fact]
    public void EveryKeyExistsInEveryLanguage()
    {
        var en = L.All("en");
        Assert.True(en.Count > 800, $"bara {en.Count} nycklar");
        foreach (var lang in L.Languages.Keys.Where(l => l != "en"))
        {
            var other = L.All(lang);
            Assert.Empty(en.Keys.Except(other.Keys));
            Assert.Empty(other.Keys.Except(en.Keys));
            Assert.All(other, kv => Assert.False(string.IsNullOrWhiteSpace(kv.Value), kv.Key));
        }
    }

    [Fact]
    public void PlaceholdersMatchBetweenLanguages()
    {
        static string Holes(string s) => string.Join(",", Regex.Matches(s, @"\{(\d+)[^}]*\}").Select(m => m.Groups[1].Value).Distinct().OrderBy(x => x));
        var en = L.All("en");
        var sv = L.All("sv");
        foreach (var (key, text) in en)
            Assert.True(Holes(text) == Holes(sv[key]), $"{key}: en '{text}' / sv '{sv[key]}'");
        // Alla format måste gå att formatera med lika många värden som de har platser.
        foreach (var (key, text) in en.Concat(sv))
        {
            var n = Regex.Matches(text, @"\{(\d+)").Select(m => int.Parse(m.Groups[1].Value)).DefaultIfEmpty(-1).Max() + 1;
            var ex = Record.Exception(() => string.Format(CultureInfo.InvariantCulture, text, Enumerable.Repeat<object?>(new AnyFormat(), n).ToArray()));
            Assert.True(ex is null, $"{key}: {ex?.Message}");
        }
    }

    [Fact]
    public void SwitchingLanguageChangesTextsAndNumberFormat()
    {
        try
        {
            L.SetLanguage("en");
            Assert.Equal("Settings", L.T("Nav_Settings"));
            Assert.Equal("Speed (01 0D) at least every other second", L.T("TestCase_Speed010DAtLeastEvery"));
            Assert.Equal("Driving the route: 1.5 km. The car moves at the speed on Overview.", L.F("Map_DrivingTheRoute000", 1.5));
            L.SetLanguage("sv");
            Assert.Equal("Inställningar", L.T("Nav_Settings"));
            Assert.StartsWith("Kör rutten: 1,5 km.", L.F("Map_DrivingTheRoute000", 1.5));
            Assert.Equal("Systemets språk", L.T("Settings_SystemLanguage"));
        }
        finally { L.SetLanguage("sv"); }
    }

    [Fact]
    public void MissingKeyShowsTheKey() => Assert.Equal("Finns_Inte", L.T("Finns_Inte"));

    [Fact]
    public void NoHardCodedTextsInTheUi()
    {
        var xaml = File.ReadAllText(Path.Combine(Root, "src", "Cyberdrome.App", "Views", "MainWindow.axaml"));
        // Text/Content/Watermark/Header/ToolTip/Title med bokstäver ska vara översatta ({l:T …}) eller bindningar.
        var allowed = new HashSet<string> { "Cyberdrome Simulator", "Cyberdrome Cockpit", "OBD", "Android", "Esc / F11", "km/h", "rpm", "DID", "dpi", "KEYCODE_MEDIA_PREVIOUS", "KEYCODE_MEDIA_NEXT", "KEYCODE_VOLUME_MUTE" };
        var hard = Regex.Matches(xaml, @"\b(Text|Content|OnContent|OffContent|Watermark|ToolTip\.Tip|Header|Title)=""([^""{][^""]*)""")
            .Select(m => m.Groups[2].Value).Where(v => Regex.IsMatch(v, @"[A-Za-zÅÄÖåäö]{2,}") && !allowed.Contains(v)).ToList();
        Assert.Empty(hard);
        Assert.DoesNotMatch(@"StringFormat='[^']*[A-Za-zÅÄÖåäö]{3,}", xaml);
    }

    [Fact]
    public void NoSwedishLiteralsLeftInCode()
    {
        // Svenska bokstäver i strängar i koden (utom kommentarer och några namn/data) tyder på en oöversatt text.
        var ok = new[] { "Jouko Broström", "Citroën", "Škoda", "æ", "ø", "ë", "Stadskörning", "Motorväg", "Kallstart", "Filerna i den här mappen", "Ändrade versioner av datat" };
        var bad = new List<string>();
        foreach (var file in Directory.GetFiles(Path.Combine(Root, "src"), "*.cs", SearchOption.AllDirectories)
                     .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")))
            foreach (var line in File.ReadLines(file))
            {
                var code = line.TrimStart();
                if (code.StartsWith("//")) continue;
                foreach (Match m in Regex.Matches(code, @"""[^""]*[åäöÅÄÖ][^""]*"""))
                    if (!ok.Any(o => m.Value.Contains(o))) bad.Add($"{Path.GetFileName(file)}: {m.Value}");
            }
        Assert.Empty(bad);
    }

    /// <summary>Värde som accepterar alla formatsträngar (X3, 0.0, HH:mm:ss …).</summary>
    private sealed class AnyFormat : IFormattable { public string ToString(string? format, IFormatProvider? p) => "x"; }
}
