// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

using System.Net;
using Cyberdrome.Core;
using Cyberdrome.Core.Profiles;
using Cyberdrome.Obd;
using Cyberdrome.Obd.Real;

namespace Cyberdrome.Tests;

/// <summary>Riktig adapter: tolkning, skrivfilter, säkerhetskopia och vidarekoppling – mot en låtsasbil (vår emulator på loopback).</summary>
public class RealAdapterTests
{
    private static CarProfile Profile(string id) => ProfileLoader.LoadFolder(Path.Combine(AppContext.BaseDirectory, "profiles")).First(x => x.Id == id);

    /// <summary>Låtsasbil på en ledig port + ansluten adapter.</summary>
    private static async Task<(ObdTcpServer Server, VirtualCar Car, ObdAdapter Adapter)> FakeCarAsync(string id = "volvo-xc60")
    {
        var car = new VirtualCar(Profile(id));
        car.State.Ignition = true;
        car.State.SpeedKmh = 77;
        car.State.Rpm = 2000;
        car.State.BatteryVolt = 12.6;
        var server = new ObdTcpServer(car);
        server.Start(0, IPAddress.Loopback);
        var elm = new ElmConnection(new TcpAdapterTransport("127.0.0.1", server.Port));
        await elm.OpenAsync();
        var a = new ObdAdapter(elm);
        await a.InitAsync();
        return (server, car, a);
    }

    [Fact]
    public void ParsesSingleAndMultiFrameCan()
    {
        var (r, e) = ObdAdapter.ParseReply("7E8 03 41 0D 4D 00 00 00 00\n7E9 03 41 0D 4D 00 00 00 00", can: true);
        Assert.Null(e);
        Assert.Equal(2, r.Count);
        Assert.Equal(new byte[] { 0x41, 0x0D, 0x4D }, r[0].Data);
        var (m, _) = ObdAdapter.ParseReply("7E8 10 14 49 02 01 57 56 57\n7E8 21 5A 5A 5A 43 44 5A 4D\n7E8 22 57 30 30 30 30 30 31", can: true);
        Assert.Equal(20, m[0].Data.Length);
        Assert.Equal("WVWZZZCDZMW000001", new string(m[0].Data.Skip(3).Select(b => (char)b).ToArray()));
        var (n, ne) = ObdAdapter.ParseReply("NO DATA", can: true);
        Assert.Empty(n);
        Assert.Equal("NO DATA", ne);
        var (k, _) = ObdAdapter.ParseReply("48 6B 10 41 0D 32 8B", can: false); // ISO 9141: 3 rubrikbyte + data + kontrollsumma
        Assert.Equal(new byte[] { 0x41, 0x0D, 0x32 }, k[0].Data);
    }

    [Theory]
    [InlineData("010D", FilterKind.Read)]
    [InlineData("03", FilterKind.Read)]
    [InlineData("0902", FilterKind.Read)]
    [InlineData("22F190", FilterKind.Read)]
    [InlineData("1902FF", FilterKind.Read)]
    [InlineData("3E00", FilterKind.Read)]
    [InlineData("1001", FilterKind.Read)]
    [InlineData("04", FilterKind.Write)]
    [InlineData("1003", FilterKind.Write)]
    [InlineData("1002", FilterKind.Write)]
    [InlineData("1101", FilterKind.Write)]
    [InlineData("14FFFFFF", FilterKind.Write)]
    [InlineData("2E060001", FilterKind.Write)]
    [InlineData("2F123403", FilterKind.Write)]
    [InlineData("31010203", FilterKind.Write)]
    [InlineData("3400", FilterKind.Write)]
    [InlineData("35", FilterKind.Write)]
    [InlineData("3601", FilterKind.Write)]
    [InlineData("37", FilterKind.Write)]
    [InlineData("3B9A01", FilterKind.Write)]
    [InlineData("2801", FilterKind.Write)]
    [InlineData("8502", FilterKind.Write)]
    [InlineData("3D00", FilterKind.Write)]
    [InlineData("AA01", FilterKind.Write)] // okänd tjänst räknas som skrivning
    [InlineData("2701", FilterKind.Security)]
    public void WriteFilterMatrix(string hex, FilterKind expected) =>
        Assert.Equal(expected, WriteFilter.Classify(Convert.FromHexString(hex)).Kind);

    [Fact]
    public async Task InitAndScanFakeCar()
    {
        var (server, _, a) = await FakeCarAsync();
        try
        {
            Assert.Contains("ELM327", a.Info!.Version);
            Assert.Equal("6", a.Info.Protocol);
            Assert.Equal(12.6, a.Info.Voltage);
            var scan = await RealCarScanner.ScanAsync(a);
            Assert.Equal(Profile("volvo-xc60").Vin, scan.Vin);
            Assert.Contains("0D", scan.SupportedPids);
            Assert.Equal(77, scan.Live["km/h"]);
            Assert.Equal(2000, scan.Live["rpm"]);
            Assert.Contains(scan.Ecus, e => e.RequestId == 0x7E0 && e.AnswersObd);
            Assert.Equal(Profile("volvo-xc60").Ecus.Count, scan.Ecus.Count(e => e.AnswersUds));

            var dir = Path.Combine(Path.GetTempPath(), "cd-real-" + Guid.NewGuid().ToString("N"));
            var store = new ProfileStore(dir, []);
            var p = RealCarScanner.ToProfile(scan, store);
            Assert.Empty(ProfileLoader.Validate(p));
            Assert.Equal("Volvo", p.Make);
            Assert.EndsWith("000001", p.Vin);
            Assert.Equal(77, p.DefaultValues!.Speed);
        }
        finally { await a.DisposeAsync(); await server.StopAsync(); }
    }

