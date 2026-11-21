// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

using CommunityToolkit.Mvvm.ComponentModel;
using Cyberdrome.Core.Drives;

namespace Cyberdrome.App.ViewModels;

/// <summary>En rad i listan över sparade körningar.</summary>
public sealed partial class DriveItemViewModel : ObservableObject
{
    public DriveItemViewModel(Drive drive)
    {
        Drive = drive;
        _editName = drive.Name;
    }

    public Drive Drive { get; }

    public string Name => Drive.Name;
    public string Description => Drive.Description;

    public string Info => $"{FormatTime(Drive.Duration)} · max {Drive.MaxSpeed:0} km/h · {Drive.Samples.Count} punkter"
                          + (Drive.Samples.Any(s => s.Lat is not null) ? " · GPS" : "");

    /// <summary>Namnfältet (ändras innan man trycker Byt namn).</summary>
    [ObservableProperty]
    private string _editName;

    public void NameChanged()
    {
        EditName = Drive.Name;
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Info));
    }

    public static string FormatTime(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");
    }
}
