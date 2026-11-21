// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

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
                    "Skriva till den riktiga bilen?",
                    $"Styrdon (CAN-id): {req.Ecu}\nTjänst: {req.Service}\nData: {req.DataHex}\n" +
                    $"Batterispänning: {(req.Voltage is { } v ? v.ToString("0.0") + " V" : "okänd")}\n" +
                    $"Säkerhetskopia av originalvärdet: {req.BackupFile}\n\n" +
                    "Motorn ska vara avstängd och tändningen på. Fel värden kan göra att styrdonet slutar fungera."));
        };
    }

    // ----- Om / Licens -----

    private async void AboutClick(object? sender, RoutedEventArgs e) => await InfoDialogs.ShowAboutAsync(this);

    private async void LicenseClick(object? sender, RoutedEventArgs e) => await InfoDialogs.ShowLicenseAsync(this);

    /// <summary>Första starten: ansvarsfriskrivningen (GPL – ingen garanti, simulator, riktiga bilar på egen risk).</summary>
    private async void OnOpenedCheckEula(object? sender, EventArgs e)
    {
        if (DataContext is not MainViewModel vm || vm.DisclaimerShown) return;
        await InfoDialogs.ShowDisclaimerAsync(this);
        vm.MarkDisclaimerShown();
    }

    // ----- Bilmodeller: exportera / importera (.cbs) -----

    private static readonly FilePickerFileType CarFileType = new("Cyberdrome System (.cbs)") { Patterns = ["*.cbs"] };
    private static readonly FilePickerFileType AnyCarFileType = new("Bilmodell (.cbs, .pdx, .odx)") { Patterns = ["*.cbs", "*.cyberbil", "*.pdx", "*.odx", "*.odx-d"] };
    private static readonly FilePickerFileType PdxFileType = new("ASAM ODX-paket (.pdx)") { Patterns = ["*.pdx"] };
    private static readonly FilePickerFileType OdxFileType = new("ASAM ODX (.odx-d)") { Patterns = ["*.odx-d", "*.odx"] };
    private static readonly FilePickerFileType CsvFileType = new("CSV") { Patterns = ["*.csv", "*.txt"] };
    private static readonly FilePickerFileType VssFileType = new("VSS-JSON") { Patterns = ["*.json"] };

    private async void ExportCarClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || vm.SelectedProfile is null) return;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Exportera bilmodell", SuggestedFileName = vm.SelectedProfile.Id + ".cbs",
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
            Title = "Importera bilmodell (.cbs eller ODX/PDX)", AllowMultiple = false, FileTypeFilter = [AnyCarFileType],
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
        var path = await SaveAsync("Exportera som ASAM ODX (.pdx)", vm.SelectedProfile.Id + ".pdx", "pdx", PdxFileType);
        if (path is not null) vm.ExportOdx(path);
    }

    private async void ImportDtcTextsClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        var path = await OpenAsync("Importera felkodstexter (CSV: P0300;Text)", CsvFileType);
        if (path is not null) vm.ImportDtcTexts(path);
    }

    private async void ImportPidsClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        var path = await OpenAsync("Importera egna PID:ar (Torque/Car Scanner-CSV)", CsvFileType);
        if (path is not null) vm.ImportCustomPids(path);
    }

    private async void ExportPidsClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || vm.SelectedProfile is null) return;
        var path = await SaveAsync("Exportera PID-lista (Torque/Car Scanner-CSV)", vm.SelectedProfile.Id + "-pid.csv", "csv", CsvFileType);
        if (path is not null) vm.ExportCustomPids(path);
    }

    private async void ImportObdbJsonClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        var path = await OpenAsync("Importera tillverkarsignaler (OBDb-format, signalsets/v3 JSON)", new FilePickerFileType("OBDb-JSON") { Patterns = ["*.json"] });
        if (path is not null) vm.ImportObdbJson(path);
    }

    private async void ExportVssClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || vm.SelectedProfile is null) return;
        var path = await SaveAsync("Exportera värden som VSS-JSON", vm.SelectedProfile.Id + "-vss.json", "json", VssFileType);
        if (path is not null) vm.ExportVss(path);
    }

    private async void ImportVssClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        var path = await OpenAsync("Importera värden (VSS-JSON)", VssFileType);
        if (path is not null) vm.ImportVss(path);
    }

    // ----- Testfall: importera / exportera -----

    private static readonly FilePickerFileType TestFileType = new("Testfall") { Patterns = ["*.json"] };

    private async void ImportTestClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Importera testfall (JSON)", AllowMultiple = false, FileTypeFilter = [TestFileType],
        });
        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (path is not null) vm.ImportTestCase(path);
    }

    private async void ExportTestClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || vm.SelectedTest is null) return;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Exportera testfall som mall", SuggestedFileName = vm.SelectedTest.Id + ".json",
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
            Title = "Ladda upp eget tema (zip eller theme.json)",
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
            Title = "Ladda upp eget skal (zip med skin.json, eller PNG med genomskinlig skärmyta)",
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
            Title = "Välj en app (APK) att installera",
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

    private static readonly FilePickerFileType DriveFileType = new("Cyberdrome-körning") { Patterns = ["*.json"] };

    private async void ExportDriveClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || (sender as Control)?.Tag is not DriveItemViewModel item) return;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Exportera körning",
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
            Title = "Importera körning",
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
}
