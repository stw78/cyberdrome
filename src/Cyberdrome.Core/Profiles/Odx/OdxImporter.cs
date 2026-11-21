// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;

namespace Cyberdrome.Core.Profiles.Odx;

/// <summary>Resultat av en ODX-import: bilmodellen och en lista (på svenska) över det som inte kunde användas.</summary>
public sealed record OdxImportResult(CarProfile Profile, IReadOnlyList<string> Ignored, IReadOnlyList<string> Notes);

/// <summary>
/// Läser den del av ASAM ODX (ISO 22901-1, version 2.0.1/2.2) som Cyberdrome använder:
/// diagnoslager (BASE-VARIANT/ECU-VARIANT) → styrdon, CAN-id från COMPARAM-REF, tjänsterna 0x22/0x2E med DID
/// och datalängd → kodning, och DTC-DOP → felkoder med text. Allt annat hoppas över och listas.
/// Tolken är skriven av oss utifrån ODX-strukturen; inga tredjepartsbibliotek.
/// </summary>
public static class OdxImporter
{
    public const string PlaceholderVin = "CDX00000000000001";

    private static IEnumerable<XElement> Desc(XElement e, string name) => e.Descendants().Where(x => x.Name.LocalName == name);
    private static XElement? Child(XElement? e, string name) => e?.Elements().FirstOrDefault(x => x.Name.LocalName == name);
    private static IEnumerable<XElement> Children(XElement? e, string name) => e?.Elements().Where(x => x.Name.LocalName == name) ?? [];
    private static string? Text(XElement? e, string name) => Child(e, name)?.Value.Trim();
    private static string? XsiType(XElement e) => e.Attributes().FirstOrDefault(a => a.Name.LocalName == "type")?.Value;

    /// <summary>Läser .odx / .odx-d (XML) eller .pdx (zip med en eller flera ODX-filer).</summary>
    public static OdxImportResult Import(string path)
    {
        var docs = new List<XDocument>();
        if (path.EndsWith(".pdx", StringComparison.OrdinalIgnoreCase) || IsZip(path))
        {
            using var zip = ZipFile.OpenRead(path);
            foreach (var e in zip.Entries.Where(e => e.Name.Contains(".odx", StringComparison.OrdinalIgnoreCase)))
            {
                using var s = e.Open();
                docs.Add(XDocument.Load(s));
            }
            if (docs.Count == 0) throw new InvalidDataException(L.T("OdxImporter_ThePDXFileContainsNoOdx"));
        }
        else
        {
            try { docs.Add(XDocument.Load(path)); }
            catch (System.Xml.XmlException ex) { throw new InvalidDataException(L.T("OdxImporter_TheFileIsNotValidODX") + ex.Message); }
        }
        return Import(docs, Path.GetFileNameWithoutExtension(path));
    }

    private static bool IsZip(string path)
    {
        using var f = File.OpenRead(path);
        return f.ReadByte() == 'P' && f.ReadByte() == 'K';
    }

