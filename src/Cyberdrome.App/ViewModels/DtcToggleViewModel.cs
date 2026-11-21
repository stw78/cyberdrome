// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

using CommunityToolkit.Mvvm.ComponentModel;
using Cyberdrome.Core;

namespace Cyberdrome.App.ViewModels;

/// <summary>Ett snabbval för en vanlig felkod (av/på).</summary>
public sealed partial class DtcToggleViewModel : ObservableObject
{
    private readonly VehicleState _state;

    public DtcToggleViewModel(string code, string description, VehicleState state)
    {
        Code = code;
        Description = description;
        _state = state;
    }

    public string Code { get; }
    public string Description { get; }
    public string Label => $"{Code} – {Description}";

    public bool IsOn
    {
        get => _state.Dtcs.Contains(Code);
        set
        {
            if (value == IsOn) return;
            if (value) _state.AddDtc(Code); else _state.RemoveDtc(Code);
            OnPropertyChanged();
        }
    }

    public void Refresh() => OnPropertyChanged(nameof(IsOn));

    /// <summary>Vanliga felkoder (allmänna SAE-koder, samma för alla märken).</summary>
    public static readonly (string Code, string Description)[] Common =
    [
        ("P0300", L.T("DtcToggleViewModel_RandomMisfire")),
        ("P0301", L.T("DtcToggleViewModel_MisfireCylinder1")),
        ("P0171", L.T("DtcToggleViewModel_SystemTooLeanBank1")),
        ("P0420", L.T("DtcToggleViewModel_CatalystEfficiencyBelowThreshold")),
        ("P0128", L.T("DtcToggleViewModel_ThermostatTemperatureTooLow")),
        ("P0562", L.T("DtcToggleViewModel_SystemVoltageLow")),
        ("C0035", L.T("DtcToggleViewModel_WheelSpeedSensorFrontLeft")),
        ("U0100", L.T("DtcToggleViewModel_LostCommunicationWithEngineControlUnit")),
    ];
}
