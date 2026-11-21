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
        if (ScreenFrame is not WriteableBitmap wb || wb.PixelSize.Width != f.Width || wb.PixelSize.Height != f.Height)
        {
            wb = new WriteableBitmap(new PixelSize(f.Width, f.Height), new Vector(96, 96), PixelFormat.Rgba8888, AlphaFormat.Opaque);
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

    private static void CopyInto(WriteableBitmap wb, RawFrame f)
    {
        using var fb = wb.Lock();
        int row = f.Width * 4;
        if (fb.RowBytes == row) Marshal.Copy(f.Rgba, 0, fb.Address, row * f.Height);
        else for (int y = 0; y < f.Height; y++) Marshal.Copy(f.Rgba, y * row, fb.Address + y * fb.RowBytes, row);
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
