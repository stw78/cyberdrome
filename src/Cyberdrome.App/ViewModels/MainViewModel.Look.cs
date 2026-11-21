// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cyberdrome.Vm;
using Cyberdrome.Vm.Skins;
using Cyberdrome.Vm.Themes;

namespace Cyberdrome.App.ViewModels;

/// <summary>Ett val i en lista (tema eller skal).</summary>
public sealed record LookOption(string Id, string Name)
{
    public override string ToString() => Name;
}

/// <summary>Bilens utseende: tema i Android (startskärm) och skal runt skärmen – per bilmodell.</summary>
public sealed partial class MainViewModel
{
    private readonly ThemeStore _themeStore = new(ThemeStore.DefaultFolder);
    private readonly SkinStore _skinStore = new(SkinStore.DefaultFolder);
    private bool _lookBusy;

    public ObservableCollection<LookOption> ThemeOptions { get; } = new();
    public ObservableCollection<LookOption> SkinOptions { get; } = new();

    [ObservableProperty] private string _lookStatus = "";

    /// <summary>Skalet som visas runt Android-skärmen (null = enkel ram).</summary>
    [ObservableProperty] private Skin? _activeSkin;

    private string ProfileId => _car?.Profile.Id ?? "";

    private void InitLook()
    {
        ReloadLookOptions();
        UpdateActiveSkin();
    }

    private void ReloadLookOptions()
    {
        ThemeOptions.Clear();
        ThemeOptions.Add(new(BuiltInThemes.CarDefault, L.T("Look_TheCarSOwnThemeDefault")));
        foreach (var t in BuiltInThemes.All) ThemeOptions.Add(new(t.Id, t.Name));
        foreach (var t in _themeStore.LoadAll()) ThemeOptions.Add(new(t.Id, "Eget: " + t.Name));
        ThemeOptions.Add(new(BuiltInThemes.Off, L.T("Look_OffAndroidSNormalHomeScreen")));

        SkinOptions.Clear();
        SkinOptions.Add(new(BuiltInSkins.CarDefault, L.T("Look_TheCarSOwnSkinDefault")));
        foreach (var s in BuiltInSkins.All) SkinOptions.Add(new(s.Id, s.Name));
        foreach (var s in _skinStore.LoadAll()) SkinOptions.Add(new(s.Id, "Eget: " + s.Name));
        SkinOptions.Add(new(BuiltInSkins.None, L.T("Look_NoSkin")));
        OnPropertyChanged(nameof(SelectedThemeOption));
        OnPropertyChanged(nameof(SelectedSkinOption));
    }

    private string ThemeChoice => _appSettings.ThemeByProfile.GetValueOrDefault(ProfileId, BuiltInThemes.CarDefault);
    private string SkinChoice => _appSettings.SkinByProfile.GetValueOrDefault(ProfileId, BuiltInSkins.CarDefault);

    public LookOption? SelectedThemeOption
    {
        get => ThemeOptions.FirstOrDefault(o => o.Id == ThemeChoice) ?? ThemeOptions.FirstOrDefault();
        set
        {
            if (value is null || value.Id == ThemeChoice || _car is null) return;
            _appSettings.ThemeByProfile[ProfileId] = value.Id;
            _appSettings.Save();
            OnPropertyChanged();
            _ = ApplyCarLookAsync();
        }
    }

    public LookOption? SelectedSkinOption
    {
        get => SkinOptions.FirstOrDefault(o => o.Id == SkinChoice) ?? SkinOptions.FirstOrDefault();
        set
        {
            if (value is null || value.Id == SkinChoice || _car is null) return;
            _appSettings.SkinByProfile[ProfileId] = value.Id;
            _appSettings.Save();
            OnPropertyChanged();
            UpdateActiveSkin();
        }
    }

    /// <summary>Tema-id som gäller för vald bil ("auto" → bilens eget om det finns).</summary>
    public string EffectiveThemeId =>
        ThemeChoice == BuiltInThemes.CarDefault && _car?.Profile.Theme is { Length: > 0 } own ? own : ResolveTheme(ThemeChoice, ProfileId);

