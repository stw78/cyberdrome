// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cyberdrome.Core.Drives;

namespace Cyberdrome.App.ViewModels;

/// <summary>Sidan Körningar: spela in, spara och spela upp körningar.</summary>
public sealed partial class MainViewModel
{
    private readonly DriveStore _driveStore = new(DriveStore.DefaultFolder);
    private DriveRecorder? _recorder;
    private DrivePlayer? _player;
    private DispatcherTimer? _driveTimer;
    private DateTime _lastTick;

    public ObservableCollection<DriveItemViewModel> Drives { get; } = new();

    private void InitDrives()
    {
        try { _driveStore.EnsureExamples(); } catch { /* skrivskyddat? då blir listan tom */ }
        ReloadDrives();
    }

    private void ReloadDrives(string? select = null)
    {
        Drives.Clear();
        foreach (var d in _driveStore.LoadAll()) Drives.Add(new DriveItemViewModel(d));
        if (select is not null) SelectedDrive = Drives.FirstOrDefault(x => x.Name == select);
    }

    [ObservableProperty] private DriveItemViewModel? _selectedDrive;
    [ObservableProperty] private string _driveMessage = "";

    // ---------------- Inspelning ----------------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRecord), nameof(ManualControl), nameof(RecordButtonText))]
    private bool _isRecording;

    [ObservableProperty] private string _recordName = L.T("Drives_MyDrive");
    [ObservableProperty] private string _recordStatus = "";

    public string RecordButtonText => IsRecording ? L.T("Drives_StopAndSave") : L.T("Drives_Record");
    public bool CanRecord => !IsPlayingDrive;

    [RelayCommand]
    private void ToggleRecord()
    {
        if (_car is null) return;
        if (!IsRecording)
        {
            if (IsPlayingDrive) return;
            _recorder = new DriveRecorder();
            _recorder.Capture(_car.State, 0);
            IsRecording = true;
            _lastTick = DateTime.UtcNow;
            StartDriveTimer();
            RecordStatus = L.T("Drives_RecordingMoveTheSlidersOnOverview");
            return;
        }

        IsRecording = false;
        StopDriveTimerIfIdle();
        var name = string.IsNullOrWhiteSpace(RecordName) ? L.T("Drives_MyDrive") : RecordName.Trim();
        var baseName = name;
        for (int i = 2; _driveStore.Exists(name); i++) name = $"{baseName} ({i})";
        var drive = _recorder!.ToDrive(name);
        _recorder = null;
        try
        {
            _driveStore.Save(drive);
            RecordStatus = $"Sparad: {name} ({DriveItemViewModel.FormatTime(drive.Duration)})";
            ReloadDrives(name);
        }
        catch (Exception ex) { RecordStatus = L.T("Main_CouldNotSave") + ex.Message; }
    }

