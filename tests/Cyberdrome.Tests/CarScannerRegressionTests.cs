// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

using Cyberdrome.Core;
using Cyberdrome.Core.Profiles;
using Cyberdrome.Obd;

namespace Cyberdrome.Tests;

/// <summary>
/// Exakt den sekvens som Car Scanner (iPhone, Bluetooth LE) skickade 2026-11-20, där appen
/// visade fel värden (12 km/h i stället för 54, 352 rpm i stället för 1850).
/// </summary>
public class CarScannerRegressionTests
{
    private static VirtualCar Golf()
    {
        var car = new VirtualCar(ProfileLoader.LoadFolder(Path.Combine(AppContext.BaseDirectory, "profiles")).First(x => x.Id == "vw-golf8"));
        car.State.SpeedKmh = 54;
        car.State.Rpm = 1850;
        car.State.CoolantC = 88;
        return car;
    }

    [Fact]
    public void EmptyLinesRightAfterAtzGiveNoReplySoAnswersStayInStep()
    {
        var s = new ObdLineSession(Golf(), "test");
        string output = s.Feed("ATZ\r") + s.Feed("ATE0\r");
        // Car Scanner "tömmer" adaptern med tomma rader direkt efter ATZ.
        for (int i = 0; i < 9; i++) output += s.Feed("\r");
        Assert.Equal(2, output.Count(c => c == '>')); // bara ATZ och ATE0 får svar

        string[] setup = ["ATE0", "STI", "VTI", "ATD", "ATD0", "ATE0", "ATH1", "ATSP6", "ATE0", "ATH1", "ATM0", "ATS0", "ATAT1", "ATAL", "ATST96"];
        foreach (var c in setup)
        {
            var r = s.Feed(c + "\r");
            Assert.Equal(1, r.Count(ch => ch == '>')); // exakt ett svar per kommando
        }

        // Live-data som Car Scanner frågar efter (ATH1, ATS0 → hela 8-byte-ramen).
        Assert.Equal("7E804410C1CE8000000\r\r>", s.Feed("010C\r"));
        Assert.Equal("7E803410D3600000000\r\r>", s.Feed("010D\r"));
        Assert.Equal("7E80341058000000000\r\r>", s.Feed("0105\r"));
    }

    [Fact]
    public void ResponseCountSuffixIsAccepted()
    {
        var s = new ObdLineSession(Golf(), "test");
        s.Feed("ATZ\r"); s.Feed("ATE0\r"); s.Feed("ATH1\r"); s.Feed("ATS0\r");
        Assert.Equal("7E804410C1CE8000000\r\r>", s.Feed("010C1\r"));
        Assert.Equal("7E80341058000000000\r\r>", s.Feed("01051\r"));
    }

    [Fact]
    public void MultiPidRequestIsAnsweredInRequestOrderAndPadded()
    {
        var s = new ObdLineSession(Golf(), "test");
        s.Feed("ATZ\r"); s.Feed("ATE0\r"); s.Feed("ATH1\r"); s.Feed("ATS0\r");
        var r = s.Feed("01050C0D\r");
        var lines = r.Split('\r', StringSplitOptions.RemoveEmptyEntries).Where(l => l != ">").ToArray();
        // 41 05 80 0C 1C E8 0D 36 = 8 byte -> första ram + en följdram, båda 8 byte långa.
        Assert.Equal(["7E810084105800C1CE8", "7E8210D360000000000"], lines);
    }

    [Fact]
    public void EmptyLineLaterStillRepeatsLastCommand()
    {
        var now = new DateTime(2026, 11, 20, 1, 0, 0, DateTimeKind.Utc);
        var e = new Elm327Emulator(Golf(), () => now);
        e.Handle("ATZ"); e.Handle("ATE0");
        Assert.Equal("", e.Handle(""));        // direkt efter ATZ: ignoreras
        now += TimeSpan.FromSeconds(3);
        Assert.Equal("41 0D 36\r\r>", e.Handle("010D"));
        Assert.Equal("41 0D 36\r\r>", e.Handle("")); // senare: upprepa senaste (som riktig ELM)
    }
}