    public static string ResolveTheme(string choice, string profileId) => BuiltInThemes.Resolve(choice, profileId);
    public static string ResolveSkin(string choice, string profileId) => BuiltInSkins.Resolve(choice, profileId);

    private void UpdateActiveSkin()
    {
        // Egna bilmodeller med ett inbyggt tema får samma skal som originalmodellen.
        var baseId = SkinChoice == BuiltInSkins.CarDefault && _car?.Profile.Theme is { } th && BuiltInSkins.Find(th) is not null ? th : ProfileId;
        var id = ResolveSkin(SkinChoice, baseId);
        ActiveSkin = id == BuiltInSkins.None ? null
            : BuiltInSkins.Find(id) ?? _skinStore.LoadAll().FirstOrDefault(s => s.Id == id);
    }

    /// <summary>Byter bilmodellens utseende: skärmformat, skal och tema i Android.</summary>
    private async Task ApplyCarLookAsync()
    {
        if (_car is null || _lookBusy) return;
        _lookBusy = true;
        try
        {
            UpdateActiveSkin();
            var themeId = EffectiveThemeId;

            // Skärmformat: från bilmodellens tema (eller eget temas screenPreset).
            var builtIn = BuiltInThemes.All.FirstOrDefault(t => t.Id == themeId);
            var custom = builtIn.Id is null ? _themeStore.LoadAll().FirstOrDefault(t => t.Id == themeId) : null;
            string? preset = _car.Profile.ScreenPreset ?? (builtIn.Id is not null ? builtIn.ScreenPreset : custom?.ScreenPreset);
            if (preset is not null)
            {
                _vmSettings.ScreenPresetId = preset;
                if (builtIn.Id is not null) _vmSettings.ScreenOrientation = builtIn.Orientation;
                _vmSettings.Save();
                OnPropertyChanged(nameof(SelectedScreenPreset));
                OnPropertyChanged(nameof(ScreenLandscape));
                OnPropertyChanged(nameof(ScreenPortrait));
                OnPropertyChanged(nameof(IsCustomScreen));
                OnPropertyChanged(nameof(ScreenLabel));
            }
            await ApplyScreenAsync();

            if (_android is not EmulatorVm emu || !AndroidRunning)
            {
                LookStatus = L.T("Look_TheThemeIsUsedWhenAndroid");
                return;
            }

            if (themeId == BuiltInThemes.Off)
            {
                await emu.ResetHomeAsync();
                LookStatus = L.T("Look_AndroidSNormalHomeScreen");
                return;
            }

            if (!await EnsureLauncherAsync(emu)) return;
            var args = builtIn.Id is not null || custom is null
                ? CyberdromeLauncher.BroadcastBuiltIn(themeId)
                : CyberdromeLauncher.BroadcastCustom(custom);
            var r = await emu.RunShellAsync(args);
            await emu.SetHomeAsync(CyberdromeLauncher.HomeActivity);
            LookStatus = r.Ok && r.Output.Contains("\"OK\"")
                ? L.T("Look_ThemePrefix") + (ThemeOptions.FirstOrDefault(o => o.Id == themeId)?.Name ?? themeId)
                : L.T("Look_CouldNotChangeTheme") + r.Output.Trim();
        }
        catch (Exception ex) { LookStatus = L.T("Look_ThemePrefix") + ex.Message; }
        finally { _lookBusy = false; }
    }

