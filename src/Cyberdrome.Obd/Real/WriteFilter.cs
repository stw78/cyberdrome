// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

namespace Cyberdrome.Obd.Real;

public enum FilterKind
{
    /// <summary>Läsning – skickas alltid.</summary>
    Read,
    /// <summary>Ändrar något i bilen – bara med "Tillåt skrivning" + bekräftelse.</summary>
    Write,
    /// <summary>Säkerhetsåtkomst (0x27) – bara när skrivning är tillåten; nyckeln kommer från appen.</summary>
    Security,
}

public sealed record FilterDecision(FilterKind Kind, string Service);

/// <summary>
/// Skrivfilter för riktiga bilar. Standard är läsläge: allt som kan ändra något i bilen stoppas.
/// Okända tjänster räknas som skrivning (hellre stoppa än skada).
/// </summary>
public static class WriteFilter
{
    private static readonly Dictionary<byte, string> ReadServices = new()
    {
        [0x01] = "OBD 01 aktuella värden",
        [0x02] = "OBD 02 frysta värden",
        [0x03] = "OBD 03 läs felkoder",
        [0x05] = "OBD 05 lambdasondtest (läs)",
        [0x06] = "OBD 06 testresultat",
        [0x07] = "OBD 07 väntande felkoder",
        [0x09] = "OBD 09 fordonsinfo",
        [0x0A] = "OBD 0A permanenta felkoder",
        [0x19] = "UDS 19 läs felkodsinfo",
        [0x1A] = "KWP 1A läs styrdons-id",
        [0x21] = "KWP 21 läs data (lokalt id)",
        [0x22] = "UDS 22 läs data (DID)",
        [0x3E] = "UDS 3E tester present",
    };

    private static readonly Dictionary<byte, string> WriteServices = new()
    {
        [0x04] = "OBD 04 radera felkoder",
        [0x08] = "OBD 08 styr system i bilen",
        [0x10] = "UDS 10 byt diagnossession",
        [0x11] = "UDS 11 starta om styrdon",
        [0x14] = "UDS 14 radera felkoder",
        [0x23] = "UDS 23 läs minne",
        [0x28] = "UDS 28 styr kommunikation",
        [0x2C] = "UDS 2C definiera data",
        [0x2E] = "UDS 2E skriv data (DID)",
        [0x2F] = "UDS 2F styr in-/utgång",
        [0x31] = "UDS 31 rutin",
        [0x34] = "UDS 34 nedladdning",
        [0x35] = "UDS 35 uppladdning",
        [0x36] = "UDS 36 dataöverföring",
        [0x37] = "UDS 37 avsluta överföring",
        [0x3B] = "KWP 3B skriv data (lokalt id)",
        [0x3D] = "UDS 3D skriv minne",
        [0x85] = "UDS 85 styr felkodsinställning",
    };

    public static FilterDecision Classify(byte[] request)
    {
        if (request.Length == 0) return new(FilterKind.Write, "tom fråga");
        byte sid = request[0];
        if (sid == 0x10)
            return request.Length == 2 && request[1] == 0x01 ? new(FilterKind.Read, "UDS 10 01 standardsession") : new(FilterKind.Write, WriteServices[0x10]);
        if (sid == 0x27) return new(FilterKind.Security, "UDS 27 säkerhetsåtkomst");
        if (ReadServices.TryGetValue(sid, out var r)) return new(FilterKind.Read, r);
        if (WriteServices.TryGetValue(sid, out var w)) return new(FilterKind.Write, w);
        return new(FilterKind.Write, $"okänd tjänst 0x{sid:X2} (räknas som skrivning)");
    }
}
