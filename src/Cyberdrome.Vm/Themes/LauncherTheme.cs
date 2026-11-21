// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Cyberdrome.Vm.Themes;

/// <summary>Färger i ett tema (hex, t.ex. "#0B0F17").</summary>
public sealed class ThemeColors
{
    public string Background { get; set; } = "#0E1116";
    public string Surface { get; set; } = "#1B2230";
    public string Accent { get; set; } = "#3BA7FF";
    public string Text { get; set; } = "#FFFFFF";
    public string Muted { get; set; } = "#94A3B8";
}

/// <summary>
/// Ett tema för Cyberdrome Launcher (startskärmen i Android). Samma format som theme.json.
/// Inbyggda teman (ett per bilmodell) finns i launcher-appen; egna teman skickas som JSON.
/// </summary>
public sealed partial class LauncherTheme
{
    public static readonly string[] Layouts = ["tiles", "leftmenu", "portrait", "split", "tabs"];
    public static readonly string[] Fonts = ["sans", "light", "condensed", "medium"];
    public static readonly string[] Wallpapers = ["gradient", "lines", "dots", "plain"];

    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Layout { get; set; } = "tiles";
    public ThemeColors Colors { get; set; } = new();
    public int CornerRadius { get; set; } = 14;
    public string Font { get; set; } = "sans";
    public string Wallpaper { get; set; } = "gradient";
    /// <summary>Flikfärger för layouten "tabs" (6 st).</summary>
    public List<string>? TabColors { get; set; }

    /// <summary>Skärmförval som passar temat (valfritt).</summary>
    public string? ScreenPreset { get; set; }

    [JsonIgnore] public string? Folder { get; set; }

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public static LauncherTheme FromJson(string json) =>
        JsonSerializer.Deserialize<LauncherTheme>(json, Json) ?? throw new InvalidDataException(L.T("LauncherTheme_EmptyThemeJson"));

    [GeneratedRegex("^#?([0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})$")]
    private static partial Regex HexColor();

    public static bool IsColor(string? s) => s is not null && HexColor().IsMatch(s);

    /// <summary>Kontrollerar temat. Tom lista = OK.</summary>
    public IReadOnlyList<string> Validate()
    {
        var e = new List<string>();
        if (string.IsNullOrWhiteSpace(Id)) e.Add(L.T("TestCase_IdIsMissing"));
        if (string.IsNullOrWhiteSpace(Name)) e.Add(L.T("TestCase_NameIsMissing"));
        if (!Layouts.Contains(Layout)) e.Add(L.F("LauncherTheme_LayoutMustBeOneOf0", string.Join(", ", Layouts)));
        if (!Fonts.Contains(Font)) e.Add(L.F("LauncherTheme_FontMustBeOneOf0", string.Join(", ", Fonts)));
        if (!Wallpapers.Contains(Wallpaper)) e.Add(L.F("LauncherTheme_WallpaperMustBeOneOf0", string.Join(", ", Wallpapers)));
        foreach (var (n, c) in new[] { ("background", Colors.Background), ("surface", Colors.Surface), ("accent", Colors.Accent), ("text", Colors.Text), ("muted", Colors.Muted) })
            if (!IsColor(c)) e.Add(L.F("LauncherTheme_Colors0IsNotAColour", n));
        if (CornerRadius is < 0 or > 60) e.Add(L.T("LauncherTheme_CornerRadiusMustBe060"));
        if (TabColors is not null && (TabColors.Count != 6 || TabColors.Any(c => !IsColor(c)))) e.Add(L.T("LauncherTheme_TabColorsMustBe6Colours"));
        if (ScreenPreset is not null && ScreenPresets.All.All(p => p.Id != ScreenPreset)) e.Add(L.F("Skin_UnknownScreenPreset0", ScreenPreset));
        return e;
    }

    /// <summary>Temat som base64 (skickas till launchern med am broadcast --es json64).</summary>
    public string ToBase64() => Convert.ToBase64String(Encoding.UTF8.GetBytes(ToJson()));
}

/// <summary>Inbyggda teman (finns i launcher-appen; samma id som bilprofilerna).</summary>
public static class BuiltInThemes
{
    public const string CarDefault = "auto";
    public const string Off = "off";

