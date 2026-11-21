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
            var vm = new MainViewModel(Path.Combine(AppContext.BaseDirectory, "profiles"));
            desktop.MainWindow = new MainWindow { DataContext = vm };
            desktop.ShutdownRequested += (_, _) => vm.Shutdown();
            // Startade Windows programmet efter omstarten? Fortsätt då installationen av Android direkt.
            if (AutoResume.IsResumeStart(desktop.Args))
                desktop.MainWindow.Opened += async (_, _) => await vm.ContinueInstallAfterReboot();
        }
        base.OnFrameworkInitializationCompleted();
    }
}
