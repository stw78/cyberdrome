// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cyberdrome.Core.Profiles;
using Cyberdrome.Obd.Testing;
using Cyberdrome.Vm.Skins;
using Cyberdrome.Vm.Themes;

namespace Cyberdrome.App.ViewModels;

/// <summary>Sidan Bilmodeller: egna bilmodeller (kopiera, redigera, ta bort) och delning som .cbs.</summary>
public sealed partial class MainViewModel
{
    private ProfileStore? _profileStore;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsEditingCar))] private CarEditorViewModel? _carEditor;
    [ObservableProperty] private string _carStatus = "";

    public bool IsEditingCar => CarEditor is not null;
    public bool SelectedIsOwn => SelectedProfile?.IsOwn == true;

    private void InitCars(IEnumerable<string> builtInIds)
    {
        _profileStore = new ProfileStore(ProfileStore.DefaultFolder, builtInIds);
        foreach (var p in _profileStore.LoadOwn()) Profiles.Add(p);
    }

    private void ReloadOwnProfiles(string? selectId = null)
    {
        if (_profileStore is null) return;
        var keepId = selectId ?? SelectedProfile?.Id;
        foreach (var own in Profiles.Where(p => p.IsOwn).ToList()) Profiles.Remove(own);
        foreach (var p in _profileStore.LoadOwn()) Profiles.Add(p);
        var sel = Profiles.FirstOrDefault(p => p.Id == keepId) ?? Profiles.FirstOrDefault();
        if (sel is not null && !ReferenceEquals(sel, SelectedProfile)) SelectedProfile = sel;
        OnPropertyChanged(nameof(SelectedIsOwn));
    }

    // ---------------- Ny / redigera / ta bort ----------------

    [RelayCommand]
    private void NewCarFromCopy()
    {
        if (_profileStore is null || SelectedProfile is null) return;
        var copy = _profileStore.Duplicate(SelectedProfile);
        // En kopia av en inbyggd modell ärver modellens tema och skärmformat.
        var bt = BuiltInThemes.All.FirstOrDefault(t => t.Id == SelectedProfile.Id);
        copy.Theme ??= bt.Id;
        copy.ScreenPreset ??= bt.ScreenPreset;
        CarEditor = new CarEditorViewModel(copy, isNew: true);
        CarStatus = L.F("Cars_CopyOf0ChangeItAnd", SelectedProfile.DisplayName);
    }

    [RelayCommand]
    private void EditCar()
    {
        if (SelectedProfile is not { IsOwn: true } p) { CarStatus = L.T("Cars_TheBuiltInCarModelsAre"); return; }
        CarEditor = new CarEditorViewModel(p, isNew: false);
        CarStatus = "";
    }

    [RelayCommand]
    private void SaveCar()
    {
        if (CarEditor is null || _profileStore is null) return;
        var p = CarEditor.Build();
        if (CarEditor.IsNew || !string.Equals(p.Id, SelectedProfile?.Id, StringComparison.OrdinalIgnoreCase))
            p.Id = _profileStore.UniqueId(p.Id, except: CarEditor.IsNew ? null : SelectedProfile?.Id);
        var errors = ProfileLoader.Validate(p);
        if (_profileStore.IsBuiltIn(p.Id)) errors.Insert(0, L.T("Cars_TheIdBelongsToABuilt"));
        if (errors.Count > 0) { CarEditor.Errors = string.Join("\n", errors); return; }
        try
        {
            if (!CarEditor.IsNew && SelectedProfile is { IsOwn: true } old && old.Id != p.Id) _profileStore.Delete(old);
            _profileStore.Save(p);
            CarEditor = null;
            ReloadOwnProfiles(p.Id);
            CarStatus = $"Sparad: {p.DisplayName}";
        }
        catch (Exception ex) { if (CarEditor is { } ed) ed.Errors = ex.Message; else CarStatus = ex.Message; }
    }

    [RelayCommand]
    private void CancelCarEdit() { CarEditor = null; CarStatus = ""; }

    [RelayCommand]
    private void DeleteCar()
    {
        if (_profileStore is null || SelectedProfile is not { IsOwn: true } p) { CarStatus = L.T("Cars_OnlyYourOwnCarModelsCan"); return; }
        try
        {
            _profileStore.Delete(p);
            Profiles.Remove(p);
            SelectedProfile = Profiles.FirstOrDefault();
            CarStatus = $"Borttagen: {p.DisplayName}";
        }
        catch (Exception ex) { CarStatus = ex.Message; }
    }

    // ---------------- Dela: export / import ----------------

    /// <summary>Exporterar vald bilmodell som .cbs, med nuvarande kodning, eget tema/skal och egna testfall för modellen.</summary>
    public void ExportCar(string path, bool withCurrentCoding)
    {
        if (SelectedProfile is null || _car is null) return;
        try
        {
            var p = ProfileStore.Clone(SelectedProfile);
            if (withCurrentCoding)
                foreach (var e in p.Ecus) e.Coding = Convert.ToHexString(_car.Coding.Get(e.Key));
            var pkg = new CarPackage { Profile = p };

            // Eget tema / skal som valts för modellen följer med.
            var themeId = BuiltInThemes.Resolve(_appSettings.ThemeByProfile.GetValueOrDefault(p.Id, BuiltInThemes.CarDefault), p.Id);
            var theme = _themeStore.LoadAll().FirstOrDefault(t => t.Id == themeId);
            if (theme is not null) { pkg.Extras["tema/theme.json"] = System.Text.Encoding.UTF8.GetBytes(theme.ToJson()); p.Theme = theme.Id; }
            var skinId = BuiltInSkins.Resolve(_appSettings.SkinByProfile.GetValueOrDefault(p.Id, BuiltInSkins.CarDefault), p.Id);
            var skin = _skinStore.LoadAll().FirstOrDefault(s => s.Id == skinId);
            if (skin?.Folder is { } dir)
                foreach (var f in Directory.GetFiles(dir)) pkg.Extras["skal/" + Path.GetFileName(f)] = File.ReadAllBytes(f);
            // Egna testfall som hör till modellen.
            foreach (var t in _testStore.LoadAll().Where(t => t.FilePath is not null && t.Profile == SelectedProfile.Id))
                pkg.Extras["testfall/" + Path.GetFileName(t.FilePath!)] = File.ReadAllBytes(t.FilePath!);

            pkg.Save(path);
            CarStatus = $"Exporterad: {path}" + (pkg.Extras.Count > 0 ? $" (med {string.Join(", ", pkg.Extras.Keys.Select(k => k.Split('/')[0]).Distinct())})" : "");
        }
        catch (Exception ex) { CarStatus = L.T("Cars_CouldNotExport") + ex.Message; }
    }

    public void ImportCar(string path)
    {
        if (_profileStore is null) return;
        var temp = Path.Combine(Path.GetTempPath(), "cbs-" + Guid.NewGuid().ToString("N"));
        try
        {
            var pkg = _profileStore.Import(path);
            var p = pkg.Profile;
            var notes = new List<string>();
            if (pkg.Extras.Count > 0)
            {
                foreach (var (name, bytes) in pkg.Extras)
                {
                    var target = Path.Combine(temp, name.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.WriteAllBytes(target, bytes);
                }
                if (File.Exists(Path.Combine(temp, "tema", "theme.json")))
                {
                    var t = _themeStore.Import(Path.Combine(temp, "tema", "theme.json"));
                    _appSettings.ThemeByProfile[p.Id] = t.Id;
                    notes.Add("tema");
                }
                if (File.Exists(Path.Combine(temp, "skal", "skin.json")))
                {
                    var s = _skinStore.Import(Path.Combine(temp, "skal"));
                    _appSettings.SkinByProfile[p.Id] = s.Id;
                    notes.Add("skal");
                }
                if (Directory.Exists(Path.Combine(temp, "testfall")))
                    foreach (var f in Directory.GetFiles(Path.Combine(temp, "testfall"), "*.json"))
                    {
                        try { _testStore.Import(f); notes.Add("testfall"); } catch { }
                    }
                _appSettings.Save();
                ReloadLookOptions();
                InitTests();
            }
            ReloadOwnProfiles(p.Id);
            CarStatus = $"Importerad: {p.DisplayName}" + (notes.Count > 0 ? $" (med {string.Join(", ", notes.Distinct())})" : "");
        }
        catch (Exception ex) { CarStatus = L.T("Cars_CouldNotImport") + ex.Message; }
        finally { if (Directory.Exists(temp)) Directory.Delete(temp, true); }
    }
}

