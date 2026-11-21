// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Cyberdrome.App.ViewModels;
using Cyberdrome.App.Views;

namespace Cyberdrome.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // --lang=sv|en (skärmbilder): språket väljs innan sidorna och listorna byggs.
            if ((desktop.Args ?? []).FirstOrDefault(a => a.StartsWith("--lang=")) is { } langArg) L.SetLanguage(langArg[7..]);
            var vm = new MainViewModel(Path.Combine(AppContext.BaseDirectory, "profiles"));
            desktop.MainWindow = new MainWindow { DataContext = vm };
            desktop.ShutdownRequested += (_, _) => vm.Shutdown();
            // Startade Windows programmet efter omstarten? Fortsätt då installationen av Android direkt.
            // Dokumentation: --shots=<mapp> tar en skärmbild per sida och avslutar (--lang=sv|en, --size=BxH, --pages=0,10).
            var args = desktop.Args ?? [];
            string? Arg(string name) => args.FirstOrDefault(a => a.StartsWith("--" + name + "="))?[(name.Length + 3)..];
            if (Arg("shots") is { } shots)
            {
                var win = (MainWindow)desktop.MainWindow;
                win.ShotMode = true;
                if (Arg("size") is { } sz && sz.Split('x') is [var w, var h]) { win.Width = double.Parse(w); win.Height = double.Parse(h); }
                win.Opened += async (_, _) => { await Task.Delay(2500); if (args.Contains("--fullscreen")) win.SetFullscreen(true); await win.TakePageShotsAsync(shots, Arg("pages")); desktop.Shutdown(); };
            }
            if (AutoResume.IsResumeStart(desktop.Args))
                desktop.MainWindow.Opened += async (_, _) => await vm.ContinueInstallAfterReboot();
        }
        base.OnFrameworkInitializationCompleted();
    }
}
