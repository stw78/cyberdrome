// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

using Cyberdrome.Core.Profiles;

namespace Cyberdrome.Tests;

/// <summary>Avkodning av tillverkarsignaler när de höga bitarna är satta (0x80–0xFF) – får aldrig bli teckenfel.</summary>
public class SignalDecodeTests
{
    [Theory]
    [InlineData(new byte[] { 0xFF }, 0, 8, false, 255)]
    [InlineData(new byte[] { 0x80 }, 0, 8, false, 128)]
    [InlineData(new byte[] { 0xFF }, 0, 8, true, -1)]
    [InlineData(new byte[] { 0x80 }, 0, 8, true, -128)]
    [InlineData(new byte[] { 0x7F }, 0, 8, true, 127)]
    [InlineData(new byte[] { 0xFF, 0xFF }, 0, 16, false, 65535)]
    [InlineData(new byte[] { 0x80, 0x00 }, 0, 16, true, -32768)]
    [InlineData(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF }, 0, 32, false, 4294967295)]
    [InlineData(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF }, 0, 32, true, -1)]
    [InlineData(new byte[] { 0xF0 }, 0, 4, false, 15)]
    [InlineData(new byte[] { 0x0F }, 4, 4, false, 15)]
    [InlineData(new byte[] { 0x01, 0x80 }, 7, 2, false, 3)]
    public void DecodesHighBitsWithoutSignErrors(byte[] data, int bix, int len, bool signed, double expected)
    {
        var s = new ExtraSignal { Bix = bix, Len = len, Signed = signed };
        Assert.Equal(expected, s.Decode(data));
    }

    [Theory]
    [InlineData(8, false, 0, 255)]
    [InlineData(8, true, -128, 127)]
    [InlineData(16, false, 0, 65535)]
    [InlineData(16, true, -32768, 32767)]
    [InlineData(32, false, 0, 4294967295)]
    public void WriteAndDecodeRoundTripOverTheWholeRange(int len, bool signed, double lo, double hi)
    {
        foreach (var v in new[] { lo, hi, lo + 1, hi - 1, Math.Round((lo + hi) / 2) })
        {
            var s = new ExtraSignal { Bix = 0, Len = len, Signed = signed, Max = hi, Min = lo, Value = v };
            var data = new byte[len / 8];
            s.Write(data);
            Assert.Equal(v, s.Decode(data));
        }
    }
}