    // ---------------- Uppspelning ----------------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ManualControl), nameof(CanRecord), nameof(PlayButtonText))]
    private bool _isPlayingDrive;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(PlayButtonText))] private bool _drivePaused;
    [ObservableProperty] private bool _driveLoop;
    [ObservableProperty] private int _driveRateIndex; // 0 = 1x, 1 = 2x, 2 = 5x
    [ObservableProperty] private string _playingName = "";
    [ObservableProperty] private double _drivePosition;
    [ObservableProperty] private double _driveDuration = 1;
    [ObservableProperty] private string _drivePositionText = "";

    public string[] DriveRates { get; } = ["1×", "2×", "5×"];
    private static readonly double[] RateValues = [1, 2, 5];

    /// <summary>Reglagen på Översikt går att använda (inte under uppspelning).</summary>
    public bool ManualControl => !IsPlayingDrive;

    public string PlayButtonText => !IsPlayingDrive ? L.T("Ui_Play") : DrivePaused ? L.T("Drives_Continue") : "⏸ Pausa";

    partial void OnDriveRateIndexChanged(int value) { if (_player is not null) _player.Rate = RateValues[Math.Clamp(value, 0, 2)]; }
    partial void OnDriveLoopChanged(bool value) { if (_player is not null) _player.Loop = value; }

    private bool _seeking;
    partial void OnDrivePositionChanged(double value)
    {
        if (_seeking || _player is null) return;
        _player.Seek(value);
        ApplySample(_player.Current);
    }

    [RelayCommand]
    private void PlayPauseDrive(DriveItemViewModel? item)
    {
        if (IsPlayingDrive && (item is null || item.Drive == _player?.Drive))
        {
            DrivePaused = !DrivePaused;
            _player!.Paused = DrivePaused;
            _lastTick = DateTime.UtcNow;
            return;
        }
        item ??= SelectedDrive;
        if (item is null) { DriveMessage = L.T("Drives_ChooseADriveInTheList"); return; }
        if (IsRecording) { DriveMessage = L.T("Drives_StopTheRecordingFirst"); return; }
        StartPlayback(item.Drive);
    }

    public void StartPlayback(Drive drive)
    {
        _player = new DrivePlayer(drive) { Rate = RateValues[Math.Clamp(DriveRateIndex, 0, 2)], Loop = DriveLoop };
        PlayingName = drive.Name;
        DriveDuration = Math.Max(0.1, drive.Duration);
        DrivePaused = false;
        IsPlayingDrive = true;
        DriveMessage = "";
        _lastTick = DateTime.UtcNow;
        ApplySample(_player.Current);
        StartDriveTimer();
        AddLog(new Cyberdrome.Obd.ObdTrafficEntry(DateTime.Now, "program", false, L.F("Drives_PlaybackStarted0", drive.Name)));
    }

    [RelayCommand]
    private void StopDrive()
    {
        if (!IsPlayingDrive) return;
        IsPlayingDrive = false;
        DrivePaused = false;
        _player = null;
        StopDriveTimerIfIdle();
        AddLog(new Cyberdrome.Obd.ObdTrafficEntry(DateTime.Now, "program", false, L.T("Drives_PlaybackStopped")));
    }

    /// <summary>Skriver ett prov till bilen och låter reglagen följa med.</summary>
    private void ApplySample(DriveSample s)
    {
        if (_car is null) return;
        s.ApplyTo(_car.State);
        // Reglagen följer värdena (deras OnChanged skriver samma värde till bilen igen – ofarligt).
        Speed = Math.Round(s.Speed, 1);
        Rpm = Math.Round(s.Rpm);
        Coolant = Math.Round(s.Coolant, 1);
        Fuel = Math.Round(s.Fuel, 1);
        Gear = s.Gear;
        Ignition = s.Ignition;
        Dark = s.Dark;
        DriveSampleApplied?.Invoke(s);

        _seeking = true;
        DrivePosition = _player?.Position ?? 0;
        _seeking = false;
        DrivePositionText = $"{DriveItemViewModel.FormatTime(DrivePosition)} / {DriveItemViewModel.FormatTime(DriveDuration)}";
    }

    /// <summary>Anropas för varje prov under uppspelning (t.ex. för kartan/GPS).</summary>
    public event Action<DriveSample>? DriveSampleApplied;

    private void StartDriveTimer()
    {
        if (_driveTimer is not null) return;
        _driveTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background, (_, _) => DriveTick());
        _driveTimer.Start();
    }

    private void StopDriveTimerIfIdle()
    {
        if (IsRecording || IsPlayingDrive || _driveTimer is null) return;
        _driveTimer.Stop();
        _driveTimer = null;
    }

    private void DriveTick()
    {
        var now = DateTime.UtcNow;
        double dt = (now - _lastTick).TotalSeconds;
        _lastTick = now;
        if (_car is null) return;

        if (IsRecording && _recorder is not null)
        {
            _recorder.Capture(_car.State, dt);
            RecordStatus = L.F("Drives_Recording01Points", DriveItemViewModel.FormatTime(_recorder.Elapsed), _recorder.Count);
        }

        if (IsPlayingDrive && _player is not null)
        {
            var s = _player.Advance(dt);
            ApplySample(s);
            if (_player.Finished)
            {
                DriveMessage = L.F("Drives_PlaybackOf0IsFinished", PlayingName);
                StopDrive();
            }
        }
    }

    // ---------------- Byt namn / ta bort / exportera / importera ----------------

    [RelayCommand]
    private void RenameDrive(DriveItemViewModel? item)
    {
        if (item is null) return;
        if (IsPlayingDrive && _player?.Drive == item.Drive) { DriveMessage = L.T("Drives_StopThePlaybackFirst"); return; }
        if (_driveStore.Rename(item.Drive, item.EditName))
        {
            item.NameChanged();
            DriveMessage = L.F("Drives_RenamedTo0", item.Name);
        }
        else
        {
            DriveMessage = L.T("Drives_TheNameIsEmptyUnchangedOr");
            item.EditName = item.Name;
        }
    }

    [RelayCommand]
    private void DeleteDrive(DriveItemViewModel? item)
    {
        if (item is null) return;
        if (IsPlayingDrive && _player?.Drive == item.Drive) StopDrive();
        _driveStore.Delete(item.Name);
        Drives.Remove(item);
        DriveMessage = L.F("Drives_Deleted0TheExampleDrivesCome", item.Name);
    }

    public void ExportDrive(DriveItemViewModel item, string path)
    {
        try { _driveStore.Export(item.Drive, path); DriveMessage = "Exporterad: " + path; }
        catch (Exception ex) { DriveMessage = L.T("Cars_CouldNotExport") + ex.Message; }
    }

    public void ImportDrive(string path)
    {
        try
        {
            var d = _driveStore.Import(path);
            ReloadDrives(d.Name);
            DriveMessage = "Importerad: " + d.Name;
        }
        catch (Exception ex) { DriveMessage = L.T("Cars_CouldNotImport") + ex.Message; }
    }
}
