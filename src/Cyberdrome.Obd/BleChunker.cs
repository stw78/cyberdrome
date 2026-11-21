// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

using System.Text;

namespace Cyberdrome.Obd;

/// <summary>
/// Delar upp ett ELM327-svar i bitar som får plats i en Bluetooth LE-notifiering.
/// Standard är 20 byte (minsta storleken alla telefoner klarar); större om telefonen förhandlat det.
/// </summary>
public static class BleChunker
{
    public const int DefaultChunk = 20;

    public static IEnumerable<byte[]> Split(string reply, int maxPdu = 0)
    {
        int size = maxPdu > 3 ? Math.Min(maxPdu - 3, 512) : DefaultChunk;
        var bytes = Encoding.ASCII.GetBytes(reply);
        for (int i = 0; i < bytes.Length; i += size)
            yield return bytes.AsSpan(i, Math.Min(size, bytes.Length - i)).ToArray();
    }
}