/// <summary>Standardformat: ODX/PDX, VSS-JSON, Torque/Car Scanner-CSV och felkodstexter (CSV).</summary>
public sealed partial class MainViewModel
{
    /// <summary>Importerar .cbs/.cyberbil (Cyberdromes paket) eller .pdx/.odx/.odx-d (ASAM ODX).</summary>
    public void ImportCarAny(string path)
    {
        if (CarPackage.IsPackage(path)) { ImportCar(path); return; }
        if (_profileStore is null) return;
        try
        {
            var res = Cyberdrome.Core.Profiles.Odx.OdxImporter.Import(path);
            var p = res.Profile;
            var wanted = p.Id;
            p.Id = _profileStore.UniqueId(p.Id);
            if (p.Id != wanted && !p.Model.EndsWith("(importerad)", StringComparison.Ordinal)) p.Model += " (importerad)";
            var errors = ProfileLoader.Validate(p);
            if (errors.Count > 0) throw new InvalidDataException(string.Join(" ", errors));
            _profileStore.Save(p);
            ReloadOwnProfiles(p.Id);
            var msg = L.F("Cars_ImportedFromODX01Control", p.DisplayName, p.Ecus.Count, p.DtcDescriptions?.Count ?? 0);
            if (res.Notes.Count > 0) msg += "\nNoteringar: " + string.Join("; ", res.Notes);
            if (res.Ignored.Count > 0) msg += L.T("Cars_IgnoredNotSupportedByTheSimulator") + string.Join("; ", res.Ignored);
            CarStatus = msg;
        }
        catch (Exception ex) { CarStatus = L.T("Cars_CouldNotImportODX") + ex.Message; }
    }

