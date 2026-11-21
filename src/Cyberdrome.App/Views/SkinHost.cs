// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Cyberdrome.Vm.Skins;

namespace Cyberdrome.App.Views;

/// <summary>
/// Ritar ett skal (instrumentbräda/ram) och lägger sitt innehåll (Android-skärmen) i skalets skärmyta.
/// Inbyggda skal ritas här med egen grafik (inga logotyper eller tillverkarbilder);
/// egna skal ritas från sin bild. Knappar på skalet skickar Android-tangenter via <see cref="ButtonPressed"/>.
/// Utan skal visas en enkel mörk ram.
/// </summary>
public sealed class SkinHost : Decorator, Avalonia.Rendering.ICustomHitTest
{
    /// <summary>Hela skalet tar emot klick (det ritas i Render, utan bakgrundspensel).</summary>
    public bool HitTest(Point point) => new Rect(Bounds.Size).Contains(point);

    public static readonly StyledProperty<Skin?> SkinProperty =
        AvaloniaProperty.Register<SkinHost, Skin?>(nameof(Skin));

    static SkinHost()
    {
        AffectsArrange<SkinHost>(SkinProperty);
        AffectsRender<SkinHost>(SkinProperty);
        ClipToBoundsProperty.OverrideDefaultValue<SkinHost>(true);
    }

    public Skin? Skin { get => GetValue(SkinProperty); set => SetValue(SkinProperty, value); }

    /// <summary>En knapp på skalet trycktes.</summary>
    public event Action<SkinButton>? ButtonPressed;

