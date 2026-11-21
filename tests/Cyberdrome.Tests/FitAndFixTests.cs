// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

using Cyberdrome.Core.Updates;
using Cyberdrome.Vm;
using Cyberdrome.Vm.Skins;

namespace Cyberdrome.Tests;

/// <summary>
/// Version 1.1: skalet ryms alltid i sin yta (även i fullskärm), tryck på skärmen hamnar rätt i alla storlekar,
/// WSL2/Cuttlefish-kontrollen hittar nya Cuttlefish-paket och uppdateringsreglerna (GitHub Releases).
/// </summary>
public class FitAndFixTests
{
    public static IEnumerable<object[]> Sizes() =>
        from skin in BuiltInSkins.All
        from size in new[] { (640.0, 360.0), (900.0, 540.0), (1280.0, 720.0), (1920.0, 1080.0), (3840.0, 2160.0), (500.0, 900.0), (2560.0, 1080.0) }
        select new object[] { skin.Id, size.Item1, size.Item2 };

    [Theory]
    [MemberData(nameof(Sizes))]
    public void SkinScalesUniformlyAndNeverOverflows(string skinId, double viewW, double viewH)
    {
        var skin = BuiltInSkins.Find(skinId)!;
        var fit = SkinLayout.Fit(skin, viewW, viewH);
        double w = skin.Width * fit.Scale, h = skin.Height * fit.Scale;
        // Samma skala åt båda hållen (bildförhållandet behålls) och hela skalet syns.
        Assert.True(w <= viewW + 0.001 && h <= viewH + 0.001, $"{skinId} {w}x{h} ryms inte i {viewW}x{viewH}");
        Assert.True(Math.Abs(w - viewW) < 0.001 || Math.Abs(h - viewH) < 0.001, "skalet ska fylla ytan åt minst ett håll");
        Assert.True(fit.OffsetX >= -0.001 && fit.OffsetY >= -0.001);
        // Skärmen ligger helt inne i skalet och därmed i ytan.
        var (sx, sy, sw, sh) = fit.ToView(skin.Screen);
        Assert.True(sx >= 0 && sy >= 0 && sx + sw <= viewW + 0.001 && sy + sh <= viewH + 0.001);
    }

    [Theory]
    [MemberData(nameof(Sizes))]
    public void TapsMapToTheRightAndroidPixel(string skinId, double viewW, double viewH)
    {
        var skin = BuiltInSkins.Find(skinId)!;
        var preset = ScreenPresets.Find(skin.ScreenPreset)!;
        var (devW, devH) = skin.Orientation == ScreenOrientation.Portrait
            ? (Math.Min(preset.Width, preset.Height), Math.Max(preset.Width, preset.Height))
            : (Math.Max(preset.Width, preset.Height), Math.Min(preset.Width, preset.Height));
        var fit = SkinLayout.Fit(skin, viewW, viewH);
        var (sx, sy, sw, sh) = fit.ToView(skin.Screen);

        // Bilden visas med Stretch=Uniform i skärmytan: räkna fram var Android-bildens hörn och mitt hamnar.
        double scale = Math.Min(sw / devW, sh / devH);
        double ox = (sw - devW * scale) / 2, oy = (sh - devH * scale) / 2;
        (double X, double Y) View(double dx, double dy) => (ox + dx * scale, oy + dy * scale);

        foreach (var (dx, dy) in new[] { (0.5, 0.5), (devW / 2.0, devH / 2.0), (devW - 1.5, devH - 1.5), (devW * 0.25, devH * 0.75) })
        {
            var (px, py) = View(dx, dy);
            var hit = ScreenMapping.ToDevice(px, py, sw, sh, devW, devH);
            Assert.NotNull(hit);
            Assert.InRange(hit!.Value.X, (int)dx - 1, (int)dx + 1);
            Assert.InRange(hit.Value.Y, (int)dy - 1, (int)dy + 1);
            // Samma punkt i skalets koordinater ligger på skärmen, inte på en knapp.
            var (kx, ky) = fit.ToSkin(sx + px, sy + py);
            Assert.True(skin.Screen.Contains(kx, ky));
            Assert.Null(skin.ButtonAt(kx, ky));
        }
        // Utanför bilden (i kanten runt den) blir det inget tryck.
        if (ox > 2) Assert.Null(ScreenMapping.ToDevice(ox / 2, sh / 2, sw, sh, devW, devH));
        if (oy > 2) Assert.Null(ScreenMapping.ToDevice(sw / 2, oy / 2, sw, sh, devW, devH));
    }

