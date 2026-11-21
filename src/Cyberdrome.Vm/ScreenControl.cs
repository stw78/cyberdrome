// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

using System.Globalization;
using System.Text.RegularExpressions;

namespace Cyberdrome.Vm;

/// <summary>Rattknapparna i bilen.</summary>
public enum SteeringButton { Previous, Next, Mute }

public static class SteeringKeys
{
    /// <summary>Androids tangentkod för en rattknapp (KEYCODE_MEDIA_PREVIOUS/NEXT, KEYCODE_VOLUME_MUTE).</summary>
    public static int KeyCode(SteeringButton b) => b switch
    {
        SteeringButton.Previous => 88,
        SteeringButton.Next => 87,
        SteeringButton.Mute => 164,
        _ => throw new ArgumentOutOfRangeException(nameof(b)),
    };

    public static string Label(SteeringButton b) => b switch
    {
        SteeringButton.Previous => "Föregående",
        SteeringButton.Next => "Nästa",
        SteeringButton.Mute => "Ljud av",
        _ => b.ToString(),
    };
}

public static class ScreenMapping
{
    /// <summary>
    /// Räknar om en punkt i en bild som visas med "Uniform" (hela bilden syns, centrerad, med kanter)
    /// till skärmens pixlar. Null om punkten ligger utanför själva bilden.
    /// </summary>
    public static (int X, int Y)? ToDevice(double px, double py, double viewW, double viewH, int devW, int devH)
    {
        if (viewW <= 0 || viewH <= 0 || devW <= 0 || devH <= 0) return null;
        double scale = Math.Min(viewW / devW, viewH / devH);
        double offX = (viewW - devW * scale) / 2, offY = (viewH - devH * scale) / 2;
        double x = (px - offX) / scale, y = (py - offY) / scale;
        if (x < 0 || y < 0 || x >= devW || y >= devH) return null;
        return ((int)x, (int)y);
    }
}

public static class ScreenshotNaming
{
    /// <summary>Standardmappen för skärmbilder: &lt;arbetsmapp&gt;\skarmbilder (se <see cref="WorkFolder"/>).</summary>
    public static string DefaultFolder => Path.Combine(WorkFolder.Root, "skarmbilder");

    /// <summary>"android-2026-11-20_00-45-12.png" – sorteras i tidsordning och innehåller inga ogiltiga tecken.</summary>
    public static string FileName(DateTime time) =>
        "android-" + time.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture) + ".png";

    /// <summary>Ger en ledig sökväg i mappen (lägger till -2, -3 … om två bilder tas samma sekund).</summary>
    public static string UniquePath(string folder, DateTime time)
    {
        var name = FileName(time);
        var path = Path.Combine(folder, name);
        for (int i = 2; File.Exists(path); i++)
            path = Path.Combine(folder, Path.GetFileNameWithoutExtension(name) + $"-{i}.png");
        return path;
    }
}

/// <summary>
/// Android-skärmen i programfönstret: skärmbilder, klick, svep och knappar via adb.
/// Finns i läget Utan omstart (emulatorn).
/// </summary>
public interface IScreenControl
{
    /// <summary>En skärmbild (PNG) av bilens huvudskärm, eller null om Android inte svarar.</summary>
    Task<byte[]?> CaptureScreenAsync(CancellationToken ct = default);

    Task TapAsync(int x, int y, CancellationToken ct = default);
    Task SwipeAsync(int x1, int y1, int x2, int y2, int durationMs, CancellationToken ct = default);
    Task KeyEventAsync(int keyCode, CancellationToken ct = default);

    /// <summary>Stänger och startar om appen som syns i förgrunden. Returnerar paketnamnet, eller null.</summary>
    Task<string?> RestartForegroundAppAsync(Action<string> log, CancellationToken ct = default);

    /// <summary>Startar en installerad app.</summary>
    Task<bool> LaunchAppAsync(string packageName, CancellationToken ct = default);
}

public static partial class ActivityParser
{
    /// <summary>
    /// Hittar komponenten (paket/aktivitet) som syns överst i "dumpsys activity activities",
    /// t.ex. "topResumedActivity=ActivityRecord{… u0 com.android.car.carlauncher/.CarLauncher t5}".
    /// </summary>
    public static string? TopComponent(string dumpsys)
    {
        foreach (var key in new[] { "topResumedActivity", "mResumedActivity", "ResumedActivity" })
        {
            foreach (var line in dumpsys.Split('\n'))
            {
                if (!line.Contains(key, StringComparison.Ordinal)) continue;
                var m = ComponentRegex().Match(line);
                if (m.Success) return m.Groups[1].Value;
            }
        }
        return null;
    }

    [GeneratedRegex(@"u\d+\s+([\w.]+/[\w.$]+)")]
    private static partial Regex ComponentRegex();
}

/// <summary>
/// Arbetsmapp för filer som användaren vill se (skärmbilder, uppdateringspaket): Skrivbordet\Projekt om den finns,
/// annars Dokument\Cyberdrome.
/// </summary>
public static class WorkFolder
{
    public static string Root
    {
        get
        {
            var projekt = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Projekt");
            return Directory.Exists(projekt) ? projekt
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Cyberdrome");
        }
    }
}
