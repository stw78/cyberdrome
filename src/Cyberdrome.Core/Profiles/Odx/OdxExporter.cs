// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace Cyberdrome.Core.Profiles.Odx;

/// <summary>
/// Skriver en bilmodell som ASAM ODX 2.2 (DIAG-LAYER-CONTAINER med ett BASE-VARIANT per styrdon) och som
/// .pdx (zip med index.xml + .odx-d). Innehåller CAN-id (COMPARAM-REF), läs/skriv kodning (0x22/0x2E med DID),
/// läs VIN (0x22 F190) och felkoder (DTC-DOP). Det som ODX inte har plats för (dolda funktioner, PID-lista, tema …)
/// sparas i en SDG "Cyberdrome" så att export → import blir förlustfri. Seed/key exporteras aldrig.
/// </summary>
public static class OdxExporter
{
    private static readonly XNamespace Xsi = "http://www.w3.org/2001/XMLSchema-instance";
    public const string ComparamSubset = "ISO_15765_2";

    private static XElement Sn(string s) => new("SHORT-NAME", Odx(s));
    private static string Odx(string s) => new(s.Select(c => char.IsLetterOrDigit(c) && c < 128 || c == '_' ? c : '_').ToArray());

    private static XElement CodedConst(string name, string semantic, int pos, int value, int bits) =>
        new("PARAM", new XAttribute("SEMANTIC", semantic), new XAttribute(Xsi + "type", "CODED-CONST"),
            Sn(name), new XElement("BYTE-POSITION", pos), new XElement("CODED-VALUE", value),
            new XElement("DIAG-CODED-TYPE", new XAttribute("BASE-DATA-TYPE", "A_UINT32"), new XAttribute(Xsi + "type", "STANDARD-LENGTH-TYPE"),
                new XElement("BIT-LENGTH", bits)));

    private static XElement ValueParam(string name, int pos, string dopId) =>
        new("PARAM", new XAttribute("SEMANTIC", "DATA"), new XAttribute(Xsi + "type", "VALUE"),
            Sn(name), new XElement("BYTE-POSITION", pos), new XElement("DOP-REF", new XAttribute("ID-REF", dopId)));

    private static XElement Dop(string id, string name, int bits) =>
        new("DATA-OBJECT-PROP", new XAttribute("ID", id), Sn(name),
            new XElement("COMPU-METHOD", new XElement("CATEGORY", "IDENTICAL")),
            new XElement("DIAG-CODED-TYPE", new XAttribute("BASE-DATA-TYPE", "A_BYTEFIELD"), new XAttribute(Xsi + "type", "STANDARD-LENGTH-TYPE"),
                new XElement("BIT-LENGTH", bits)),
            new XElement("PHYSICAL-TYPE", new XAttribute("BASE-DATA-TYPE", "A_BYTEFIELD")));

    private static XElement ComparamRef(string name, int value) =>
        new("COMPARAM-REF", new XAttribute("ID-REF", $"{ComparamSubset}.{name}"), new XAttribute("DOCREF", ComparamSubset),
            new XAttribute("DOCTYPE", "COMPARAM-SUBSET"), new XElement("SIMPLE-VALUE", value));

