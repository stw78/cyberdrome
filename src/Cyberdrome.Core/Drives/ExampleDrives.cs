// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

namespace Cyberdrome.Core.Drives;

/// <summary>
/// Inbyggda exempelkörningar. Egen, genererad data (inga slumptal), så att appar får exakt
/// samma värden varje gång.
/// </summary>
public static class ExampleDrives
{
    public const string City = "Stadskörning 5 min";
    public const string Highway = "Motorväg 10 min";
    public const string ColdStart = "Kallstart";

    public static IReadOnlyList<Drive> All => [CityDrive(), HighwayDrive(), ColdStartDrive()];

    // Påhittade rutter (raka sträckor som ungefär följer gatorna). Koordinater i WGS84.
    public static readonly Geo.GeoPoint[] CityRoute =
    [
        new(59.33258, 18.06490), // Sergels torg
        new(59.33300, 18.07150), // Hamngatan
        new(59.33050, 18.07300), // Kungsträdgårdsgatan
        new(59.32830, 18.07400), // Strömgatan
        new(59.32480, 18.07650), // Skeppsbron
        new(59.31990, 18.07200), // Slussen
        new(59.31930, 18.06000), // Söder Mälarstrand
        new(59.32150, 18.04200), // Västerbron
        new(59.32720, 18.04400), // Norr Mälarstrand
        new(59.33000, 18.05300), // Hantverkargatan
        new(59.33100, 18.05900), // Vasagatan
        new(59.33258, 18.06490), // tillbaka till Sergels torg
    ];

    public static readonly Geo.GeoPoint[] HighwayRoute =
    [
        new(59.35000, 18.04200), // Norrtull
        new(59.36900, 18.00800), // Haga
        new(59.38800, 17.99300), // Järva
        new(59.41700, 17.95200), // Sollentuna
        new(59.45300, 17.92700), // Upplands Väsby
        new(59.52000, 17.91000),
        new(59.58500, 17.90000), // Arlanda
        new(59.65000, 17.88000),
        new(59.72000, 17.80000),
    ];

    public static readonly Geo.GeoPoint ParkedSpot = new(59.31500, 18.08000); // Södermalm

    /// <summary>Lägger till GPS-position i varje prov genom att köra längs rutten med provets fart.</summary>
    public static Drive WithRoute(Drive d, IReadOnlyList<Geo.GeoPoint> points)
    {
        var route = new Geo.GeoRoute(points);
        double traveled = 0;
        var result = new List<DriveSample>(d.Samples.Count);
        for (int i = 0; i < d.Samples.Count; i++)
        {
            var s = d.Samples[i];
            if (i > 0)
            {
                var prev = d.Samples[i - 1];
                traveled += (prev.Speed + s.Speed) / 2 / 3.6 * (s.T - prev.T); // trapetsregel
            }
            var (p, _) = route.At(traveled);
            result.Add(s with { Lat = Math.Round(p.Lat, 6), Lon = Math.Round(p.Lon, 6) });
        }
        d.Samples = result;
        return d;
    }

    private static Drive Make(string name, string description, int seconds, Func<int, DriveSample> at) => new()
    {
        Name = name,
        Description = description,
        Created = new DateTime(2026, 1, 1),
        Samples = Enumerable.Range(0, seconds + 1).Select(at).ToList(),
    };

    /// <summary>Varvtal som ungefär följer en automatlåda: växlar upp var 25:e km/h.</summary>
    private static double RpmFor(double speed, double throttle)
    {
        if (speed < 0.5) return 800;
        double inGear = speed % 25;
        return Math.Round(1100 + inGear * 55 + throttle * 12);
    }

    /// <summary>Stopp-och-kör i stan, 0–50 km/h, 5 minuter.</summary>
    public static Drive CityDrive() => WithRoute(CityDriveRaw(), CityRoute);

    private static Drive CityDriveRaw()
    {
        // En cykel på 40 s: stå still 8 s, accelerera 10 s, rulla 14 s, bromsa 8 s. Toppfarten varierar.
        double[] tops = [30, 50, 40, 50, 25, 45, 50, 35];
        return Make(City, "Stopp-och-kör i stan, 0–50 km/h", 300, t =>
        {
            int cycle = t / 40, s = t % 40;
            double top = tops[cycle % tops.Length];
            double speed, throttle;
            if (t < 3 || t > 296) { speed = 0; throttle = 0; }
            else if (s < 8) { speed = 0; throttle = 0; }
            else if (s < 18) { speed = top * (s - 8) / 10.0; throttle = 35; }
            else if (s < 32) { speed = top; throttle = 12; }
            else { speed = top * (40 - s) / 8.0; throttle = 0; }
            return new DriveSample
            {
                T = t,
                Speed = Math.Round(speed, 1),
                Rpm = RpmFor(speed, throttle),
                Coolant = 88,
                Fuel = Math.Round(62 - t * 0.003, 2),
                Throttle = throttle,
                Gear = t < 3 || t > 296 ? Gear.P : Gear.D,
            };
        });
    }

    /// <summary>Påfart och motorväg i 110–120 km/h, 10 minuter.</summary>
    public static Drive HighwayDrive() => WithRoute(HighwayDriveRaw(), HighwayRoute);

    private static Drive HighwayDriveRaw() => Make(Highway, "Påfart till 110–120 km/h och motorväg", 600, t =>
    {
        double speed, throttle;
        if (t < 3) { speed = 0; throttle = 0; }
        else if (t < 33) { speed = 110.0 * (t - 3) / 30; throttle = 55; }
        else if (t < 570) { speed = 115 + 5 * Math.Sin((t - 33) * 2 * Math.PI / 120); throttle = 22; }
        else { speed = 110.0 * (600 - t) / 30; throttle = 0; }
        return new DriveSample
        {
            T = t,
            Speed = Math.Round(Math.Max(0, speed), 1),
            Rpm = speed < 0.5 ? 800 : Math.Round(1500 + speed * 13 + throttle * 6),
            Coolant = 90,
            Fuel = Math.Round(62 - t * 0.006, 2),
            Throttle = throttle,
            Gear = t < 3 ? Gear.P : Gear.D,
        };
    });

    /// <summary>Kallstart en vintermorgon: kylvätska 5 → 90 °C, högt tomgångsvarv som sjunker.</summary>
    public static Drive ColdStartDrive() => WithRoute(ColdStartDriveRaw(), [ParkedSpot]);

    private static Drive ColdStartDriveRaw() => Make(ColdStart, "Kallstart: kylvätska 5 → 90 °C, tomgång 1300 → 800 rpm", 600, t =>
    {
        double coolant = 90 - 85 * Math.Exp(-t / 150.0);
        double warm = (coolant - 5) / 85; // 0..1
        return new DriveSample
        {
            T = t,
            Speed = 0,
            Rpm = Math.Round(800 + 500 * (1 - warm)),
            Coolant = Math.Round(coolant, 1),
            Fuel = Math.Round(62 - t * 0.0008, 2),
            Throttle = 0,
            Gear = Gear.P,
            Dark = true,
        };
    });
}
