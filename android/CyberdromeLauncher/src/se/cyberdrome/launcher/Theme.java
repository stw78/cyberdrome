// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

package se.cyberdrome.launcher;

import android.graphics.Color;

import org.json.JSONObject;

/**
 * Ett tema för startskärmen. Inbyggda teman är inspirerade av respektive bilmärkes
 * system (layout och färger) men ritas helt av oss – inga logotyper, märkesfonter eller märkesgrafik.
 * Egna teman skickas som JSON (theme.json) från Cyberdrome.
 */
public final class Theme {
    /** Layoutvarianter. */
    public static final String TILES = "tiles";         // rutnät med widgetrutor
    public static final String LEFT_MENU = "leftmenu";  // smal meny till vänster + breda rutor i rad
    public static final String PORTRAIT = "portrait";   // staplade rutor för stående skärm
    public static final String SPLIT = "split";         // 2–3 delade fält
    public static final String TABS = "tabs";           // tre stora fält + flikrad längst ner

    public String id = "standard";
    public String name = "Standard";
    public String layout = TILES;
    public int background = Color.rgb(14, 17, 22);
    public int surface = Color.rgb(27, 34, 48);
    public int accent = Color.rgb(59, 167, 255);
    public int text = Color.WHITE;
    public int muted = Color.rgb(148, 163, 184);
    /** Hörnradie i dp. */
    public int corner = 16;
    /** "sans", "light", "condensed" eller "medium" (Roboto-varianter som finns i Android). */
    public String font = "sans";
    /** Bakgrundsmönster: "gradient", "lines", "dots", "plain". */
    public String wallpaper = "gradient";
    /** Flikfärger (layout "tabs"): ljud, klimat, telefon, navigation, appar, inställningar. */
    public int[] tabColors = null;

    public static int parseColor(String s, int fallback) {
        try { return Color.parseColor(s.startsWith("#") ? s : "#" + s); } catch (Exception e) { return fallback; }
    }

    /** Läser ett tema från JSON. Saknade fält får standardvärden. */
    public static Theme fromJson(String json) throws Exception {
        JSONObject o = new JSONObject(json);
        Theme t = new Theme();
        t.id = o.optString("id", t.id);
        t.name = o.optString("name", t.name);
        t.layout = o.optString("layout", t.layout);
        JSONObject c = o.optJSONObject("colors");
        if (c != null) {
            t.background = parseColor(c.optString("background", ""), t.background);
            t.surface = parseColor(c.optString("surface", ""), t.surface);
            t.accent = parseColor(c.optString("accent", ""), t.accent);
            t.text = parseColor(c.optString("text", ""), t.text);
            t.muted = parseColor(c.optString("muted", ""), t.muted);
        }
        t.corner = o.optInt("cornerRadius", t.corner);
        t.font = o.optString("font", t.font);
        t.wallpaper = o.optString("wallpaper", t.wallpaper);
        org.json.JSONArray tabs = o.optJSONArray("tabColors");
        if (tabs != null) {
            t.tabColors = new int[tabs.length()];
            for (int i = 0; i < tabs.length(); i++) t.tabColors[i] = parseColor(tabs.optString(i), t.accent);
        }
        return t;
    }

    private static Theme make(String id, String name, String layout, String bg, String surface, String accent,
                              String muted, int corner, String font, String wallpaper) {
        Theme t = new Theme();
        t.id = id; t.name = name; t.layout = layout;
        t.background = Color.parseColor(bg); t.surface = Color.parseColor(surface);
        t.accent = Color.parseColor(accent); t.muted = Color.parseColor(muted);
        t.corner = corner; t.font = font; t.wallpaper = wallpaper;
        return t;
    }

    /** Inbyggda teman – ett per bilmodell i Cyberdrome (samma id som bilprofilen). */
    public static Theme builtIn(String id) {
        switch (id) {
            case "vw-golf8":
                return make(id, "Golf 8-stil", TILES, "#0B0F17", "#1A2333", "#3BA7FF", "#8FA3BF", 14, "sans", "gradient");
            case "skoda-octavia4":
                return make(id, "Octavia 4-stil", TILES, "#0C1311", "#1B2724", "#58C77A", "#93AFA5", 20, "sans", "lines");
            case "bmw-g20":
                return make(id, "3-serie G20-stil", LEFT_MENU, "#07090C", "#171B21", "#4FA3FF", "#8A96A8", 6, "condensed", "lines");
            case "volvo-xc60":
                return make(id, "XC60-stil", PORTRAIT, "#0F1113", "#1D2125", "#A9D3F5", "#9BA5AE", 10, "light", "plain");
            case "toyota-rav4":
                return make(id, "RAV4-stil", SPLIT, "#061732", "#0E2A52", "#2D8CFF", "#9DB7DA", 8, "medium", "dots");
            case "ford-focus4": {
                Theme t = make(id, "Focus Mk4-stil", TABS, "#000000", "#1C1C1E", "#F28C28", "#A0A0A5", 4, "medium", "plain");
                t.tabColors = new int[]{
                        Color.parseColor("#F28C28"), Color.parseColor("#E5484D"), Color.parseColor("#F5C518"),
                        Color.parseColor("#3DBE6B"), Color.parseColor("#9B6CF2"), Color.parseColor("#8E8E93")};
                return t;
            }
            default:
                return new Theme();
        }
    }
}