    public static readonly IReadOnlyList<(string Id, string Name, string ScreenPreset, ScreenOrientation Orientation)> All =
    [
        ("vw-golf8", L.T("LauncherTheme_Golf8StyleGridBlueAccent"), "10in-1560x700", ScreenOrientation.Landscape),
        ("skoda-octavia4", L.T("LauncherTheme_Octavia4StyleGridGreenAccent"), "10in-1280x800", ScreenOrientation.Landscape),
        ("bmw-g20", L.T("LauncherTheme_3SeriesG20StyleMenuOn"), "12in-1920x720", ScreenOrientation.Landscape),
        ("volvo-xc60", L.T("LauncherTheme_XC60StylePortraitStackedTiles"), "9in-1024x768", ScreenOrientation.Portrait),
        ("toyota-rav4", L.T("LauncherTheme_RAV4StyleSplitHomeScreen"), "8in-800x480", ScreenOrientation.Landscape),
        ("ford-focus4", L.T("LauncherTheme_FocusMk4StyleThreePanelsTab"), "8in-800x480", ScreenOrientation.Landscape),
    ];

    public static bool IsBuiltIn(string id) => All.Any(t => t.Id == id);

    /// <summary>"auto" → bilmodellens eget tema (om det finns), annars valt tema.</summary>
    public static string Resolve(string choice, string profileId) =>
        choice == CarDefault ? (IsBuiltIn(profileId) ? profileId : Off) : choice;
}

/// <summary>Egna teman i %AppData%\Cyberdrome\teman\&lt;id&gt;\theme.json.</summary>
public sealed class ThemeStore
{
    public ThemeStore(string folder) => Folder = folder;
    public string Folder { get; }

    public static string DefaultFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create), "Cyberdrome", "teman");

    public IReadOnlyList<LauncherTheme> LoadAll()
    {
        if (!Directory.Exists(Folder)) return [];
        var list = new List<LauncherTheme>();
        foreach (var dir in Directory.GetDirectories(Folder))
        {
            try
            {
                var t = LauncherTheme.FromJson(File.ReadAllText(Path.Combine(dir, "theme.json")));
                t.Folder = dir;
                if (t.Validate().Count == 0) list.Add(t);
            }
            catch { }
        }
        return list.OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>Importerar ett tema från en zip-fil, en mapp eller en theme.json.</summary>
    public LauncherTheme Import(string source)
    {
        string json;
        if (File.Exists(source) && source.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            using var zip = ZipFile.OpenRead(source);
            var entry = zip.Entries.FirstOrDefault(e => e.Name.Equals("theme.json", StringComparison.OrdinalIgnoreCase))
                        ?? throw new InvalidDataException(L.T("LauncherTheme_TheZipFileContainsNoTheme"));
            using var r = new StreamReader(entry.Open());
            json = r.ReadToEnd();
        }
        else if (Directory.Exists(source))
        {
            var f = Path.Combine(source, "theme.json");
            if (!File.Exists(f)) throw new InvalidDataException(L.T("LauncherTheme_TheFolderContainsNoThemeJson"));
            json = File.ReadAllText(f);
        }
        else if (File.Exists(source)) json = File.ReadAllText(source);
        else throw new InvalidDataException(L.T("LauncherTheme_ChooseAZipFileAFolder"));

        LauncherTheme t;
        try { t = LauncherTheme.FromJson(json); }
        catch (JsonException ex) { throw new InvalidDataException(L.T("LauncherTheme_ThemeJsonCannotBeRead") + ex.Message); }
        var errors = t.Validate();
        if (errors.Count > 0) throw new InvalidDataException(L.T("LauncherTheme_TheThemeIsNotValid") + string.Join(" ", errors));

        var id = Skins.SkinStore.SafeId(t.Id);
        if (BuiltInThemes.IsBuiltIn(id) || id is BuiltInThemes.CarDefault or BuiltInThemes.Off) id = "eget-" + id;
        t.Id = id;
        var dir = Path.Combine(Folder, id);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "theme.json"), t.ToJson());
        t.Folder = dir;
        return t;
    }

    public void Delete(string id)
    {
        var dir = Path.Combine(Folder, Skins.SkinStore.SafeId(id));
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
    }
}
