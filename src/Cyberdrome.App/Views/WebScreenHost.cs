// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Microsoft.Web.WebView2.Core;

namespace Cyberdrome.App.Views;

/// <summary>
/// Visar Cuttlefishs egen WebRTC-ström (videoström från Android i WSL2, 30–60 bilder/s, tryck och svep direkt)
/// i en inbäddad WebView2 (Edge) – i stället för skärmdumpar via adb, som tar ~270 ms per bild.
/// Bara Android-skärmen visas: Cuttlefish-sidans egna knappar döljs. Det självsignerade certifikatet godtas bara
/// för localhost. Fungerar inte WebView2 visas skärmdumpar som förut.
/// </summary>
public sealed class WebScreenHost : NativeControlHost
{
    public static readonly StyledProperty<string?> UrlProperty = AvaloniaProperty.Register<WebScreenHost, string?>(nameof(Url));

    public string? Url { get => GetValue(UrlProperty); set => SetValue(UrlProperty, value); }

    /// <summary>WebView2 kunde inte startas (då används skärmdumpar).</summary>
    public event Action<string>? Failed;

    /// <summary>Uppmätt bildtakt i videoströmmen (bilder/s), en gång per sekund.</summary>
    public event Action<double>? Fps;

    private Avalonia.Threading.DispatcherTimer? _fpsTimer;
    private long _lastFrames = -1;
    private DateTime _lastFpsTime;

    private CoreWebView2Controller? _controller;
    private IntPtr _hwnd;

    /// <summary>CSS som döljer allt utom Android-skärmen (första skärmen; instrumentklustret döljs).</summary>
    public const string OnlyDisplayCss =
        "#control-panel-default-buttons,#control-panel-custom-buttons,.display-control-buttons,#error-message-div,#loader,.device-display-info{display:none!important}" +
        "html,body{margin:0!important;background:#000!important;overflow:hidden!important}" +
        ".device-display:not(:first-child){display:none!important}" +
        "#device-connection,#controls-and-displays{height:100vh!important;margin:0!important;padding:0!important}" +
        "#device-displays{margin:0!important;padding:0!important;height:100vh!important;justify-content:center;align-items:center}" +
        ".device-display,.device-video-container{max-height:100vh!important}" +
        ".device-display-video{max-height:100vh!important;max-width:100vw!important;object-fit:contain}";

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        var handle = base.CreateNativeControlCore(parent);
        _hwnd = handle.Handle;
        _ = InitAsync();
        return handle;
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        _fpsTimer?.Stop();
        try { _controller?.Close(); } catch { }
        _controller = null;
        base.DestroyNativeControlCore(control);
    }

    private async Task InitAsync()
    {
        try
        {
            var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Cyberdrome", "webview");
            var env = await CoreWebView2Environment.CreateAsync(null, data,
                new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required"));
            _controller = await env.CreateCoreWebView2ControllerAsync(_hwnd);
            var web = _controller.CoreWebView2;
            web.Settings.AreDefaultContextMenusEnabled = false;
            web.Settings.AreDevToolsEnabled = false;
            web.Settings.IsStatusBarEnabled = false;
            web.Settings.IsZoomControlEnabled = false;
            // Cuttlefish använder ett självsignerat certifikat – godta det bara för den egna datorn.
            web.ServerCertificateErrorDetected += (_, e) =>
            {
                if (Uri.TryCreate(e.RequestUri, UriKind.Absolute, out var u) && u.Host is "localhost" or "127.0.0.1")
                    e.Action = CoreWebView2ServerCertificateErrorAction.AlwaysAllow;
            };
            if (Environment.GetEnvironmentVariable("CYBERDROME_WEBVIEW_NOCSS") is null)
            await web.AddScriptToExecuteOnDocumentCreatedAsync(
                "(()=>{const s=document.createElement('style');s.textContent=" + System.Text.Json.JsonSerializer.Serialize(OnlyDisplayCss) +
                ";const add=()=>document.head?document.head.appendChild(s):setTimeout(add,10);add();})();");
            _controller.DefaultBackgroundColor = System.Drawing.Color.Black;
            UpdateBounds();
            if (Url is { } url) web.Navigate(url);
            // Bildtakt: räkna videons avkodade bilder en gång per sekund.
            _fpsTimer = new Avalonia.Threading.DispatcherTimer(TimeSpan.FromSeconds(1), Avalonia.Threading.DispatcherPriority.Background, async (_, _) => await MeasureFpsAsync());
            _fpsTimer.Start();
        }
        catch (Exception ex) { Failed?.Invoke(ex.Message); }
    }

    private async Task MeasureFpsAsync()
    {
        if (_controller is null) return;
        try
        {
            var r = await _controller.CoreWebView2.ExecuteScriptAsync(
                "(()=>{const v=document.querySelector('video');return v&&v.getVideoPlaybackQuality?v.getVideoPlaybackQuality().totalVideoFrames:-1;})()");
            if (!long.TryParse(r, out var frames) || frames < 0) return;
            var now = DateTime.UtcNow;
            if (_lastFrames >= 0 && frames >= _lastFrames) Fps?.Invoke((frames - _lastFrames) / (now - _lastFpsTime).TotalSeconds);
            _lastFrames = frames;
            _lastFpsTime = now;
        }
        catch { }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == UrlProperty && _controller is not null && Url is { } url) _controller.CoreWebView2.Navigate(url);
        if (change.Property == BoundsProperty || change.Property == IsVisibleProperty) UpdateBounds();
    }

    private void UpdateBounds()
    {
        if (_controller is null) return;
        var scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        _controller.Bounds = new System.Drawing.Rectangle(0, 0, (int)(Bounds.Width * scale), (int)(Bounds.Height * scale));
        // WebView2 startar dold om värden var dold när den skapades: visa den när den har en yta.
        _controller.IsVisible = Bounds.Width > 1 && Bounds.Height > 1;
    }
}
