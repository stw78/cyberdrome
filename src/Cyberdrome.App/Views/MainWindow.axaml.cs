// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Cyberdrome.Vm;
using Avalonia.Platform.Storage;
using Cyberdrome.App.ViewModels;

namespace Cyberdrome.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Map.MapClicked += p => (DataContext as MainViewModel)?.MapClick(p);
        SkinFrame.ButtonPressed += b => _ = (DataContext as MainViewModel)?.SkinButtonAsync(b);
        Opened += OnOpenedCheckEula;
        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainViewModel u) u.CloseForUpdate = () => Avalonia.Threading.Dispatcher.UIThread.Post(Close);
            if (DataContext is MainViewModel vm)
                vm.ConfirmWrite = req => Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => ConfirmDialog.ShowAsync(this,
                    L.T("MainWindow_WriteToTheRealCar"),
                    L.F("MainWindow_ControlUnitCANId0Service", req.Ecu, req.Service, req.DataHex) +
                    L.F("MainWindow_BatteryVoltage0", (req.Voltage is { } v ? v.ToString("0.0") + " V" : L.T("ObdTcpServer_Unknown"))) +
                    L.F("MainWindow_BackupOfTheOriginalValue0", req.BackupFile) +
                    L.T("MainWindow_TheEngineShouldBeOffAnd")));
        };
    }

    /// <summary>Skärmbildsläge (--shots): inga dialoger.</summary>
    public bool ShotMode { get; set; }

    // ----- Om / Licens -----

    private async void AboutClick(object? sender, RoutedEventArgs e) => await InfoDialogs.ShowAboutAsync(this);

    private async void LicenseClick(object? sender, RoutedEventArgs e) => await InfoDialogs.ShowLicenseAsync(this);

    /// <summary>Första starten: ansvarsfriskrivningen (GPL – ingen garanti, simulator, riktiga bilar på egen risk).</summary>
    private async void OnOpenedCheckEula(object? sender, EventArgs e)
    {
        if (ShotMode) return;
        if (DataContext is not MainViewModel vm || vm.DisclaimerShown) return;
        await InfoDialogs.ShowDisclaimerAsync(this);
        vm.MarkDisclaimerShown();
    }

    // ----- Bilmodeller: exportera / importera (.cbs) -----

    private static FilePickerFileType CarFileType => new(L.T("MainWindow_CyberdromeSystemCbs")) { Patterns = ["*.cbs"] };
    private static readonly FilePickerFileType AnyCarFileType = new("Bilmodell (.cbs, .pdx, .odx)") { Patterns = ["*.cbs", "*.cyberbil", "*.pdx", "*.odx", "*.odx-d"] };
    private static FilePickerFileType PdxFileType => new(L.T("MainWindow_ASAMODXPackagePdx")) { Patterns = ["*.pdx"] };
    private static FilePickerFileType OdxFileType => new(L.T("MainWindow_ASAMODXOdxD")) { Patterns = ["*.odx-d", "*.odx"] };
    private static readonly FilePickerFileType CsvFileType = new("CSV") { Patterns = ["*.csv", "*.txt"] };
    private static readonly FilePickerFileType VssFileType = new("VSS-JSON") { Patterns = ["*.json"] };

    private async void ExportCarClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || vm.SelectedProfile is null) return;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = L.T("MainWindow_ExportCarModel"), SuggestedFileName = vm.SelectedProfile.Id + ".cbs",
            DefaultExtension = "cbs", FileTypeChoices = [CarFileType],
        });
        var path = file?.TryGetLocalPath();
        if (path is not null) vm.ExportCar(path, ExportWithCoding.IsChecked == true);
    }

    private async void ImportCarClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = L.T("MainWindow_ImportCarModelCbsOrODX"), AllowMultiple = false, FileTypeFilter = [AnyCarFileType],
        });
        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (path is not null) vm.ImportCarAny(path);
    }

    // ----- Standardformat: ODX, CSV, VSS -----

    private async Task<string?> SaveAsync(string title, string name, string ext, FilePickerFileType type)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        { Title = title, SuggestedFileName = name, DefaultExtension = ext, FileTypeChoices = [type] });
        return file?.TryGetLocalPath();
    }

    private async Task<string?> OpenAsync(string title, FilePickerFileType type)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = title, AllowMultiple = false, FileTypeFilter = [type] });
        return files.FirstOrDefault()?.TryGetLocalPath();
    }

    private async void ExportOdxClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || vm.SelectedProfile is null) return;
        var path = await SaveAsync(L.T("MainWindow_ExportAsASAMODXPdx"), vm.SelectedProfile.Id + ".pdx", "pdx", PdxFileType);
        if (path is not null) vm.ExportOdx(path);
    }

    private async void ImportDtcTextsClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        var path = await OpenAsync(L.T("MainWindow_ImportTroubleCodeTextsCSVP0300"), CsvFileType);
        if (path is not null) vm.ImportDtcTexts(path);
    }

    private async void ImportPidsClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        var path = await OpenAsync(L.T("MainWindow_ImportCustomPIDsTorqueCarScanner"), CsvFileType);
        if (path is not null) vm.ImportCustomPids(path);
    }

    private async void ExportPidsClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || vm.SelectedProfile is null) return;
        var path = await SaveAsync(L.T("MainWindow_ExportPIDListTorqueCarScanner"), vm.SelectedProfile.Id + "-pid.csv", "csv", CsvFileType);
        if (path is not null) vm.ExportCustomPids(path);
    }

    private async void ImportObdbJsonClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        var path = await OpenAsync(L.T("MainWindow_ImportManufacturerSignalsOBDbFormatSignalsets"), new FilePickerFileType("OBDb-JSON") { Patterns = ["*.json"] });
        if (path is not null) vm.ImportObdbJson(path);
    }

    private async void ExportVssClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || vm.SelectedProfile is null) return;
        var path = await SaveAsync(L.T("MainWindow_ExportValuesAsVSSJSON"), vm.SelectedProfile.Id + "-vss.json", "json", VssFileType);
        if (path is not null) vm.ExportVss(path);
    }

    private async void ImportVssClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        var path = await OpenAsync(L.T("MainWindow_ImportValuesVSSJSON"), VssFileType);
        if (path is not null) vm.ImportVss(path);
    }

    // ----- Testfall: importera / exportera -----

    private static readonly FilePickerFileType TestFileType = new("Testfall") { Patterns = ["*.json"] };

    private async void ImportTestClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = L.T("MainWindow_ImportTestCaseJSON"), AllowMultiple = false, FileTypeFilter = [TestFileType],
        });
        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (path is not null) vm.ImportTestCase(path);
    }

    private async void ExportTestClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || vm.SelectedTest is null) return;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = L.T("MainWindow_ExportTestCaseAsTemplate"), SuggestedFileName = vm.SelectedTest.Id + ".json",
            DefaultExtension = "json", FileTypeChoices = [TestFileType],
        });
        var path = file?.TryGetLocalPath();
        if (path is not null) vm.ExportTestCase(path);
    }

    // ----- Tema och skal: importera -----

    private async void ImportThemeClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = L.T("MainWindow_UploadYourOwnThemeZipOr"),
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Tema") { Patterns = ["*.zip", "*.json"] }],
        });
        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (path is not null) vm.ImportTheme(path);
    }

    private async void ImportSkinClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = L.T("MainWindow_UploadYourOwnSkinZipWith"),
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Skal") { Patterns = ["*.zip", "*.png"] }],
        });
        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (path is not null) vm.ImportSkin(path, ReadAlpha);
    }

    /// <summary>Läser alfakanalen i en PNG (för att hitta skärmytan i ett eget skal).</summary>
    private static (byte[] Alpha, int W, int H)? ReadAlpha(string path)
    {
        try
        {
            using var bmp = new Avalonia.Media.Imaging.Bitmap(path);
            int w = bmp.PixelSize.Width, h = bmp.PixelSize.Height;
            var pixels = new byte[w * h * 4];
            var handle = System.Runtime.InteropServices.GCHandle.Alloc(pixels, System.Runtime.InteropServices.GCHandleType.Pinned);
            try { bmp.CopyPixels(new Avalonia.PixelRect(0, 0, w, h), handle.AddrOfPinnedObject(), pixels.Length, w * 4); }
            finally { handle.Free(); }
            var alpha = new byte[w * h];
            for (int i = 0; i < alpha.Length; i++) alpha[i] = pixels[i * 4 + 3]; // BGRA/RGBA: alfa sist
            return (alpha, w, h);
        }
        catch { return null; }
    }

    private async void PickApk(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = L.T("MainWindow_ChooseAnAppAPKToInstall"),
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Android-app") { Patterns = ["*.apk"] }],
        });
        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (path is not null) await vm.InstallApk(path);
    }

    // ----- Klick och svep på Android-skärmen -----

    private (int X, int Y)? _pressAt;
    private DateTime _pressTime;

    /// <summary>Räknar om en punkt i bilden till Android-skärmens pixlar (bilden visas med Stretch=Uniform).</summary>
    private (int X, int Y)? ToDevice(Avalonia.Point p)
    {
        if (AndroidScreen.Source is not Avalonia.Media.Imaging.Bitmap bmp) return null;
        var size = AndroidScreen.Bounds.Size;
        return ScreenMapping.ToDevice(p.X, p.Y, size.Width, size.Height, bmp.PixelSize.Width, bmp.PixelSize.Height);
    }

    private void ScreenPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _pressAt = ToDevice(e.GetPosition(AndroidScreen));
        _pressTime = DateTime.UtcNow;
        e.Pointer.Capture(AndroidScreen);
    }

    private async void ScreenPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        e.Pointer.Capture(null);
        if (_pressAt is not { } start || DataContext is not MainViewModel vm) return;
        _pressAt = null;
        var end = ToDevice(e.GetPosition(AndroidScreen)) ?? start;
        await vm.ScreenPointerAsync(start.X, start.Y, end.X, end.Y, DateTime.UtcNow - _pressTime);
    }

    // ----- Körningar: exportera / importera -----

    private static FilePickerFileType DriveFileType => new(L.T("MainWindow_CyberdromeDrive")) { Patterns = ["*.json"] };

    private async void ExportDriveClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || (sender as Control)?.Tag is not DriveItemViewModel item) return;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = L.T("MainWindow_ExportDrive"),
            SuggestedFileName = Cyberdrome.Core.Drives.DriveStore.SafeFileName(item.Name) + ".json",
            DefaultExtension = "json",
            FileTypeChoices = [DriveFileType],
        });
        var path = file?.TryGetLocalPath();
        if (path is not null) vm.ExportDrive(item, path);
    }

    private async void ImportDriveClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = L.T("MainWindow_ImportDrive"),
            AllowMultiple = false,
            FileTypeFilter = [DriveFileType],
        });
        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (path is not null) vm.ImportDrive(path);
    }

    private void OpenFeatures(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm) vm.PageIndex = 1;
    }

    // ----- Fullskärm: bara bilskärmen med skal, utan ram (F11 växlar, Esc lämnar) -----

    private bool _fullscreen;
    private WindowState _stateBeforeFullscreen = WindowState.Normal;

    private void ToggleFullscreenClick(object? sender, RoutedEventArgs e) => SetFullscreen(!_fullscreen);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.F11) { SetFullscreen(!_fullscreen); e.Handled = true; return; }
        if (e.Key == Key.Escape && _fullscreen) { SetFullscreen(false); e.Handled = true; return; }
        base.OnKeyDown(e);
    }

    /// <summary>
    /// Döljer allt utom skärmen med skalet och visar fönstret över hela bildskärmen. Samma SkinHost och samma
    /// Android-bild används, så tryck räknas om exakt som vanligt (SkinLayout + ScreenMapping).
    /// </summary>
    public void SetFullscreen(bool on)
    {
        if (on == _fullscreen) return;
        if (on && DataContext is MainViewModel vm) vm.PageIndex = 0;
        _fullscreen = on;
        TopBar.IsVisible = StatusBar.IsVisible = SideBar.IsVisible = !on;
        OverviewTitle.IsVisible = OverviewHeader.IsVisible = OverviewFooter.IsVisible = CarPanel.IsVisible = !on;
        OverviewGrid.ColumnDefinitions[1].Width = on ? new GridLength(0) : new GridLength(340);
        OverviewGrid.ColumnSpacing = on ? 0 : 20;
        ContentGrid.Margin = on ? new Thickness(0) : new Thickness(28, 22, 28, 22);
        if (on)
        {
            OverviewCard.Padding = new Thickness(0);
            OverviewCard.CornerRadius = new CornerRadius(0);
            OverviewCard.Background = Avalonia.Media.Brushes.Black;
            _stateBeforeFullscreen = WindowState;
            WindowState = WindowState.FullScreen;
        }
        else
        {
            OverviewCard.ClearValue(Border.PaddingProperty);
            OverviewCard.ClearValue(Border.CornerRadiusProperty);
            OverviewCard.ClearValue(Border.BackgroundProperty);
            WindowState = _stateBeforeFullscreen;
        }
        ExitFullscreenButton.IsVisible = on;
        Focus();
    }

    // ----- Skärmbilder av alla sidor (för dokumentation): Cyberdrome.exe --shots=<mapp> [--lang=sv|en] [--size=1600x1000] -----

    /// <summary>Tar en PNG per sida och avslutar. Används för att dokumentera designen; påverkar inte vanlig körning.</summary>
    public async Task TakePageShotsAsync(string folder, string? onlyPages = null)
    {
        if (DataContext is not MainViewModel vm) return;
        Directory.CreateDirectory(folder);
        var names = new[] { "01-oversikt", "02-dolda-funktioner", "03-obd-logg", "04-android", "05-korningar", "06-karta", "07-testfall", "08-bilmodeller", "09-obd-kalla", "10-bildatabas", "11-installningar" };
        for (int i = 0; i < names.Length; i++)
        {
            if (onlyPages is not null && !onlyPages.Split(',').Contains(i.ToString())) continue;
            vm.PageIndex = i;
            await Task.Delay(1200);
            var scale = RenderScaling;
            var size = new PixelSize((int)(Bounds.Width * scale), (int)(Bounds.Height * scale));
            using var rtb = new Avalonia.Media.Imaging.RenderTargetBitmap(size, new Vector(96 * scale, 96 * scale));
            rtb.Render(this);
            rtb.Save(Path.Combine(folder, names[i] + ".png"));
        }
    }
}
