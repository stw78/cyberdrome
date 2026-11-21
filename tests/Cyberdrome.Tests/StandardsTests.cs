// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;
using Cyberdrome.Core;
using Cyberdrome.Core.Profiles;
using Cyberdrome.Core.Profiles.Odx;
using Cyberdrome.Obd;

namespace Cyberdrome.Tests;

/// <summary>Standardformat: ASAM ODX (egen påhittad exempelfil), VSS, Torque-CSV, felkodstexter och .cbs-filändelsen.</summary>
public class StandardsTests
{
    private static List<CarProfile> BuiltIns() => ProfileLoader.LoadFolder(Path.Combine(AppContext.BaseDirectory, "profiles"));
    private static CarProfile Golf() => BuiltIns().First(x => x.Id == "vw-golf8");

    /// <summary>Handskriven ODX 2.2 för ett påhittat styrdon (ingen tillverkares data).</summary>
    private const string SampleOdx = """
        <?xml version="1.0" encoding="UTF-8"?>
        <ODX MODEL-VERSION="2.2.0" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
          <DIAG-LAYER-CONTAINER ID="DLC_Testbil">
            <SHORT-NAME>Testbil</SHORT-NAME>
            <BASE-VARIANTS>
              <BASE-VARIANT ID="BV_Klimat">
                <SHORT-NAME>Klimat</SHORT-NAME>
                <LONG-NAME>Påhittad klimatanläggning</LONG-NAME>
                <DIAG-DATA-DICTIONARY-SPEC>
                  <DTC-DOPS>
                    <DTC-DOP ID="DD1"><SHORT-NAME>DTCs</SHORT-NAME>
                      <DTCS>
                        <DTC ID="D1"><SHORT-NAME>B1234</SHORT-NAME><TROUBLE-CODE>0x9234</TROUBLE-CODE><TEXT>Fläktmotor svarar inte</TEXT></DTC>
                        <DTC ID="D2"><SHORT-NAME>P0300</SHORT-NAME><TROUBLE-CODE>768</TROUBLE-CODE><DISPLAY-TROUBLE-CODE>P0300</DISPLAY-TROUBLE-CODE><TEXT>Slumpmässig feltändning</TEXT></DTC>
                      </DTCS>
                    </DTC-DOP>
                  </DTC-DOPS>
                  <DATA-OBJECT-PROPS>
                    <DATA-OBJECT-PROP ID="DOP_Kod"><SHORT-NAME>Kod</SHORT-NAME>
                      <DIAG-CODED-TYPE BASE-DATA-TYPE="A_BYTEFIELD" xsi:type="STANDARD-LENGTH-TYPE"><BIT-LENGTH>16</BIT-LENGTH></DIAG-CODED-TYPE>
                    </DATA-OBJECT-PROP>
                  </DATA-OBJECT-PROPS>
                </DIAG-DATA-DICTIONARY-SPEC>
                <DIAG-COMMS>
                  <DIAG-SERVICE ID="S1"><SHORT-NAME>SkrivKodning</SHORT-NAME><REQUEST-REF ID-REF="RQ1"/></DIAG-SERVICE>
                  <DIAG-SERVICE ID="S2"><SHORT-NAME>Seed</SHORT-NAME><REQUEST-REF ID-REF="RQ2"/></DIAG-SERVICE>
                </DIAG-COMMS>
                <REQUESTS>
                  <REQUEST ID="RQ1"><SHORT-NAME>RQ1</SHORT-NAME><PARAMS>
                    <PARAM SEMANTIC="SERVICE-ID" xsi:type="CODED-CONST"><SHORT-NAME>SID</SHORT-NAME><BYTE-POSITION>0</BYTE-POSITION><CODED-VALUE>46</CODED-VALUE></PARAM>
                    <PARAM SEMANTIC="ID" xsi:type="CODED-CONST"><SHORT-NAME>DID</SHORT-NAME><BYTE-POSITION>1</BYTE-POSITION><CODED-VALUE>0xF1A0</CODED-VALUE></PARAM>
                    <PARAM SEMANTIC="DATA" xsi:type="VALUE"><SHORT-NAME>Data</SHORT-NAME><BYTE-POSITION>3</BYTE-POSITION><DOP-REF ID-REF="DOP_Kod"/></PARAM>
                  </PARAMS></REQUEST>
                  <REQUEST ID="RQ2"><SHORT-NAME>RQ2</SHORT-NAME><PARAMS>
                    <PARAM SEMANTIC="SERVICE-ID" xsi:type="CODED-CONST"><SHORT-NAME>SID</SHORT-NAME><BYTE-POSITION>0</BYTE-POSITION><CODED-VALUE>39</CODED-VALUE></PARAM>
                  </PARAMS></REQUEST>
                </REQUESTS>
                <COMPARAM-REFS>
                  <COMPARAM-REF ID-REF="ISO_15765_2.CP_CanPhysReqId"><VALUE>1810</VALUE></COMPARAM-REF>
                  <COMPARAM-REF ID-REF="ISO_15765_2.CP_CanRespUSDTId"><SIMPLE-VALUE>0x71A</SIMPLE-VALUE></COMPARAM-REF>
                </COMPARAM-REFS>
              </BASE-VARIANT>
            </BASE-VARIANTS>
            <FLASHS/>
          </DIAG-LAYER-CONTAINER>
          <FLASH ID="F1"><SHORT-NAME>Flash</SHORT-NAME></FLASH>
        </ODX>
        """;

