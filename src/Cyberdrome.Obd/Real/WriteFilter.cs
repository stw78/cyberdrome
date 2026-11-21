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
        [0x01] = L.T("WriteFilter_OBD01CurrentValues"),
        [0x02] = L.T("WriteFilter_OBD02FreezeFrame"),
        [0x03] = L.T("WriteFilter_OBD03ReadTroubleCodes"),
        [0x05] = L.T("WriteFilter_OBD05OxygenSensorTestRead"),
        [0x06] = "OBD 06 testresultat",
        [0x07] = L.T("WriteFilter_OBD07PendingTroubleCodes"),
        [0x09] = "OBD 09 fordonsinfo",
        [0x0A] = L.T("WriteFilter_OBD0APermanentTroubleCodes"),
        [0x19] = L.T("WriteFilter_UDS19ReadDTCInformation"),
        [0x1A] = L.T("WriteFilter_KWP1AReadECUIdentification"),
        [0x21] = L.T("WriteFilter_KWP21ReadDataLocalId"),
        [0x22] = L.T("WriteFilter_UDS22ReadDataDID"),
        [0x3E] = L.T("WriteFilter_UDS3ETesterPresent"),
    };

    private static readonly Dictionary<byte, string> WriteServices = new()
    {
        [0x04] = L.T("WriteFilter_OBD04ClearTroubleCodes"),
        [0x08] = L.T("WriteFilter_OBD08ControlOnBoardSystem"),
        [0x10] = L.T("WriteFilter_UDS10ChangeDiagnosticSession"),
        [0x11] = L.T("WriteFilter_UDS11ResetControlUnit"),
        [0x14] = L.T("WriteFilter_UDS14ClearTroubleCodes"),
        [0x23] = L.T("WriteFilter_UDS23ReadMemory"),
        [0x28] = L.T("WriteFilter_UDS28CommunicationControl"),
        [0x2C] = L.T("WriteFilter_UDS2CDefineData"),
        [0x2E] = L.T("WriteFilter_UDS2EWriteDataDID"),
        [0x2F] = L.T("WriteFilter_UDS2FInputOutputControl"),
        [0x31] = "UDS 31 rutin",
        [0x34] = "UDS 34 nedladdning",
        [0x35] = "UDS 35 uppladdning",
        [0x36] = L.T("WriteFilter_UDS36TransferData"),
        [0x37] = L.T("WriteFilter_UDS37RequestTransferExit"),
        [0x3B] = L.T("WriteFilter_KWP3BWriteDataLocalId"),
        [0x3D] = L.T("WriteFilter_UDS3DWriteMemory"),
        [0x85] = L.T("WriteFilter_UDS85ControlDTCSetting"),
    };

    public static FilterDecision Classify(byte[] request)
    {
        if (request.Length == 0) return new(FilterKind.Write, L.T("WriteFilter_EmptyRequest"));
        byte sid = request[0];
        if (sid == 0x10)
            return request.Length == 2 && request[1] == 0x01 ? new(FilterKind.Read, "UDS 10 01 standardsession") : new(FilterKind.Write, WriteServices[0x10]);
        if (sid == 0x27) return new(FilterKind.Security, L.T("WriteFilter_UDS27SecurityAccess"));
        if (ReadServices.TryGetValue(sid, out var r)) return new(FilterKind.Read, r);
        if (WriteServices.TryGetValue(sid, out var w)) return new(FilterKind.Write, w);
        return new(FilterKind.Write, L.F("WriteFilter_UnknownService0x0X2Counted", sid));
    }
}