    public static OdxImportResult Import(IReadOnlyList<XDocument> docs, string fallbackName)
    {
        var ignored = new List<string>();
        var notes = new List<string>();
        var p = new CarProfile { Make = "ODX-import", Model = fallbackName, Year = DateTime.Now.Year, Vin = PlaceholderVin, Protocol = "6" };
        var dtcTexts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var codings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var doc in docs)
        {
            var root = doc.Root ?? throw new InvalidDataException(L.T("OdxImporter_EmptyODXFile"));
            if (root.Name.LocalName != "ODX") throw new InvalidDataException(L.T("OdxImporter_TheFileIsNotODXThe"));
            var ver = root.Attribute("MODEL-VERSION")?.Value;
            if (ver is not null && !ver.StartsWith("2.", StringComparison.Ordinal)) notes.Add(L.F("OdxImporter_ODXVersion0Only2X", ver));

            // Våra egna tilläggsdata (SDG "Cyberdrome") gör export → import förlustfri.
            foreach (var sdg in Desc(root, "SDG").Where(s => Text(Child(s, "SDG-CAPTION"), "SHORT-NAME") == "Cyberdrome"))
                ReadCyberdromeSdg(sdg, p, codings);

            // Delar vi inte använder
            void Skip(string element, string what)
            {
                int n = Desc(root, element).Count();
                if (n > 0) ignored.Add(L.F("Odx_Count01", what, n));
            }
            Skip("FLASH", L.T("Odx_Flash"));
            Skip("SINGLE-ECU-JOB", L.T("Odx_SingleJob"));
            Skip("MULTIPLE-ECU-JOB", L.T("OdxImporter_JobsAcrossSeveralControlUnitsMULTIPLE"));
            Skip("FUNCTIONAL-GROUP", L.T("Odx_FunctionalGroup"));
            Skip("COMPARAM-SPEC", L.T("Odx_ComparamSpec"));
            Skip("VEHICLE-INFO-SPEC", L.T("Odx_VehicleInfo"));
            Skip("PROTOCOL", L.T("Odx_Protocol"));

            var layers = Desc(root, "BASE-VARIANT").Concat(Desc(root, "ECU-VARIANT")).ToList();
            foreach (var layer in layers)
            {
                var shortName = Text(layer, "SHORT-NAME") ?? "styrdon";
                var key = ProfileStore.SafeId(shortName);
                // ECU-VARIANT som ärver ett BASE-VARIANT med samma CAN-id slås ihop.
                var (req, resp) = CanIds(layer);
                if (req is null)
                {
                    if (layer.Name.LocalName == "ECU-VARIANT") { ignored.Add(L.F("OdxImporter_ECUVariant0WithoutItsOwn", shortName)); continue; }
                    notes.Add(L.F("OdxImporter_0CANIdMissing7E07E8", shortName));
                    req = 0x7E0; resp = 0x7E8;
                }
                if (p.Ecus.Any(e => e.RequestIdValue == req)) { notes.Add(L.F("OdxImporter_0SameCANIdAsAnother", shortName)); continue; }

                var ecu = new EcuProfile
                {
                    Key = key, Name = Text(layer, "LONG-NAME") ?? shortName,
                    RequestId = req.Value.ToString("X3"), ResponseId = (resp ?? req.Value + 8).ToString("X3"),
                };
                ReadCodingService(layer, ecu, notes, ignored);
                p.Ecus.Add(ecu);

                foreach (var dtc in Desc(layer, "DTC"))
                {
                    var code = Text(dtc, "DISPLAY-TROUBLE-CODE") ?? Text(dtc, "SHORT-NAME") ?? FromTroubleCode(Text(dtc, "TROUBLE-CODE"));
                    if (code is null || code.Length != 5) continue;
                    code = code.ToUpperInvariant();
                    dtcTexts[code] = Text(dtc, "TEXT") ?? Text(dtc, "LONG-NAME") ?? "";
                }

                int sec = Desc(layer, "DIAG-SERVICE").Count(s => ServiceId(layer, s) == 0x27);
                if (sec > 0) ignored.Add(L.F("OdxImporter_0SecurityAccess0x271Services", shortName, sec));
                int other = Desc(layer, "DIAG-SERVICE").Count(s => ServiceId(layer, s) is not (0x22 or 0x2E or 0x27 or 0x10 or 0x3E or 0x19 or 0x14));
                if (other > 0) ignored.Add(L.F("OdxImporter_0OtherDiagnosticServices1", shortName, other));
            }
        }

