// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

namespace Cyberdrome.Core;

/// <summary>Ett svar från ett styrdon: CAN-id och hela (ihopsatta) svaret.</summary>
public sealed record EcuReply(int ResponseId, byte[] Data);

/// <summary>Resultat av en vidarekopplad fråga. Hint visas i OBD-loggen.</summary>
public sealed record BridgeReply(IReadOnlyList<EcuReply> Replies, string? Error = null, string? Hint = null);

/// <summary>
/// Vidarekoppling till en riktig bil: när den är satt på <see cref="VirtualCar.Bridge"/> svarar den riktiga bilen
/// (via adaptern) i stället för simuleringen. AT-kommandon från appen hanteras fortfarande lokalt.
/// </summary>
public interface IObdBridge
{
    /// <summary>Skickar en OBD/UDS-fråga till CAN-id <paramref name="header"/> (0x7DF = alla).</summary>
    BridgeReply Request(int header, byte[] request);

    /// <summary>Senast uppmätta batterispänning från adaptern (ATRV), om känd.</summary>
    double? Voltage { get; }

    /// <summary>ELM-protokollnummer för den riktiga bilen, t.ex. "6".</summary>
    string? Protocol { get; }
}