    public static XDocument ToOdx(CarProfile p)
    {
        var containerId = "DLC_" + Odx(p.Id);
        var layers = new List<XElement>();
        bool first = true;
        foreach (var ecu in p.Ecus)
        {
            var k = Odx(ecu.Key);
            var codingBytes = Math.Max(1, ecu.Coding.Length / 2);
            int did = ecu.CodingDidValue;
            var dops = new List<XElement> { Dop($"DOP_{k}_Coding", "Coding", codingBytes * 8) };
            var services = new List<XElement>();
            var requests = new List<XElement>();
            var responses = new List<XElement>();

            void Service(string name, string semantic, int sid, int id, string? reqData, string? respData, string? dopId)
            {
                var rq = new XElement("REQUEST", new XAttribute("ID", $"RQ_{k}_{name}"), Sn($"RQ_{name}"),
                    new XElement("PARAMS", CodedConst("SID_RQ", "SERVICE-ID", 0, sid, 8), CodedConst("DID", "ID", 1, id, 16),
                        reqData is not null ? ValueParam(reqData, 3, dopId!) : null));
                var pr = new XElement("POS-RESPONSE", new XAttribute("ID", $"PR_{k}_{name}"), Sn($"PR_{name}"),
                    new XElement("PARAMS", CodedConst("SID_PR", "SERVICE-ID", 0, sid + 0x40, 8), CodedConst("DID", "ID", 1, id, 16),
                        respData is not null ? ValueParam(respData, 3, dopId!) : null));
                requests.Add(rq);
                responses.Add(pr);
                services.Add(new XElement("DIAG-SERVICE", new XAttribute("ID", $"DS_{k}_{name}"), new XAttribute("SEMANTIC", semantic), Sn(name),
                    new XElement("REQUEST-REF", new XAttribute("ID-REF", $"RQ_{k}_{name}")),
                    new XElement("POS-RESPONSE-REFS", new XElement("POS-RESPONSE-REF", new XAttribute("ID-REF", $"PR_{k}_{name}")))));
            }

            Service("ReadCoding", "DATA-READ", 0x22, did, null, "CodingData", $"DOP_{k}_Coding");
            Service("WriteCoding", "DATA-WRITE", 0x2E, did, "CodingData", null, $"DOP_{k}_Coding");
            if (first)
            {
                dops.Add(Dop($"DOP_{k}_VIN", "VIN", 17 * 8));
                Service("ReadVIN", "IDENTIFICATION", 0x22, 0xF190, null, "VIN", $"DOP_{k}_VIN");
            }

            var spec = new XElement("DIAG-DATA-DICTIONARY-SPEC");
            if (first)
            {
                var codes = p.StartDtcs.Concat(p.DtcDescriptions?.Keys ?? Enumerable.Empty<string>())
                    .Select(c => c.ToUpperInvariant()).Distinct().OrderBy(c => c).ToList();
                if (codes.Count > 0)
                    spec.Add(new XElement("DTC-DOPS", new XElement("DTC-DOP", new XAttribute("ID", $"DTCDOP_{k}"), Sn("DTCs"),
                        new XElement("DIAG-CODED-TYPE", new XAttribute("BASE-DATA-TYPE", "A_UINT32"), new XAttribute(Xsi + "type", "STANDARD-LENGTH-TYPE"),
                            new XElement("BIT-LENGTH", 24)),
                        new XElement("PHYSICAL-TYPE", new XAttribute("BASE-DATA-TYPE", "A_UINT32")),
                        new XElement("COMPU-METHOD", new XElement("CATEGORY", "IDENTICAL")),
                        new XElement("DTCS", codes.Select(c => new XElement("DTC", new XAttribute("ID", $"DTC_{c}"), Sn(c),
                            new XElement("TROUBLE-CODE", TroubleCode(c)), new XElement("DISPLAY-TROUBLE-CODE", c),
                            new XElement("TEXT", p.DtcDescriptions?.GetValueOrDefault(c) ?? "")))))));
            }
            spec.Add(new XElement("DATA-OBJECT-PROPS", dops));

            // Ordning enligt ODX: namn, admin-data, ordbok, tjänster, frågor/svar, SDGS, kommunikationsparametrar.
            var layer = new XElement("BASE-VARIANT", new XAttribute("ID", $"BV_{k}"), Sn(ecu.Key), new XElement("LONG-NAME", ecu.Name),
                first ? new XElement("ADMIN-DATA", new XElement("LANGUAGE", "sv-SE")) : null,
                spec,
                new XElement("DIAG-COMMS", services),
                new XElement("REQUESTS", requests),
                new XElement("POS-RESPONSES", responses),
                first ? Sdg(p) : null, // Cyberdromes tilläggsdata (förlustfri återimport)
                new XElement("COMPARAM-REFS", ComparamRef("CP_CanPhysReqId", ecu.RequestIdValue), ComparamRef("CP_CanRespUSDTId", ecu.ResponseIdValue)));
            layers.Add(layer);
            first = false;
        }

        var root = new XElement("ODX", new XAttribute("MODEL-VERSION", "2.2.0"), new XAttribute(XNamespace.Xmlns + "xsi", Xsi.NamespaceName),
            new XElement("DIAG-LAYER-CONTAINER", new XAttribute("ID", containerId), Sn(p.Id), new XElement("LONG-NAME", p.DisplayName),
                new XElement("BASE-VARIANTS", layers)));
        return new XDocument(new XDeclaration("1.0", "UTF-8", null), root);
    }