        // Kodningsvärden från Cyberdromes SDG (ODX har bara längden).
        foreach (var e in p.Ecus)
            if (codings.TryGetValue(e.Key, out var c) && c.Length == e.Coding.Length) e.Coding = c;
        if (p.Ecus.Count == 0) throw new InvalidDataException(L.T("OdxImporter_NoControlUnitsBASEVARIANTECU"));
        if (dtcTexts.Count > 0) p.DtcDescriptions = dtcTexts;
        p.Id = ProfileStore.SafeId(p.Id.Length > 0 ? p.Id : p.Make + "-" + p.Model);
        if (p.Vin == PlaceholderVin) notes.Add(L.T("OdxImporter_VINMissingInODXThePlaceholder"));
        return new OdxImportResult(p, ignored, notes);
    }

    private static void ReadCyberdromeSdg(XElement sdg, CarProfile p, Dictionary<string, string> codings)
    {
        foreach (var sd in Children(sdg, "SD"))
        {
            var v = sd.Value.Trim();
            var si = sd.Attribute("SI")?.Value ?? "";
            if (si.StartsWith("coding:", StringComparison.Ordinal)) { codings[si[7..]] = v; continue; }
            switch (si)
            {
                case "id": p.Id = v; break;
                case "make": p.Make = v; break;
                case "model": p.Model = v; break;
                case "generation": p.Generation = v; break;
                case "year": if (int.TryParse(v, out var y)) p.Year = y; break;
                case "vin": p.Vin = v; break;
                case "protocol": p.Protocol = v; break;
                case "theme": p.Theme = v; break;
                case "screenPreset": p.ScreenPreset = v; break;
                case "startDtcs": p.StartDtcs = v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(); break;
                case "supportedPids": p.SupportedPids = v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(); break;
                case "features":
                    try { p.Features = System.Text.Json.JsonSerializer.Deserialize<List<CodingFeature>>(v, ProfileLoader.JsonOptions) ?? new(); } catch { }
                    break;
            }
        }
    }

    /// <summary>CAN-id för fråga och svar ur COMPARAM-REF (CP_CanPhysReqId / CP_CanRespUSDTId).</summary>
    private static (int? Req, int? Resp) CanIds(XElement layer)
    {
        int? req = null, resp = null;
        foreach (var c in Children(Child(layer, "COMPARAM-REFS"), "COMPARAM-REF"))
        {
            var id = c.Attribute("ID-REF")?.Value ?? "";
            var val = Text(c, "SIMPLE-VALUE") ?? Text(c, "VALUE");
            if (ParseInt(val) is not { } n) continue;
            if (id.EndsWith("CP_CanPhysReqId", StringComparison.Ordinal)) req = n;
            else if (id.EndsWith("CP_CanRespUSDTId", StringComparison.Ordinal)) resp = n;
        }
        return (req, resp);
    }

    public static int? ParseInt(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim();
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return int.TryParse(s[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var h) ? h : null;
        return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var d) ? d : null;
    }

    private static XElement? Resolve(XElement layer, XElement? refEl)
    {
        var id = refEl?.Attribute("ID-REF")?.Value;
        if (id is null) return null;
        return layer.Document?.Descendants().FirstOrDefault(e => e.Attribute("ID")?.Value == id);
    }

    /// <summary>Värdet i en CODED-CONST-parameter med viss semantik (t.ex. SERVICE-ID eller ID).</summary>
    private static int? Coded(XElement? request, string semantic) =>
        request is null ? null :
        Desc(request, "PARAM").Where(pp => pp.Attribute("SEMANTIC")?.Value == semantic && XsiType(pp) == "CODED-CONST")
            .Select(pp => ParseInt(Text(pp, "CODED-VALUE"))).FirstOrDefault(v => v is not null);

    private static int? ServiceId(XElement layer, XElement service) => Coded(Resolve(layer, Child(service, "REQUEST-REF")), "SERVICE-ID");

    /// <summary>Kodning: tjänsten 2E (skriv) eller 22 (läs) med en DID; längden tas från datafältets DOP.</summary>
    private static void ReadCodingService(XElement layer, EcuProfile ecu, List<string> notes, List<string> ignored)
    {
        var services = Desc(layer, "DIAG-SERVICE").ToList();
        var candidates = services.Select(s => (Service: s, Req: Resolve(layer, Child(s, "REQUEST-REF")))).ToList();
        var write = candidates.FirstOrDefault(c => Coded(c.Req, "SERVICE-ID") == 0x2E);
        var read = candidates.FirstOrDefault(c => Coded(c.Req, "SERVICE-ID") == 0x22 &&
            ((Text(c.Service, "SHORT-NAME") ?? "") + (Text(c.Service, "LONG-NAME") ?? "")).Contains("od", StringComparison.OrdinalIgnoreCase));
        var chosen = write.Req is not null ? write : read;
        if (chosen.Req is null) { notes.Add(L.F("OdxImporter_0NoCodingService222E", ecu.Key)); ecu.Coding = "00000000"; return; }

        var did = Coded(chosen.Req, "ID") ?? Coded(chosen.Req, "DATA-ID");
        if (did is { } d) ecu.CodingDid = d.ToString("X4");
        // Datalängd: VALUE-parameter i skrivfrågan, annars i läs-svaret.
        int bits = DataBits(layer, chosen.Req);
        if (bits == 0)
        {
            var posRef = Children(Child(chosen.Service, "POS-RESPONSE-REFS"), "POS-RESPONSE-REF").FirstOrDefault();
            bits = DataBits(layer, Resolve(layer, posRef));
        }
        int bytes = bits > 0 ? (bits + 7) / 8 : 4;
        if (bytes > ProfileLoader.MaxCodingBytes)
        {
            ignored.Add(L.F("OdxImporter_0Coding1BytesOnlyThe", ecu.Key, bytes, ProfileLoader.MaxCodingBytes));
            bytes = ProfileLoader.MaxCodingBytes;
        }
        ecu.Coding = new string('0', bytes * 2);
    }

    private static int DataBits(XElement layer, XElement? message)
    {
        if (message is null) return 0;
        foreach (var pp in Desc(message, "PARAM").Where(x => XsiType(x) == "VALUE"))
        {
            var dop = Resolve(layer, Child(pp, "DOP-REF"));
            var bl = dop is null ? null : Desc(dop, "BIT-LENGTH").FirstOrDefault()?.Value;
            if (ParseInt(bl) is { } b) return b;
        }
        return 0;
    }

    /// <summary>TROUBLE-CODE (tal) → "P0300".</summary>
    public static string? FromTroubleCode(string? tc)
    {
        if (ParseInt(tc) is not { } v) return null;
        v &= 0xFFFF;
        return "PCBU"[(v >> 14) & 3] + (v & 0x3FFF).ToString("X4");
    }
}