    [Fact]
    public void StatusScriptFindsNewCuttlefishPackages()
    {
        var script = AndroidVm.ReadScript("status.sh");
        // Cuttlefish 1.x har "cvd" i stället för launch_cvd; kontrollen fastnade tidigare på "delvis installerat".
        Assert.Contains("command -v cvd", script);
        Assert.Contains("/usr/lib/cuttlefish-common/bin/cvd", script);
        Assert.Contains("id cvd", script);
        Assert.DoesNotContain("\r", script);
    }

    [Fact]
    public void StartScriptFailsLoudlyWithoutLauncher()
    {
        var script = AndroidVm.ReadScript("start.sh");
        Assert.Contains("launch_cvd saknas", script);
        Assert.Contains("exit 3", script);
    }

    [Fact]
    public void RetiredAndroidBuildIsReplacedWhenSettingsAreLoaded()
    {
        var json = """{ "Mode": "Fast", "AndroidBuild": "aosp-android-latest-release/aosp_cf_x86_64_only_auto-userdebug" }""";
        var s = VmSettings.FromJson(json);
        Assert.Equal("aosp-android-latest-release/aosp_cf_x86_64_auto-userdebug", s.AndroidBuild);
        Assert.Equal(new VmSettings().AndroidBuild, s.AndroidBuild);
        // Egna byggmål lämnas orörda.
        Assert.Equal("x/y", VmSettings.FromJson("""{ "AndroidBuild": "x/y" }""").AndroidBuild);
    }

    [Fact]
    public void EveryInstallStopExplainsWhy()
    {
        // Språket byts inte här (testerna körs parallellt); standard i testerna är svenska.
        foreach (var st in Enum.GetValues<AndroidState>())
            Assert.False(string.IsNullOrWhiteSpace(AndroidVm.DescribeState(st)), st.ToString());
        Assert.Contains("nestedVirtualization", AndroidVm.DescribeState(AndroidState.NoKvm));
        Assert.Contains("Plattform för virtuella datorer", AndroidVm.DescribeState(AndroidState.WslMissing));
        Assert.Contains("Virtual Machine Platform", L.In("AndroidVm_WslNotWorkingHelp", "en"));
    }

    [Fact]
    public void AutoUpdateCheckAtMostOncePerDay()
    {
        var now = new DateTime(2026, 11, 21, 12, 0, 0, DateTimeKind.Utc);
        Assert.True(UpdatePolicy.ShouldAutoCheck(null, now));
        Assert.False(UpdatePolicy.ShouldAutoCheck(now.AddHours(-3), now));
        Assert.True(UpdatePolicy.ShouldAutoCheck(now.AddHours(-25), now));
        Assert.True(UpdatePolicy.ShouldAutoCheck(now.AddDays(2), now)); // klockan har ändrats bakåt
    }

    [Fact]
    public void UpdateErrorsAreFriendly()
    {
        Assert.Contains("GitHub begränsar", UpdatePolicy.FriendlyError("Response status code does not indicate success: 403 (rate limit exceeded)."));
        Assert.Contains("Kunde inte nå GitHub", UpdatePolicy.FriendlyError("No such host is known. (api.github.com:443)"));
        Assert.Contains("Inga releaser", UpdatePolicy.FriendlyError("404 (Not Found)"));
    }

    [Fact]
    public void ReleaseNotesBecomePlainText()
    {
        var notes = UpdatePolicy.CleanNotes("## Cyberdrome Cockpit 1.1.0\r\n\r\n\r\n\r\n- **Ny design**\n- `GitHub`-uppdateringar");
        Assert.Equal("Cyberdrome Cockpit 1.1.0\n\n- Ny design\n- GitHub-uppdateringar", notes);
        Assert.Equal("", UpdatePolicy.CleanNotes(null));
        Assert.EndsWith(" …", UpdatePolicy.CleanNotes(new string('x', 2000)));
    }
}
