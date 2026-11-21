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
            CarDbStatus = $"Databasen: {makes} märken, {models} modell/år, {vins} avkodade VIN, {obdb} fordon med tillverkarsignaler (OBDb).";
        }
        catch (Exception ex) { CarDbStatus = "Bildatabasen kunde inte öppnas: " + ex.Message; }
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
        catch (Exception ex) { CarDbStatus = "Fel: " + ex.Message + " (det som redan finns i databasen fungerar utan internet)."; }
        finally { CarDbBusy = false; }
    }

    [RelayCommand]
    private void SearchCarDb()
    {
        int? year = int.TryParse(CarDbYear, out var y) ? y : null;
        CarDbHits.Clear();
        foreach (var h in CarDb.Search(CarDbQuery, year)) CarDbHits.Add(h);
        if (CarDbHits.Count == 0) CarDbDetails = "Inga träffar. Hämta märken/modeller från vPIC eller listan från OBDb först, eller avkoda ett VIN.";
        UpdateCarDbStats();
        CarDbStatus = $"{CarDbHits.Count} träffar. " + CarDbStatus;
    }

    [RelayCommand]
    private Task FetchCarMakes() => CarDbWork("Hämtar bilmärken från NHTSA vPIC …", async _ => $"{await CarDb.RefreshMakesAsync()} märken hämtade.");

    [RelayCommand]
    private Task FetchCarModels()
    {
        if (string.IsNullOrWhiteSpace(CarDbMake)) { CarDbStatus = "Skriv ett märke, t.ex. Toyota."; return Task.CompletedTask; }
        if (!int.TryParse(CarDbFrom, out var from) || !int.TryParse(CarDbTo, out var to) || from > to || to - from > 40) { CarDbStatus = "Ange årsmodeller, t.ex. 2015–2026."; return Task.CompletedTask; }
        var make = CarDbMake.Trim();
        return CarDbWork($"Hämtar {make} {from}–{to} från vPIC …", async p =>
        {
            var n = await CarDb.FetchModelsAsync(make, from, to, p);
            return $"{n} nya modell/år för {make}.";
        });
    }

    [RelayCommand]
    private Task FetchObdbList() => CarDbWork("Hämtar fordonslistan från OBDb …", async p => $"{await CarDb.RefreshObdbAsync(p)} fordon med tillverkarsignaler i OBDb.");

    [RelayCommand]
    private Task DecodeVin()
    {
        var vin = CarDbVin.Trim().ToUpperInvariant();
        if (vin.Length != 17) { CarDbStatus = "Ett VIN har 17 tecken."; return Task.CompletedTask; }
        return CarDbWork("Avkodar VIN …", async _ =>
        {
            var v = await CarDb.DecodeVinAsync(vin);
            _lastVin = v;
            var text = $"VIN {v.Vin}: {(v.HasModel ? $"{v.Make} {v.Model} {v.Year}" : "modellen okänd")}" +
                       $"\nTillverkare: {v.Manufacturer}\nKaross: {v.BodyClass}\nBränsle: {v.FuelType}\nMotor: {v.Engine}\nFabriksland: {v.PlantCountry}" +
                       (v.ErrorText.Length > 0 ? $"\nAnmärkning från vPIC: {v.ErrorText}" : "");
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
            return v.HasModel ? $"VIN avkodat: {v.Make} {v.Model} {v.Year}." : "VIN:et kunde inte avkodas helt (vPIC har i första hand bilar sålda i USA).";
        });
    }

    partial void OnSelectedCarDbHitChanged(CarDbHit? value)
    {
        _hitSignals = null;
        if (value is null) return;
        var head = $"{value.Make} {value.Model}  ·  årsmodeller {value.Years}  ·  källor: {value.SourceText}";
        if (value.Obdb is null) { CarDbDetails = head + "\nOBDb har inga tillverkarsignaler för modellen – bilmodellen får standard-PID:ar."; return; }
        CarDbDetails = head + "\nHämtar tillverkarsignaler från OBDb …";
        _ = LoadSignalsAsync(value, head);
    }

    private async Task LoadSignalsAsync(CarDbHit hit, string head)
    {
        try
        {
            var set = await Task.Run(() => CarDb.GetSignalsAsync(hit.Make, hit.Model));
            if (!ReferenceEquals(SelectedCarDbHit, hit)) return;
            _hitSignals = set;
            if (set is null) { CarDbDetails = head + "\nOBDb: ingen signalfil."; return; }
            if (set.SignalCount == 0) { CarDbDetails = head + $"\nOBDb: arkivet {set.Repo} finns men innehåller inga signaler ännu – bilmodellen får standard-PID:ar."; return; }
            if (!set.Repo.Contains('-')) head += $"\n(Modellen saknar egna signaler – märkets gemensamma signaler från arkivet {set.Repo} visas.)";
            var lines = set.Commands.Take(60).SelectMany(c => c.Signals.Select(s =>
                $"  {c.Header}  {c.Service} {c.Pid,-6} {s.Name} [{s.Unit}]" + (c.FilterFrom is not null || c.FilterTo is not null ? $"  (år {c.FilterFrom}–{c.FilterTo})" : "")));
            CarDbDetails = head + $"\nOBDb: {set.SignalCount} signaler i {set.Commands.Count} frågor ({ObdbClient.Attribution}):\n" + string.Join("\n", lines.Take(80)) +
                           (set.SignalCount > 80 ? $"\n  … och {set.SignalCount - 80} till" : "");
        }
        catch (Exception ex) { if (ReferenceEquals(SelectedCarDbHit, hit)) CarDbDetails = head + "\nKunde inte hämta OBDb-signalerna: " + ex.Message; }
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
            else { CarDbStatus = $"Årsmodellen för {hit.Make} {hit.Model} är okänd – skriv den i fältet År och tryck Skapa bilmodell igen."; return; }
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
            CarDbStatus = $"Ny bilmodell vald: {p.DisplayName}. " + string.Join(" ", notes);
        }
        catch (Exception ex) { CarDbStatus = "Kunde inte skapa bilmodellen: " + ex.Message; }
    }
}

