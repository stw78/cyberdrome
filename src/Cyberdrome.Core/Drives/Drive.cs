// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cyberdrome.Core.Drives;

/// <summary>Ett ögonblick i en körning. <see cref="T"/> är sekunder från start.</summary>
public sealed record DriveSample
{
    public double T { get; init; }
    public double Speed { get; init; }
    public double Rpm { get; init; }
    public double Coolant { get; init; }
    public double Fuel { get; init; }
    public double Throttle { get; init; }
    [JsonConverter(typeof(JsonStringEnumConverter<Gear>))]
    public Gear Gear { get; init; }
    public bool Ignition { get; init; } = true;
    public bool Dark { get; init; }
    public string[] Dtcs { get; init; } = [];

    /// <summary>GPS-position (valfri). Interpoleras mellan prover.</summary>
    public double? Lat { get; init; }
    public double? Lon { get; init; }

    /// <summary>Läser av bilens nuvarande värden.</summary>
    public static DriveSample From(VehicleState s, double t) => new()
    {
        T = t,
        Speed = s.SpeedKmh,
        Rpm = s.Rpm,
        Coolant = s.CoolantC,
        Fuel = s.FuelPercent,
        Throttle = s.ThrottlePercent,
        Gear = s.Gear,
        Ignition = s.Ignition,
        Dark = s.Dark,
        Dtcs = s.Dtcs.ToArray(),
    };

    /// <summary>Skriver värdena till bilen (felkoder bara om de ändrats).</summary>
    public void ApplyTo(VehicleState s)
    {
        s.SpeedKmh = Speed;
        s.Rpm = Rpm;
        s.CoolantC = Coolant;
        s.FuelPercent = Fuel;
        s.ThrottlePercent = Throttle;
        if (s.Gear != Gear) s.Gear = Gear;
        if (s.Ignition != Ignition) s.Ignition = Ignition;
        if (s.Dark != Dark) s.Dark = Dark;
        if (!s.Dtcs.SequenceEqual(Dtcs)) s.SetDtcs(Dtcs);
    }
}

/// <summary>En inspelad (eller genererad) körning.</summary>
public sealed class Drive
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public DateTime Created { get; set; } = DateTime.Now;

    /// <summary>Tidsordnade prover (minst ett).</summary>
    public List<DriveSample> Samples { get; set; } = new();

    [JsonIgnore] public double Duration => Samples.Count == 0 ? 0 : Samples[^1].T;
    [JsonIgnore] public double MaxSpeed => Samples.Count == 0 ? 0 : Samples.Max(s => s.Speed);

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public static Drive FromJson(string json)
    {
        var d = JsonSerializer.Deserialize<Drive>(json, Json) ?? throw new InvalidDataException("Tom körning.");
        if (d.Samples.Count == 0) throw new InvalidDataException("Körningen saknar data.");
        d.Samples.Sort((a, b) => a.T.CompareTo(b.T));
        return d;
    }

    /// <summary>
    /// Värdena vid tiden <paramref name="t"/> (sekunder). Siffror interpoleras linjärt mellan proverna;
    /// växel, tändning, mörker och felkoder tas från provet före (de hoppar, de glider inte).
    /// </summary>
    public DriveSample At(double t)
    {
        if (Samples.Count == 0) throw new InvalidOperationException("Körningen saknar data.");
        if (t <= Samples[0].T) return Samples[0] with { T = t };
        if (t >= Samples[^1].T) return Samples[^1] with { T = t };

        // Binärsökning efter sista provet med T <= t.
        int lo = 0, hi = Samples.Count - 1;
        while (hi - lo > 1)
        {
            int mid = (lo + hi) / 2;
            if (Samples[mid].T <= t) lo = mid; else hi = mid;
        }
        var a = Samples[lo];
        var b = Samples[hi];
        double f = b.T == a.T ? 0 : (t - a.T) / (b.T - a.T);
        double L(double x, double y) => x + (y - x) * f;
        double? LN(double? x, double? y) => x is { } xv && y is { } yv ? L(xv, yv) : x;
        return a with
        {
            T = t,
            Lat = LN(a.Lat, b.Lat),
            Lon = LN(a.Lon, b.Lon),
            Speed = L(a.Speed, b.Speed),
            Rpm = L(a.Rpm, b.Rpm),
            Coolant = L(a.Coolant, b.Coolant),
            Fuel = L(a.Fuel, b.Fuel),
            Throttle = L(a.Throttle, b.Throttle),
        };
    }
}
