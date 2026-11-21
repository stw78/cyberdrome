// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

package se.cyberdrome.launcher;

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
        MAP.put("Navigation", R.string.navigation);
        MAP.put("Media", R.string.media);
        MAP.put("Telefon", R.string.phone);
        MAP.put("Fordon", R.string.vehicle);
        MAP.put("Appar", R.string.apps);
        MAP.put("Öppna kartan", R.string.open_map);
        MAP.put("Ingen telefon ansluten", R.string.no_phone);
        MAP.put("Alla appar", R.string.all_apps);
        MAP.put("Karta och vägvisning", R.string.map_nav);
        MAP.put("Fordonsstatus", R.string.vehicle_status);
        MAP.put("Tryck för att öppna kartan", R.string.tap_open_map);
        MAP.put("Senaste app", R.string.recent_app);
        MAP.put("Öppna navigationen", R.string.open_nav);
        MAP.put("Hem", R.string.home);
        MAP.put("Karta", R.string.map);
        MAP.put("Ljud", R.string.audio);
        MAP.put("Klimat", R.string.climate);
        MAP.put("Bil", R.string.car);
        MAP.put("Inställn.", R.string.settings_short);
        MAP.put("Kylv. –", R.string.coolant_none);
        MAP.put("Bränsle –", R.string.fuel_none);
        MAP.put("Fordon (OBD)", R.string.vehicle_obd);
        MAP.put("Stäng", R.string.close);
        MAP.put("Ingen kartapp installerad – installera Organic Maps från Cyberdrome.", R.string.no_map_app);
        MAP.put("Cyberdrome Radio är inte installerad.", R.string.radio_missing);
        MAP.put("⚠ Felkod: ", R.string.dtc_prefix);
        MAP.put("● OBD ansluten", R.string.obd_connected);
        MAP.put("OBD ansluter …", R.string.obd_connecting);
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