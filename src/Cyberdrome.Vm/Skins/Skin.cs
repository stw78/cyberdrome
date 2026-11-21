// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cyberdrome.Vm.Skins;

/// <summary>En rektangel i skalets egna pixlar.</summary>
public sealed record SkinRect(double X, double Y, double W, double H)
{
    public bool Contains(double px, double py) => px >= X && py >= Y && px < X + W && py < Y + H;
    public bool FitsIn(double width, double height) => X >= 0 && Y >= 0 && W > 0 && H > 0 && X + W <= width && Y + H <= height;
}

/// <summary>En knapp eller ett vred på skalet. Ett tryck skickar Android-tangentkoden <see cref="KeyCode"/>.</summary>
public sealed record SkinButton(string Name, SkinRect Rect, int KeyCode, string Label = "")
{
    /// <summary>Vanliga Android-tangentkoder för knappar på en bilradio.</summary>
    public static class Keys
    {
        public const int Home = 3, Back = 4, VolumeUp = 24, VolumeDown = 25, PlayPause = 85,
            Next = 87, Previous = 88, Mute = 164, Menu = 82;
    }
}

/// <summary>
/// Ett skal: ramen/instrumentbrädan runt Android-skärmen.
/// Inbyggda skal ritas av programmet (Style); egna skal har en bild (Image) i samma mapp som skin.json.
/// </summary>
public sealed class Skin
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";

    /// <summary>Skalets storlek i pixlar (bildens storlek för egna skal).</summary>
    public double Width { get; set; }
    public double Height { get; set; }

    /// <summary>Var Android-skärmen sitter i skalet.</summary>
    public SkinRect Screen { get; set; } = new(0, 0, 0, 0);

    /// <summary>Skärmförval som passar skalet (se ScreenPresets), och orientering.</summary>
    public string ScreenPreset { get; set; } = ScreenPresets.DefaultId;

    [JsonConverter(typeof(JsonStringEnumConverter<ScreenOrientation>))]
    public ScreenOrientation Orientation { get; set; } = ScreenOrientation.Landscape;

    public List<SkinButton> Buttons { get; set; } = new();

    /// <summary>Bildfil (PNG) relativt skin.json – för egna skal.</summary>
    public string? Image { get; set; }

    /// <summary>Vilken inbyggd ritning som används (för inbyggda skal).</summary>
    public string? Style { get; set; }

    /// <summary>Mappen skalet lästes från (null för inbyggda).</summary>
    [JsonIgnore] public string? Folder { get; set; }

    [JsonIgnore] public string? ImagePath => Image is null || Folder is null ? null : Path.Combine(Folder, Image);

    [JsonIgnore] public bool IsBuiltIn => Style is not null && Folder is null;

    public override string ToString() => Name;

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public static Skin FromJson(string json) =>
        JsonSerializer.Deserialize<Skin>(json, Json) ?? throw new InvalidDataException("Tom skin.json.");

    /// <summary>Kontrollerar att skalet går att använda. Returnerar en lista med fel (tom = OK).</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(Id)) errors.Add("id saknas.");
        if (string.IsNullOrWhiteSpace(Name)) errors.Add("name saknas.");
        if (Width <= 0 || Height <= 0) errors.Add("width och height måste vara större än 0.");
        if (!Screen.FitsIn(Width, Height)) errors.Add("screen måste ligga helt inne i skalet.");
        if (Screen.W < 100 || Screen.H < 100) errors.Add("screen är för liten (minst 100×100).");
        if (ScreenPresets.All.All(p => p.Id != ScreenPreset)) errors.Add($"okänt screenPreset: {ScreenPreset}.");
        foreach (var b in Buttons)
        {
            if (!b.Rect.FitsIn(Width, Height)) errors.Add($"knappen \"{b.Name}\" ligger utanför skalet.");
            if (b.KeyCode is < 0 or > 300) errors.Add($"knappen \"{b.Name}\" har ogiltig keyCode {b.KeyCode}.");
        }
        if (Style is null && Image is null) errors.Add("image saknas.");
        if (ImagePath is { } img && !File.Exists(img)) errors.Add($"bilden {Image} finns inte.");
        return errors;
    }

    /// <summary>Knappen på en punkt (skalets pixlar), eller null.</summary>
    public SkinButton? ButtonAt(double x, double y) => Buttons.LastOrDefault(b => b.Rect.Contains(x, y));
}

/// <summary>Hur skalet ritas i en yta: likformig skalning, centrerat (svarta kanter vid behov).</summary>
public readonly record struct SkinLayout(double Scale, double OffsetX, double OffsetY)
{
    public static SkinLayout Fit(Skin skin, double viewW, double viewH)
    {
        double s = Math.Min(viewW / skin.Width, viewH / skin.Height);
        return new SkinLayout(s, (viewW - skin.Width * s) / 2, (viewH - skin.Height * s) / 2);
    }

    /// <summary>Skalets rektangel i ytans koordinater.</summary>
    public (double X, double Y, double W, double H) ToView(SkinRect r) =>
        (OffsetX + r.X * Scale, OffsetY + r.Y * Scale, r.W * Scale, r.H * Scale);

    /// <summary>En punkt i ytan → skalets pixlar.</summary>
    public (double X, double Y) ToSkin(double vx, double vy) => ((vx - OffsetX) / Scale, (vy - OffsetY) / Scale);
}
