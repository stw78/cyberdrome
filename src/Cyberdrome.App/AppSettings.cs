// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

using System.Text.Json;

namespace Cyberdrome.App;

/// <summary>Programmets egna inställningar. Sparas i %AppData%\Cyberdrome\app.json.</summary>
public sealed class AppSettings
{
    /// <summary>Virtuell Bluetooth OBD-dongel ("OBDII") påslagen. Standard: av.</summary>
    public bool BluetoothObd { get; set; }

    /// <summary>Virtuell Bluetooth LE OBD-dongel (för iPhone). Standard: av.</summary>
    public bool BleObd { get; set; }

    /// <summary>
    /// Adress till kartrutor ({z}/{x}/{y}). Standard: OpenStreetMap, som bara får användas måttligt
    /// enligt deras regler. I den sålda produkten ska en kommersiell leverantör användas.
    /// </summary>
    public string MapTileUrl { get; set; } = "https://tile.openstreetmap.org/{z}/{x}/{y}.png";

    /// <summary>Valt tema per bilmodell (profil-id → tema-id). Saknas = bilens eget ("auto").</summary>
    public Dictionary<string, string> ThemeByProfile { get; set; } = new();

    /// <summary>Valt skal per bilmodell (profil-id → skal-id). Saknas = bilens eget ("auto").</summary>
    public Dictionary<string, string> SkinByProfile { get; set; } = new();

    /// <summary>Vilken version (APK-tidsstämpel) av Cyberdrome Launcher som installerats i varje virtuell bil.</summary>
    public Dictionary<string, long> InstalledLauncher { get; set; } = new();

    /// <summary>Skicka bilens GPS-position till Android-emulatorn. Standard: på.</summary>
    public bool SendGpsToAndroid { get; set; } = true;

    /// <summary>Vilken version av ansvarsfriskrivningen som visats (null = inte visad än).</summary>
    public string? EulaAccepted { get; set; }

    /// <summary>Mapp med uppdateringar (Velopack-paket). Tom = &lt;arbetsmapp&gt;\release\uppdateringar (se WorkFolder).</summary>
    public string? UpdateFolder { get; set; }

    public static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Cyberdrome", "app.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new();
        }
        catch { /* trasig fil: standardvärden */ }
        return new();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* inte kritiskt */ }
    }
}
