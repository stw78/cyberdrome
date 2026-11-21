// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

using System.Buffers.Binary;
using System.Text.RegularExpressions;

namespace Cyberdrome.Vm;

/// <summary>Små hjälpare för skärmbilder: bildstorlek ur en PNG och skärmstorlek ur "wm size".</summary>
public static partial class PngInfo
{
    /// <summary>Bredd och höjd ur PNG-huvudet (IHDR), eller null om det inte är en PNG.</summary>
    public static (int W, int H)? Size(byte[] png)
    {
        if (png.Length < 24 || png[0] != 0x89 || png[1] != (byte)'P' || png[12] != (byte)'I' || png[13] != (byte)'H') return null;
        return (BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4)), BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4)));
    }

    /// <summary>"Physical size: 1560x700 / Override size: 1280x800" → den storlek Android ritar i (override vinner).</summary>
    public static (int W, int H)? ParseWmSize(string output)
    {
        (int, int)? physical = null, over = null;
        foreach (Match m in WmLine().Matches(output))
        {
            var v = (int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value));
            if (m.Groups[1].Value.StartsWith("Override", StringComparison.OrdinalIgnoreCase)) over = v; else physical = v;
        }
        return over ?? physical;
    }

    /// <summary>Både fysisk storlek och override ur "wm size" (override null om den saknas).</summary>
    public static ((int W, int H)? Physical, (int W, int H)? Override) ParseWmSizes(string output)
    {
        (int, int)? physical = null, over = null;
        foreach (Match m in WmLine().Matches(output))
        {
            var v = (int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value));
            if (m.Groups[1].Value.StartsWith("Override", StringComparison.OrdinalIgnoreCase)) over = v; else physical = v;
        }
        return (physical, over);
    }

    [GeneratedRegex(@"(Physical|Override) size:\s*(\d+)x(\d+)")]
    private static partial Regex WmLine();
}
