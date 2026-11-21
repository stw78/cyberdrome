// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

namespace Cyberdrome.Vm;

/// <summary>Inställningar i Android som gör gränssnittet snabbare (samma i emulatorn och i Cuttlefish).</summary>
public static class AndroidTuning
{
    public static readonly string[] AnimationKeys = ["window_animation_scale", "transition_animation_scale", "animator_duration_scale"];

    /// <summary>0,5 = halva animationstiden (snabbare, men animationerna syns fortfarande); 1 = Androids standard.</summary>
    public static string AnimationScale(bool fast) => fast ? "0.5" : "1.0";
}

/// <summary>Takten för skärmbilderna i programmet.</summary>
public static class ScreenPacing
{
    /// <summary>Mål: en bild var 66:e ms (~15 bilder/s), men alltid minst 15 ms paus så att datorn inte går på fullt.</summary>
    public static int DelayAfter(long captureMs) => (int)Math.Clamp(66 - captureMs, 15, 66);
}
