// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cyberdrome.Core.Profiles;

namespace Cyberdrome.App.ViewModels;

/// <summary>En rad för ett styrdon i redigeraren.</summary>
public sealed partial class EcuRow : ObservableObject
{
    [ObservableProperty] private string _key = "";
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _requestId = "7E0";
    [ObservableProperty] private string _responseId = "7E8";
    [ObservableProperty] private string _coding = "00000000";
    [ObservableProperty] private string _codingDid = "0600";
}

/// <summary>En rad för en dold funktion i redigeraren.</summary>
public sealed partial class FeatureRow : ObservableObject
{
    [ObservableProperty] private string _id = "";
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _description = "";
    [ObservableProperty] private string _ecu = "";
    [ObservableProperty] private int _byte;
    [ObservableProperty] private int _bit;
}

/// <summary>Formulär för en egen bilmodell (bygger en CarProfile och validerar den).</summary>
public sealed partial class CarEditorViewModel : ObservableObject
{
    private readonly CarProfile _original;

    public CarEditorViewModel(CarProfile source, bool isNew)
    {
        _original = ProfileStore.Clone(source);
        IsNew = isNew;
        _id = source.Id;
        _make = source.Make;
        _model = source.Model;
        _generation = source.Generation;
        _year = source.Year;
        _vin = source.Vin;
        _protocol = source.Protocol;
        _startDtcs = string.Join(", ", source.StartDtcs);
        _supportedPids = source.SupportedPids is { Count: > 0 } l ? string.Join(", ", l) : "";
        _screenPreset = source.ScreenPreset ?? "";
        _theme = source.Theme ?? "";
        var d = source.DefaultValues;
        _defSpeed = Fmt(d?.Speed); _defRpm = Fmt(d?.Rpm); _defCoolant = Fmt(d?.Coolant); _defFuel = Fmt(d?.Fuel);
        foreach (var e in source.Ecus)
            Ecus.Add(new EcuRow { Key = e.Key, Name = e.Name, RequestId = e.RequestId, ResponseId = e.ResponseId, Coding = e.Coding, CodingDid = e.CodingDid });
        foreach (var f in source.Features)
            Features.Add(new FeatureRow { Id = f.Id, Name = f.Name, Description = f.Description, Ecu = f.Ecu, Byte = f.Byte, Bit = f.Bit });
    }

    private static string Fmt(double? v) => v is { } x ? x.ToString("0.#", CultureInfo.InvariantCulture) : "";

    public bool IsNew { get; }
    public string Title => IsNew ? L.T("CarEditorViewModel_NewCarModelCopy") : L.T("CarEditorViewModel_EditOwnCarModel");

    [ObservableProperty] private string _id;
    [ObservableProperty] private string _make;
    [ObservableProperty] private string _model;
    [ObservableProperty] private string _generation;
    [ObservableProperty] private int _year;
    [ObservableProperty] private string _vin;
    [ObservableProperty] private string _protocol;
    [ObservableProperty] private string _startDtcs;
    [ObservableProperty] private string _supportedPids;
    [ObservableProperty] private string _screenPreset;
    [ObservableProperty] private string _theme;
    [ObservableProperty] private string _defSpeed;
    [ObservableProperty] private string _defRpm;
    [ObservableProperty] private string _defCoolant;
    [ObservableProperty] private string _defFuel;
    [ObservableProperty] private string _errors = "";

    public ObservableCollection<EcuRow> Ecus { get; } = new();
    public ObservableCollection<FeatureRow> Features { get; } = new();

    public string KnownPidsText => L.T("CarEditorViewModel_PossiblePIDs") + string.Join(", ", ProfileLoader.KnownPids.Select(p => p.ToString("X2"))) + " (tomt = alla)";

    [RelayCommand] private void AddEcu() => Ecus.Add(new EcuRow { Key = "styrdon" + (Ecus.Count + 1), Name = L.T("CarEditorViewModel_NewControlUnit"), RequestId = "7E1", ResponseId = "7E9" });
    [RelayCommand] private void RemoveEcu(EcuRow? r) { if (r is not null) Ecus.Remove(r); }
    [RelayCommand] private void AddFeature() => Features.Add(new FeatureRow { Id = "funktion" + (Features.Count + 1), Name = L.T("CarEditorViewModel_NewFeature"), Ecu = Ecus.FirstOrDefault()?.Key ?? "" });
    [RelayCommand] private void RemoveFeature(FeatureRow? r) { if (r is not null) Features.Remove(r); }

    private static double? Num(string s) =>
        double.TryParse(s.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;

    private static List<string> Split(string s) =>
        s.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(x => x.ToUpperInvariant()).ToList();

    /// <summary>Bygger profilen från formuläret. Säkerhetsformeln (UDS 27) behålls från originalet.</summary>
    public CarProfile Build()
    {
        var p = ProfileStore.Clone(_original);
        p.Id = Id.Trim();
        p.Make = Make.Trim(); p.Model = Model.Trim(); p.Generation = Generation.Trim(); p.Year = Year;
        p.Vin = Vin.Trim().ToUpperInvariant();
        p.Protocol = string.IsNullOrWhiteSpace(Protocol) ? "6" : Protocol.Trim();
        p.StartDtcs = Split(StartDtcs);
        var pids = Split(SupportedPids);
        p.SupportedPids = pids.Count > 0 ? pids : null;
        p.ScreenPreset = string.IsNullOrWhiteSpace(ScreenPreset) ? null : ScreenPreset.Trim();
        p.Theme = string.IsNullOrWhiteSpace(Theme) ? null : Theme.Trim();
        var dv = new ProfileDefaults { Speed = Num(DefSpeed), Rpm = Num(DefRpm), Coolant = Num(DefCoolant), Fuel = Num(DefFuel) };
        p.DefaultValues = dv.Speed is null && dv.Rpm is null && dv.Coolant is null && dv.Fuel is null ? null : dv;
        // Behåll varje styrdons egen säkerhetsformel om nyckeln finns kvar.
        var oldSec = _original.Ecus.ToDictionary(e => e.Key, e => e.SecurityAccess);
        p.Ecus = Ecus.Select(e => new EcuProfile
        {
            Key = e.Key.Trim(), Name = e.Name.Trim(), RequestId = e.RequestId.Trim().ToUpperInvariant(),
            ResponseId = e.ResponseId.Trim().ToUpperInvariant(), Coding = e.Coding.Trim().ToUpperInvariant(),
            CodingDid = e.CodingDid.Trim().ToUpperInvariant(), SecurityAccess = oldSec.GetValueOrDefault(e.Key.Trim()),
        }).ToList();
        p.Features = Features.Select(f => new CodingFeature
        {
            Id = f.Id.Trim(), Name = f.Name.Trim(), Description = f.Description.Trim(), Ecu = f.Ecu.Trim(), Byte = f.Byte, Bit = f.Bit,
        }).ToList();
        return p;
    }
}