    private Bitmap? _image;
    private string? _imagePath;
    private SkinButton? _pressed;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SkinProperty)
        {
            var path = Skin?.ImagePath;
            if (path != _imagePath)
            {
                _image?.Dispose();
                _image = null;
                _imagePath = path;
                if (path is not null && File.Exists(path))
                {
                    try { _image = new Bitmap(path); } catch { _image = null; }
                }
            }
        }
    }

    private SkinLayout Layout(Size size) => SkinLayout.Fit(Skin!, size.Width, size.Height);

    protected override Size MeasureOverride(Size availableSize)
    {
        Child?.Measure(availableSize);
        return availableSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Child is null) return finalSize;
        if (Skin is null)
        {
            Child.Arrange(new Rect(12, 12, Math.Max(0, finalSize.Width - 24), Math.Max(0, finalSize.Height - 24)));
            return finalSize;
        }
        var (x, y, w, h) = Layout(finalSize).ToView(Skin.Screen);
        Child.Arrange(new Rect(x, y, w, h));
        return finalSize;
    }

    // ---------------- Rita ----------------

    private static Color C(string hex) => Color.Parse(hex);
    private static IBrush B(string hex) => new SolidColorBrush(C(hex));

    public override void Render(DrawingContext ctx)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        var skin = Skin;
        if (skin is null)
        {
            ctx.DrawRectangle(B("#1E293B"), null, new RoundedRect(new Rect(0, 0, w, h), 18));
            return;
        }
        var L = Layout(Bounds.Size);
        Rect R(SkinRect r) { var (x, y, rw, rh) = L.ToView(r); return new Rect(x, y, rw, rh); }
        var canvas = new Rect(L.OffsetX, L.OffsetY, skin.Width * L.Scale, skin.Height * L.Scale);
        double s = L.Scale;

        if (_image is not null)
        {
            ctx.DrawImage(_image, new Rect(0, 0, _image.PixelSize.Width, _image.PixelSize.Height), canvas);
            return;
        }

        var st = StyleFor(skin.Style ?? "");
        // Instrumentbrädan
        var dash = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(C(st.DashTop), 0), new GradientStop(C(st.DashBottom), 1) },
        };
        ctx.DrawRectangle(dash, null, new RoundedRect(canvas, 26 * s));
        // Dekorlist tvärs över instrumentbrädan
        if (st.TrimY > 0)
            ctx.DrawRectangle(B(st.Trim), null, new Rect(canvas.X, canvas.Y + st.TrimY * s, canvas.Width, 6 * s));

        // Ventiler
        foreach (var v in st.Vents)
        {
            var vr = R(v);
            ctx.DrawRectangle(B("#0A0C0F"), new Pen(B(st.Trim), 2 * s), new RoundedRect(vr, 10 * s));
            bool vertical = v.H > v.W;
            int slats = 6;
            for (int i = 1; i < slats; i++)
            {
                if (vertical) { double x = vr.X + vr.Width * i / slats; ctx.DrawLine(new Pen(B("#2A2F36"), 3 * s), new Point(x, vr.Y + 6 * s), new Point(x, vr.Bottom - 6 * s)); }
                else { double y = vr.Y + vr.Height * i / slats; ctx.DrawLine(new Pen(B("#2A2F36"), 3 * s), new Point(vr.X + 6 * s, y), new Point(vr.Right - 6 * s, y)); }
            }
        }

        // Skärmhuset (fristående surfplatta eller inbyggd ram)
        var scr = R(skin.Screen);
        var bezel = scr.Inflate(st.Bezel * s);
        if (st.Floating)
            ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(110, 0, 0, 0)), null, new RoundedRect(bezel.Translate(new Vector(0, 14 * s)).Inflate(6 * s), (st.Corner + 8) * s));
        ctx.DrawRectangle(B(st.BezelColor), st.Chrome ? new Pen(B("#C9CED6"), 4 * s) : new Pen(B("#2B3038"), 2 * s), new RoundedRect(bezel, st.Corner * s));
        // blank reflex överst på glaset
        ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(28, 255, 255, 255)), null, new RoundedRect(new Rect(bezel.X, bezel.Y, bezel.Width, bezel.Height * 0.08), st.Corner * s));
        ctx.DrawRectangle(Brushes.Black, null, scr);

        // Pekreglage-list (Golf/Octavia-stil)
        if (st.SliderBar is { } sb)
        {
            var r = R(sb);
            ctx.DrawRectangle(B("#101317"), new Pen(B("#2B3038"), 2 * s), new RoundedRect(r, r.Height / 2));
        }

        // Knappar och vred
        foreach (var btn in skin.Buttons)
        {
            var r = R(btn.Rect);
            bool knob = btn.Name.Contains("vred", StringComparison.OrdinalIgnoreCase) || Math.Abs(btn.Rect.W - btn.Rect.H) < 1;
            var fill = btn == _pressed ? B("#3A4250") : B(st.ButtonColor);
            if (knob)
            {
                var c = r.Center;
                double rad = Math.Min(r.Width, r.Height) / 2;
                ctx.DrawEllipse(fill, new Pen(B("#9AA3AE"), 3 * s), c, rad, rad);
                for (int i = 0; i < 24; i++) // räfflad kant
                {
                    double a = i * Math.PI / 12;
                    ctx.DrawLine(new Pen(B("#5B636E"), 1.5 * s), new Point(c.X + Math.Cos(a) * rad * 0.82, c.Y + Math.Sin(a) * rad * 0.82), new Point(c.X + Math.Cos(a) * rad, c.Y + Math.Sin(a) * rad));
                }
            }
            else
                ctx.DrawRectangle(fill, new Pen(B("#3B424C"), 1.5 * s), new RoundedRect(r, 8 * s));
            if (!string.IsNullOrEmpty(btn.Label))
            {
                var ft = new FormattedText(btn.Label, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                    Typeface.Default, Math.Max(9, Math.Min(r.Height * 0.42, 28 * s)), B("#E5E7EB"));
                ctx.DrawText(ft, new Point(r.Center.X - ft.Width / 2, r.Center.Y - ft.Height / 2));
            }
        }
    }

    private sealed record SkinStyle(string DashTop, string DashBottom, string Trim, double TrimY, string BezelColor, double Bezel,
        double Corner, bool Floating, bool Chrome, string ButtonColor, SkinRect[] Vents, SkinRect? SliderBar);

    /// <summary>Färger och former per inbyggt skal – egna val, inspirerade av respektive bils interiör.</summary>
    private static SkinStyle StyleFor(string style) => style switch
    {
        "vw-golf8" => new("#2B2F35", "#15181C", "#4A5058", 70, "#050608", 16, 14, true, false, "#1C2026",
            [new(40, 300, 150, 140), new(1410, 300, 150, 140)], new SkinRect(520, 780, 560, 70)),
        "skoda-octavia4" => new("#2E302F", "#171918", "#8A9097", 80, "#050606", 14, 18, true, true, "#1D2120",
            [new(500, 800, 260, 60), new(840, 800, 260, 60)], new SkinRect(580, 765, 440, 70)),
        "bmw-g20" => new("#25282D", "#0F1114", "#596170", 650, "#08090B", 14, 8, true, false, "#1A1D22",
            [new(30, 120, 70, 420), new(1600, 120, 70, 420)], null),
        "volvo-xc60" => new("#3A3631", "#1E1C1A", "#B9BEC5", 1000, "#0A0A0B", 18, 12, false, true, "#25221F",
            [new(40, 140, 110, 560), new(850, 140, 110, 560)], null),
        "toyota-rav4" => new("#2A2C2F", "#141618", "#6D7380", 60, "#08090A", 22, 10, false, false, "#22262C",
            [new(400, 790, 400, 50), new(900, 790, 400, 50)], null),
        "ford-focus4" => new("#292B2E", "#131416", "#6A707A", 90, "#060708", 16, 12, true, false, "#1E2125",
            [new(60, 260, 160, 120), new(1380, 260, 160, 120)], null),
        _ => new("#24272C", "#121417", "#555B64", 0, "#08090B", 14, 12, false, false, "#1E2125", [], null),
    };

    // ---------------- Tryck på skalets knappar ----------------

    private SkinButton? HitButton(Point p)
    {
        var skin = Skin;
        if (skin is null) return null;
        var (x, y) = Layout(Bounds.Size).ToSkin(p.X, p.Y);
        if (skin.Screen.Contains(x, y)) return null; // tryck på skärmen hanteras av skärmen själv
        return skin.ButtonAt(x, y);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        _pressed = HitButton(e.GetPosition(this));
        if (_pressed is not null) { InvalidateVisual(); e.Handled = true; }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_pressed is null) return;
        var btn = _pressed;
        _pressed = null;
        InvalidateVisual();
        if (HitButton(e.GetPosition(this)) == btn) ButtonPressed?.Invoke(btn);
        e.Handled = true;
    }
}
