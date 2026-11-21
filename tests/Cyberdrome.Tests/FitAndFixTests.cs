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

/// <summary>WSL2 + Cuttlefish: start utan sandlåda, tidsgräns, skärmstorlek och OBD-adress från Android.</summary>
public class WslStartTests
{
    [Fact]
    public void StartScriptWorksInWsl2AndExplainsFailures()
    {
        var s = AndroidVm.ReadScript("start.sh");
        Assert.Contains("--enable_sandbox=false", s);          // crosvm-sandlådan kraschar i WSL2
        Assert.Contains("gfxstream guest_swiftshader", s);        // grafikkortet via /dev/dxg, mjukvara som reserv
        Assert.Contains("GALLIUM_DRIVER=d3d12", s);
        Assert.Contains("--display0=width=$X_RES,height=$Y_RES,dpi=$DPI", s); // skärmstorleken följer Inställningar
        Assert.Contains("timeout \"${BOOT_TIMEOUT:-300}\"", s); // väntar aldrig för evigt
        Assert.Contains("cvd stop", s);                        // städar en tidigare halvstartad instans
        Assert.Contains("FEL: Android startade inte inom", s);
        Assert.Contains("Caused by", s);                       // visar orsaken ur Cuttlefish-loggen
        Assert.Contains("adb connect", s);
        Assert.Contains("cvd stop", AndroidVm.ReadScript("stop.sh"));
        Assert.Contains("gw=", AndroidVm.ReadScript("status.sh"));
    }

    [Fact]
    public void StartEnvironmentUsesTheChosenScreenAndATimeout()
    {
        var vs = new VmSettings { ScreenPresetId = "10in-1560x700", ScreenOrientation = ScreenOrientation.Landscape, Cpus = 4, MemoryMb = 8192, BootTimeoutSeconds = 10 };
        var env = AndroidVm.StartEnv(vs);
        Assert.Equal("1560", env["X_RES"]);
        Assert.Equal("700", env["Y_RES"]);
        Assert.Equal("4", env["CPUS"]);
        Assert.Equal("8192", env["MEM"]);
        Assert.Equal("60", env["BOOT_TIMEOUT"]); // minst en minut
        Assert.Equal(vs.AdbPort.ToString(), env["ADB_PORT"]);
    }

    [Fact]
    public void TheReasonForAFailedStartIsShown()
    {
        var output = ">> Startar Android\nFEL: Android startade inte inom 300 sekunder.\nFEL: Android kunde inte starta (kod 124). Det viktigaste ur Cuttlefish-loggen:\n  Caused by: x\n";
        Assert.Equal("Android kunde inte starta (kod 124). Det viktigaste ur Cuttlefish-loggen:", AndroidVm.StartFailureReason(output));
        Assert.Null(AndroidVm.StartFailureReason(">> Android startat\n"));
    }
}

/// <summary>Prestanda: snabba skärmbilder, grafikkort med reserv, fler kärnor och kortare animationer.</summary>
public class PerformanceTests
{
    [Theory]
    [InlineData(16, 8)]
    [InlineData(8, 4)]
    [InlineData(4, 2)]
    [InlineData(2, 2)]
    [InlineData(64, 8)]
    public void CoresAreHalfTheThreadsWithinLimits(int threads, int cores) => Assert.Equal(cores, VmSettings.DefaultCpusFor(threads));

    [Fact]
    public void OldSettingsAreUpgradedOnce()
    {
        var old = VmSettings.FromJson("""{ "Mode": "GoogleApps", "Cpus": 4, "EmulatorMemoryMb": 4096 }""");
        Assert.True(old.Migrated);
        Assert.Equal(VmSettings.DefaultCpus(), old.Cpus);
        Assert.Equal("host", old.EmulatorGpu);
        Assert.True(old.FastAnimations);
        // Egna val efter uppgraderingen ändras inte igen.
        var mine = VmSettings.FromJson("""{ "Cpus": 3, "EmulatorGpu": "swiftshader_indirect", "FastAnimations": false, "PerformanceVersion": 1 }""");
        Assert.False(mine.Migrated);
        Assert.Equal(3, mine.Cpus);
        Assert.Equal("swiftshader_indirect", mine.EmulatorGpu);
        Assert.False(mine.FastAnimations);
    }

    [Fact]
    public void GpuFallsBackToSoftware()
    {
        Assert.Equal(["host", "swiftshader_indirect"], EmulatorVm.GpuModes("host"));
        Assert.Equal(["swiftshader_indirect"], EmulatorVm.GpuModes("swiftshader_indirect"));
        Assert.Equal(["swiftshader_indirect"], EmulatorVm.GpuModes(null));
        var args = new EmulatorVm(new VmSettings { Cpus = 8, EmulatorMemoryMb = 4096 }).EmulatorArgs("host");
        Assert.Contains("host", args);
        Assert.Equal("8", args[Array.IndexOf(args, "-cores") + 1]);
        Assert.Contains("-no-audio", args);
    }

    [Theory]
    [InlineData(0, 66)]
    [InlineData(30, 36)]
    [InlineData(60, 15)]
    [InlineData(300, 15)]
    public void ScreenPacingAimsForAbout15Fps(long captureMs, int delay) => Assert.Equal(delay, ScreenPacing.DelayAfter(captureMs));

    [Fact]
    public void AnimationScale() { Assert.Equal("0.5", AndroidTuning.AnimationScale(true)); Assert.Equal("1.0", AndroidTuning.AnimationScale(false)); }

