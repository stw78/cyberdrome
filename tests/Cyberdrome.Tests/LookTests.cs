// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

using System.IO.Compression;
using System.Text;
using Cyberdrome.Vm;
using Cyberdrome.Vm.Skins;
using Cyberdrome.Vm.Themes;

namespace Cyberdrome.Tests;

/// <summary>Teman (startskärmen i Android) och skal (ramen runt skärmen).</summary>
public class LookTests
{
    private static string TempDir() => Path.Combine(Path.GetTempPath(), "cd-look-" + Guid.NewGuid().ToString("N"));

    private const string GoodTheme = """
        { "id": "Mitt Tema", "name": "Mitt tema", "layout": "portrait",
          "colors": { "background": "#101010", "surface": "#202020", "accent": "#FF8800", "text": "#FFFFFF", "muted": "#999999" },
          "cornerRadius": 8, "font": "light", "wallpaper": "dots", "screenPreset": "9in-1024x768" }
        """;

    [Fact]
    public void ThemeJsonParsesAndValidates()
    {
        var t = LauncherTheme.FromJson(GoodTheme);
        Assert.Equal("portrait", t.Layout);
        Assert.Equal("#FF8800", t.Colors.Accent);
        Assert.Empty(t.Validate());
    }

    [Theory]
    [InlineData("\"layout\": \"portrait\"", "\"layout\": \"diagonal\"", "layout")]
    [InlineData("\"accent\": \"#FF8800\"", "\"accent\": \"orange\"", "colors.accent")]
    [InlineData("\"font\": \"light\"", "\"font\": \"MärkesFont\"", "font")]
    [InlineData("\"screenPreset\": \"9in-1024x768\"", "\"screenPreset\": \"x\"", "screenPreset")]
    public void InvalidThemeIsRejected(string from, string to, string expectedWord)
    {
        var t = LauncherTheme.FromJson(GoodTheme.Replace(from, to));
        Assert.Contains(t.Validate(), e => e.Contains(expectedWord));
    }

    [Fact]
    public void ThemeBroadcastUsesBase64WithoutSpecialChars()
    {
        var t = LauncherTheme.FromJson(GoodTheme);
        var args = CyberdromeLauncher.BroadcastCustom(t);
        var b64 = args[^1];
        Assert.Equal("json64", args[^2]);
        Assert.DoesNotContain(b64, c => c is ' ' or '"' or '\'' or '{');
        var back = LauncherTheme.FromJson(Encoding.UTF8.GetString(Convert.FromBase64String(b64)));
        Assert.Equal(t.Colors.Background, back.Colors.Background);
        Assert.Equal(["am", "broadcast", "--user", "current", "-n", "se.cyberdrome.launcher/.ThemeReceiver", "-a", "se.cyberdrome.launcher.SET_THEME", "--es", "id", "bmw-g20"],
            CyberdromeLauncher.BroadcastBuiltIn("bmw-g20"));
    }

