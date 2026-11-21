// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

namespace Cyberdrome.Core.Drives;

/// <summary>
/// Spelar upp en körning. Tiden drivs av <see cref="Advance"/> (inte av klockan), så samma
/// steg ger alltid exakt samma värden – deterministiskt för tester och appar.
/// </summary>
public sealed class DrivePlayer
{
    public DrivePlayer(Drive drive) => Drive = drive;

    public Drive Drive { get; }

    /// <summary>Position i körningen, sekunder.</summary>
    public double Position { get; private set; }

    /// <summary>Uppspelningshastighet: 1, 2 eller 5 gånger.</summary>
    public double Rate { get; set; } = 1;

    public bool Loop { get; set; }
    public bool Paused { get; set; }

    /// <summary>True när körningen spelats klart (och inte går i slinga).</summary>
    public bool Finished { get; private set; }

    public void Seek(double seconds)
    {
        Position = Math.Clamp(seconds, 0, Drive.Duration);
        Finished = false;
    }

    /// <summary>Flyttar fram <paramref name="realSeconds"/> verklig tid (gånger <see cref="Rate"/>) och ger värdena där.</summary>
    public DriveSample Advance(double realSeconds)
    {
        if (!Paused && !Finished && realSeconds > 0)
        {
            double pos = Position + realSeconds * Rate;
            double dur = Drive.Duration;
            if (pos >= dur)
            {
                if (Loop && dur > 0) pos %= dur;
                else { pos = dur; Finished = true; }
            }
            Position = pos;
        }
        return Drive.At(Position);
    }

    /// <summary>Värdena där uppspelningen står just nu.</summary>
    public DriveSample Current => Drive.At(Position);
}

/// <summary>Spelar in bilens värden med jämna mellanrum.</summary>
public sealed class DriveRecorder
{
    private readonly List<DriveSample> _samples = new();
    private double _t;

    public double Elapsed => _t;
    public int Count => _samples.Count;

    /// <summary>Tar ett prov. <paramref name="dtSeconds"/> = tid sedan förra provet (0 för första).</summary>
    public void Capture(VehicleState s, double dtSeconds)
    {
        if (_samples.Count > 0) _t += Math.Max(0, dtSeconds);
        _samples.Add(DriveSample.From(s, Math.Round(_t, 3)));
    }

    public Drive ToDrive(string name) => new()
    {
        Name = name,
        Description = "Inspelad i Cyberdrome",
        Created = DateTime.Now,
        Samples = _samples.ToList(),
    };
}