    [Fact]
    public void PngSizeAndWmSize()
    {
        var png = new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 13, 10, 26, 10, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R', 0, 0, 6, 0x18, 0, 0, 2, 0xBC };
        Assert.Equal((1560, 700), PngInfo.Size(png));
        Assert.Null(PngInfo.Size([1, 2, 3]));
        Assert.Equal((1560, 700), PngInfo.ParseWmSize("Physical size: 1560x700\n"));
        Assert.Equal((1280, 800), PngInfo.ParseWmSize("Physical size: 1560x700\nOverride size: 1280x800\n"));
        Assert.Null(PngInfo.ParseWmSize("error"));
    }

    [Fact]
    public async Task ConsoleRepliesAreRead()
    {
        Assert.Equal(5554, EmulatorConsole.PortFromSerial("emulator-5554"));
        Assert.Null(EmulatorConsole.PortFromSerial("127.0.0.1:6520"));
        Assert.True(await EmulatorConsole.ReadReplyAsync(new StringReader("Android Console\nOK\n"), default));
        Assert.False(await EmulatorConsole.ReadReplyAsync(new StringReader("KO: unknown command\n"), default));
    }
}

/// <summary>Bildström och pekare via emulatorns gRPC-API.</summary>
public class StreamTests
{
    [Fact]
    public void DiscoveryFileIsParsed()
    {
        var ini = "port.serial=5554\r\nport.adb=5555\r\ncmdline=\"C:\\emu\\qemu.exe\" \"-avd\" \"Cyberdrome_Automotive_Google\" \"-grpc\" \"8554\"\r\ngrpc.token=abc123\r\ngrpc.port=8554\r\n";
        var (port, token, cmd) = EmulatorGrpc.ParseDiscovery(ini);
        Assert.Equal(8554, port);
        Assert.Equal("abc123", token);
        Assert.Contains("\"Cyberdrome_Automotive_Google\"", cmd);
        Assert.Null(EmulatorGrpc.ParseDiscovery("port.serial=5554").Port);
        Assert.Null(EmulatorGrpc.Discover("Finns_Inte_" + Guid.NewGuid()));
    }

    [Fact]
    public void EmulatorStartsWithProtectedGrpc()
    {
        var args = new EmulatorVm(new VmSettings { EmulatorGrpcPort = 8600 }).EmulatorArgs("host");
        Assert.Equal("8600", args[Array.IndexOf(args, "-grpc") + 1]);
        Assert.Contains("-grpc-use-token", args); // ingen oskyddad port på nätverket
    }

    [Fact]
    public async Task NoStreamWithoutAnEmulator()
    {
        var vm = new EmulatorVm(new VmSettings { AvdName = "Finns_Inte_" + Guid.NewGuid() });
        Assert.False(await vm.StreamScreenAsync(_ => { }, default)); // då används skärmdumpar som förut
        Assert.False(await vm.PointerAsync(1, 1, true));
    }
}

/// <summary>WSL2: WebRTC-strömmens adress och grafikstöd i installationen.</summary>
public class WslVideoTests
{
    [Fact]
    public void WebScreenUrlComesFromTheDeviceList()
    {
        Assert.Equal("https://localhost:8443/devices/cvd_1-1-1/files/client.html",
            AndroidVm.WebScreenUrlFrom("https://localhost:8443/", """[{"device_id":"cvd_1-1-1","group_name":"cvd_1","adb_port":6520}]"""));
        Assert.Null(AndroidVm.WebScreenUrlFrom("https://localhost:8443", "[]"));
        Assert.Null(AndroidVm.WebScreenUrlFrom("https://localhost:8443", "inte json"));
    }

    [Fact]
    public void SetupInstallsGpuSupport() => Assert.Contains("mesa-vulkan-drivers", AndroidVm.ReadScript("setup.sh"));
}

/// <summary>Inga svarta kanter: bildströmmen beskärs till ytan Android ritar i; Launcher får rätt OBD-adress.</summary>
public class SkinFillTests
{
    [Fact]
    public void CropMatchesTheLogicalScreenInsideThePhysicalOne()
    {
        // Emulatorn startad som 1280x800, sedan bytt till 1560x700 under körningen (wm size override).
        Assert.Equal(new CropRect(0, 113, 1280, 574), CropRect.ForOverride((1280, 800), (1560, 700)));
        Assert.Equal(new CropRect(140, 0, 1000, 800), CropRect.ForOverride((1280, 800), (800, 640)));
        Assert.Null(CropRect.ForOverride((1560, 700), (1560, 700)));
        Assert.Null(CropRect.ForOverride((1560, 700), null));
        var (p, o) = PngInfo.ParseWmSizes("Physical size: 1280x800\nOverride size: 1560x700\n");
        Assert.Equal((1280, 800), p);
        Assert.Equal((1560, 700), o);
        Assert.Null(PngInfo.ParseWmSizes("Physical size: 1280x800\n").Override);
    }

    [Fact]
    public void LauncherGetsTheObdAddressSeenFromAndroid()
    {
        var a = CyberdromeLauncher.BroadcastBuiltIn("vw-golf8", "172.25.160.1:35000");
        Assert.Equal("172.25.160.1:35000", a[Array.IndexOf(a, "obd") + 1]);
        Assert.DoesNotContain("obd", CyberdromeLauncher.BroadcastBuiltIn("vw-golf8"));
    }
}

public class BackButtonTests
{
    [Fact]
    public void EverySkinHasABackButton()
    {
        foreach (var s in Cyberdrome.Vm.Skins.BuiltInSkins.All)
        {
            Assert.Contains(s.Buttons, b => b.KeyCode == Cyberdrome.Vm.Skins.SkinButton.Keys.Back);
            Assert.Empty(s.Validate()); // knapparna ryms i skalet och täcker inte skärmen
        }
    }
}
