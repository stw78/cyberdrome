// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

using System.Text.RegularExpressions;

namespace Cyberdrome.Core.Updates;

/// <summary>Regler för uppdateringskontrollen: hur ofta, hur fel visas och hur releaseanteckningar städas.</summary>
public static partial class UpdatePolicy
{
    /// <summary>Automatisk kontroll vid start görs högst en gång per dygn.</summary>
    public static readonly TimeSpan AutoInterval = TimeSpan.FromHours(24);

    public static bool ShouldAutoCheck(DateTime? lastCheckUtc, DateTime nowUtc) =>
        lastCheckUtc is not { } last || nowUtc - last >= AutoInterval || last > nowUtc;

    /// <summary>Gör om tekniska fel (GitHub-gräns, ingen internet) till en tydlig mening med nästa steg.</summary>
    public static string FriendlyError(string message)
    {
        var m = message ?? "";
        if (m.Contains("rate limit", StringComparison.OrdinalIgnoreCase) || m.Contains("403"))
            return L.T("Release_GitHubRateLimit");
        if (m.Contains("404"))
            return L.T("Release_NoReleasesFound");
        if (m.Contains("No such host", StringComparison.OrdinalIgnoreCase) || m.Contains("network", StringComparison.OrdinalIgnoreCase)
            || m.Contains("connection", StringComparison.OrdinalIgnoreCase) || m.Contains("timed out", StringComparison.OrdinalIgnoreCase))
            return L.T("Release_NoInternet");
        return L.T("Release_CouldNotCheckForUpdates") + m;
    }

    /// <summary>Releaseanteckningar som ren text: rubrikmarkeringar och fetstil tas bort, högst 1200 tecken.</summary>
    public static string CleanNotes(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return "";
        var t = markdown.Replace("\r\n", "\n");
        t = Heading().Replace(t, "");
        t = t.Replace("**", "").Replace("`", "");
        t = BlankLines().Replace(t, "\n\n").Trim();
        return t.Length > 1200 ? t[..1200].TrimEnd() + " …" : t;
    }

    [GeneratedRegex(@"^#{1,6}\s*", RegexOptions.Multiline)] private static partial Regex Heading();
    [GeneratedRegex(@"\n{3,}")] private static partial Regex BlankLines();
}
