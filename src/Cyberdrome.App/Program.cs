// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

using System.Runtime.InteropServices;
using Avalonia;
using Velopack;

namespace Cyberdrome.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Velopack måste köras först: hanterar installation, uppdatering och avinstallation (och avslutar då direkt).
        VelopackApp.Build()
            .OnBeforeUninstallFastCallback(_ => AskRemoveUserData())
            .Run();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    /// <summary>
    /// Vid avinstallation: dina data (bilmodeller, teman, körningar, testfall, inställningar i %AppData%\Cyberdrome och
    /// loggar/cache i %LocalAppData%\Cyberdrome) behålls – om du inte väljer "Ja, ta bort mina data".
    /// </summary>
    private static void AskRemoveUserData()
    {
        if (!OperatingSystem.IsWindows()) return;
        const uint MB_YESNO = 0x4, MB_ICONQUESTION = 0x20, MB_DEFBUTTON2 = 0x100, MB_TOPMOST = 0x40000;
        const int IDYES = 6;
        var roaming = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Cyberdrome");
        var local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Cyberdrome");
        int answer = MessageBoxW(IntPtr.Zero,
            "Cyberdrome Simulator avinstalleras.\n\nVill du även ta bort mina data?\n" +
            $"  {roaming}\n  {local}\n\n(egna bilmodeller, teman, skal, körningar, testfall, säkerhetskopior, inställningar och loggar)\n\n" +
            "Nej (standard) = dina data finns kvar om du installerar igen.",
            "Ta bort mina data?", MB_YESNO | MB_ICONQUESTION | MB_DEFBUTTON2 | MB_TOPMOST);
        if (answer != IDYES) return;
        foreach (var dir in new[] { roaming, local })
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { /* låst fil – resten tas bort */ }
    }
}
