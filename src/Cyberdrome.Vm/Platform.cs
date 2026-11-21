// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

namespace Cyberdrome.Vm;

/// <summary>Skillnader mellan Windows och Linux: programnamn, skript och standardmappar.</summary>
public static class Platform
{
    public static bool IsWindows => OperatingSystem.IsWindows();
    public static bool IsLinux => OperatingSystem.IsLinux();

    /// <summary>"adb" → "adb.exe" på Windows.</summary>
    public static string Exe(string name) => IsWindows ? name + ".exe" : name;

    /// <summary>"sdkmanager" → "sdkmanager.bat" på Windows (skal-skript utan ändelse på Linux).</summary>
    public static string Script(string name) => IsWindows ? name + ".bat" : name;

    /// <summary>Standardmapp för Android SDK: C:\CyberdromeSDK på Windows, ~/CyberdromeSDK på Linux.</summary>
    public static string DefaultSdkRoot => IsWindows
        ? @"C:\CyberdromeSDK"
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "CyberdromeSDK");

    /// <summary>"windows" eller "linux" i Googles och Adoptiums nedladdningsadresser.</summary>
    public static string DownloadOs => IsWindows ? "win" : "linux";
}
