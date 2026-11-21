// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cyberdrome.Core.CarDb;
using Cyberdrome.Core.Profiles;
using Cyberdrome.Vm.Skins;
using Cyberdrome.Vm.Themes;

namespace Cyberdrome.App.ViewModels;

/// <summary>Sidan Bildatabas: sök märke/modell/år, avkoda VIN, se OBDb-signaler och skapa en bilmodell.</summary>
public sealed partial class MainViewModel
{
    private IBilDatabas? _carDb;
    private IBilDatabas CarDb => _carDb ??= LocalBilDatabas.OpenDefault();

    public string ObdbAttribution => ObdbClient.Attribution;

    [ObservableProperty] private string _carDbQuery = "";
    [ObservableProperty] private string _carDbYear = "";
    [ObservableProperty] private string _carDbMake = "";
    [ObservableProperty] private string _carDbFrom = "2015";
    [ObservableProperty] private string _carDbTo = DateTime.Now.Year.ToString();
    [ObservableProperty] private string _carDbVin = "";
    [ObservableProperty] private string _carDbStatus = "";
    [ObservableProperty] private string _carDbDetails = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanUseCarDb))] private bool _carDbBusy;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasCarDbHit))] private CarDbHit? _selectedCarDbHit;
    public bool HasCarDbHit => SelectedCarDbHit is not null;
    public bool CanUseCarDb => !CarDbBusy;
    public ObservableCollection<CarDbHit> CarDbHits { get; } = new();

    private ObdbSignalSet? _hitSignals;
    private VinInfo? _lastVin;

    private void UpdateCarDbStats()
    {
        try
        {
            var (makes, models, vins, obdb) = CarDb.Stats();
            CarDbStatus = L.F("CarDb_Database0Makes1ModelYears", makes, models, vins, obdb);
        }
        catch (Exception ex) { CarDbStatus = L.T("CarDb_TheCarDatabaseCouldNotBe") + ex.Message; }
    }

    private async Task CarDbWork(string start, Func<IProgress<string>, Task<string>> work)
    {
        CarDbBusy = true;
        CarDbStatus = start;
        try
        {
            var progress = new Progress<string>(s => CarDbStatus = s);
            var msg = await Task.Run(() => work(progress));
            UpdateCarDbStats();
            CarDbStatus = msg + "  " + CarDbStatus;
        }
        catch (Exception ex) { CarDbStatus = "Fel: " + ex.Message + L.T("CarDb_WhatIsAlreadyInTheDatabase"); }
        finally { CarDbBusy = false; }
    }

    [RelayCommand]
    private void SearchCarDb()
    {
        int? year = int.TryParse(CarDbYear, out var y) ? y : null;
        CarDbHits.Clear();
        foreach (var h in CarDb.Search(CarDbQuery, year)) CarDbHits.Add(h);
        if (CarDbHits.Count == 0) CarDbDetails = L.T("CarDb_NoResultsGetMakesModelsFrom");
        UpdateCarDbStats();
        CarDbStatus = L.F("CarDb_0Results", CarDbHits.Count) + CarDbStatus;
    }

    [RelayCommand]
    private Task FetchCarMakes() => CarDbWork(L.T("CarDb_GettingCarMakesFromNHTSAVPIC"), async _ => L.F("CarDb_0MakesDownloaded", await CarDb.RefreshMakesAsync()));

    [RelayCommand]
    private Task FetchCarModels()
    {
        if (string.IsNullOrWhiteSpace(CarDbMake)) { CarDbStatus = L.T("CarDb_EnterAMakeEGToyota"); return Task.CompletedTask; }
        if (!int.TryParse(CarDbFrom, out var from) || !int.TryParse(CarDbTo, out var to) || from > to || to - from > 40) { CarDbStatus = L.T("CarDb_EnterModelYearsEG2015"); return Task.CompletedTask; }
        var make = CarDbMake.Trim();
        return CarDbWork(L.F("CarDb_Getting012FromVPIC", make, from, to), async p =>
        {
            var n = await CarDb.FetchModelsAsync(make, from, to, p);
            return L.F("CarDb_0NewModelYearsFor1", n, make);
        });
    }

    [RelayCommand]
    private Task FetchObdbList() => CarDbWork(L.T("CarDb_GettingTheVehicleListFromOBDb"), async p => L.F("CarDb_0VehiclesWithManufacturerSignalsIn", await CarDb.RefreshObdbAsync(p)));

    [RelayCommand]
    private Task DecodeVin()
    {
        var vin = CarDbVin.Trim().ToUpperInvariant();
        if (vin.Length != 17) { CarDbStatus = L.T("CarDb_AVINHas17Characters"); return Task.CompletedTask; }
        return CarDbWork(L.T("CarDb_DecodingVIN"), async _ =>
        {
            var v = await CarDb.DecodeVinAsync(vin);
            _lastVin = v;
            var text = $"VIN {v.Vin}: {(v.HasModel ? $"{v.Make} {v.Model} {v.Year}" : L.T("CarDb_ModelUnknown"))}" +
                       L.F("CarDb_Manufacturer0Body1Fuel2", v.Manufacturer, v.BodyClass, v.FuelType, v.Engine, v.PlantCountry) +
                       (v.ErrorText.Length > 0 ? L.F("CarDb_NoteFromVPIC0", v.ErrorText) : "");
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                CarDbDetails = text;
                if (v.HasModel)
                {
                    CarDbQuery = $"{v.Make} {v.Model}";
                    CarDbYear = v.Year.ToString()!;
                    SearchCarDb();
                    SelectedCarDbHit = CarDbHits.FirstOrDefault();
                }
            });
            return v.HasModel ? L.F("CarDb_VINDecoded012", v.Make, v.Model, v.Year) : L.T("CarDb_TheVINCouldNotBeFully");
        });
    }

    partial void OnSelectedCarDbHitChanged(CarDbHit? value)
    {
        _hitSignals = null;
        if (value is null) return;
        var head = L.F("CarDb_01ModelYears2Sources", value.Make, value.Model, value.Years, value.SourceText);
        if (value.Obdb is null) { CarDbDetails = head + L.T("CarDb_OBDbHasNoManufacturerSignalsFor"); return; }
        CarDbDetails = head + L.T("CarDb_GettingManufacturerSignalsFromOBDb");
        _ = LoadSignalsAsync(value, head);
    }

    private async Task LoadSignalsAsync(CarDbHit hit, string head)
    {
        try
        {
            var set = await Task.Run(() => CarDb.GetSignalsAsync(hit.Make, hit.Model));
            if (!ReferenceEquals(SelectedCarDbHit, hit)) return;
            _hitSignals = set;
            if (set is null) { CarDbDetails = head + L.T("CarDb_OBDbNoSignalFile"); return; }
            if (set.SignalCount == 0) { CarDbDetails = head + L.F("CarDb_OBDbTheRepository0ExistsBut", set.Repo); return; }
            if (!set.Repo.Contains('-')) head += L.F("CarDb_TheModelHasNoSignalsOf", set.Repo);
            var lines = set.Commands.Take(60).SelectMany(c => c.Signals.Select(s =>
                $"  {c.Header}  {c.Service} {c.Pid,-6} {s.Name} [{s.Unit}]" + (c.FilterFrom is not null || c.FilterTo is not null ? L.F("CarDb_Years01", c.FilterFrom, c.FilterTo) : "")));
            CarDbDetails = head + L.F("CarDb_OBDb0SignalsIn1Requests", set.SignalCount, set.Commands.Count, ObdbClient.Attribution) + string.Join("\n", lines.Take(80)) +
                           (set.SignalCount > 80 ? L.F("CarDb_And0More", set.SignalCount - 80) : "");
        }
        catch (Exception ex) { if (ReferenceEquals(SelectedCarDbHit, hit)) CarDbDetails = head + L.T("CarDb_CouldNotGetTheOBDbSignals") + ex.Message; }
    }

    [RelayCommand]
    private void CreateCarFromDb()
    {
        if (SelectedCarDbHit is not { } hit || _profileStore is null) return;
        try
        {
            int year;
            if (int.TryParse(CarDbYear, out var y)) year = y;
            else if (hit.ToYear is { } t) year = t;
            else { CarDbStatus = L.F("CarDb_TheModelYearFor01", hit.Make, hit.Model); return; }
            var notes = new List<string>();
            var vin = _lastVin is { } v && CarModelDb.Key(v.Make, v.Model) == CarModelDb.Key(hit.Make, hit.Model) ? v : null;
            var builtIns = Profiles.Where(p => !p.IsOwn).ToList();
            var p = CarProfileFactory.Create(hit.Make, hit.Model, year, _hitSignals, builtIns, _profileStore, vin, notes, out var template);
            p.Theme = BuiltInThemes.Resolve(BuiltInThemes.CarDefault, template.Id);
            var errors = ProfileLoader.Validate(p);
            if (errors.Count > 0) throw new InvalidDataException(string.Join(" ", errors.Take(5)));
            _profileStore.Save(p);
            // Tema och skal: samma som grundmodellen (inga logotyper).
            _appSettings.ThemeByProfile[p.Id] = p.Theme;
            _appSettings.SkinByProfile[p.Id] = BuiltInSkins.Resolve(BuiltInSkins.CarDefault, template.Id);
            _appSettings.Save();
            ReloadOwnProfiles(p.Id);
            ReloadLookOptions();
            CarDbStatus = L.F("CarDb_NewCarModelSelected0", p.DisplayName) + string.Join(" ", notes);
        }
        catch (Exception ex) { CarDbStatus = L.T("CarDb_CouldNotCreateTheCarModel") + ex.Message; }
    }
}