    private static XElement Sdg(CarProfile p)
    {
        XElement Sd(string si, string? v) => new("SD", new XAttribute("SI", si), v ?? "");
        var sds = new List<XElement>
        {
            Sd("id", p.Id), Sd("make", p.Make), Sd("model", p.Model), Sd("generation", p.Generation), Sd("year", p.Year.ToString()),
            Sd("vin", p.Vin), Sd("protocol", p.Protocol),
        };
        if (p.Theme is not null) sds.Add(Sd("theme", p.Theme));
        if (p.ScreenPreset is not null) sds.Add(Sd("screenPreset", p.ScreenPreset));
        if (p.StartDtcs.Count > 0) sds.Add(Sd("startDtcs", string.Join(",", p.StartDtcs)));
        if (p.SupportedPids is { Count: > 0 }) sds.Add(Sd("supportedPids", string.Join(",", p.SupportedPids)));
        foreach (var e in p.Ecus) sds.Add(Sd("coding:" + e.Key, e.Coding));
        if (p.Features.Count > 0) sds.Add(Sd("features", System.Text.Json.JsonSerializer.Serialize(p.Features, ProfileLoader.JsonOptions)));
        return new XElement("SDGS", new XElement("SDG", new XElement("SDG-CAPTION", new XAttribute("ID", "SDGC_Cyberdrome"), Sn("Cyberdrome")), sds));
    }

    /// <summary>"P0300" → 0x0300 (felkodens två byte; tredje byten, feltyp, är 0).</summary>
    public static int TroubleCode(string code)
    {
        int letter = "PCBU".IndexOf(char.ToUpperInvariant(code[0]));
        int rest = Convert.ToInt32(code[1..], 16);
        return ((letter << 14) | (rest & 0x3FFF)) & 0xFFFF;
    }

    public static string ToOdxString(CarProfile p)
    {
        var sb = new StringBuilder();
        using (var w = new Utf8StringWriter(sb)) ToOdx(p).Save(w);
        return sb.ToString();
    }

    private sealed class Utf8StringWriter(StringBuilder sb) : StringWriter(sb) { public override Encoding Encoding => Encoding.UTF8; }

    /// <summary>Sparar som .pdx: zip med index.xml (katalog) och &lt;id&gt;.odx-d.</summary>
    public static void SavePdx(CarProfile p, string path)
    {
        var odxName = Odx(p.Id) + ".odx-d";
        if (File.Exists(path)) File.Delete(path);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        using (var w = new StreamWriter(zip.CreateEntry(odxName).Open(), new UTF8Encoding(false))) w.Write(ToOdxString(p));
        var index = new XDocument(new XDeclaration("1.0", "UTF-8", null),
            new XElement("CATALOG", new XAttribute("F-DTD-VERSION", "ODX-2.2.0"), Sn(p.Id),
                new XElement("ABLOCKS", new XElement("ABLOCK", Sn(p.Id), new XElement("CATEGORY", "ODX-DATA"),
                    new XElement("FILES", new XElement("FILE", odxName))))));
        using var iw = new StreamWriter(zip.CreateEntry("index.xml").Open(), new UTF8Encoding(false));
        index.Save(iw);
    }
}
