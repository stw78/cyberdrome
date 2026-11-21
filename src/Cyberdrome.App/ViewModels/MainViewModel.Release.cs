// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cyberdrome.App.Views;
using Velopack;
using Velopack.Sources;
using Cyberdrome.Core.Updates;

namespace Cyberdrome.App.ViewModels;

/// <summary>
/// Version, ansvarsfriskrivning vid första starten och uppdateringar (Velopack).
/// Uppdateringar hämtas från GitHub Releases (publikt repo, ingen nyckel i programmet).
/// En lokal mapp kan väljas under Avancerat för att testa egna byggen.
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>Repot som releaser hämtas från.</summary>
    public const string GitHubRepoUrl = "https://github.com/stw78/cyberdrome";

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
    [ObservableProperty] private double _updateProgress;
    [ObservableProperty] private bool _updateDownloading;
    /// <summary>Versionen som finns att hämta (tom = ingen känd uppdatering).</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(UpdateAvailable))] private string _updateVersion = "";
    /// <summary>Releaseanteckningar för den nya versionen (Markdown som text).</summary>
    [ObservableProperty] private string _updateNotes = "";
    private UpdateInfo? _pendingUpdate;
    private UpdateManager? _updates;

    public bool UpdateAvailable => UpdateVersion.Length > 0;

    /// <summary>Hämta uppdateringar från en lokal mapp i stället för GitHub (Avancerat, för test).</summary>
    public bool UseLocalUpdateFolder
    {
        get => _appSettings.UseLocalUpdateFolder;
        set
        {
            if (value == _appSettings.UseLocalUpdateFolder) return;
            _appSettings.UseLocalUpdateFolder = value;
            _appSettings.Save();
            ResetUpdateSource();
            OnPropertyChanged();
            OnPropertyChanged(nameof(UpdateSourceText));
        }
    }

    /// <summary>Automatisk kontroll vid start (Avancerat). Av = programmet kontaktar GitHub bara när du trycker själv.</summary>
    public bool AutoCheckUpdates
    {
        get => _appSettings.AutoCheckUpdates;
        set
        {
            if (value == _appSettings.AutoCheckUpdates) return;
            _appSettings.AutoCheckUpdates = value;
            _appSettings.Save();
            OnPropertyChanged();
        }
    }

    /// <summary>Ta med förhandsversioner (pre-release på GitHub). Standard: av.</summary>
    public bool IncludePrereleases
    {
        get => _appSettings.IncludePrereleases;
        set
        {
            if (value == _appSettings.IncludePrereleases) return;
            _appSettings.IncludePrereleases = value;
            _appSettings.Save();
            ResetUpdateSource();
            OnPropertyChanged();
        }
    }

    public string UpdateSourceText => UseLocalUpdateFolder
        ? L.F("Release_SourceFolder0", UpdateFolder)
        : L.F("Release_SourceGitHub0", GitHubRepoUrl.Replace("https://", ""));

    /// <summary>Sätts av fönstret: stänger programmet (vanlig avstängning) så att uppdateringen kan installeras.</summary>
    public Action? CloseForUpdate { get; set; }

    private void InitUpdates()
    {
        UpdateFolder = string.IsNullOrWhiteSpace(_appSettings.UpdateFolder) ? DefaultUpdateFolder : _appSettings.UpdateFolder!;
        // Automatisk kontroll vid start, högst en gång per dygn, en stund efter att fönstret visats.
        if (_appSettings.AutoCheckUpdates && UpdatePolicy.ShouldAutoCheck(_appSettings.LastUpdateCheckUtc, DateTime.UtcNow))
            _ = Task.Delay(TimeSpan.FromSeconds(8)).ContinueWith(_ =>
                Avalonia.Threading.Dispatcher.UIThread.Post(() => _ = CheckUpdatesCore(automatic: true)));
    }

    partial void OnUpdateFolderChanged(string value)
    {
        ResetUpdateSource();
        _appSettings.UpdateFolder = value == DefaultUpdateFolder ? null : value;
        _appSettings.Save();
        OnPropertyChanged(nameof(UpdateSourceText));
    }

    private void ResetUpdateSource()
    {
        _updates = null;
        _pendingUpdate = null;
        UpdateVersion = "";
        UpdateNotes = "";
    }

    private UpdateManager Updates => _updates ??= new UpdateManager(UseLocalUpdateFolder
        ? new SimpleFileSource(new DirectoryInfo(UpdateFolder))
        : new GithubSource(GitHubRepoUrl, accessToken: null, prerelease: IncludePrereleases));

    [RelayCommand]
    private Task CheckUpdates() => CheckUpdatesCore(automatic: false);

    private async Task CheckUpdatesCore(bool automatic)
    {
        if (UpdateBusy) return;
        UpdateBusy = true;
        try
        {
            if (!Updates.IsInstalled) { if (!automatic) UpdateStatus = L.T("Release_UpdatesWorkInTheInstalledVersion"); return; }
            if (UseLocalUpdateFolder && !Directory.Exists(UpdateFolder)) { UpdateStatus = L.F("Release_TheFolderDoesNotExist0", UpdateFolder); return; }
            if (!automatic) UpdateStatus = L.T("Release_Checking");
            _pendingUpdate = await Updates.CheckForUpdatesAsync();
            _appSettings.LastUpdateCheckUtc = DateTime.UtcNow;
            _appSettings.Save();
            if (_pendingUpdate is null)
            {
                UpdateVersion = "";
                UpdateNotes = "";
                UpdateStatus = L.F("Release_YouHaveTheLatestVersion0", InfoDialogs.AppVersion);
            }
            else
            {
                var target = _pendingUpdate.TargetFullRelease;
                UpdateVersion = target.Version.ToString();
                UpdateNotes = UpdatePolicy.CleanNotes(target.NotesMarkdown);
                UpdateStatus = L.F("Release_ANewVersionIsAvailable0", UpdateVersion);
            }
        }
        catch (Exception ex)
        {
            // Vid automatisk kontroll stör vi inte med fel (t.ex. ingen internet); visas när man trycker själv.
            UpdateStatus = UpdatePolicy.FriendlyError(ex.Message);
        }
        finally { UpdateBusy = false; }
    }

    [RelayCommand]
    private async Task InstallUpdate()
    {
        if (_pendingUpdate is null) { await CheckUpdates(); if (_pendingUpdate is null) return; }
        UpdateBusy = true;
        UpdateDownloading = true;
        try
        {
            UpdateStatus = L.T("Release_DownloadingTheUpdate");
            UpdateProgress = 0;
            await Updates.DownloadUpdatesAsync(_pendingUpdate, p => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                UpdateProgress = p;
                UpdateStatus = L.F("Release_DownloadingTheUpdate0", p);
            }));
            UpdateStatus = L.T("Release_InstallingAndRestarting");
            // Väntar tills programmet har avslutats normalt (servrar och emulator stängs), installerar och startar igen.
            Updates.WaitExitThenApplyUpdates(_pendingUpdate, silent: false, restart: true);
            CloseForUpdate?.Invoke();
        }
        catch (Exception ex)
        {
            UpdateStatus = L.T("Release_TheUpdateFailed") + UpdatePolicy.FriendlyError(ex.Message);
            UpdateBusy = false;
            UpdateDownloading = false;
        }
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