public sealed partial class MainViewModel
{
    [RelayCommand]
    private Task FetchWikidata()
    {
        if (string.IsNullOrWhiteSpace(CarDbMake)) { CarDbStatus = L.T("CarDb_EnterAMakeEGSaab"); return Task.CompletedTask; }
        var make = CarDbMake.Trim();
        return CarDbWork(L.F("CarDb_Getting0ModelsFromWikidata", make), async _ =>
        {
            var n = await CarDb.FetchWikidataAsync(make, force: true);
            return L.F("CarDb_0NewModelRowsFor1", n, make);
        });
    }
}

/// <summary>Standard-OBD-bil när modellen inte finns i någon källa, och import av signaler i OBDb-format.</summary>
public sealed partial class MainViewModel
{
    [ObservableProperty] private string _stdMake = "";
    [ObservableProperty] private string _stdModel = "";
    [ObservableProperty] private string _stdYear = "";

    /// <summary>Skapar en bilmodell med bara standard-OBD-II (samma som när OBDb saknar modellen).</summary>
    [RelayCommand]
    private void CreateStandardCar()
    {
        if (_profileStore is null) return;
        var make = StdMake.Trim();
        var model = StdModel.Trim();
        if (make.Length == 0 || model.Length == 0) { CarDbStatus = L.T("CarDb_EnterMakeAndModelForThe"); return; }
        if (!int.TryParse(StdYear, out var year) || year is < 1950 or > 2100) { CarDbStatus = L.T("CarDb_EnterTheModelYearEG"); return; }
        try
        {
            var notes = new List<string>();
            var p = CarProfileFactory.Create(make, model, year, null, Profiles.Where(x => !x.IsOwn).ToList(), _profileStore, null, notes, out var template);
            p.Theme = BuiltInThemes.Resolve(BuiltInThemes.CarDefault, template.Id);
            var errors = ProfileLoader.Validate(p);
            if (errors.Count > 0) throw new InvalidDataException(string.Join(" ", errors.Take(5)));
            _profileStore.Save(p);
            _appSettings.ThemeByProfile[p.Id] = p.Theme;
            _appSettings.SkinByProfile[p.Id] = BuiltInSkins.Resolve(BuiltInSkins.CarDefault, template.Id);
            _appSettings.Save();
            ReloadOwnProfiles(p.Id);
            ReloadLookOptions();
            CarDbStatus = L.F("CarDb_StandardOBDCarSelected0", p.DisplayName) + string.Join(" ", notes.Where(n => n != L.T("BilDatabas_OBDbDoesNotHaveTheModel"))) +
                          L.T("CarDb_ManufacturerSignalsCanBeAddedLater");
        }
        catch (Exception ex) { CarDbStatus = L.T("CarDb_CouldNotCreateTheCarModel") + ex.Message; }
    }

    [RelayCommand]
    private void GoToCars() => PageIndex = 7;

    /// <summary>Lägger till signaler från en JSON-fil i OBDb-format (signalsets/v3) i vald egen bilmodell.</summary>
    public void ImportObdbJson(string path)
    {
        try
        {
            var set = ObdbSignalSet.Parse(File.ReadAllText(path), Path.GetFileNameWithoutExtension(path));
            if (set.SignalCount == 0) throw new InvalidDataException(L.T("CarDb_TheFileContainsNoSignals"));
            var p = EditableProfile(out var note);
            if (p is null) return;
            var notes = new List<string>();
            CarProfileFactory.ApplySignals(p, set, notes, attribution: p.Attribution ?? L.F("CarDb_SignalsFrom0OBDbFormat", Path.GetFileName(path)), source: Path.GetFileName(path));
            var errors = ProfileLoader.Validate(p);
            if (errors.Count > 0) throw new InvalidDataException(string.Join(" ", errors.Take(5)));
            SaveEdited(p);
            CarStatus = string.Join(" ", notes) + note + L.T("CarDb_TheSimulatorNowAnswersThem");
        }
        catch (Exception ex) { CarStatus = L.T("CarDb_CouldNotReadTheSignalFile") + ex.Message; }
    }
}
