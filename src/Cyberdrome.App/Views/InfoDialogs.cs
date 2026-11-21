// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

using System.Diagnostics;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Cyberdrome.App.Views;

/// <summary>Fönstren "Om Cyberdrome", "Licens" (GPL-3.0 och tredjepartslicenser) och ansvarsfriskrivningen vid första starten.</summary>
public static class InfoDialogs
{
    /// <summary>Ansvarsfriskrivningens version. Ändras den visas den igen vid nästa start.</summary>
    public const string DisclaimerVersion = "1.0-gpl";

    public static string AppVersion
    {
        get
        {
            var v = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(1, 0, 0);
            return $"{v.Major}.{v.Minor}.{v.Build}";
        }
    }

    public static string ReadText(string file)
    {
        var p = Path.Combine(AppContext.BaseDirectory, file);
        return File.Exists(p) ? File.ReadAllText(p) : $"({file} saknas i programmappen)";
    }

    /// <summary>Markdown → läsbar text (rubriker och fetstil utan tecken).</summary>
    public static string PlainMarkdown(string md) =>
        string.Join("\n", md.Replace("\r", "").Split('\n').Select(l =>
            l.StartsWith("## ") ? "\n" + l[3..].ToUpperInvariant() : l.StartsWith("# ") ? l[2..].ToUpperInvariant() : l)).Replace("**", "");

    private static readonly IBrush Accent = new SolidColorBrush(Color.Parse("#2563EB"));
    private static readonly IBrush Muted = new SolidColorBrush(Color.Parse("#475569"));
    private static readonly IBrush Warn = new SolidColorBrush(Color.Parse("#FEF3C7"));
    private static readonly IBrush Soft = new SolidColorBrush(Color.Parse("#EEF2FF"));

    private static Window NewWindow(string title, double width, double height) => new()
    {
        Title = title, Width = width, Height = height, MinWidth = 520, MinHeight = 420, Background = Brushes.White,
        WindowStartupLocation = WindowStartupLocation.CenterOwner,
        Icon = new WindowIcon(Avalonia.Platform.AssetLoader.Open(new Uri("avares://Cyberdrome/Assets/cyberdrome.ico"))),
    };

    private static TextBlock Text(string s, double size = 13, FontWeight? weight = null, IBrush? brush = null) =>
        new() { Text = s, FontSize = size, FontWeight = weight ?? FontWeight.Normal, Foreground = brush ?? Brushes.Black, TextWrapping = TextWrapping.Wrap };

    private static Control Mono(string text) => new ScrollViewer
    {
        Content = new SelectableTextBlock { Text = text, FontFamily = new FontFamily("Consolas,Menlo,monospace"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Black, Margin = new Thickness(0, 0, 12, 0) },
    };

    public static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }

    public const string Description =
        "Cyberdrome simulerar en Android-bilradio med en virtuell OBD-port, så att du kan utveckla och testa media-, radio- och " +
        "OBD-appar utan en riktig bil. Programmet kör Android Automotive, emulerar bilmodeller med egna teman, felkoder, kodning " +
        "och GPS, och kan även ansluta en riktig OBD-adapter.";

    public static readonly string[] Features =
    [
        "Android Automotive i programmet (utan omstart), egna teman och skal per bilmodell",
        "Virtuell OBD-port (ELM327) via Wi-Fi, Bluetooth och Bluetooth LE, med UDS och säkerhetsnyckel",
        "Bilmodeller: egna, delade (.cbs), ODX/VSS/CSV och bildatabas (vPIC, Wikidata, OBDb)",
        "Körningar och karta med GPS till Android",
        "Testfall med godkänt/underkänt och rapporter",
        "Riktig OBD-adapter: läsläge, vidarekoppling till Android och skrivskydd",
    ];

    public const string Disclaimer =
        "Cyberdrome tillhandahålls utan någon garanti. Det är en simulator för utveckling och test – inte för användning under " +
        "körning. Bilmodellernas värden, felkoder och kodningar är påhittade exempel. Skrivning till riktiga bilar sker på egen " +
        "risk (programmet är i läsläge som standard). Android-avbildningar med Google-tjänster hämtas av dig från Google och får " +
        "bara användas för utveckling och test. Inga tillverkares logotyper, typsnitt eller säkerhetsalgoritmer ingår.";

    private static Control DonationBox()
    {
        var btn = new Button
        {
            Content = Donation.Available ? "Donera via PayPal" : "PayPal-länk kommer snart",
            IsEnabled = Donation.Available, Padding = new Thickness(14, 6), Background = Accent, Foreground = Brushes.White,
        };
        btn.Click += (_, _) => OpenUrl(Donation.Url);
        return new Border
        {
            Background = Soft, CornerRadius = new CornerRadius(8), Padding = new Thickness(12),
            Child = new StackPanel { Spacing = 6, Children =
            {
                Text("Stöd utvecklingen", 14, FontWeight.SemiBold),
                Text("Cyberdrome är gratis och öppen källkod. Utvecklingen drivs av donationer – varje bidrag hjälper till med nya " +
                     "bilmodeller, funktioner och tester mot riktiga bilar."),
                btn,
            } },
        };
    }