    public void ExportOdx(string path)
    {
        if (SelectedProfile is null) return;
        try
        {
            var p = ProfileStore.Clone(SelectedProfile);
            if (_car is not null && ReferenceEquals(_car.Coding.Profile, SelectedProfile))
                foreach (var e in p.Ecus) e.Coding = Convert.ToHexString(_car.Coding.Get(e.Key));
            if (path.EndsWith(".pdx", StringComparison.OrdinalIgnoreCase)) Cyberdrome.Core.Profiles.Odx.OdxExporter.SavePdx(p, path);
            else File.WriteAllText(path, Cyberdrome.Core.Profiles.Odx.OdxExporter.ToOdxString(p), new System.Text.UTF8Encoding(false));
            CarStatus = L.F("Cars_ExportedAsODX220", path);
        }
        catch (Exception ex) { CarStatus = L.T("Cars_CouldNotExportODX") + ex.Message; }
    }

    /// <summary>Egen modell att ändra i: vald modell om den är egen, annars en ny kopia.</summary>
    private CarProfile? EditableProfile(out string note)
    {
        note = "";
        if (_profileStore is null || SelectedProfile is null) return null;
        if (SelectedProfile.IsOwn) return SelectedProfile;
        var copy = _profileStore.Duplicate(SelectedProfile);
        note = L.F("Cars_BuiltInModelSavedInThe", copy.DisplayName);
        return copy;
    }

