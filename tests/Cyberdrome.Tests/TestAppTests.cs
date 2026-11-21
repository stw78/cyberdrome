// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

using System.IO.Compression;
using Cyberdrome.Vm;

namespace Cyberdrome.Tests;

public class TestAppTests
{
    [Fact]
    public void BuildsFdroidUrls()
    {
        Assert.Equal("https://f-droid.org/api/v1/packages/com.fr3ts0n.ecu.gui.androbd", AndrObd.ApiUrl);
        Assert.Equal("https://f-droid.org/repo/com.fr3ts0n.ecu.gui.androbd_20710.apk", AndrObd.ApkUrl(20710));
    }

    [Fact]
    public void CachesInLocalAppDataCyberdromeApps()
    {
        var p = AndrObd.CachePath(20710);
        Assert.EndsWith(Path.Combine("Cyberdrome", "apps", "com.fr3ts0n.ecu.gui.androbd_20710.apk"), p);
        Assert.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create), p);
    }

    [Fact]
    public void RecognisesValidAndInvalidApk()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cd-apk-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var good = Path.Combine(dir, "good.apk");
            using (var z = ZipFile.Open(good, ZipArchiveMode.Create))
            {
                z.CreateEntry("AndroidManifest.xml");
                z.CreateEntry("classes.dex");
            }
            Assert.True(AndrObd.IsValidApk(good));

            var html = Path.Combine(dir, "error.apk");
            File.WriteAllText(html, "<html>404</html>");
            Assert.False(AndrObd.IsValidApk(html));

            var noDex = Path.Combine(dir, "nodex.apk");
            using (var z = ZipFile.Open(noDex, ZipArchiveMode.Create)) z.CreateEntry("AndroidManifest.xml");
            Assert.False(AndrObd.IsValidApk(noDex));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void HelpTextNamesEmulatorAddress() =>
        Assert.Contains("10.0.2.2", AndrObd.HelpText);
}
