// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

namespace Cyberdrome.Core;

/// <summary>Växelläge som visas i UI och kan läsas via OBD.</summary>
public enum Gear { P, R, N, D }

/// <summary>
/// Bilens nuvarande värden. Både UI och den virtuella OBD-adaptern läser härifrån,
/// så Android och programmet ser alltid samma värden. Trådsäker.
/// </summary>
public sealed class VehicleState
{
    private readonly object _lock = new();
    private double _speedKmh;
    private double _rpm = 800;
    private double _coolantC = 88;
    private double _intakeC = 25;
    private double _ambientC = 7;
    private double _fuelPercent = 62;
    private double _throttlePercent = 12;
    private double _batteryVolt = 14.1;
    private Gear _gear = Gear.P;
    private bool _ignition = true;
    private bool _dark;
    private readonly List<string> _dtcs = new();

    /// <summary>Körs varje gång något värde ändras.</summary>
    public event EventHandler? Changed;

    public double SpeedKmh { get => Get(() => _speedKmh); set => Set(() => _speedKmh = Math.Clamp(value, 0, 255)); }
    public double Rpm { get => Get(() => _rpm); set => Set(() => _rpm = Math.Clamp(value, 0, 16383)); }
    public double CoolantC { get => Get(() => _coolantC); set => Set(() => _coolantC = Math.Clamp(value, -40, 215)); }
    public double IntakeC { get => Get(() => _intakeC); set => Set(() => _intakeC = Math.Clamp(value, -40, 215)); }
    public double AmbientC { get => Get(() => _ambientC); set => Set(() => _ambientC = Math.Clamp(value, -40, 215)); }
    public double FuelPercent { get => Get(() => _fuelPercent); set => Set(() => _fuelPercent = Math.Clamp(value, 0, 100)); }
    public double ThrottlePercent { get => Get(() => _throttlePercent); set => Set(() => _throttlePercent = Math.Clamp(value, 0, 100)); }
    public double BatteryVolt { get => Get(() => _batteryVolt); set => Set(() => _batteryVolt = Math.Clamp(value, 0, 20)); }
    public Gear Gear { get => Get(() => _gear); set => Set(() => _gear = value); }
    public bool Ignition { get => Get(() => _ignition); set => Set(() => _ignition = value); }
    public bool Dark { get => Get(() => _dark); set => Set(() => _dark = value); }

    /// <summary>Aktiva felkoder, t.ex. "P0301".</summary>
    public IReadOnlyList<string> Dtcs { get { lock (_lock) return _dtcs.ToArray(); } }

    public void SetDtcs(IEnumerable<string> codes) => Set(() => { _dtcs.Clear(); _dtcs.AddRange(codes); });
    public void AddDtc(string code) => Set(() => { if (!_dtcs.Contains(code)) _dtcs.Add(code); });
    public void RemoveDtc(string code) => Set(() => _dtcs.Remove(code));
    public void ClearDtcs() => Set(() => _dtcs.Clear());

    private T Get<T>(Func<T> f) { lock (_lock) return f(); }

    private void Set(Action a)
    {
        lock (_lock) a();
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
