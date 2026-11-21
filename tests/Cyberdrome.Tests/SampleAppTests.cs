// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

using Cyberdrome.Vm;

namespace Cyberdrome.Tests;

public class SampleAppTests
{
    [Fact]
    public void FindsRadioProjectFromBuildOutputFolder()
    {
        // Testerna körs från tests\Cyberdrome.Tests\bin\...; källkoden ligger i kod\android\CyberdromeRadio.
        var dir = CyberdromeRadio.FindProjectDir(AppContext.BaseDirectory);
        Assert.NotNull(dir);
        Assert.True(File.Exists(Path.Combine(dir!, "AndroidManifest.xml")));
        Assert.EndsWith(Path.Combine("CyberdromeRadio", "bin", "CyberdromeRadio.apk"), CyberdromeRadio.ApkPath(dir!));
    }

    [Fact]
    public void ReturnsNullWhenNoProjectAbove() =>
        Assert.Null(CyberdromeRadio.FindProjectDir(Path.GetPathRoot(Path.GetTempPath())!));

    [Fact]
    public void ManifestDeclaresMediaBrowserServiceAndPackage()
    {
        var manifest = File.ReadAllText(Path.Combine(CyberdromeRadio.FindProjectDir(AppContext.BaseDirectory)!, "AndroidManifest.xml"));
        Assert.Contains($"package=\"{CyberdromeRadio.PackageName}\"", manifest);
        Assert.Contains("android.media.browse.MediaBrowserService", manifest);
    }
}