    private static OdxImportResult ImportSample() => OdxImporter.Import([XDocument.Parse(SampleOdx.Trim())], "testbil");

    [Fact]
    public void OdxSampleMapsEcuCanIdsCodingAndDtcTexts()
    {
        var r = ImportSample();
        var ecu = Assert.Single(r.Profile.Ecus);
        Assert.Equal("712", ecu.RequestId); // 1810 = 0x712
        Assert.Equal("71A", ecu.ResponseId);
        Assert.Equal("F1A0", ecu.CodingDid);
        Assert.Equal(4, ecu.Coding.Length); // 16 bitar = 2 byte
        Assert.Equal("Fläktmotor svarar inte", r.Profile.DtcDescriptions!["B1234"]);
        Assert.Equal("Slumpmässig feltändning", r.Profile.DtcDescriptions["P0300"]);
        Assert.Contains(r.Ignored, i => i.Contains("0x27"));
        Assert.Contains(r.Ignored, i => i.Contains("FLASH", StringComparison.OrdinalIgnoreCase) || i.Contains("Flash", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(ProfileLoader.Validate(r.Profile));
    }

    [Fact]
    public void OdxRejectsNonOdx()
    {
        Assert.ThrowsAny<Exception>(() => OdxImporter.Import([XDocument.Parse("<foo/>")], "x"));
    }

    [Fact]
    public void GolfPdxRoundTripIsLossless()
    {
        var golf = Golf();
        golf.DtcDescriptions = new() { ["P0300"] = "Slumpmässig feltändning" };
        golf.StartDtcs = ["P0300"];
        var file = Path.Combine(Path.GetTempPath(), "cd-" + Guid.NewGuid().ToString("N") + ".pdx");
        try
        {
            OdxExporter.SavePdx(golf, file);
            using (var zip = ZipFile.OpenRead(file))
            {
                Assert.NotNull(zip.GetEntry("index.xml"));
                Assert.NotNull(zip.GetEntry("vw_golf8.odx-d") ?? zip.Entries.FirstOrDefault(e => e.Name.EndsWith(".odx-d")));
            }
            var back = OdxImporter.Import(file).Profile;
            Assert.Equal(golf.Id, back.Id);
            Assert.Equal(golf.Vin, back.Vin);
            Assert.Equal(golf.Year, back.Year);
            Assert.Equal(golf.Ecus.Select(e => (e.Key, e.RequestId, e.ResponseId, e.CodingDid, e.Coding)),
                         back.Ecus.Select(e => (e.Key, e.RequestId, e.ResponseId, e.CodingDid, e.Coding)));
            Assert.Equal(golf.Features.Select(f => (f.Id, f.Ecu, f.Byte, f.Bit)), back.Features.Select(f => (f.Id, f.Ecu, f.Byte, f.Bit)));
            Assert.Equal("Slumpmässig feltändning", back.DtcDescriptions!["P0300"]);
            Assert.Equal(golf.StartDtcs, back.StartDtcs);
            Assert.Null(back.SecurityAccess); // seed/key exporteras aldrig
            Assert.DoesNotContain("5A3C96E1", OdxExporter.ToOdxString(golf));

            // Den återimporterade bilen svarar på UDS 22 som originalet.
            var e = new Elm327Emulator(new VirtualCar(back));
            e.Handle("ATZ"); e.Handle("ATE0"); e.Handle("ATSH714");
            Assert.Contains("62 06 00", e.Handle("220600"));
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void TroubleCodeEncoding()
    {
        Assert.Equal(0x0300, OdxExporter.TroubleCode("P0300"));
        Assert.Equal("B1234", OdxImporter.FromTroubleCode("0x9234"));
        Assert.Equal("P0300", OdxImporter.FromTroubleCode("768"));
    }

    [Fact]
    public void CbsPackageContainsOdxAndLegacyExtensionLoads()
    {
        Assert.Equal(".cbs", CarPackage.Extension);
        Assert.True(CarPackage.IsPackage("x.CBS"));
        Assert.True(CarPackage.IsPackage("gammal.cyberbil"));
        Assert.False(CarPackage.IsPackage("x.pdx"));
        var dir = Path.Combine(Path.GetTempPath(), "cd-cbs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var file = Path.Combine(dir, "golf.cbs");
            new CarPackage { Profile = Golf() }.Save(file);
            using (var zip = ZipFile.OpenRead(file)) Assert.NotNull(zip.GetEntry("odx/vw-golf8.odx-d"));
            var pkg = CarPackage.Load(file);
            Assert.Empty(pkg.Extras); // ODX-kopian är inte en extrafil
            var legacy = Path.Combine(dir, "golf.cyberbil");
            File.Copy(file, legacy);
            var store = new ProfileStore(Path.Combine(dir, "bilar"), BuiltIns().Select(p => p.Id));
            Assert.Equal("Golf 8 (importerad)", store.Import(legacy).Profile.Model);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void TorqueCsvRoundTripAndEmulatorAnswers()
    {
        const string csv = """
            Name,ShortName,ModeAndPID,Equation,Min Value,Max Value,Units,Header
            Oljetemperatur,OLJA,0x221234,A-40,-40,215,°C,7E0
            "Turbotryck, abs",TURBO,0x22F40B,(A*256+B)/10,0,300,kPa,
            Trasig,X,zz,A,0,1,,
            """;
        var problems = new List<string>();
        var pids = TorqueCsv.Parse(csv, problems);
        Assert.Equal(2, pids.Count);
        Assert.Single(problems);
        Assert.Equal("Turbotryck, abs", pids[1].Name);
        Assert.Null(pids[1].Header);
        var again = TorqueCsv.Parse(TorqueCsv.Write(pids));
        Assert.Equal(pids.Select(p => (p.Name, p.Request, p.Equation, p.Header)), again.Select(p => (p.Name, p.Request, p.Equation, p.Header)));

        pids[0].Value = 90;   // A-40 → A = 130 = 0x82
        pids[1].Value = 123.4; // (A*256+B)/10 → 1234 = 04 D2
        var golf = Golf();
        golf.CustomPids = pids;
        Assert.Empty(ProfileLoader.Validate(golf));
        var e = new Elm327Emulator(new VirtualCar(golf));
        e.Handle("ATZ"); e.Handle("ATE0");
        Assert.StartsWith("62 F4 0B 04 D2", e.Handle("22F40B"));
        e.Handle("ATSH7E0");
        Assert.StartsWith("62 12 34 82", e.Handle("221234"));
    }

    [Fact]
    public void DtcCsvParsesSemicolonAndComma()
    {
        var problems = new List<string>();
        var d = DtcCsv.Parse("Kod;Text\nP0300;Slumpmässig feltändning\nu0100,\"Ingen kontakt med motorstyrdon\"\nXYZ;fel\n", problems);
        Assert.Equal(2, d.Count);
        Assert.Equal("Ingen kontakt med motorstyrdon", d["U0100"]);
        Assert.Single(problems);
    }

    [Fact]
    public void VssExportImportRoundTrip()
    {
        var golf = Golf();
        var s = new VehicleState { SpeedKmh = 54, Rpm = 2100, CoolantC = 88, Gear = Gear.D, Ignition = true };
        s.SetDtcs(["P0300"]);
        var json = Vss.Export(golf, s);
        using (var doc = JsonDocument.Parse(json))
        {
            Assert.Equal(54, doc.RootElement.GetProperty("Vehicle.Speed").GetDouble());
            Assert.Equal(golf.Vin, doc.RootElement.GetProperty("Vehicle.VehicleIdentification.VIN").GetString());
        }
        var t = new VehicleState();
        var unknown = Vss.Import(json + "", t);
        Assert.Empty(unknown);
        Assert.Equal(54, t.SpeedKmh);
        Assert.Equal(2100, t.Rpm);
        Assert.Equal(Gear.D, t.Gear);
        Assert.True(t.Ignition);
        Assert.Equal(["P0300"], t.Dtcs);

        var u = Vss.Import("""{"Vehicle.Speed": {"value": 30}, "Vehicle.Cabin.Door.Row1.IsOpen": true}""", t);
        Assert.Equal(30, t.SpeedKmh);
        Assert.Equal(["Vehicle.Cabin.Door.Row1.IsOpen"], u);
        Assert.All(Vss.Signals, x => Assert.StartsWith("Vehicle.", x.Path));
    }

    [Fact]
    public void CustomPidEncodeFormulas()
    {
        Assert.Equal(new byte[] { 200 }, new CustomPid { Equation = "A", Value = 200 }.Encode());
        Assert.Equal(new byte[] { 50 }, new CustomPid { Equation = "A*2", Value = 100 }.Encode());
        Assert.Equal(new byte[] { 255 }, new CustomPid { Equation = "A*100/255", Value = 100 }.Encode());
        Assert.Equal(new byte[] { 0x03, 0xE8 }, new CustomPid { Equation = "A*256+B", Value = 1000 }.Encode());
    }
}
