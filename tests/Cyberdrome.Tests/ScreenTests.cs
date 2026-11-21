// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

using Cyberdrome.Vm;

namespace Cyberdrome.Tests;

public class ScreenTests
{
    [Theory]
    [InlineData(SteeringButton.Previous, 88)] // KEYCODE_MEDIA_PREVIOUS
    [InlineData(SteeringButton.Next, 87)]     // KEYCODE_MEDIA_NEXT
    [InlineData(SteeringButton.Mute, 164)]    // KEYCODE_VOLUME_MUTE
    public void SteeringButtonsMapToAndroidKeyCodes(SteeringButton b, int code) =>
        Assert.Equal(code, SteeringKeys.KeyCode(b));

    [Fact]
    public void EverySteeringButtonHasSwedishLabel()
    {
        foreach (var b in Enum.GetValues<SteeringButton>())
            Assert.False(string.IsNullOrWhiteSpace(SteeringKeys.Label(b)));
    }

    [Fact]
    public void ScreenshotFileNameHasTimestampAndNoInvalidChars()
    {
        var name = ScreenshotNaming.FileName(new DateTime(2026, 11, 20, 0, 45, 12));
        Assert.Equal("android-2026-11-20_00-45-12.png", name);
        Assert.Equal(-1, name.IndexOfAny(Path.GetInvalidFileNameChars()));
    }

    [Fact]
    public void ScreenshotPathIsUniqueWithinSameSecond()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cd-shot-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var t = new DateTime(2026, 11, 20, 0, 45, 12);
            var first = ScreenshotNaming.UniquePath(dir, t);
            File.WriteAllText(first, "x");
            var second = ScreenshotNaming.UniquePath(dir, t);
            Assert.NotEqual(first, second);
            Assert.EndsWith("android-2026-11-20_00-45-12-2.png", second);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void DefaultScreenshotFolderIsProjektSkarmbilder() =>
        Assert.EndsWith(Path.Combine("Projekt", "skarmbilder"), ScreenshotNaming.DefaultFolder);

    [Fact]
    public void MapsClicksThroughLetterbox()
    {
        // 1024x768-skärm i en 800x800-yta: skala 0.78125, höjd 600, svarta kanter 100 px upptill/nedtill.
        Assert.Equal((512, 384), ScreenMapping.ToDevice(400, 400, 800, 800, 1024, 768));
        Assert.Equal((0, 0), ScreenMapping.ToDevice(0, 100, 800, 800, 1024, 768));
        Assert.Null(ScreenMapping.ToDevice(400, 50, 800, 800, 1024, 768)); // i den svarta kanten
    }

    [Fact]
    public void FindsTopActivityInDumpsys()
    {
        const string dump = """
              mResumedActivity: ActivityRecord{1a2b u0 com.example.radio/.MainActivity t12}
            topResumedActivity=ActivityRecord{3c4d u0 com.android.car.carlauncher/.CarLauncher t5}
            """;
        Assert.Equal("com.android.car.carlauncher/.CarLauncher", ActivityParser.TopComponent(dump));
        Assert.Null(ActivityParser.TopComponent("nothing here"));
    }
}