    private void SaveEdited(CarProfile p)
    {
        _profileStore!.Save(p);
        if (ReferenceEquals(p, SelectedProfile)) { OnPropertyChanged(nameof(SelectedProfile)); RefreshDtcToggleTexts(); }
        else ReloadOwnProfiles(p.Id);
    }

    public void ImportDtcTexts(string path)
    {
        try
        {
            var problems = new List<string>();
            var texts = DtcCsv.Parse(File.ReadAllText(path), problems);
            if (texts.Count == 0) throw new InvalidDataException(L.T("Cars_NoTroubleCodesFoundFormatP0300"));
            var p = EditableProfile(out var note);
            if (p is null) return;
            p.DtcDescriptions ??= new(StringComparer.OrdinalIgnoreCase);
            foreach (var (k, v) in texts) p.DtcDescriptions[k] = v;
            SaveEdited(p);
            CarStatus = L.F("Cars_0TroubleCodeTextsLoaded1", texts.Count, note) + (problems.Count > 0 ? L.T("Cars_Skipped") + string.Join("; ", problems.Take(5)) : "");
        }
        catch (Exception ex) { CarStatus = L.T("Cars_CouldNotReadTroubleCodeTexts") + ex.Message; }
    }

    public void ImportCustomPids(string path)
    {
        try
        {
            var problems = new List<string>();
            var pids = TorqueCsv.Parse(File.ReadAllText(path), problems);
            if (pids.Count == 0) throw new InvalidDataException(L.T("Cars_NoPIDsFoundFormatNameShortName"));
            var p = EditableProfile(out var note);
            if (p is null) return;
            p.CustomPids ??= [];
            foreach (var n in pids)
            {
                p.CustomPids.RemoveAll(o => o.Request == n.Request && o.Header == n.Header);
                p.CustomPids.Add(n);
            }
            var errors = ProfileLoader.Validate(p);
            if (errors.Count > 0) throw new InvalidDataException(string.Join(" ", errors));
            SaveEdited(p);
            CarStatus = L.F("Cars_0CustomPIDsLoaded1The", pids.Count, note) + (problems.Count > 0 ? L.T("Cars_Skipped") + string.Join("; ", problems.Take(5)) : "");
        }
        catch (Exception ex) { CarStatus = L.T("Cars_CouldNotReadThePIDList") + ex.Message; }
    }

    public void ExportCustomPids(string path)
    {
        if (SelectedProfile is null) return;
        try
        {
            File.WriteAllText(path, TorqueCsv.Write(TorqueCsv.FromProfile(SelectedProfile)), new System.Text.UTF8Encoding(false));
            CarStatus = L.F("Cars_PIDListExportedTorqueCarScanner", path);
        }
        catch (Exception ex) { CarStatus = L.T("Cars_CouldNotExportThePIDList") + ex.Message; }
    }

    public void ExportVss(string path)
    {
        if (SelectedProfile is null || _car is null) return;
        try
        {
            File.WriteAllText(path, Vss.Export(SelectedProfile, _car.State), new System.Text.UTF8Encoding(false));
            CarStatus = L.F("Cars_ValuesExportedAsVSSJSON0", path);
        }
        catch (Exception ex) { CarStatus = L.T("Cars_CouldNotExportVSS") + ex.Message; }
    }

    public void ImportVss(string path)
    {
        if (_car is null) return;
        try
        {
            var unknown = Vss.Import(File.ReadAllText(path), _car.State);
            CarStatus = L.T("Cars_VSSValuesLoadedIntoTheCar") + (unknown.Count > 0 ? L.T("Cars_UnknownNames") + string.Join(", ", unknown.Take(8)) : "");
        }
        catch (Exception ex) { CarStatus = L.T("Cars_CouldNotReadVSS") + ex.Message; }
    }
}

public sealed partial class MainViewModel
{
    private void RefreshDtcToggleTexts() => UpdateDtcs();
}
