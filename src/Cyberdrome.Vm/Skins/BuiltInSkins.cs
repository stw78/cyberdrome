// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

namespace Cyberdrome.Vm.Skins;

/// <summary>
/// De inbyggda skalen – ett per bilmodell. Formerna är inspirerade av respektive bils skärm och
/// instrumentbräda, men ritas helt av oss (inga logotyper, märken, foton eller tillverkarens grafik).
/// Ritningen görs i appen (Style = id); här finns mått, skärmplats och knappar.
/// </summary>
public static class BuiltInSkins
{
    public const string None = "none";
    public const string CarDefault = "auto";

    private static SkinButton B(string name, double x, double y, double w, double h, int key, string label) =>
        new(name, new SkinRect(x, y, w, h), key, label);

    public static readonly IReadOnlyList<Skin> All =
    [
        // Fristående 10" skärm ovanpå instrumentbrädan, pekreglage för volym/klimat under skärmen.
        new Skin
        {
            Id = "vw-golf8", Style = "vw-golf8", Name = L.T("BuiltInSkins_Golf8StyleFreeStanding10"),
            Description = L.T("BuiltInSkins_FreeStandingGlossyScreenWithA"),
            Width = 1600, Height = 860, Screen = new(240, 120, 1120, 503),
            ScreenPreset = "10in-1560x700", Orientation = ScreenOrientation.Landscape,
            Buttons =
            [
                B(L.T("BuiltInSkins_VolumeDown"), 540, 790, 90, 60, SkinButton.Keys.VolumeDown, "−"),
                B("Tillbaka", 640, 790, 100, 60, SkinButton.Keys.Back, "↩"),
                B("Hem", 750, 790, 130, 60, SkinButton.Keys.Home, "⌂"),
                B(L.T("BuiltInSkins_VolumeUp"), 890, 790, 90, 60, SkinButton.Keys.VolumeUp, "+"),
            ],
        },
        // Fristående 10" skärm med kromram och knapprad under.
        new Skin
        {
            Id = "skoda-octavia4", Style = "skoda-octavia4", Name = L.T("BuiltInSkins_Octavia4StyleFreeStanding10"),
            Description = L.T("BuiltInSkins_FreeStandingScreenWithChromeTrim"),
            Width = 1600, Height = 900, Screen = new(300, 110, 1000, 625),
            ScreenPreset = "10in-1280x800", Orientation = ScreenOrientation.Landscape,
            Buttons =
            [
                B(L.T("BuiltInSkins_VolumeDown"), 590, 775, 90, 50, SkinButton.Keys.VolumeDown, "−"),
                B("Tillbaka", 690, 775, 100, 50, SkinButton.Keys.Back, "↩"),
                B("Hem", 800, 775, 100, 50, SkinButton.Keys.Home, "⌂"),
                B(L.T("BuiltInSkins_VolumeUp"), 910, 775, 90, 50, SkinButton.Keys.VolumeUp, "+"),
            ],
        },
        // Bred skärm, lätt vinklad mot föraren, rad med snabbknappar och volymvred under.
        new Skin
        {
            Id = "bmw-g20", Style = "bmw-g20", Name = L.T("BuiltInSkins_3SeriesG20StyleWide10"),
            Description = L.T("BuiltInSkins_WideFreeStandingScreenRowOf"),
            Width = 1700, Height = 820, Screen = new(130, 90, 1440, 540),
            ScreenPreset = "12in-1920x720", Orientation = ScreenOrientation.Landscape,
            Buttons =
            [
                B(L.T("ScreenControl_Previous"), 380, 690, 120, 60, SkinButton.Keys.Previous, "⏮"),
                B(L.T("ScreenControl_Next"), 520, 690, 120, 60, SkinButton.Keys.Next, "⏭"),
                B("Tillbaka", 1060, 690, 120, 60, SkinButton.Keys.Back, "↩"),
                B("Hem", 1200, 690, 120, 60, SkinButton.Keys.Home, "⌂"),
                B("Volym (vred)", 760, 675, 90, 90, SkinButton.Keys.Mute, "🔇"),
                B(L.T("BuiltInSkins_VolumeDown"), 660, 690, 80, 60, SkinButton.Keys.VolumeDown, "−"),
                B(L.T("BuiltInSkins_VolumeUp"), 870, 690, 80, 60, SkinButton.Keys.VolumeUp, "+"),
            ],
        },
        // Stående 9" skärm inbyggd i mittkonsolen, hemknapp under, vertikala ventiler på sidorna.
        new Skin
        {
            Id = "volvo-xc60", Style = "volvo-xc60", Name = L.T("BuiltInSkins_XC60StylePortrait9Screen"),
            Description = L.T("BuiltInSkins_PortraitScreenWithChromeFrameAnd"),
            Width = 1000, Height = 1320, Screen = new(200, 110, 600, 800),
            ScreenPreset = "9in-1024x768", Orientation = ScreenOrientation.Portrait,
            Buttons =
            [
                B("Tillbaka", 355, 935, 90, 50, SkinButton.Keys.Back, "↩"),
                B("Hem", 455, 935, 90, 50, SkinButton.Keys.Home, "⌂"),
                B(L.T("ScreenControl_Previous"), 300, 1080, 100, 70, SkinButton.Keys.Previous, "⏮"),
                B("Spela/paus", 450, 1060, 100, 100, SkinButton.Keys.PlayPause, "⏯"),
                B(L.T("ScreenControl_Next"), 600, 1080, 100, 70, SkinButton.Keys.Next, "⏭"),
            ],
        },
        // Inbyggd 8–9" skärm med fysiska knappar och två vred på sidorna.
        new Skin
        {
            Id = "toyota-rav4", Style = "toyota-rav4", Name = L.T("BuiltInSkins_RAV4StyleScreenWithPhysicalButtons"),
            Description = L.T("BuiltInSkins_ScreenBuiltIntoTheCentreConsole"),
            Width = 1700, Height = 860, Screen = new(290, 110, 1120, 672),
            ScreenPreset = "8in-800x480", Orientation = ScreenOrientation.Landscape,
            Buttons =
            [
                B(L.T("BuiltInSkins_VolumeUp"), 100, 140, 150, 70, SkinButton.Keys.VolumeUp, "VOL +"),
                B(L.T("BuiltInSkins_VolumeDown"), 100, 230, 150, 70, SkinButton.Keys.VolumeDown, "VOL −"),
                B(L.T("ScreenControl_Mute"), 110, 330, 130, 130, SkinButton.Keys.Mute, "🔇"),
                B("Hem", 100, 500, 150, 70, SkinButton.Keys.Home, "HOME"),
                B("Meny", 100, 600, 150, 70, SkinButton.Keys.Menu, "MENU"),
                B(L.T("ScreenControl_Previous"), 1450, 140, 150, 70, SkinButton.Keys.Previous, "⏮ SEEK"),
                B(L.T("ScreenControl_Next"), 1450, 230, 150, 70, SkinButton.Keys.Next, "SEEK ⏭"),
                B("Spela/paus", 1460, 330, 130, 130, SkinButton.Keys.PlayPause, "⏯"),
                B("Tillbaka", 1450, 500, 150, 70, SkinButton.Keys.Back, "BACK"),
            ],
        },
        // Fristående 8" skärm med knapprad under.
        new Skin
        {
            Id = "ford-focus4", Style = "ford-focus4", Name = L.T("BuiltInSkins_FocusMk4StyleFreeStanding8"),
            Description = L.T("BuiltInSkins_FreeStandingScreenRowOfPhysical"),
            Width = 1600, Height = 960, Screen = new(300, 100, 1000, 600),
            ScreenPreset = "8in-800x480", Orientation = ScreenOrientation.Landscape,
            Buttons =
            [
                B("Tillbaka", 290, 760, 120, 70, SkinButton.Keys.Back, "↩"),
                B(L.T("ScreenControl_Previous"), 430, 760, 120, 70, SkinButton.Keys.Previous, "⏮"),
                B("Spela/paus", 570, 760, 120, 70, SkinButton.Keys.PlayPause, "⏯"),
                B(L.T("ScreenControl_Next"), 710, 760, 120, 70, SkinButton.Keys.Next, "⏭"),
                B(L.T("BuiltInSkins_MuteKnob"), 860, 735, 120, 120, SkinButton.Keys.Mute, "🔇"),
                B(L.T("BuiltInSkins_VolumeDown"), 1010, 760, 90, 70, SkinButton.Keys.VolumeDown, "−"),
                B(L.T("BuiltInSkins_VolumeUp"), 1110, 760, 90, 70, SkinButton.Keys.VolumeUp, "+"),
            ],
        },
    ];

    public static Skin? Find(string id) => All.FirstOrDefault(s => s.Id == id);

    /// <summary>Bilmodellens eget skal (samma id som bilprofilen).</summary>
    public static Skin? ForProfile(string profileId) => Find(profileId);

    /// <summary>"auto" → bilmodellens eget skal (om det finns), annars valt skal.</summary>
    public static string Resolve(string choice, string profileId) =>
        choice == CarDefault ? (Find(profileId) is not null ? profileId : None) : choice;
}