    public static async Task ShowAboutAsync(Window owner)
    {
        var win = NewWindow("Om Cyberdrome", 720, 760);
        var licenseBtn = new Button { Content = "Licens …", Padding = new Thickness(14, 6) };
        var close = new Button { Content = "Stäng", Padding = new Thickness(14, 6), IsDefault = true, IsCancel = true };
        close.Click += (_, _) => win.Close();
        licenseBtn.Click += async (_, _) => await ShowLicenseAsync(win);
        var features = new StackPanel { Spacing = 3 };
        foreach (var f in Features) features.Children.Add(Text("•  " + f));
        var content = new StackPanel
        {
            Spacing = 10, Margin = new Thickness(24),
            Children =
            {
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, Children =
                {
                    new Image { Source = new Avalonia.Media.Imaging.Bitmap(Avalonia.Platform.AssetLoader.Open(new Uri("avares://Cyberdrome/Assets/cyberdrome-256.png"))), Width = 56, Height = 56 },
                    new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children =
                    {
                        Text("Cyberdrome", 26, FontWeight.Bold),
                        Text("Jouko Broström", 16, FontWeight.SemiBold),
                    } },
                } },
                Text($"Version {AppVersion}", 13, FontWeight.Medium, Accent),
                Text("Öppen källkod under GPL-3.0", 13, FontWeight.SemiBold),
                Text(Description),
                Text("Det här kan programmet:", 13, FontWeight.SemiBold),
                features,
                DonationBox(),
                new Border { Background = Warn, CornerRadius = new CornerRadius(8), Padding = new Thickness(10), Child = Text(Disclaimer, 12) },
                Text("Cyberdrome © 2026 Jouko Broström. Detta program är fri programvara: du får sprida och ändra det enligt GNU General " +
                     "Public License version 3 eller (om du vill) senare. Det sprids UTAN NÅGON GARANTI. Programmet använder öppen källkod " +
                     "(bl.a. Avalonia, CommunityToolkit.Mvvm, Velopack, SQLite, SkiaSharp och .NET). Data som hämtas: © OpenStreetMap-" +
                     "bidragsgivare (ODbL), OBDb (CC BY-SA 4.0), Wikidata (CC0) och NHTSA vPIC. Allt finns under Licens.", 12, null, Muted),
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right, Children = { licenseBtn, close } },
            },
        };
        win.Content = new ScrollViewer { Content = content };
        await win.ShowDialog(owner);
    }

    /// <summary>Licensfönstret: GPL-3.0 och tredjepartslicenser.</summary>
    public static async Task ShowLicenseAsync(Window owner)
    {
        var win = NewWindow("Licens", 820, 680);
        var tabs = new TabControl
        {
            Items =
            {
                new TabItem { Header = "Licens (GPL-3.0)", Content = Mono(ReadText("LICENSE")) },
                new TabItem { Header = "Tredjepartslicenser", Content = Mono(ReadText("THIRD-PARTY-NOTICES.txt")) },
                new TabItem { Header = "Ansvarsfriskrivning", Content = Mono(PlainMarkdown(ReadText("DISCLAIMER.md"))) },
            },
        };
        var close = new Button { Content = "Stäng", Padding = new Thickness(14, 6), IsDefault = true, IsCancel = true };
        close.Click += (_, _) => win.Close();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0), Children = { close } };
        var head = Text($"Cyberdrome · Jouko Broström · version {AppVersion} · GPL-3.0-or-later", 13, FontWeight.Medium);
        var dock = new DockPanel { Margin = new Thickness(18) };
        DockPanel.SetDock(head, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        dock.Children.Add(head);
        dock.Children.Add(buttons);
        dock.Children.Add(tabs);
        win.Content = dock;
        await win.ShowDialog(owner);
    }

    /// <summary>Första starten: ansvarsfriskrivningen (ingen garanti, simulator, riktiga bilar på egen risk).</summary>
    public static async Task ShowDisclaimerAsync(Window owner)
    {
        var win = NewWindow("Välkommen till Cyberdrome", 760, 620);
        var ok = new Button { Content = "Jag förstår", Padding = new Thickness(14, 6), Background = Accent, Foreground = Brushes.White, IsDefault = true };
        ok.Click += (_, _) => win.Close();
        var head = new StackPanel { Spacing = 4, Children =
        {
            Text("Cyberdrome är fri programvara (GPL-3.0) – utan garanti", 16, FontWeight.SemiBold),
            Text("Läs ansvarsfriskrivningen. Hela licensen finns under knappen Licens.", 12, null, Muted),
        } };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0), Children = { ok } };
        var dock = new DockPanel { Margin = new Thickness(18) };
        DockPanel.SetDock(head, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        dock.Children.Add(head);
        dock.Children.Add(buttons);
        dock.Children.Add(Mono(PlainMarkdown(ReadText("DISCLAIMER.md"))));
        win.Content = dock;
        await win.ShowDialog(owner);
    }
}
