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
        return string.Create(CultureInfo.GetCultureInfo("sv-SE"),
            $"{w}×{h}, {Dpi} dpi ≈ {DiagonalInches:0.0}\" ({(o == ScreenOrientation.Portrait ? "stående" : "liggande")})");
    }

    public override string ToString() => Name;
}

public static class ScreenPresets
{
    public const string DefaultId = "10in-1280x800";
    public const string CustomId = "custom";

    public static readonly IReadOnlyList<ScreenPreset> All =
    [
        new("10in-1280x800", "10,1\" surfplatta/bilradio 1280×800", 1280, 800, 160),
        new("10in-1560x700", "10\" bred bilskärm 1560×700", 1560, 700, 171),
        new("10in-1024x600", "10,1\" billig bilradio 1024×600", 1024, 600, 160),
        new("7in-1024x600", "7\" bilradio 1024×600", 1024, 600, 170),
        new("7in-800x480", "7\" äldre bilradio 800×480", 800, 480, 133),
        new("9in-1280x720", "9\" bilradio 1280×720", 1280, 720, 160),
        new("9in-1024x768", "9\" 4:3-skärm 1024×768 (t.ex. stående)", 1024, 768, 142),
        new("8in-1280x720", "8\" bilradio 1280×720", 1280, 720, 183),
        new("8in-800x480", "8\" bilradio 800×480", 800, 480, 120),
        new("12in-1920x720", "12,3\" bred skärm 1920×720", 1920, 720, 160),
        new("10in-1920x1200", "10,4\" Tesla-stil 1920×1200", 1920, 1200, 213),
        new("10in-1920x1080", "10,4\" Tesla-stil 1920×1080", 1920, 1080, 213),
        new(CustomId, "Anpassad", 1280, 800, 160),
    ];

    public static ScreenPreset Find(string? id) => All.FirstOrDefault(p => p.Id == id) ?? All[0];

    /// <summary>Rimliga gränser för anpassad storlek.</summary>
    public static bool IsValid(int w, int h, int dpi) => w is >= 320 and <= 4096 && h is >= 320 and <= 4096 && dpi is >= 80 and <= 640;
}
