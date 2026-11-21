// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Cyberdrome.App.Views;

/// <summary>Enkel Ja/Nej-dialog (används för att bekräfta skrivning till en riktig bil).</summary>
public static class ConfirmDialog
{
    public static async Task<bool> ShowAsync(Window owner, string title, string text, string yes = "Skriv till bilen", string no = "Avbryt")
    {
        var result = false;
        var win = new Window
        {
            Title = title, Width = 560, SizeToContent = SizeToContent.Height, CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Brushes.White,
        };
        var yesBtn = new Button { Content = yes, Background = new SolidColorBrush(Color.Parse("#B91C1C")), Foreground = Brushes.White, Padding = new Thickness(14, 8) };
        var noBtn = new Button { Content = no, Padding = new Thickness(14, 8), IsDefault = true, IsCancel = true };
        yesBtn.Click += (_, _) => { result = true; win.Close(); };
        noBtn.Click += (_, _) => win.Close();
        win.Content = new StackPanel
        {
            Margin = new Thickness(20), Spacing = 14,
            Children =
            {
                new TextBlock { Text = title, FontSize = 18, FontWeight = FontWeight.SemiBold, Foreground = new SolidColorBrush(Color.Parse("#B91C1C")) },
                new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Consolas,Menlo,monospace"), FontSize = 13, Foreground = Brushes.Black },
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right, Children = { noBtn, yesBtn } },
            },
        };
        await win.ShowDialog(owner);
        return result;
    }
}
