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
            if (!Updates.IsInstalled) { UpdateStatus = L.T("Release_UpdatesWorkInTheInstalledVersion"); return; }
            if (!Directory.Exists(UpdateFolder)) { UpdateStatus = L.F("Release_TheFolderDoesNotExist0", UpdateFolder); return; }
            _pendingUpdate = await Updates.CheckForUpdatesAsync();
            UpdateStatus = _pendingUpdate is null
                ? L.F("Release_YouHaveTheLatestVersion0", InfoDialogs.AppVersion)
                : L.F("Release_ANewVersionIsAvailable0", _pendingUpdate.TargetFullRelease.Version);
        }
        catch (Exception ex) { UpdateStatus = L.T("Release_CouldNotCheckForUpdates") + ex.Message; }
        finally { UpdateBusy = false; }
    }

    [RelayCommand]
    private async Task InstallUpdate()
    {
        if (_pendingUpdate is null) { await CheckUpdates(); if (_pendingUpdate is null) return; }
        UpdateBusy = true;
        try
        {
            UpdateStatus = L.T("Release_DownloadingTheUpdate");
            await Updates.DownloadUpdatesAsync(_pendingUpdate, p => Avalonia.Threading.Dispatcher.UIThread.Post(() => UpdateStatus = L.F("Release_DownloadingTheUpdate0", p)));
            UpdateStatus = L.T("Release_InstallingAndRestarting");
            // Väntar tills programmet har avslutats normalt (servrar och emulator stängs), installerar och startar igen.
            Updates.WaitExitThenApplyUpdates(_pendingUpdate, silent: false, restart: true);
            CloseForUpdate?.Invoke();
        }
        catch (Exception ex) { UpdateStatus = L.T("Release_TheUpdateFailed") + ex.Message; UpdateBusy = false; }
    }
}

/// <summary>Språkval (Inställningar): systemets språk, svenska eller engelska. Byts direkt.</summary>
public sealed partial class MainViewModel
{
    public sealed record LanguageOption(string Code, string Name)
    {
        /// <summary>"Systemets språk" översätts vid visning; språknamnen visas på sitt eget språk.</summary>
        public string Label => Code == L.SystemLanguage ? L.T("Settings_SystemLanguage") : Name;
        public override string ToString() => Label;
    }

    private LanguageOption[]? _languageOptions;
    public LanguageOption[] LanguageOptions => _languageOptions ??=
        [new(L.SystemLanguage, L.T("Settings_SystemLanguage")), .. L.Languages.Select(kv => new LanguageOption(kv.Key, kv.Value))];

    public LanguageOption? SelectedLanguage
    {
        get => LanguageOptions.FirstOrDefault(o => o.Code == _appSettings.Language) ?? LanguageOptions[0];
        set
        {
            if (value is null || value.Code == _appSettings.Language) return;
            _appSettings.Language = value.Code;
            _appSettings.Save();
            L.SetLanguage(value.Code);
            // Uppdatera alla beräknade texter (menyer, knappar, val i listor).
            OnPropertyChanged(string.Empty);
        }
    }
}
