// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;
using Cyberdrome.Vm;

namespace Cyberdrome.App.ViewModels;

/// <summary>Status för ett Android-läge i Inställningar: färgad punkt, kort text och om "Visa hur" ska synas.</summary>
public sealed record ModeStatus(string Text, IBrush Brush, bool Problem)
{
    public static readonly IBrush Green = new SolidColorBrush(Color.Parse("#34C759"));
    public static readonly IBrush Grey = new SolidColorBrush(Color.Parse("#AEAEB2"));
    public static readonly IBrush Orange = new SolidColorBrush(Color.Parse("#FF9F0A"));
    public static readonly IBrush Red = new SolidColorBrush(Color.Parse("#FF453A"));
}

/// <summary>Navigering, OBD-brytare och statusen för Android-lägena (ny design).</summary>
public sealed partial class MainViewModel
{
    /// <summary>Byter sida från sidomenyn och genvägar. Parametern är sidans nummer som text.</summary>
    [RelayCommand]
    private void GoToPage(string? index)
    {
        if (int.TryParse(index, out var i) && i >= 0 && i < PageKeys.Length) PageIndex = i;
    }

    /// <summary>Virtuell OBD på/av som en brytare.</summary>
    public bool ObdOn
    {
        get => ObdRunning;
        set
        {
            if (value == ObdRunning) return;
            if (value) StartObd(); else _ = StopObd();
        }
    }

    partial void OnObdRunningChanged(bool value) => OnPropertyChanged(nameof(ObdOn));

    public ModeStatus ModeStatusNoRestart => StatusFor(AndroidMode.NoRestart);
    public ModeStatus ModeStatusGoogle => StatusFor(AndroidMode.GoogleApps);
    public ModeStatus ModeStatusFast => StatusFor(AndroidMode.Fast);

    private void RaiseModeStatus()
    {
        OnPropertyChanged(nameof(ModeStatusNoRestart));
        OnPropertyChanged(nameof(ModeStatusGoogle));
        OnPropertyChanged(nameof(ModeStatusFast));
    }

    partial void OnAndroidStateChanged(AndroidState value) { RaiseModeStatus(); RaiseAndroidSwitch(); }

    /// <summary>
    /// Det valda läget visar sitt verkliga tillstånd (installerat, igång, problem); de andra visar ungefärlig nedladdning.
    /// </summary>
    private ModeStatus StatusFor(AndroidMode mode)
    {
        if (_vmSettings.Mode != mode)
            return new(L.T(mode switch
            {
                AndroidMode.NoRestart => "Settings_ModeSizeQuick",
                AndroidMode.GoogleApps => "Settings_ModeSizeGoogle",
                _ => "Settings_ModeSizeFast",
            }), ModeStatus.Grey, false);
        return AndroidState switch
        {
            AndroidState.Running => new(L.T("Settings_ModeRunning"), ModeStatus.Green, false),
            AndroidState.Stopped => new(L.T("Settings_ModeInstalled"), ModeStatus.Green, false),
            AndroidState.WslMissing or AndroidState.NoKvm or AndroidState.NoAcceleration or AndroidState.NotWindows
                => new(L.T("Settings_ModeProblem"), ModeStatus.Red, true),
            AndroidState.RebootNeeded => new(L.T("Settings_ModeRebootNeeded"), ModeStatus.Orange, true),
            _ => new(L.T("Settings_ModeNotInstalled"), ModeStatus.Orange, false),
        };
    }
}

/// <summary>Vad Android håller på med just nu (för på/av-brytaren).</summary>
public enum AndroidPhase { None, Starting, Stopping }

/// <summary>På/av-brytaren för Android (Översikt, toppfältet och sidan Android).</summary>
public sealed partial class MainViewModel
{
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    [CommunityToolkit.Mvvm.ComponentModel.NotifyPropertyChangedFor(nameof(AndroidOn), nameof(AndroidSwitchText), nameof(AndroidSwitchEnabled), nameof(AndroidSwitching))]
    private AndroidPhase _androidPhase;

    /// <summary>Senaste starten misslyckades: visa länken "Visa logg".</summary>
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private bool _androidStartFailed;

    /// <summary>Android är installerat (brytaren visas i stället för "Installera Android").</summary>
    public bool AndroidInstalled => AndroidState is AndroidState.Stopped or AndroidState.Running;

    /// <summary>Brytarens läge: på när Android kör eller håller på att starta.</summary>
    public bool AndroidOn
    {
        get => AndroidPhase switch { AndroidPhase.Starting => true, AndroidPhase.Stopping => false, _ => AndroidRunning };
        set
        {
            if (value == AndroidOn || AndroidBusy) return;
            if (value) _ = StartAndroid(); else _ = StopAndroid();
            OnPropertyChanged();
        }
    }

    /// <summary>Brytaren är låst medan Android startar eller stängs (tills det bootat eller misslyckats).</summary>
    public bool AndroidSwitchEnabled => AndroidInstalled && AndroidPhase == AndroidPhase.None && !AndroidBusy;

    public bool AndroidSwitching => AndroidPhase != AndroidPhase.None;

    public string AndroidSwitchText => AndroidPhase switch
    {
        AndroidPhase.Starting => L.T("Android_Starting"),
        AndroidPhase.Stopping => L.T("Android_Stopping"),
        _ => AndroidRunning ? L.T("Android_On") : L.T("Android_Off"),
    };

    private void RaiseAndroidSwitch()
    {
        OnPropertyChanged(nameof(AndroidInstalled));
        OnPropertyChanged(nameof(AndroidOn));
        OnPropertyChanged(nameof(AndroidSwitchText));
        OnPropertyChanged(nameof(AndroidSwitchEnabled));
        OnPropertyChanged(nameof(AndroidObdAddress));
        OnPropertyChanged(nameof(HasAndroidObdAddress));
    }
}

/// <summary>Tillbaka och Hem i Android (knappar, högerklick, musens bakåtknapp, Backspace).</summary>
public sealed partial class MainViewModel
{
    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private Task AndroidBack() => AndroidKeyAsync(Cyberdrome.Vm.Skins.SkinButton.Keys.Back);

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private Task AndroidHome() => AndroidKeyAsync(Cyberdrome.Vm.Skins.SkinButton.Keys.Home);

    /// <summary>Skickar en Android-tangent (t.ex. KEYCODE_BACK = 4) om Android kör.</summary>
    public async Task AndroidKeyAsync(int keyCode)
    {
        if (_android is not IScreenControl s || !AndroidRunning) return;
        try { await s.KeyEventAsync(keyCode); }
        catch (Exception ex) { AndroidLogLine(L.T("Main_ErrorPrefix") + ex.Message); }
    }
}
