// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Cyberdrome.Vm;

namespace Cyberdrome.App.ViewModels;

/// <summary>
/// Android-skärmen som en levande bildström: råa RGBA-bilder skrivs direkt in i en och samma WriteableBitmap
/// (ingen PNG-avkodning, inga nya bitmappar per bild). Bara den senaste bilden visas – äldre som inte hunnit visas
/// hoppas över, så att skärmen aldrig släpar efter.
/// </summary>
public sealed partial class MainViewModel
{
    private RawFrame? _pendingFrame;
    private int _framePosted;
    private int _framesThisSecond;
    private DateTime _fpsWindow = DateTime.UtcNow;

    /// <summary>Skärmen strömmas (direkt pekinmatning finns).</summary>
    private readonly Avalonia.Threading.DispatcherTimer? _androidWatch;

    [ObservableProperty] private bool _liveScreen;

    /// <summary>Uppmätt bildtakt, t.ex. "58 bilder/s" (visas under skärmen).</summary>
    [ObservableProperty] private string _screenFpsText = "";

    /// <summary>Fönstret ritar om Android-bilden (samma bitmapp, nytt innehåll).</summary>
    public event Action? FrameUpdated;

    private void OnRawFrame(RawFrame frame)
    {
        Interlocked.Exchange(ref _pendingFrame, frame);
        if (Interlocked.Exchange(ref _framePosted, 1) == 0)
            Dispatcher.UIThread.Post(ApplyPendingFrame, DispatcherPriority.Render);
    }

    private void ApplyPendingFrame()
    {
        Interlocked.Exchange(ref _framePosted, 0);
        var f = Interlocked.Exchange(ref _pendingFrame, null);
        if (f is null || _screenCts is null) return;
        var c = f.Crop ?? new CropRect(0, 0, f.Width, f.Height);
        if (ScreenFrame is not WriteableBitmap wb || wb.PixelSize.Width != c.W || wb.PixelSize.Height != c.H)
        {
            wb = new WriteableBitmap(new PixelSize(c.W, c.H), new Vector(96, 96), PixelFormat.Rgba8888, AlphaFormat.Opaque);
            CopyInto(wb, f);
            SetFrame(wb);
        }
        else
        {
            CopyInto(wb, f);
            FrameUpdated?.Invoke();
        }
        CountFrame();
    }

    private static unsafe void CopyInto(WriteableBitmap wb, RawFrame f)
    {
        using var fb = wb.Lock();
        var c = f.Crop ?? new CropRect(0, 0, f.Width, f.Height);
        int srcRow = f.Width * 4, row = c.W * 4;
        var src = f.Rgba.Span;
        if (c.X == 0 && c.W == f.Width && fb.RowBytes == row)
            src.Slice(c.Y * srcRow, row * c.H).CopyTo(new Span<byte>((void*)fb.Address, row * c.H));
        else
            for (int y = 0; y < c.H; y++)
                src.Slice((c.Y + y) * srcRow + c.X * 4, row).CopyTo(new Span<byte>((void*)(fb.Address + y * fb.RowBytes), row));
    }

    private void CountFrame()
    {
        _framesThisSecond++;
        var now = DateTime.UtcNow;
        if ((now - _fpsWindow).TotalSeconds >= 1)
        {
            ScreenFpsText = L.F("Screen_Fps0", Math.Round(_framesThisSecond / (now - _fpsWindow).TotalSeconds));
            _framesThisSecond = 0;
            _fpsWindow = now;
        }
    }

    /// <summary>Pekare direkt till Android (ned/flytta/upp). False = använd tryck/svep via adb som förut.</summary>
    public async Task<bool> LivePointerAsync(int x, int y, bool down)
    {
        if (!LiveScreen || _android is not IScreenStream s || !AndroidRunning) return false;
        return await s.PointerAsync(x, y, down);
    }
}

/// <summary>WSL2 + Cuttlefish: Androids WebRTC-videoström i en inbäddad WebView2 i stället för skärmdumpar.</summary>
public sealed partial class MainViewModel
{
    /// <summary>Adress till WebRTC-sidan när Android kör i WSL2 (annars null).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasWebScreen), nameof(ShowScreenPlaceholder))]
    private string? _webScreenUrl;

    public bool HasWebScreen => WebScreenUrl is not null;

    /// <summary>WebView2 gick inte att starta: använd skärmdumpar.</summary>
    public void WebScreenFailed(string why)
    {
        AndroidLogLine(L.F("Screen_WebFailed0", why));
        _webScreenBroken = true;
        WebScreenUrl = null;
        UpdateScreenLoop();
    }

    private bool _webScreenBroken;

    private async Task UpdateWebScreenAsync()
    {
        if (_webScreenBroken || _android is not AndroidVm vm || !AndroidRunning) { WebScreenUrl = null; return; }
        var url = await vm.WebScreenUrlAsync();
        WebScreenUrl = AndroidRunning ? url : null;
        UpdateScreenLoop();
    }
}
