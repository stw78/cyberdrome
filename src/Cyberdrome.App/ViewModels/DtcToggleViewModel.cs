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
        ("P0300", "slumpmässig feltändning"),
        ("P0301", "feltändning cylinder 1"),
        ("P0171", "för magert, bank 1"),
        ("P0420", "katalysator, låg verkningsgrad"),
        ("P0128", "termostat, för låg temperatur"),
        ("P0562", "låg systemspänning"),
        ("C0035", "hjulhastighetsgivare vänster fram"),
        ("U0100", "tappad kommunikation med motorstyrdon"),
    ];
}
