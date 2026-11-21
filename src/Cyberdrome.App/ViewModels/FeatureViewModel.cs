// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

using CommunityToolkit.Mvvm.ComponentModel;
using Cyberdrome.Core.Profiles;

namespace Cyberdrome.App.ViewModels;

/// <summary>En rad i listan "Dolda funktioner".</summary>
public sealed partial class FeatureViewModel : ObservableObject
{
    private readonly CodingStore _store;
    private bool _syncing;

    public FeatureViewModel(CodingFeature feature, CodingStore store)
    {
        Feature = feature;
        _store = store;
        _isOn = store.IsOn(feature);
        EcuName = store.Profile.Ecus.First(e => e.Key == feature.Ecu).Name;
    }

    public CodingFeature Feature { get; }
    public string Name => Feature.Name;
    public string Description => Feature.Description;
    public string EcuName { get; }
    public string Where => $"{EcuName} · byte {Feature.Byte}, bit {Feature.Bit}";

    [ObservableProperty]
    private bool _isOn;

    partial void OnIsOnChanged(bool value)
    {
        if (!_syncing) _store.SetFeature(Feature, value);
    }

    /// <summary>Läs om från kodningen (t.ex. efter att en app skrivit via OBD).</summary>
    public void Refresh()
    {
        _syncing = true;
        IsOn = _store.IsOn(Feature);
        _syncing = false;
    }
}
