// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

package se.cyberdrome.radio;

import android.content.Context;

import java.util.HashMap;

/**
 * Översättning av appens texter. Texterna finns i res/values/strings.xml (engelska) och res/values-sv/strings.xml
 * (svenska); Android väljer efter telefonens språk. d() tar den svenska källtexten (som också används som id i koden)
 * och visar den på rätt språk.
 */
final class S {
    private static Context ctx;
    private static final HashMap<String, Integer> MAP = new HashMap<>();

    static {
        MAP.put("Stationer", R.string.stations);
        MAP.put("⏮  Föregående", R.string.previous);
        MAP.put("▶  Spela", R.string.play);
        MAP.put("Nästa  ⏭", R.string.next);
        MAP.put("⏸  Pausa", R.string.pause);
        MAP.put("Spelar", R.string.playing);
        MAP.put("Pausad", R.string.paused);
        MAP.put("Volym efter fart", R.string.gala_switch);
        MAP.put("Volym efter fart: av", R.string.gala_off);
        MAP.put("OBD-adress:", R.string.obd_address);
        MAP.put("Anslut", R.string.connect);
        MAP.put("OBD startar …", R.string.obd_starting);
        MAP.put("Kunde inte starta ljudet", R.string.audio_failed);
        MAP.put("OBD avstängd", R.string.obd_off);
    }

    private S() { }

    static void init(Context c) { if (ctx == null) ctx = c.getApplicationContext(); }

    /** Svensk källtext → text på telefonens språk (okända texter visas som de är). */
    static String d(String sv) {
        Integer id = MAP.get(sv);
        return id == null || ctx == null ? sv : ctx.getString(id);
    }

    /** Formaterad text från strings.xml, t.ex. f(R.string.coolant_value, 90). */
    static String f(int id, Object... args) { return ctx == null ? "" : ctx.getString(id, args); }
}