    /// <summary>Installerar (och bygger vid behov) Cyberdrome Launcher.</summary>
    private async Task<bool> EnsureLauncherAsync(EmulatorVm emu)
    {
        var app = CyberdromeLauncher.App;
        var project = app.FindProjectDir(AppContext.BaseDirectory);
        bool installed = await emu.IsInstalledAsync(app.PackageName);
        bool rebuild = project is not null && app.NeedsBuild(project);
        long stamp = project is not null && File.Exists(app.ApkPath(project)) ? File.GetLastWriteTimeUtc(app.ApkPath(project)).Ticks : 0;
        bool current = _appSettings.InstalledLauncher.GetValueOrDefault(emu.AvdName) == stamp;
        if (project is null)
        {
            // Installerad version: färdigbyggd APK i programmappen (apps\).
            var bundled = SampleApp.Bundled(app.ApkName);
            if (bundled is null) { LookStatus = L.T("Look_CyberdromeLauncherNotFoundAppsOr"); return false; }
            long bstamp = File.GetLastWriteTimeUtc(bundled).Ticks;
            if (installed && _appSettings.InstalledLauncher.GetValueOrDefault(emu.AvdName) == bstamp) return true;
            LookStatus = L.T("Look_InstallingCyberdromeLauncher");
            var br = await emu.InstallApkAsync(bundled, AndroidLogLine);
            if (!br.Ok) { LookStatus = L.T("Look_CouldNotInstallCyberdromeLauncher"); return false; }
            _appSettings.InstalledLauncher[emu.AvdName] = bstamp;
            _appSettings.Save();
            return true;
        }
        if (installed && !rebuild && current) return true;
        var apk = app.ApkPath(project);
        if (rebuild)
        {
            LookStatus = L.T("Look_BuildingCyberdromeLauncher");
            if (await app.BuildAsync(project, _vmSettings.SdkRoot, AndroidLogLine) is null)
            {
                LookStatus = L.T("Look_BuildingCyberdromeLauncherFailedSeeThe");
                return false;
            }
        }
        LookStatus = L.T("Look_InstallingCyberdromeLauncher");
        var r = await emu.InstallApkAsync(apk, AndroidLogLine);
        if (!r.Ok) { LookStatus = L.T("Look_CouldNotInstallCyberdromeLauncher"); return false; }
        _appSettings.InstalledLauncher[emu.AvdName] = File.GetLastWriteTimeUtc(apk).Ticks;
        _appSettings.Save();
        return true;
    }

    [RelayCommand]
    private Task ReapplyLook() => ApplyCarLookAsync();

    /// <summary>Tryck på en knapp på skalet → Android-tangent.</summary>
    public async Task SkinButtonAsync(SkinButton b)
    {
        if (_android is IScreenControl s && AndroidRunning)
        {
            await s.KeyEventAsync(b.KeyCode);
            AndroidLogLine($"Skalknapp: {b.Name}");
        }
    }

    public void ImportTheme(string path)
    {
        try
        {
            var t = _themeStore.Import(path);
            ReloadLookOptions();
            SelectedThemeOption = ThemeOptions.FirstOrDefault(o => o.Id == t.Id);
            LookStatus = L.T("Look_ThemeImported") + t.Name;
        }
        catch (Exception ex) { LookStatus = L.T("Look_CouldNotImportTheme") + ex.Message; }
    }

    /// <summary>Importerar ett skal: zip/mapp med skin.json, eller en PNG där skärmytan är genomskinlig.</summary>
    public void ImportSkin(string path, Func<string, (byte[] Alpha, int W, int H)?> readAlpha)
    {
        try
        {
            Skin s;
            if (path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                var a = readAlpha(path) ?? throw new InvalidDataException(L.T("Look_TheImageCouldNotBeRead"));
                var rect = SkinStore.DetectScreen(a.Alpha, a.W, a.H)
                           ?? throw new InvalidDataException(L.T("Look_NoTransparentScreenAreaAtLeast"));
                var (w, h, _) = _vmSettings.EffectiveScreen();
                s = _skinStore.SaveFromImage(path, new Skin
                {
                    Id = Path.GetFileNameWithoutExtension(path),
                    Name = Path.GetFileNameWithoutExtension(path),
                    Width = a.W, Height = a.H, Screen = rect,
                    ScreenPreset = _vmSettings.ScreenPresetId == ScreenPresets.CustomId ? ScreenPresets.DefaultId : _vmSettings.ScreenPresetId,
                    Orientation = rect.H > rect.W ? ScreenOrientation.Portrait : ScreenOrientation.Landscape,
                });
            }
            else s = _skinStore.Import(path);
            ReloadLookOptions();
            SelectedSkinOption = SkinOptions.FirstOrDefault(o => o.Id == s.Id);
            LookStatus = L.T("Look_SkinImported") + s.Name;
        }
        catch (Exception ex) { LookStatus = L.T("Look_CouldNotImportSkin") + ex.Message; }
    }
}
