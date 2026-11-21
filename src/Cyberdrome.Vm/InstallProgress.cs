// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

using System.Globalization;

namespace Cyberdrome.Vm;

/// <summary>
/// Hur långt installationen har kommit.
/// <paramref name="Percent"/> är 0–100 för hela installationen, eller null när det inte går att veta (rullande stapel).
/// </summary>
public sealed record InstallProgress(double? Percent, string Text, bool Done = false);

/// <summary>
/// Läser förloppsrader som våra skript skriver ut:
///   @@STEP 0.4 Installerar Cuttlefish   (hur långt skriptet kommit, 0–1, och vad det gör)
///   @@BYTES 123456789                   (hur många byte som laddats ner hittills)
/// </summary>
public static class ProgressParser
{
    public const string Prefix = "@@";

    public static bool IsProgressLine(string line) => line.StartsWith(Prefix, StringComparison.Ordinal);

    /// <summary>"@@STEP 0.4 Text" → (0.4, "Text"). Null om raden inte är en STEP-rad.</summary>
    public static (double Fraction, string Text)? ParseStep(string line)
    {
        if (!line.StartsWith("@@STEP ", StringComparison.Ordinal)) return null;
        var parts = line[7..].Split(' ', 2);
        if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var f)) return null;
        return (Math.Clamp(f, 0, 1), parts.Length > 1 ? parts[1].Trim() : "");
    }

    /// <summary>"@@BYTES 123" → 123. Null om raden inte är en BYTES-rad.</summary>
    public static long? ParseBytes(string line)
    {
        if (!line.StartsWith("@@BYTES ", StringComparison.Ordinal)) return null;
        return long.TryParse(line[8..].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var b) ? Math.Max(0, b) : null;
    }

    /// <summary>
    /// Andel nedladdat (0–0.99) utifrån förväntad storlek. Stannar på 99 % tills skriptet säger att det är klart,
    /// eftersom storleken bara är en uppskattning.
    /// </summary>
    public static double DownloadFraction(long bytes, long expectedBytes) =>
        expectedBytes <= 0 ? 0 : Math.Min(0.99, (double)bytes / expectedBytes);

    public static string FormatMb(long bytes) =>
        (bytes / 1024d / 1024d).ToString("N0", CultureInfo.GetCultureInfo("sv-SE")) + " MB";
}
