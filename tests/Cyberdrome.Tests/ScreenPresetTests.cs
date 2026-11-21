// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

using Cyberdrome.Vm;

namespace Cyberdrome.Tests;

public class ScreenPresetTests
{
    [Fact]
    public void DefaultIs10InchTablet1280x800()
    {
        var s = new VmSettings();
        Assert.Equal(ScreenPresets.DefaultId, s.ScreenPresetId);
        Assert.Equal((1280, 800, 160), s.EffectiveScreen());
    }

    [Fact]
    public void ListContainsTheRequestedSizes()
    {
        var sizes = ScreenPresets.All.Select(p => (p.Width, p.Height, p.Dpi)).ToList();
        Assert.Contains((1280, 800, 160), sizes);
        Assert.Contains((1024, 600, 160), sizes);
        Assert.Contains((1024, 600, 170), sizes);
        Assert.Contains((800, 480, 133), sizes);
        Assert.Contains((1280, 720, 160), sizes);
        Assert.Contains((1920, 720, 160), sizes);
        Assert.Contains(ScreenPresets.All, p => p.Id == ScreenPresets.CustomId);
        Assert.Equal(ScreenPresets.All.Count, ScreenPresets.All.Select(p => p.Id).Distinct().Count());
    }

    [Theory]
    [InlineData("10in-1280x800", 1280, 800)]
    [InlineData("12in-1920x720", 1920, 720)]
    [InlineData("10in-1920x1200", 1920, 1200)]
    public void PortraitSwapsWidthAndHeight(string id, int w, int h)
    {
        var p = ScreenPresets.Find(id);
        Assert.Equal((w, h), p.SizeFor(ScreenOrientation.Landscape));
        Assert.Equal((h, w), p.SizeFor(ScreenOrientation.Portrait));
    }

    [Fact]
    public void CustomSizeIsUsedWhenValid()
    {
        var s = new VmSettings { ScreenPresetId = ScreenPresets.CustomId, CustomScreenWidth = 1600, CustomScreenHeight = 900, CustomScreenDpi = 200, ScreenOrientation = ScreenOrientation.Portrait };
        Assert.Equal((900, 1600, 200), s.EffectiveScreen());
        s.CustomScreenWidth = 10; // ogiltig → förvalets standard
        Assert.Equal((800, 1280, 160), s.EffectiveScreen());
    }

    [Fact]
    public void SettingsRoundTripKeepsScreen()
    {
        var s = new VmSettings { ScreenPresetId = "7in-800x480", ScreenOrientation = ScreenOrientation.Portrait };
        var back = VmSettings.FromJson(s.ToJson());
        Assert.Equal("7in-800x480", back.ScreenPresetId);
        Assert.Equal(ScreenOrientation.Portrait, back.ScreenOrientation);
        Assert.Contains("\"ScreenOrientation\": \"Portrait\"", s.ToJson());
    }

    [Fact]
    public void LabelShowsResolutionDpiAndDiagonal()
    {
        var p = ScreenPresets.Find("10in-1280x800");
        Assert.Equal("1280×800, 160 dpi ≈ 9,4\" (liggande)", p.Label(ScreenOrientation.Landscape));
        Assert.StartsWith("800×1280", p.Label(ScreenOrientation.Portrait));
    }

    /// <summary>Klick i panelen (letterbox) räknas om till rätt pixel för varje förval och orientering.</summary>
    [Theory]
    [InlineData("10in-1280x800", ScreenOrientation.Landscape)]
    [InlineData("10in-1280x800", ScreenOrientation.Portrait)]
    [InlineData("10in-1024x600", ScreenOrientation.Landscape)]
    [InlineData("12in-1920x720", ScreenOrientation.Landscape)]
    [InlineData("12in-1920x720", ScreenOrientation.Portrait)]
    [InlineData("10in-1920x1200", ScreenOrientation.Portrait)]
    public void ClickMappingForEveryPreset(string id, ScreenOrientation o)
    {
        var (w, h) = ScreenPresets.Find(id).SizeFor(o);
        const double viewW = 800, viewH = 600;
        double scale = Math.Min(viewW / w, viewH / h);
        double offX = (viewW - w * scale) / 2, offY = (viewH - h * scale) / 2;

        // Mitten av panelen = mitten av skärmen
        Assert.Equal((w / 2, h / 2), ScreenMapping.ToDevice(viewW / 2, viewH / 2, viewW, viewH, w, h));
        // Övre vänstra hörnet av själva bilden = (0,0)
        Assert.Equal((0, 0), ScreenMapping.ToDevice(offX + 0.01, offY + 0.01, viewW, viewH, w, h));
        // Nedre högra hörnet = sista pixeln
        var br = ScreenMapping.ToDevice(offX + w * scale - 0.01, offY + h * scale - 0.01, viewW, viewH, w, h);
        Assert.Equal((w - 1, h - 1), br);
        // Utanför bilden (i svarta kanten) = inget klick
        if (offX > 1) Assert.Null(ScreenMapping.ToDevice(offX / 2, viewH / 2, viewW, viewH, w, h));
        if (offY > 1) Assert.Null(ScreenMapping.ToDevice(viewW / 2, offY / 2, viewW, viewH, w, h));
    }
}