    [Fact]
    public void ThemeImportFromZipGetsSafeIdAndAvoidsBuiltInNames()
    {
        var dir = TempDir();
        try
        {
            Directory.CreateDirectory(dir);
            var zip = Path.Combine(dir, "tema.zip");
            using (var z = ZipFile.Open(zip, ZipArchiveMode.Create))
            using (var w = new StreamWriter(z.CreateEntry("undermapp/theme.json").Open())) w.Write(GoodTheme);
            var store = new ThemeStore(Path.Combine(dir, "teman"));
            var t = store.Import(zip);
            Assert.Equal("mitt-tema", t.Id);
            Assert.Single(store.LoadAll());

            var json = Path.Combine(dir, "theme.json");
            File.WriteAllText(json, GoodTheme.Replace("\"Mitt Tema\"", "\"vw-golf8\""));
            Assert.Equal("eget-vw-golf8", store.Import(json).Id); // inbyggda namn skrivs inte över

            File.WriteAllText(json, GoodTheme.Replace("\"portrait\"", "\"x\""));
            Assert.Throws<InvalidDataException>(() => store.Import(json));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void EveryCarProfileHasItsOwnThemeAndSkinWithMatchingScreen()
    {
        string[] profiles = ["vw-golf8", "skoda-octavia4", "bmw-g20", "volvo-xc60", "toyota-rav4", "ford-focus4"];
        foreach (var p in profiles)
        {
            Assert.Equal(p, BuiltInThemes.Resolve(BuiltInThemes.CarDefault, p));
            Assert.Equal(p, BuiltInSkins.Resolve(BuiltInSkins.CarDefault, p));
            var theme = BuiltInThemes.All.Single(t => t.Id == p);
            var skin = BuiltInSkins.Find(p)!;
            Assert.Empty(skin.Validate());
            Assert.Equal(theme.ScreenPreset, skin.ScreenPreset);
            // Skärmytan i skalet har ungefär samma proportioner som skärmformatet
            var (w, h) = ScreenPresets.Find(skin.ScreenPreset).SizeFor(skin.Orientation);
            Assert.InRange(skin.Screen.W / skin.Screen.H, (double)w / h * 0.97, (double)w / h * 1.03);
        }
        Assert.Equal(BuiltInThemes.Off, BuiltInThemes.Resolve(BuiltInThemes.CarDefault, "okand-bil"));
        Assert.Equal("ford-focus4", BuiltInThemes.Resolve("ford-focus4", "vw-golf8")); // valt tema gäller
        Assert.Equal("volvo-xc60", BuiltInThemes.All.Single(t => t.Orientation == ScreenOrientation.Portrait).Id);
    }

    [Fact]
    public void SkinJsonParsesAndValidates()
    {
        var dir = TempDir();
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, "ram.png"), [137, 80, 78, 71]);
            File.WriteAllText(Path.Combine(dir, "skin.json"), """
                { "id": "min ram", "name": "Min ram", "width": 1000, "height": 700,
                  "screen": { "x": 100, "y": 100, "w": 800, "h": 500 }, "screenPreset": "10in-1280x800",
                  "buttons": [ { "name": "Hem", "rect": { "x": 450, "y": 620, "w": 100, "h": 60 }, "keyCode": 3 } ],
                  "image": "ram.png" }
                """);
            var s = SkinStore.Load(dir);
            Assert.Empty(s.Validate());
            Assert.Equal(3, s.ButtonAt(500, 650)!.KeyCode);
            Assert.Null(s.ButtonAt(500, 300));

            var store = new SkinStore(Path.Combine(dir, "skal"));
            var imported = store.Import(dir);
            Assert.Equal("min-ram", imported.Id);
            Assert.Single(store.LoadAll());

            // Skärmen utanför skalet → fel
            File.WriteAllText(Path.Combine(dir, "skin.json"), File.ReadAllText(Path.Combine(dir, "skin.json")).Replace("\"w\": 800", "\"w\": 990"));
            Assert.Throws<InvalidDataException>(() => store.Import(dir));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void SkinLayoutMapsScreenAndClicks()
    {
        var skin = BuiltInSkins.Find("volvo-xc60")!; // 1000×1320, skärm 600×800 vid (200,110)
        var L = SkinLayout.Fit(skin, 500, 1320); // höjden styr: skala 1 → nej, 500/1000 = 0,5
        Assert.Equal(0.5, L.Scale, 6);
        var (x, y, w, h) = L.ToView(skin.Screen);
        Assert.Equal((100.0, 55.0 + L.OffsetY, 300.0, 400.0), (x, y, w, h));
        // Klick på hemknappen (455,935 + 90×50) i ytans koordinater
        var (sx, sy) = L.ToSkin(L.OffsetX + 500 * 0.5, L.OffsetY + 960 * 0.5);
        Assert.Equal(SkinButton.Keys.Home, skin.ButtonAt(sx, sy)!.KeyCode);
        // Klick i skärmytan träffar ingen knapp
        var (cx, cy) = L.ToSkin(x + w / 2, y + h / 2);
        Assert.True(skin.Screen.Contains(cx, cy));
        Assert.Null(skin.ButtonAt(cx, cy));
    }

    [Fact]
    public void DetectsTransparentScreenAreaInPng()
    {
        int W = 400, H = 300;
        var alpha = Enumerable.Repeat((byte)255, W * H).ToArray();
        for (int yy = 50; yy < 250; yy++)
            for (int xx = 80; xx < 330; xx++)
                alpha[yy * W + xx] = 0;
        Assert.Equal(new SkinRect(80, 50, 250, 200), SkinStore.DetectScreen(alpha, W, H));
        Assert.Null(SkinStore.DetectScreen(Enumerable.Repeat((byte)255, W * H).ToArray(), W, H));
    }
}
