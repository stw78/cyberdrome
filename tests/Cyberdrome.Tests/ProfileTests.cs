// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

using Cyberdrome.Core.Profiles;

namespace Cyberdrome.Tests;

public class ProfileTests
{
    private static string Folder => Path.Combine(AppContext.BaseDirectory, "profiles");

    [Fact]
    public void AllSixProfilesLoad()
    {
        var list = ProfileLoader.LoadFolder(Folder);
        Assert.Equal(6, list.Count);
        Assert.All(list, p => Assert.Empty(ProfileLoader.Validate(p)));
        Assert.Equal(list.Count, list.Select(p => p.Id).Distinct().Count());
    }

    [Fact]
    public void BadProfileGivesSwedishError()
    {
        var ex = Assert.Throws<InvalidDataException>(() => ProfileLoader.Parse("""{"id":"x","vin":"KORT","ecus":[]}"""));
        Assert.Contains("VIN", ex.Message);
    }

    [Fact]
    public void FeatureToggleChangesCodingBit()
    {
        var p = ProfileLoader.LoadFolder(Folder).First(x => x.Id == "vw-golf8");
        var store = new CodingStore(p);
        var f = p.Features.First(x => x.Id == "needle-sweep");
        Assert.False(store.IsOn(f));
        store.SetFeature(f, true);
        Assert.True(store.IsOn(f));
        Assert.Equal(0x01, store.Get("instrument")[0]);
        store.Reset();
        Assert.False(store.IsOn(f));
    }
}
