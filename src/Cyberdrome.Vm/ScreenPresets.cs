// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

using System.Globalization;

namespace Cyberdrome.Vm;

public enum ScreenOrientation { Landscape, Portrait }

/// <summary>En skärmstorlek för bilradion/surfplattan (anges liggande, bredd ≥ höjd).</summary>
public sealed record ScreenPreset(string Id, string Name, int Width, int Height, int Dpi)
{
    /// <summary>Upplösning för vald orientering: liggande = bred, stående = hög.</summary>
    public (int Width, int Height) SizeFor(ScreenOrientation o)
    {
        int big = Math.Max(Width, Height), small = Math.Min(Width, Height);
        return o == ScreenOrientation.Portrait ? (small, big) : (big, small);
    }

    /// <summary>Fysisk diagonal i tum utifrån upplösning och dpi.</summary>
    public double DiagonalInches => Math.Sqrt((double)Width * Width + (double)Height * Height) / Dpi;

    public string Label(ScreenOrientation o)
    {
        var (w, h) = SizeFor(o);
        return string.Create(CultureInfo.CurrentCulture,
            $"{w}×{h}, {Dpi} dpi ≈ {DiagonalInches:0.0}\" ({(o == ScreenOrientation.Portrait ? L.T("Screen_PortraitLower") : L.T("Screen_LandscapeLower"))})");
    }

    public override string ToString() => Name;
}

public static class ScreenPresets
{
    public const string DefaultId = "10in-1280x800";
    public const string CustomId = "custom";

    public static readonly IReadOnlyList<ScreenPreset> All =
    [
        new("10in-1280x800", L.T("Screen_10Tablet"), 1280, 800, 160),
        new("10in-1560x700", L.T("ScreenPresets_10WideCarScreen1560700"), 1560, 700, 171),
        new("10in-1024x600", L.T("ScreenPresets_101BudgetCarRadio1024"), 1024, 600, 160),
        new("7in-1024x600", L.T("Screen_7Radio1024"), 1024, 600, 170),
        new("7in-800x480", L.T("ScreenPresets_7OlderCarRadio800480"), 800, 480, 133),
        new("9in-1280x720", L.T("Screen_9Radio1280"), 1280, 720, 160),
        new("9in-1024x768", L.T("ScreenPresets_943Screen1024768"), 1024, 768, 142),
        new("8in-1280x720", L.T("Screen_8Radio1280"), 1280, 720, 183),
        new("8in-800x480", L.T("Screen_8Radio800"), 800, 480, 120),
        new("12in-1920x720", L.T("ScreenPresets_123WideScreen1920720"), 1920, 720, 160),
        new("10in-1920x1200", L.T("Screen_TeslaStyle1200"), 1920, 1200, 213),
        new("10in-1920x1080", L.T("Screen_TeslaStyle1080"), 1920, 1080, 213),
        new(CustomId, L.T("Screen_Custom"), 1280, 800, 160),
    ];

    public static ScreenPreset Find(string? id) => All.FirstOrDefault(p => p.Id == id) ?? All[0];

    /// <summary>Rimliga gränser för anpassad storlek.</summary>
    public static bool IsValid(int w, int h, int dpi) => w is >= 320 and <= 4096 && h is >= 320 and <= 4096 && dpi is >= 80 and <= 640;
}
