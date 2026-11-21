// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

using System.Buffers.Binary;
using System.Globalization;

namespace Cyberdrome.Core.Profiles;

/// <summary>
/// Påhittad seed/key-formel för UDS 0x27 SecurityAccess i de SIMULERADE styrdonen.
/// Detta är vår egen testformel – den motsvarar inte någon riktig biltillverkares algoritm
/// och kan inte användas mot riktiga bilar.
///
/// nyckel = rotateLeft(seed XOR xorMask, rotateLeft) + add   (32 bitar, big-endian)
///
/// I profil-JSON (på bilen eller på ett enskilt styrdon):
///   "securityAccess": { "xorMask": "5A3C96E1", "rotateLeft": 7, "add": "1234ABCD" }
/// </summary>
public sealed class SecurityAccessConfig
{
    public static readonly SecurityAccessConfig Default = new();

    /// <summary>4 byte i hex.</summary>
    public string XorMask { get; set; } = "5A3C96E1";

    /// <summary>Antal bitar att rotera åt vänster (0–31).</summary>
    public int RotateLeft { get; set; } = 7;

    /// <summary>4 byte i hex som läggs till (mod 2^32).</summary>
    public string Add { get; set; } = "1234ABCD";

    private static uint Hex32(string s) =>
        uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v) ? v : 0;

    /// <summary>Räknar ut nyckeln för ett 4-byte-seed.</summary>
    public byte[] ComputeKey(ReadOnlySpan<byte> seed)
    {
        if (seed.Length != 4) throw new ArgumentException("Seed ska vara 4 byte.", nameof(seed));
        uint s = BinaryPrimitives.ReadUInt32BigEndian(seed);
        uint k = uint.RotateLeft(s ^ Hex32(XorMask), ((RotateLeft % 32) + 32) % 32) + Hex32(Add);
        var key = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(key, k);
        return key;
    }
}
