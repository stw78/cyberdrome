// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

using Cyberdrome.Core;
using Cyberdrome.Core.Profiles;
using Cyberdrome.Obd;

namespace Cyberdrome.Tests;

/// <summary>UDS 0x27 SecurityAccess i de simulerade styrdonen (påhittad formel).</summary>
public class SecurityAccessTests
{
    private static VirtualCar Golf() =>
        new(ProfileLoader.LoadFolder(Path.Combine(AppContext.BaseDirectory, "profiles")).First(x => x.Id == "vw-golf8"));

    private static string[] Run(Elm327Emulator e, string cmd)
    {
        var r = e.Handle(cmd);
        return r[..^1].Split('\r', StringSplitOptions.RemoveEmptyEntries);
    }

    private sealed class FakeClock { public DateTime Now = new(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc); }

    /// <summary>Instrumentet (714/77E) i utökad session.</summary>
    private static (Elm327Emulator E, VirtualCar Car, FakeClock Clock, List<string> Hints) Setup()
    {
        var car = Golf();
        var clock = new FakeClock();
        var e = new Elm327Emulator(car, () => clock.Now, new Random(42));
        var hints = new List<string>();
        e.Hint += hints.Add;
        Run(e, "ATZ");
        Run(e, "ATE0");
        Run(e, "ATSH714");
        Assert.Equal(["50 03 00 32 01 F4"], Run(e, "1003"));
        return (e, car, clock, hints);
    }

    private static byte[] RequestSeed(Elm327Emulator e)
    {
        var line = Run(e, "2701").Single();
        Assert.StartsWith("67 01 ", line);
        return Convert.FromHexString(line[6..].Replace(" ", ""));
    }

    private static string KeyFor(VirtualCar car, byte[] seed)
    {
        var ecu = car.Profile.Ecus.First(x => x.RequestId == "714");
        return Convert.ToHexString(car.Coding.SecurityFor(ecu).ComputeKey(seed));
    }

    [Fact]
    public void KnownSeedGivesKnownKey()
    {
        // Standardformeln: rotl(seed ^ 5A3C96E1, 7) + 1234ABCD
        var key = SecurityAccessConfig.Default.ComputeKey([0x11, 0x22, 0x33, 0x44]);
        uint x = 0x11223344u ^ 0x5A3C96E1u;
        uint expected = uint.RotateLeft(x, 7) + 0x1234ABCDu;
        Assert.Equal(expected.ToString("X8"), Convert.ToHexString(key));
    }

    [Fact]
    public void ProfilesHaveTheirOwnFormula()
    {
        var all = ProfileLoader.LoadFolder(Path.Combine(AppContext.BaseDirectory, "profiles"));
        Assert.All(all, p => Assert.NotNull(p.SecurityAccess));
        var keys = all.Select(p => Convert.ToHexString(p.SecurityAccess!.ComputeKey([1, 2, 3, 4]))).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
    }

    [Fact]
    public void SeedAndCorrectKeyUnlocksAndAllowsCoding()
    {
        var (e, car, _, hints) = Setup();

        // Låst: skrivning nekas med securityAccessDenied.
        Assert.Equal(["7F 2E 33"], Run(e, "2E060001000000"));

        var seed = RequestSeed(e);
        Assert.NotEqual(new byte[4], seed);
        var key = KeyFor(car, seed);
        Assert.Contains(hints, h => h.Contains("förväntad nyckel") && h.Replace(" ", "").Contains(key));

        Assert.Equal(["67 02"], Run(e, "2702" + key));
        Assert.Equal(["6E 06 00"], Run(e, "2E060001000000"));

        // Upplåst: nytt seed är 00 00 00 00.
        Assert.Equal(["67 01 00 00 00 00"], Run(e, "2701"));
    }

    [Fact]
    public void WrongKeyGivesInvalidKey()
    {
        var (e, car, _, _) = Setup();
        RequestSeed(e);
        Assert.Equal(["7F 27 35"], Run(e, "270200000000"));
        Assert.Equal(["7F 2E 33"], Run(e, "2E060001000000"));
        // Nyckel utan nytt seed -> requestSequenceError
        Assert.Equal(["7F 27 24"], Run(e, "270200000000"));
    }

    [Fact]
    public void ThreeWrongKeysLockOutForTenSeconds()
    {
        var (e, car, clock, _) = Setup();
        RequestSeed(e); Assert.Equal(["7F 27 35"], Run(e, "270200000000"));
        RequestSeed(e); Assert.Equal(["7F 27 35"], Run(e, "270200000000"));
        RequestSeed(e); Assert.Equal(["7F 27 36"], Run(e, "270200000000"));

        // Under spärrtiden: requiredTimeDelayNotExpired
        Assert.Equal(["7F 27 37"], Run(e, "2701"));
        clock.Now += TimeSpan.FromSeconds(9);
        Assert.Equal(["7F 27 37"], Run(e, "2701"));

        // Efter 10 s går det igen.
        clock.Now += TimeSpan.FromSeconds(2);
        var seed = RequestSeed(e);
        Assert.Equal(["67 02"], Run(e, "2702" + KeyFor(car, seed)));
    }

    [Fact]
    public void DefaultSessionLocksAgain()
    {
        var (e, car, _, _) = Setup();
        Assert.Equal(["67 02"], Run(e, "2702" + KeyFor(car, RequestSeed(e))));
        Run(e, "1001");
        Run(e, "1003");
        Assert.Equal(["7F 2E 33"], Run(e, "2E060001000000"));
    }

    [Fact]
    public void AtzLocksAgain()
    {
        var (e, car, _, _) = Setup();
        Assert.Equal(["67 02"], Run(e, "2702" + KeyFor(car, RequestSeed(e))));
        Run(e, "ATZ"); Run(e, "ATE0"); Run(e, "ATSH714"); Run(e, "1003");
        Assert.Equal(["7F 2E 33"], Run(e, "2E060001000000"));
    }

    [Fact]
    public void SecurityAccessNeedsExtendedSession()
    {
        var car = Golf();
        var e = new Elm327Emulator(car);
        Run(e, "ATZ"); Run(e, "ATE0"); Run(e, "ATSH714");
        Assert.Equal(["7F 27 7F"], Run(e, "2701"));
    }

    [Fact]
    public void ToggleOffAllowsCodingWithoutKey()
    {
        var (e, car, _, _) = Setup();
        car.Coding.RequireSecurityAccess = false;
        Assert.Equal(["6E 06 00"], Run(e, "2E060001000000"));
    }
}
