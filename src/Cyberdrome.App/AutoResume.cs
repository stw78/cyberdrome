// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

using System.Reflection;
using Microsoft.Win32;

namespace Cyberdrome.App;

/// <summary>
/// När WSL2 just slagits på måste Windows startas om. Då lägger vi in programmet i
/// HKCU\...\RunOnce så att det startar av sig självt efter omstarten och fortsätter installationen.
/// RunOnce-posten tas bort av Windows när den har körts en gång, och kräver inga administratörsrättigheter.
/// </summary>
public static class AutoResume
{
    public const string Argument = "--continue-install";
    private const string RunOnceKey = @"Software\Microsoft\Windows\CurrentVersion\RunOnce";
    private const string ValueName = "CyberdromeContinueAndroidInstall";

    public static bool IsResumeStart(IEnumerable<string>? args) =>
        args?.Any(a => string.Equals(a, Argument, StringComparison.OrdinalIgnoreCase)) == true;

    /// <summary>Kommandoraden som startar just det här programmet igen.</summary>
    public static string CommandLine()
    {
        var exe = Environment.ProcessPath ?? "";
        // Startat som "dotnet Cyberdrome.dll" (t.ex. F5 i VS Code): starta dll:en via dotnet igen.
        if (Path.GetFileNameWithoutExtension(exe).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            var dll = Assembly.GetEntryAssembly()?.Location ?? "";
            return $"\"{exe}\" \"{dll}\" {Argument}";
        }
        return $"\"{exe}\" {Argument}";
    }

    public static bool Register(Action<string> log)
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunOnceKey);
            key.SetValue(ValueName, CommandLine());
            return true;
        }
        catch (Exception ex)
        {
            log(L.T("AutoResume_CouldNotSetUpAutomaticStart") + ex.Message);
            return false;
        }
    }
}
