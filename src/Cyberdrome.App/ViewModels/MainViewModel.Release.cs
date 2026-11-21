// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cyberdrome.App.Views;
using Velopack;
using Velopack.Sources;

namespace Cyberdrome.App.ViewModels;

/// <summary>Version, ansvarsfriskrivning vid första starten och uppdateringar (Velopack, från en lokal mapp – ingen server).</summary>
public sealed partial class MainViewModel
{
    public string VersionText => "Version " + InfoDialogs.AppVersion;

    public bool DisclaimerShown => _appSettings.EulaAccepted == InfoDialogs.DisclaimerVersion;

    public void MarkDisclaimerShown()
    {
        _appSettings.EulaAccepted = InfoDialogs.DisclaimerVersion;
        _appSettings.Save();
    }

    public static string DefaultUpdateFolder => Path.Combine(
        Cyberdrome.Vm.WorkFolder.Root, "release", "uppdateringar");

    [ObservableProperty] private string _updateFolder = "";
    [ObservableProperty] private string _updateStatus = "";
    [ObservableProperty] private bool _updateBusy;
    private UpdateInfo? _pendingUpdate;
    private UpdateManager? _updates;

    /// <summary>Sätts av fönstret: stänger programmet (vanlig avstängning) så att uppdateringen kan installeras.</summary>
    public Action? CloseForUpdate { get; set; }

    private void InitUpdates() => UpdateFolder = string.IsNullOrWhiteSpace(_appSettings.UpdateFolder) ? DefaultUpdateFolder : _appSettings.UpdateFolder!;

    partial void OnUpdateFolderChanged(string value)
    {
        _updates = null;
        _pendingUpdate = null;
        _appSettings.UpdateFolder = value == DefaultUpdateFolder ? null : value;
        _appSettings.Save();
    }

    private UpdateManager Updates => _updates ??= new UpdateManager(new SimpleFileSource(new DirectoryInfo(UpdateFolder)));

    [RelayCommand]
    private async Task CheckUpdates()
    {
        UpdateBusy = true;
        try
        {
            if (!Updates.IsInstalled) { UpdateStatus = "Uppdateringar fungerar i den installerade versionen (inte när programmet körs från källkoden)."; return; }
            if (!Directory.Exists(UpdateFolder)) { UpdateStatus = $"Mappen finns inte: {UpdateFolder}"; return; }
            _pendingUpdate = await Updates.CheckForUpdatesAsync();
            UpdateStatus = _pendingUpdate is null
                ? $"Du har den senaste versionen ({InfoDialogs.AppVersion})."
                : $"Ny version finns: {_pendingUpdate.TargetFullRelease.Version}. Tryck \"Installera uppdatering\".";
        }
        catch (Exception ex) { UpdateStatus = "Kunde inte söka efter uppdateringar: " + ex.Message; }
        finally { UpdateBusy = false; }
    }

    [RelayCommand]
    private async Task InstallUpdate()
    {
        if (_pendingUpdate is null) { await CheckUpdates(); if (_pendingUpdate is null) return; }
        UpdateBusy = true;
        try
        {
            UpdateStatus = "Hämtar uppdateringen …";
            await Updates.DownloadUpdatesAsync(_pendingUpdate, p => Avalonia.Threading.Dispatcher.UIThread.Post(() => UpdateStatus = $"Hämtar uppdateringen … {p} %"));
            UpdateStatus = "Installerar och startar om …";
            // Väntar tills programmet har avslutats normalt (servrar och emulator stängs), installerar och startar igen.
            Updates.WaitExitThenApplyUpdates(_pendingUpdate, silent: false, restart: true);
            CloseForUpdate?.Invoke();
        }
        catch (Exception ex) { UpdateStatus = "Uppdateringen misslyckades: " + ex.Message; UpdateBusy = false; }
    }
}
