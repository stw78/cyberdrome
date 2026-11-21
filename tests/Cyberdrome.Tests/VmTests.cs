// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

using Cyberdrome.Vm;

namespace Cyberdrome.Tests;

public class VmTests
{
    [Theory]
    [InlineData("status.sh")]
    [InlineData("setup.sh")]
    [InlineData("fetch.sh")]
    [InlineData("start.sh")]
    [InlineData("stop.sh")]
    [InlineData("install-apk.sh")]
    public void ScriptsAreEmbeddedWithLinuxLineEndings(string name)
    {
        var s = AndroidVm.ReadScript(name);
        Assert.NotEmpty(s);
        Assert.DoesNotContain("\r", s);
    }

    [Fact]
    public async Task NonWindowsReportsNotWindows()
    {
        if (OperatingSystem.IsWindows()) return;
        Assert.Equal(AndroidState.NotWindows, await new AndroidVm(new VmSettings()).GetStateAsync());
    }

    [Fact]
    public void ParsesStepLines()
    {
        var s = ProgressParser.ParseStep("@@STEP 0.55 Installerar Cuttlefish");
        Assert.NotNull(s);
        Assert.Equal(0.55, s.Value.Fraction, 3);
        Assert.Equal("Installerar Cuttlefish", s.Value.Text);
        Assert.Null(ProgressParser.ParseStep(">> Installerar Cuttlefish"));
        Assert.Equal(1, ProgressParser.ParseStep("@@STEP 7 för mycket")!.Value.Fraction);
    }

    [Fact]
    public void ParsesByteLines()
    {
        Assert.Equal(123456789L, ProgressParser.ParseBytes("@@BYTES 123456789"));
        Assert.Equal(0L, ProgressParser.ParseBytes("@@BYTES -5"));
        Assert.Null(ProgressParser.ParseBytes("@@BYTES abc"));
        Assert.Null(ProgressParser.ParseBytes("hämtar 50%"));
        Assert.True(ProgressParser.IsProgressLine("@@BYTES 1"));
        Assert.False(ProgressParser.IsProgressLine(">> Klart"));
    }

    [Fact]
    public void DownloadFractionStopsAt99PercentUntilDone()
    {
        Assert.Equal(0.5, ProgressParser.DownloadFraction(50, 100), 3);
        Assert.Equal(0.99, ProgressParser.DownloadFraction(500, 100), 3);
        Assert.Equal(0, ProgressParser.DownloadFraction(50, 0));
    }

    [Fact]
    public void ScriptsReportProgress()
    {
        Assert.Contains("@@BYTES", AndroidVm.ReadScript("fetch.sh"));
        Assert.Contains("@@STEP", AndroidVm.ReadScript("setup.sh"));
    }

    [Fact]
    public void EveryStateHasSwedishText()
    {
        var emu = new EmulatorVm(new VmSettings());
        foreach (var s in Enum.GetValues<AndroidState>())
        {
            Assert.False(string.IsNullOrEmpty(AndroidVm.DescribeState(s)));
            Assert.False(string.IsNullOrEmpty(emu.Describe(s)));
        }
    }

    [Fact]
    public void ModeDefaultsToNoRestartAndRoundTripsAsJson()
    {
        var s = new VmSettings();
        Assert.Equal(AndroidMode.NoRestart, s.Mode);

        s.Mode = AndroidMode.Fast;
        s.SystemImage = "system-images;android-35-ext15;android-automotive;x86_64";
        var json = s.ToJson();
        Assert.Contains("\"Mode\": \"Fast\"", json);

        var back = VmSettings.FromJson(json);
        Assert.Equal(AndroidMode.Fast, back.Mode);
        Assert.Equal(s.SystemImage, back.SystemImage);

        // Gammal fil utan Mode-fält (från version 0.2) ska ge standardläget.
        Assert.Equal(AndroidMode.NoRestart, VmSettings.FromJson("{\"DistroName\":\"X\"}").Mode);
    }

    [Fact]
    public void GoogleAppsModeUsesItsOwnImageAndAvd()
    {
        var s = new VmSettings();
        var plain = new EmulatorVm(s);
        var google = (EmulatorVm)AndroidBackends.Create(new VmSettings { Mode = AndroidMode.GoogleApps });
        Assert.True(google.GoogleApps);
        Assert.Equal(AndroidMode.GoogleApps, google.Mode);
        Assert.Contains("playstore", google.SystemImage);
        Assert.DoesNotContain("playstore", plain.SystemImage);
        Assert.NotEqual(plain.AvdName, google.AvdName);
        Assert.EndsWith(Path.Combine("android-automotive-playstore", "x86_64"), google.SystemImageDir);
        Assert.Contains("\"Mode\": \"GoogleApps\"", new VmSettings { Mode = AndroidMode.GoogleApps }.ToJson());
        Assert.False(string.IsNullOrEmpty(AndroidBackends.ModeTitle(AndroidMode.GoogleApps)));
    }

    [Fact]
    public void BackendMatchesMode()
    {
        Assert.IsType<EmulatorVm>(AndroidBackends.Create(new VmSettings { Mode = AndroidMode.NoRestart }));
        Assert.IsType<AndroidVm>(AndroidBackends.Create(new VmSettings { Mode = AndroidMode.Fast }));
        Assert.Equal("10.0.2.2", new EmulatorVm(new VmSettings()).ObdHostFromAndroid);
    }

    [Theory]
    [InlineData("[=======                                ] 20% Downloading x86_64-34_r05.zip...", 20)]
    [InlineData("[=======================================] 100% Unzipping... x86_64/system.img", 100)]
    public void ParsesSdkManagerPercent(string line, double expected) =>
        Assert.Equal(expected, EmulatorVm.ParsePercent(line));

    [Fact]
    public void NonPercentLinesAreIgnored() =>
        Assert.Null(EmulatorVm.ParsePercent("Accept? (y/N): License android-sdk-license"));

    [Fact]
    public void SystemImageDirFollowsPackageName()
    {
        var s = new VmSettings { SdkRoot = @"C:\sdk", SystemImage = "system-images;android-34-ext9;android-automotive;x86_64" };
        Assert.Equal(Path.Combine(@"C:\sdk", "system-images", "android-34-ext9", "android-automotive", "x86_64"), s.SystemImageDir);
    }
}
