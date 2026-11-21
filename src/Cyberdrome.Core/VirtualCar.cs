// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

using Cyberdrome.Core.Profiles;

namespace Cyberdrome.Core;

/// <summary>
/// Den virtuella bilen: vald bilprofil + levande värden + kodning.
/// Det är detta som den virtuella OBD-adaptern "pratar med".
/// </summary>
public sealed class VirtualCar
{
    private CodingStore _coding;

    public VirtualCar(CarProfile profile)
    {
        _coding = new CodingStore(profile);
        State.SetDtcs(profile.StartDtcs);
    }

    public VehicleState State { get; } = new();

    /// <summary>Riktig bil via adapter (vidarekoppling). Null = simulerad bil.</summary>
    public IObdBridge? Bridge { get; set; }

    public CodingStore Coding => Volatile.Read(ref _coding);
    public CarProfile Profile => Coding.Profile;

    /// <summary>Körs när användaren byter bilmodell.</summary>
    public event EventHandler? ProfileChanged;

    public void LoadProfile(CarProfile profile)
    {
        Volatile.Write(ref _coding, new CodingStore(profile));
        State.SetDtcs(profile.StartDtcs);
        ProfileChanged?.Invoke(this, EventArgs.Empty);
    }
}
