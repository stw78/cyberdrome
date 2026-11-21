// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

using Cyberdrome.Core;
using Cyberdrome.Core.Profiles;
using Cyberdrome.Obd;

namespace Cyberdrome.Tests;

public class Elm327Tests
{
    private static VirtualCar Golf() =>
        new(ProfileLoader.LoadFolder(Path.Combine(AppContext.BaseDirectory, "profiles")).First(x => x.Id == "vw-golf8"));

    /// <summary>Kör ett kommando och returnerar svarsraderna utan eko och prompt.</summary>
    private static string[] Run(Elm327Emulator e, string cmd)
    {
        var r = e.Handle(cmd);
        Assert.EndsWith("\r\r>", r);
        return r[..^1].Split('\r', StringSplitOptions.RemoveEmptyEntries);
    }

    private static Elm327Emulator Ready(VirtualCar car)
    {
        var e = new Elm327Emulator(car);
        Run(e, "ATZ");
        Run(e, "ATE0");
        return e;
    }

    [Fact]
    public void ResetGivesVersion()
    {
        var e = new Elm327Emulator(Golf());
        var lines = Run(e, "ATZ");
        Assert.Equal(["ATZ", Elm327Emulator.Version], lines);
    }

    [Fact]
    public void EchoOff()
    {
        var e = new Elm327Emulator(Golf());
        Assert.Equal(["ATE0", "OK"], Run(e, "ATE0"));
        Assert.Equal(["OK"], Run(e, "ATL0"));
    }

    [Fact]
    public void SpeedAndRpm()
    {
        var car = Golf();
        car.State.SpeedKmh = 54;
        car.State.Rpm = 1850;
        var e = Ready(car);
        Assert.Equal(["41 0D 36"], Run(e, "010D"));
        Assert.Equal(["41 0C 1C E8"], Run(e, "01 0C"));
    }

    [Fact]
    public void HeadersAndNoSpaces()
    {
        var car = Golf();
        car.State.SpeedKmh = 100;
        var e = Ready(car);
        Run(e, "ATH1");
        // Riktig ELM327 visar hela 8-byte-ramen (PCI + data + utfyllnad) när rubriker är på.
        Assert.Equal(["7E8 03 41 0D 64 00 00 00 00"], Run(e, "010D"));
        Run(e, "ATS0");
        Assert.Equal(["7E803410D6400000000"], Run(e, "010D"));
    }

    [Fact]
    public void HeadersWithDlc()
    {
        var car = Golf();
        car.State.SpeedKmh = 100;
        var e = Ready(car);
        Run(e, "ATH1");
        Assert.Equal(["OK"], Run(e, "ATD1"));
        Assert.Equal(["7E8 8 03 41 0D 64 00 00 00 00"], Run(e, "010D"));
        Assert.Equal(["OK"], Run(e, "ATD0"));
        Assert.Equal(["7E8 03 41 0D 64 00 00 00 00"], Run(e, "010D"));
    }

    [Fact]
    public void MultiFrameWithHeadersIsPaddedTo8Bytes()
    {
        var e = Ready(Golf());
        Run(e, "ATH1");
        var lines = Run(e, "0902"); // VIN: 20 byte -> 3 ramar
        Assert.Equal(3, lines.Length);
        Assert.StartsWith("7E8 10 14 49 02 01", lines[0]);
        Assert.All(lines, l => Assert.Equal(9, l.Split(' ').Length - 0 - 0)); // id + 8 byte
        Assert.StartsWith("7E8 21 ", lines[1]);
        Assert.StartsWith("7E8 22 ", lines[2]);
    }

    [Fact]
    public void SupportedPidsBitmap()
    {
        var e = Ready(Golf());
        // 01,04,05,0C,0D,0F,11,1C,1F,20 -> 98 1A 80 13
        Assert.Equal(["41 00 98 1A 80 13"], Run(e, "0100"));
    }

    [Fact]
    public void VinMultiFrame()
    {
        var e = Ready(Golf());
        var lines = Run(e, "0902");
        Assert.Equal("014", lines[0]);
        Assert.StartsWith("0: 49 02 01 57 56 57", lines[1]);
        Assert.Equal(4, lines.Length);
    }

    [Fact]
    public void DtcReadAndClear()
    {
        var car = Golf();
        car.State.SetDtcs(["P0301", "U0100"]);
        var e = Ready(car);
        Assert.Equal(["43 02 03 01 C1 00"], Run(e, "03"));
        Assert.Equal(["44"], Run(e, "04"));
        Assert.Equal(["43 00"], Run(e, "03"));
    }

    [Fact]
    public void IgnitionOffGivesNoData()
    {
        var car = Golf();
        car.State.Ignition = false;
        Assert.Equal(["NO DATA"], Run(Ready(car), "010D"));
    }

    [Fact]
    public void UnknownCommand()
    {
        var e = Ready(Golf());
        Assert.Equal(["?"], Run(e, "ATXYZ"));
        Assert.Equal(["?"], Run(e, "01G"));
    }

    [Fact]
    public void UdsCodingReadWrite()
    {
        var car = Golf();
        car.Coding.RequireSecurityAccess = false; // säkerhetsnyckeln testas i SecurityAccessTests
        var e = Ready(car);
        Assert.Equal(["OK"], Run(e, "ATSH714"));
        Assert.Equal(["62 06 00 00 00 00 00"], Run(e, "220600"));

        // Skriv utan utökad session -> nekas
        Assert.Equal(["7F 2E 7F"], Run(e, "2E060001000000"));
        Assert.Equal(["50 03 00 32 01 F4"], Run(e, "1003"));
        Assert.Equal(["6E 06 00"], Run(e, "2E060001000000"));

        var needle = car.Profile.Features.First(f => f.Id == "needle-sweep");
        Assert.True(car.Coding.IsOn(needle));
    }

    [Fact]
    public void UnknownEcuGivesNoData()
    {
        var e = Ready(Golf());
        Run(e, "ATSH123");
        Assert.Equal(["NO DATA"], Run(e, "220600"));
    }

    [Fact]
    public async Task TcpServerAnswers()
    {
        var car = Golf();
        car.State.SpeedKmh = 54;
        await using var server = new ObdTcpServer(car);
        server.Start(0, System.Net.IPAddress.Loopback);

        using var client = new System.Net.Sockets.TcpClient();
        await client.ConnectAsync(System.Net.IPAddress.Loopback, server.Port);
        var s = client.GetStream();

        async Task<string> Send(string cmd)
        {
            await s.WriteAsync(System.Text.Encoding.ASCII.GetBytes(cmd + "\r"));
            var sb = new System.Text.StringBuilder();
            var buf = new byte[256];
            while (!sb.ToString().EndsWith('>'))
            {
                int n = await s.ReadAsync(buf).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
                sb.Append(System.Text.Encoding.ASCII.GetString(buf, 0, n));
            }
            return sb.ToString();
        }

        await Send("ATZ");
        await Send("ATE0");
        Assert.Equal("41 0D 36\r\r>", await Send("010D"));
    }
}
