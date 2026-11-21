// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

package se.cyberdrome.radio;

/** En påhittad radiostation. Ljudet genereras lokalt (ingen ström, inget upphovsrättsskyddat ljud). */
public final class Station {
    public final String id;
    public final String name;
    public final String frequency;
    /** Toner (Hz) som spelas i en slinga som stationens "jingel". */
    public final double[] melody;
    /** Längd per ton i millisekunder. */
    public final int noteMs;

    public Station(String id, String name, String frequency, double[] melody, int noteMs) {
        this.id = id;
        this.name = name;
        this.frequency = frequency;
        this.melody = melody;
        this.noteMs = noteMs;
    }

    /** Påhittade stationer – samma som i skissen. */
    public static final Station[] ALL = {
        new Station("neon", "Neon FM", "101,7 MHz", new double[]{523.25, 659.25, 783.99, 1046.5, 783.99, 659.25}, 220),
        new Station("puls", "Radio Puls", "98,4 MHz", new double[]{440.0, 440.0, 554.37, 659.25, 554.37, 440.0, 329.63, 440.0}, 180),
        new Station("stad", "Stadsradion", "103,1 MHz", new double[]{392.0, 493.88, 587.33, 493.88}, 320),
        new Station("kust", "Kustradio", "94,6 MHz", new double[]{293.66, 369.99, 440.0, 587.33, 440.0, 369.99}, 380),
        new Station("retro", "Retro 80", "89,2 MHz", new double[]{261.63, 329.63, 392.0, 523.25, 392.0, 329.63, 261.63, 196.0}, 160),
    };
}