public sealed partial class MainViewModel
{
    [RelayCommand]
    private Task FetchWikidata()
    {
        if (string.IsNullOrWhiteSpace(CarDbMake)) { CarDbStatus = "Skriv ett märke, t.ex. Saab."; return Task.CompletedTask; }
        var make = CarDbMake.Trim();
        return CarDbWork($"Hämtar {make}-modeller från Wikidata …", async _ =>
        {
            var n = await CarDb.FetchWikidataAsync(make, force: true);
            return $"{n} nya modellrader för {make} från Wikidata.";
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
        if (make.Length == 0 || model.Length == 0) { CarDbStatus = "Skriv märke och modell för standard-OBD-bilen."; return; }
        if (!int.TryParse(StdYear, out var year) || year is < 1950 or > 2100) { CarDbStatus = "Skriv årsmodell, t.ex. 2005."; return; }
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
            CarDbStatus = $"Standard-OBD-bil vald: {p.DisplayName}. " + string.Join(" ", notes.Where(n => !n.StartsWith("OBDb saknar"))) +
                          " Tillverkarsignaler kan läggas till senare på sidan Bilmodeller: \"Importera PID:ar (CSV)\" (Torque/Car Scanner) eller \"Importera signaler (OBDb-JSON)\".";
        }
        catch (Exception ex) { CarDbStatus = "Kunde inte skapa bilmodellen: " + ex.Message; }
    }

    [RelayCommand]
    private void GoToCars() => PageIndex = 7;

    /// <summary>Lägger till signaler från en JSON-fil i OBDb-format (signalsets/v3) i vald egen bilmodell.</summary>
    public void ImportObdbJson(string path)
    {
        try
        {
            var set = ObdbSignalSet.Parse(File.ReadAllText(path), Path.GetFileNameWithoutExtension(path));
            if (set.SignalCount == 0) throw new InvalidDataException("Filen innehåller inga signaler.");
            var p = EditableProfile(out var note);
            if (p is null) return;
            var notes = new List<string>();
            CarProfileFactory.ApplySignals(p, set, notes, attribution: p.Attribution ?? $"Signaler från {Path.GetFileName(path)} (OBDb-format)", source: Path.GetFileName(path));
            var errors = ProfileLoader.Validate(p);
            if (errors.Count > 0) throw new InvalidDataException(string.Join(" ", errors.Take(5)));
            SaveEdited(p);
            CarStatus = string.Join(" ", notes) + note + " Simulatorn svarar nu på dem.";
        }
        catch (Exception ex) { CarStatus = "Kunde inte läsa signalfilen: " + ex.Message; }
    }
}
