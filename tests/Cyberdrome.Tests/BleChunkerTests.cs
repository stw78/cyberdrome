// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

using System.Text;
using Cyberdrome.Obd;

namespace Cyberdrome.Tests;

public class BleChunkerTests
{
    [Fact]
    public void SplitsIn20ByteChunksByDefault()
    {
        var reply = "41 0C 1C E8\r41 0D 36\r\r>"; // 24 tecken
        var chunks = BleChunker.Split(reply).ToList();
        Assert.Equal(2, chunks.Count);
        Assert.Equal(20, chunks[0].Length);
        Assert.Equal(reply, Encoding.ASCII.GetString(chunks.SelectMany(c => c).ToArray()));
    }

    [Fact]
    public void UsesNegotiatedPduMinusHeader()
    {
        var reply = new string('A', 300);
        var chunks = BleChunker.Split(reply, maxPdu: 185).ToList();
        Assert.All(chunks.SkipLast(1), c => Assert.Equal(182, c.Length));
        Assert.Equal(300, chunks.Sum(c => c.Length));
    }

    [Fact]
    public void EmptyReplyGivesNoChunks() => Assert.Empty(BleChunker.Split(""));
}
