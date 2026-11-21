// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cyberdrome.Vm;

namespace Cyberdrome.App.ViewModels;

/// <summary>Skärmstorlek och orientering för Android (bilradio/surfplatta).</summary>
public sealed partial class MainViewModel
{
    public IReadOnlyList<ScreenPreset> ScreenPresetList => ScreenPresets.All;

    public ScreenPreset SelectedScreenPreset
    {
        get => ScreenPresets.Find(_vmSettings.ScreenPresetId);
        set
        {
            if (value is null || value.Id == _vmSettings.ScreenPresetId) return;
            _vmSettings.ScreenPresetId = value.Id;
            ScreenChanged();
        }
    }

    public bool ScreenLandscape
    {
        get => _vmSettings.ScreenOrientation == ScreenOrientation.Landscape;
        set { if (value && !ScreenLandscape) { _vmSettings.ScreenOrientation = ScreenOrientation.Landscape; ScreenChanged(); } }
    }

    public bool ScreenPortrait
    {
        get => _vmSettings.ScreenOrientation == ScreenOrientation.Portrait;
        set { if (value && !ScreenPortrait) { _vmSettings.ScreenOrientation = ScreenOrientation.Portrait; ScreenChanged(); } }
    }

    public bool IsCustomScreen => _vmSettings.ScreenPresetId == ScreenPresets.CustomId;

    public int CustomScreenWidth { get => _vmSettings.CustomScreenWidth; set { _vmSettings.CustomScreenWidth = value; OnPropertyChanged(); } }
    public int CustomScreenHeight { get => _vmSettings.CustomScreenHeight; set { _vmSettings.CustomScreenHeight = value; OnPropertyChanged(); } }
    public int CustomScreenDpi { get => _vmSettings.CustomScreenDpi; set { _vmSettings.CustomScreenDpi = value; OnPropertyChanged(); } }

    /// <summary>T.ex. "1280×800, 160 dpi ≈ 9,4" (liggande)".</summary>
    public string ScreenLabel
    {
        get
        {
            var (w, h, dpi) = _vmSettings.EffectiveScreen();
            var p = new ScreenPreset("x", "", w, h, dpi);
            return p.Label(_vmSettings.ScreenOrientation);
        }
    }

    [ObservableProperty] private string _screenStatus = "";

    private void ScreenChanged()
    {
        _vmSettings.Save();
        OnPropertyChanged(nameof(SelectedScreenPreset));
        OnPropertyChanged(nameof(ScreenLandscape));
        OnPropertyChanged(nameof(ScreenPortrait));
        OnPropertyChanged(nameof(IsCustomScreen));
        OnPropertyChanged(nameof(ScreenLabel));
        _ = ApplyScreenAsync();
    }

    [RelayCommand]
    private Task ApplyCustomScreen()
    {
        if (!ScreenPresets.IsValid(CustomScreenWidth, CustomScreenHeight, CustomScreenDpi))
        {
            ScreenStatus = "Ogiltig storlek: bredd/höjd 320–4096 och dpi 80–640.";
            return Task.CompletedTask;
        }
        _vmSettings.ScreenPresetId = ScreenPresets.CustomId;
        ScreenChanged();
        return Task.CompletedTask;
    }

    /// <summary>Byter skärm direkt i emulatorn (och sparar för nästa start).</summary>
    private async Task ApplyScreenAsync()
    {
        var (w, h, dpi) = _vmSettings.EffectiveScreen();
        if (_android is not EmulatorVm emu)
        {
            ScreenStatus = "Skärmstorlek gäller läget Utan omstart (emulatorn).";
            return;
        }
        if (!AndroidRunning)
        {
            await emu.ApplyScreenAsync(w, h, dpi, AndroidLogLine);
            ScreenStatus = $"Sparat: {ScreenLabel}. Gäller när Android startar.";
            return;
        }
        ScreenStatus = "Byter skärm …";
        var ok = await emu.ApplyScreenAsync(w, h, dpi, AndroidLogLine);
        ScreenStatus = ok ? $"Skärm: {ScreenLabel}" : "Kunde inte byta skärm – se loggen på sidan Android.";
    }
}