    [Fact]
    public async Task NotElmDeviceIsReported()
    {
        // En enhet som svarar men inte som ELM327 (t.ex. låst till tillverkarens app).
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var srv = Task.Run(async () =>
        {
            using var c = await listener.AcceptTcpClientAsync();
            var s = c.GetStream();
            var buf = new byte[256];
            while (await s.ReadAsync(buf) > 0) await s.WriteAsync("\u0002XX\r>"u8.ToArray());
        });
        var elm = new ElmConnection(new TcpAdapterTransport("127.0.0.1", port));
        await elm.OpenAsync();
        var a = new ObdAdapter(elm);
        await Assert.ThrowsAsync<NotElmException>(() => a.InitAsync());
        await elm.DisposeAsync();
        listener.Stop();
    }

    [Fact]
    public async Task BridgeForwardsReadsAndBlocksWritesInReadOnlyMode()
    {
        var (server, real, a) = await FakeCarAsync();
        var backup = Path.Combine(Path.GetTempPath(), "cd-backup-" + Guid.NewGuid().ToString("N"));
        try
        {
            int confirms = 0;
            var bridge = new RealCarBridge(a, backup, _ => { confirms++; return Task.FromResult(true); });
            var front = new VirtualCar(Profile("vw-golf8")) { Bridge = bridge }; // simulatorns bil: värdena kommer från den riktiga
            var hints = new List<string>();
            var app = new Elm327Emulator(front);
            app.Hint += hints.Add;
            app.Handle("ATZ"); app.Handle("ATE0");
            Assert.StartsWith("41 0D 4D", app.Handle("010D")); // 77 km/h från den "riktiga" bilen
            Assert.Contains("62 F1 90", app.Handle("0902") is var vin && vin.Contains("49 02") ? "62 F1 90" : vin);
            Assert.StartsWith("12.6V", app.Handle("ATRV"));

            // Läsläge: radera felkoder och skriv kodning stoppas.
            real.State.SetDtcs(["P0300"]);
            Assert.StartsWith("7F 04 22", app.Handle("04"));
            Assert.Equal(["P0300"], real.State.Dtcs);
            app.Handle("ATSH7E0");
            Assert.StartsWith("7F 2E 22", app.Handle("2E060001"));
            Assert.StartsWith("7F 27 22", app.Handle("2701"));
            Assert.Equal(0, confirms);
            Assert.False(Directory.Exists(backup));
            Assert.Contains(hints, h => h.Contains("läsläge"));

            // Skrivning tillåten: säkerhetskopia först, bekräftelse, sedan skickas den.
            bridge.AllowWrites = true;
            app.Handle("ATSH7DF");
            Assert.StartsWith("44", app.Handle("04"));
            Assert.Empty(real.State.Dtcs);
            Assert.Equal(1, confirms);
            var file = Assert.Single(Directory.GetFiles(backup));
            Assert.Contains("43 01 03 00".Replace(" ", ""), File.ReadAllText(file)); // felkoderna sparades innan de raderades

            // 2E: originalvärdet (22 0600) läses och sparas innan.
            app.Handle("ATSH7E0");
            app.Handle("2E060001");
            Assert.Equal(2, confirms);
            Assert.Contains(Directory.GetFiles(backup), f => File.ReadAllText(f).Contains("\"lasfraga\": \"220600\""));

            // Nej i dialogen → inget skickas.
            var deny = new RealCarBridge(a, backup, _ => Task.FromResult(false)) { AllowWrites = true };
            front.Bridge = deny;
            real.State.SetDtcs(["P0171"]);
            app.Handle("ATSH7DF");
            Assert.StartsWith("7F 04 22", app.Handle("04"));
            Assert.Equal(["P0171"], real.State.Dtcs);

            // Låg spänning → skrivning stoppas.
            real.State.BatteryVolt = 11.4;
            front.Bridge = bridge;
            Assert.StartsWith("7F 04 22", app.Handle("04"));
            Assert.Contains(hints, h => h.Contains("spänningen"));
            Assert.Equal(["P0171"], real.State.Dtcs);
        }
        finally
        {
            await a.DisposeAsync(); await server.StopAsync();
            if (Directory.Exists(backup)) Directory.Delete(backup, true);
        }
    }

    [Fact]
    public void VinHelpers()
    {
        Assert.Equal("Volkswagen", RealCarScanner.MakeFromVin("WVWZZZCDZMW000001"));
        Assert.Equal("Volvo", RealCarScanner.MakeFromVin("YV1XZ12345A000000"));
        Assert.Equal(2021, RealCarScanner.YearFromVin("WVWZZZCDZMW000001", 2026));
        Assert.Equal(2010, RealCarScanner.YearFromVin("WVWZZZ1KZAW000001", 2026));
        Assert.Equal("P0300", RealCarScanner.DecodeDtc(0x03, 0x00));
        Assert.Equal("U0100", RealCarScanner.DecodeDtc(0xC1, 0x00));
    }
}
