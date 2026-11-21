// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

package se.cyberdrome.launcher;

import android.app.Activity;
import android.app.AlertDialog;
import android.content.ComponentName;
import android.content.Context;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.content.pm.ResolveInfo;
import android.graphics.Canvas;
import android.graphics.Color;
import android.graphics.LinearGradient;
import android.graphics.Paint;
import android.graphics.Shader;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.view.Gravity;
import android.view.View;
import android.view.ViewGroup;
import android.widget.FrameLayout;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.TextView;

import java.lang.ref.WeakReference;
import java.text.SimpleDateFormat;
import java.util.ArrayList;
import java.util.Collections;
import java.util.Date;
import java.util.List;
import java.util.Locale;

/**
 * Startskärmen. Bygger hela gränssnittet i kod utifrån valt tema (Theme).
 * Fordonsdata kommer bara från den virtuella OBD-porten (10.0.2.2:35000).
 */
public final class HomeActivity extends Activity implements ObdClient.Listener {
    private static WeakReference<HomeActivity> instance = new WeakReference<>(null);
    private static final Handler MAIN = new Handler(Looper.getMainLooper());

    private Theme theme;
    private Typeface face;
    private ObdClient obd;
    private final List<TextView> speedViews = new ArrayList<>();
    private final List<TextView> rpmViews = new ArrayList<>();
    private final List<TextView> coolantViews = new ArrayList<>();
    private final List<TextView> fuelViews = new ArrayList<>();
    private final List<TextView> clockViews = new ArrayList<>();
    private final List<TextView> obdStatusViews = new ArrayList<>();
    private final List<TextView> dtcViews = new ArrayList<>();
    private String[] dtcs = new String[0];
    private int speed = -1, rpm = -1, coolant = Integer.MIN_VALUE, fuel = -1;
    private String obdStatus = "OBD ansluter …";

    /** Anropas av ThemeReceiver när Cyberdrome byter tema. */
    static void themeChanged() {
        MAIN.post(() -> {
            HomeActivity a = instance.get();
            if (a != null && !a.isFinishing()) a.rebuild();
        });
    }

    @Override
    protected void onCreate(Bundle saved) {
        super.onCreate(saved);
        S.init(this);
        obdStatus = S.d("OBD ansluter …");
        instance = new WeakReference<>(this);
        obd = new ObdClient(this);
        obd.start();
        rebuild();
        MAIN.postDelayed(clockTick, 1000);
    }

    @Override
    protected void onDestroy() {
        obd.stop();
        MAIN.removeCallbacks(clockTick);
        super.onDestroy();
    }

    @Override
    public void onBackPressed() { /* startskärmen stängs inte med Tillbaka */ }

    private final Runnable clockTick = new Runnable() {
        @Override public void run() {
            String now = new SimpleDateFormat("HH:mm", Locale.getDefault()).format(new Date());
            for (TextView t : clockViews) t.setText(now);
            MAIN.postDelayed(this, 15000);
        }
    };

    // ---------------- Bygg gränssnittet ----------------

    private void rebuild() {
        theme = ThemeReceiver.current(this);
        face = typeface(theme.font);
        dtcViews.clear(); speedViews.clear(); rpmViews.clear(); coolantViews.clear(); fuelViews.clear(); clockViews.clear(); obdStatusViews.clear();

        FrameLayout root = new FrameLayout(this);
        root.addView(new WallpaperView(this, theme), new FrameLayout.LayoutParams(-1, -1));
        View content;
        switch (theme.layout) {
            case Theme.LEFT_MENU: content = buildLeftMenu(); break;
            case Theme.PORTRAIT: content = buildPortrait(); break;
            case Theme.SPLIT: content = buildSplit(); break;
            case Theme.TABS: content = buildTabs(); break;
            default: content = buildTiles(); break;
        }
        root.addView(content, new FrameLayout.LayoutParams(-1, -1));
        setContentView(root);
        clockTick.run();
        MAIN.removeCallbacks(clockTick);
        MAIN.postDelayed(clockTick, 15000);
        updateValues();
    }

    private static Typeface typeface(String font) {
        switch (font) {
            case "light": return Typeface.create("sans-serif-light", Typeface.NORMAL);
            case "condensed": return Typeface.create("sans-serif-condensed", Typeface.NORMAL);
            case "medium": return Typeface.create("sans-serif-medium", Typeface.NORMAL);
            default: return Typeface.create("sans-serif", Typeface.NORMAL);
        }
    }

    private int dp(float v) { return Math.round(v * getResources().getDisplayMetrics().density); }

    private GradientDrawable box(int color, int radiusDp) {
        GradientDrawable g = new GradientDrawable();
        g.setColor(color);
        g.setCornerRadius(dp(radiusDp));
        return g;
    }

    private TextView text(String s, float sp, int color, boolean bold) {
        TextView t = new TextView(this);
        t.setText(S.d(s));
        t.setTextSize(sp);
        t.setTextColor(color);
        t.setTypeface(face, bold ? Typeface.BOLD : Typeface.NORMAL);
        return t;
    }

    private static LinearLayout.LayoutParams weight(float w, boolean horizontal) {
        return horizontal ? new LinearLayout.LayoutParams(0, -1, w) : new LinearLayout.LayoutParams(-1, 0, w);
    }

    private LinearLayout row() {
        LinearLayout l = new LinearLayout(this);
        l.setOrientation(LinearLayout.HORIZONTAL);
        return l;
    }

    private LinearLayout column() {
        LinearLayout l = new LinearLayout(this);
        l.setOrientation(LinearLayout.VERTICAL);
        return l;
    }

    private static int alpha(int color, int a) { return Color.argb(a, Color.red(color), Color.green(color), Color.blue(color)); }

    /** Statusrad: klocka, OBD-status och temats namn. */
    private View statusBar(boolean pullTab) {
        LinearLayout bar = row();
        bar.setGravity(Gravity.CENTER_VERTICAL);
        bar.setPadding(dp(16), dp(6), dp(16), dp(6));
        TextView clock = text("--:--", 20, theme.text, true);
        clockViews.add(clock);
        TextView status = text(obdStatus, 12, theme.muted, false);
        obdStatusViews.add(status);
        status.setPadding(dp(16), 0, 0, 0);
        bar.addView(clock);
        bar.addView(status, new LinearLayout.LayoutParams(0, -2, 1));
        TextView name = text(theme.name, 12, theme.muted, false);
        bar.addView(name);
        if (pullTab) {
            // Liten "dra ner"-flik mitt på statusraden (inspirerat av stående system)
            LinearLayout wrap = column();
            wrap.setGravity(Gravity.CENTER_HORIZONTAL);
            wrap.addView(bar, new LinearLayout.LayoutParams(-1, -2));
            View tab = new View(this);
            tab.setBackground(box(alpha(theme.muted, 120), 3));
            wrap.addView(tab, new LinearLayout.LayoutParams(dp(48), dp(5)));
            return wrap;
        }
        return bar;
    }

    /** En ruta med ikon, rubrik och undertext. */
    private LinearLayout tile(String icon, String title, String sub, View.OnClickListener click, int stripe) {
        LinearLayout t = column();
        t.setBackground(box(theme.surface, theme.corner));
        t.setPadding(dp(16), dp(14), dp(16), dp(14));
        if (stripe != 0) {
            View band = new View(this);
            band.setBackground(box(stripe, 2));
            LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(dp(42), dp(4));
            lp.bottomMargin = dp(10);
            t.addView(band, lp);
        }
        TextView i = text(icon, 26, stripe != 0 ? stripe : theme.accent, false);
        t.addView(i);
        t.addView(text(title, 18, theme.text, true));
        TextView s = text(sub, 13, theme.muted, false);
        t.addView(s);
        if ("Navigation".equals(title) || "Karta".equals(title)) {
            // Förhandsvisning av en karta, ritad av oss (inga kartdata)
            LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(-1, 0, 1);
            lp.topMargin = dp(8);
            t.addView(new MapPreviewView(this, theme), lp);
        } else if ("Media".equals(title) || "Ljud".equals(title)) {
            View spacer = new View(this);
            t.addView(spacer, new LinearLayout.LayoutParams(-1, 0, 1));
            t.addView(mediaControls());
        }
        t.setOnClickListener(click);
        return t;
    }

    /** Föregående / spela-paus / nästa – styr den app som spelar (t.ex. Cyberdrome Radio). */
    private View mediaControls() {
        LinearLayout r = row();
        r.setGravity(Gravity.CENTER_VERTICAL);
        String[] labels = {"⏮", "⏯", "⏭"};
        int[] keys = {android.view.KeyEvent.KEYCODE_MEDIA_PREVIOUS, android.view.KeyEvent.KEYCODE_MEDIA_PLAY_PAUSE, android.view.KeyEvent.KEYCODE_MEDIA_NEXT};
        for (int i = 0; i < 3; i++) {
            TextView b = text(labels[i], 22, theme.text, false);
            b.setGravity(Gravity.CENTER);
            b.setBackground(box(alpha(theme.accent, 70), theme.corner));
            final int key = keys[i];
            b.setOnClickListener(v -> mediaKey(key));
            r.addView(b, margins(new LinearLayout.LayoutParams(0, dp(46), 1), 3));
        }
        return r;
    }

    private void mediaKey(int code) {
        android.media.AudioManager am = (android.media.AudioManager) getSystemService(Context.AUDIO_SERVICE);
        am.dispatchMediaKeyEvent(new android.view.KeyEvent(android.view.KeyEvent.ACTION_DOWN, code));
        am.dispatchMediaKeyEvent(new android.view.KeyEvent(android.view.KeyEvent.ACTION_UP, code));
    }

    private LinearLayout.LayoutParams margins(LinearLayout.LayoutParams lp, int m) {
        lp.setMargins(dp(m), dp(m), dp(m), dp(m));
        return lp;
    }

    /** Fordonsruta med OBD-data (hastighet, varvtal, kylvätska, bränsle). */
    private LinearLayout vehicleTile(String title) {
        LinearLayout t = column();
        t.setBackground(box(theme.surface, theme.corner));
        t.setPadding(dp(16), dp(14), dp(16), dp(14));
        t.addView(text(title, 14, theme.muted, false));
        TextView sp = text("– km/h", 30, theme.text, true);
        speedViews.add(sp);
        t.addView(sp);
        TextView r = text("– rpm", 16, theme.accent, false);
        rpmViews.add(r);
        t.addView(r);
        LinearLayout small = row();
        TextView c = text("Kylv. –", 13, theme.muted, false);
        TextView f = text("Bränsle –", 13, theme.muted, false);
        f.setPadding(dp(14), 0, 0, 0);
        coolantViews.add(c); fuelViews.add(f);
        small.addView(c); small.addView(f);
        t.addView(small);
        TextView d = text("", 14, Color.rgb(245, 158, 11), true);
        dtcViews.add(d);
        t.addView(d);
        return t;
    }

    /** Nedre rad i "klimatstil" – visar bara OBD-värden. */
    private View vehicleBar() {
        LinearLayout bar = row();
        bar.setGravity(Gravity.CENTER_VERTICAL);
        bar.setBackground(box(alpha(theme.surface, 230), theme.corner));
        bar.setPadding(dp(18), dp(8), dp(18), dp(8));
        TextView c = text("Kylv. –", 16, theme.text, false);
        TextView s = text("– km/h", 20, theme.text, true);
        TextView f = text("Bränsle –", 16, theme.text, false);
        coolantViews.add(c); speedViews.add(s); fuelViews.add(f);
        s.setGravity(Gravity.CENTER);
        f.setGravity(Gravity.END);
        bar.addView(c, new LinearLayout.LayoutParams(0, -2, 1));
        bar.addView(s, new LinearLayout.LayoutParams(0, -2, 1));
        bar.addView(f, new LinearLayout.LayoutParams(0, -2, 1));
        return bar;
    }

    // ---------------- Layouter ----------------

    /** Rutnät med widgetrutor (VW/Skoda-inspirerat). */
    private View buildTiles() {
        LinearLayout root = column();
        root.setPadding(dp(8), dp(4), dp(8), dp(8));
        root.addView(statusBar(false));
        LinearLayout r1 = row(), r2 = row();
        r1.addView(tile("🧭", "Navigation", "Öppna kartan", v -> openNav(), 0), margins(weight(2, true), 6));
        r1.addView(tile("♫", "Media", "Cyberdrome Radio", v -> openPackage("se.cyberdrome.radio"), 0), margins(weight(1, true), 6));
        r2.addView(tile("☎", "Telefon", "Ingen telefon ansluten", v -> openPackage("com.android.car.dialer"), 0), margins(weight(1, true), 6));
        r2.addView(vehicleTile("Fordon"), margins(weight(1, true), 6));
        r2.addView(tile("▦", "Appar", "Alla appar", v -> showApps(), 0), margins(weight(1, true), 6));
        root.addView(r1, weight(1.1f, false));
        root.addView(r2, weight(1, false));
        LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(-1, -2);
        lp.setMargins(dp(6), dp(4), dp(6), 0);
        root.addView(vehicleBar(), lp);
        return root;
    }

    /** Smal meny till vänster och breda rutor i rad (BMW-inspirerat). */
    private View buildLeftMenu() {
        LinearLayout root = row();
        LinearLayout menu = column();
        menu.setGravity(Gravity.CENTER_HORIZONTAL);
        menu.setBackgroundColor(alpha(theme.surface, 235));
        menu.setPadding(0, dp(12), 0, dp(12));
        String[][] items = {{"♫", "Media"}, {"☎", "Telefon"}, {"🧭", "Navigation"}, {"⚙", "Bil"}, {"▦", "Appar"}};
        for (String[] it : items) {
            LinearLayout b = column();
            b.setGravity(Gravity.CENTER);
            TextView ic = text(it[0], 22, theme.accent, false);
            TextView lb = text(it[1], 11, theme.muted, false);
            ic.setGravity(Gravity.CENTER);
            lb.setGravity(Gravity.CENTER);
            b.addView(ic, new LinearLayout.LayoutParams(-1, -2));
            b.addView(lb, new LinearLayout.LayoutParams(-1, -2));
            final String what = it[1];
            b.setOnClickListener(v -> menuAction(what));
            menu.addView(b, new LinearLayout.LayoutParams(-1, 0, 1));
        }
        root.addView(menu, new LinearLayout.LayoutParams(dp(92), -1));

        LinearLayout main = column();
        main.setPadding(dp(10), 0, dp(10), dp(10));
        main.addView(statusBar(false));
        LinearLayout tiles = row();
        tiles.addView(tile("🧭", "Navigation", "Karta och vägvisning", v -> openNav(), 0), margins(weight(1.6f, true), 6));
        tiles.addView(tile("♫", "Media", "Cyberdrome Radio", v -> openPackage("se.cyberdrome.radio"), 0), margins(weight(1, true), 6));
        tiles.addView(vehicleTile("Fordonsstatus"), margins(weight(1, true), 6));
        main.addView(tiles, weight(1, false));
        root.addView(main, new LinearLayout.LayoutParams(0, -1, 1));
        return root;
    }

    /** Staplade rutor för stående skärm (Volvo-inspirerat). */
    private View buildPortrait() {
        LinearLayout root = column();
        root.setPadding(dp(10), 0, dp(10), dp(10));
        root.addView(statusBar(true));
        root.addView(tile("🧭", "Navigation", "Tryck för att öppna kartan", v -> openNav(), 0), margins(weight(2, false), 5));
        root.addView(tile("♫", "Media", "Cyberdrome Radio", v -> openPackage("se.cyberdrome.radio"), 0), margins(weight(1, false), 5));
        root.addView(tile("☎", "Telefon", "Ingen telefon ansluten", v -> openPackage("com.android.car.dialer"), 0), margins(weight(1, false), 5));
        root.addView(tile("▦", "Senaste app", "Alla appar", v -> showApps(), 0), margins(weight(1, false), 5));
        LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(-1, -2);
        lp.setMargins(dp(5), dp(5), dp(5), 0);
        root.addView(vehicleBar(), lp);
        return root;
    }

    /** Delad startskärm med tre fält (Toyota-inspirerat). */
    private View buildSplit() {
        LinearLayout root = column();
        root.setPadding(dp(8), 0, dp(8), dp(8));
        root.addView(statusBar(false));
        LinearLayout panes = row();
        panes.addView(tile("🧭", "Karta", "Öppna navigationen", v -> openNav(), 0), margins(weight(1.5f, true), 5));
        panes.addView(tile("♫", "Ljud", "Cyberdrome Radio", v -> openPackage("se.cyberdrome.radio"), 0), margins(weight(1, true), 5));
        panes.addView(vehicleTile("Info"), margins(weight(1, true), 5));
        root.addView(panes, weight(1, false));
        LinearLayout buttons = row();
        String[][] items = {{"⌂", "Hem"}, {"🧭", "Karta"}, {"♫", "Ljud"}, {"☎", "Telefon"}, {"▦", "Appar"}};
        for (String[] it : items) {
            TextView b = text(it[0] + "  " + S.d(it[1]), 14, Color.WHITE, true);
            b.setGravity(Gravity.CENTER);
            b.setBackground(box(alpha(theme.accent, 200), theme.corner));
            final String what = it[1];
            b.setOnClickListener(v -> menuAction(what));
            buttons.addView(b, margins(new LinearLayout.LayoutParams(0, dp(44), 1), 4));
        }
        root.addView(buttons, new LinearLayout.LayoutParams(-1, -2));
        return root;
    }

    /** Tre stora fält och en flikrad med en färg per flik (Ford-inspirerat). */
    private View buildTabs() {
        int[] c = theme.tabColors != null && theme.tabColors.length >= 6 ? theme.tabColors
                : new int[]{theme.accent, theme.accent, theme.accent, theme.accent, theme.accent, theme.accent};
        LinearLayout root = column();
        root.setPadding(dp(8), 0, dp(8), 0);
        root.addView(statusBar(false));
        LinearLayout panes = row();
        panes.addView(tile("♫", "Ljud", "Cyberdrome Radio", v -> openPackage("se.cyberdrome.radio"), c[0]), margins(weight(1, true), 5));
        panes.addView(tile("🧭", "Navigation", "Öppna kartan", v -> openNav(), c[3]), margins(weight(1, true), 5));
        panes.addView(tile("☎", "Telefon", "Ingen telefon ansluten", v -> openPackage("com.android.car.dialer"), c[2]), margins(weight(1, true), 5));
        root.addView(panes, weight(1, false));

        LinearLayout tabs = row();
        tabs.setBackgroundColor(theme.surface);
        String[] names = {"Ljud", "Klimat", "Telefon", "Navigation", "Appar", "Inställn."};
        for (int i = 0; i < names.length; i++) {
            LinearLayout tab = column();
            tab.setGravity(Gravity.CENTER);
            View line = new View(this);
            line.setBackgroundColor(c[i]);
            tab.addView(line, new LinearLayout.LayoutParams(-1, dp(4)));
            TextView t = text(names[i], 14, Color.WHITE, true);
            t.setGravity(Gravity.CENTER);
            tab.addView(t, new LinearLayout.LayoutParams(-1, 0, 1));
            final String what = names[i];
            tab.setOnClickListener(v -> menuAction(what));
            tabs.addView(tab, new LinearLayout.LayoutParams(0, -1, 1));
        }
        root.addView(tabs, new LinearLayout.LayoutParams(-1, dp(58)));
        return root;
    }

    // ---------------- Åtgärder ----------------

    private void menuAction(String what) {
        switch (what) {
            case "Media": case "Ljud": openPackage("se.cyberdrome.radio"); break;
            case "Telefon": openPackage("com.android.car.dialer"); break;
            case "Navigation": case "Karta": openNav(); break;
            case "Appar": showApps(); break;
            case "Bil": case "Klimat": showVehicle(); break;
            case "Inställn.": openPackage("com.android.car.settings"); break;
            default: break;
        }
    }

    private void openNav() {
        for (String p : new String[]{"com.google.android.apps.maps", "app.organicmaps", "net.osmand.plus"})
            if (openPackage(p)) return;
        toast("Ingen kartapp installerad – installera Organic Maps från Cyberdrome.");
    }

    private boolean openPackage(String pkg) {
        Intent i = getPackageManager().getLaunchIntentForPackage(pkg);
        if (i == null) {
            if (pkg.equals("se.cyberdrome.radio")) toast("Cyberdrome Radio är inte installerad.");
            return false;
        }
        startActivity(i);
        return true;
    }

    private void toast(String s) { android.widget.Toast.makeText(this, S.d(s), android.widget.Toast.LENGTH_SHORT).show(); }

    private void showVehicle() {
        new AlertDialog.Builder(this)
                .setTitle(S.d("Fordon (OBD)"))
                .setMessage(vehicleText())
                .setPositiveButton("OK", null)
                .show();
    }

    private void showApps() {
        Intent main = new Intent(Intent.ACTION_MAIN).addCategory(Intent.CATEGORY_LAUNCHER);
        PackageManager pm = getPackageManager();
        List<ResolveInfo> apps = pm.queryIntentActivities(main, 0);
        Collections.sort(apps, (a, b) -> a.loadLabel(pm).toString().compareToIgnoreCase(b.loadLabel(pm).toString()));
        LinearLayout list = column();
        list.setPadding(dp(8), dp(8), dp(8), dp(8));
        final AlertDialog[] dlg = new AlertDialog[1];
        for (ResolveInfo ri : apps) {
            if (ri.activityInfo.packageName.equals(getPackageName())) continue;
            TextView t = text(ri.loadLabel(pm).toString(), 18, Color.BLACK, false);
            t.setPadding(dp(12), dp(12), dp(12), dp(12));
            final ComponentName cn = new ComponentName(ri.activityInfo.packageName, ri.activityInfo.name);
            t.setOnClickListener(v -> {
                startActivity(new Intent(Intent.ACTION_MAIN).setComponent(cn).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));
                if (dlg[0] != null) dlg[0].dismiss();
            });
            list.addView(t);
        }
        ScrollView sv = new ScrollView(this);
        sv.addView(list);
        dlg[0] = new AlertDialog.Builder(this).setTitle(S.d("Appar")).setView(sv).setNegativeButton(S.d("Stäng"), null).show();
    }

    // ---------------- OBD ----------------

    private String vehicleText() {
        return S.f(R.string.vehicle_text, (speed < 0 ? "–" : speed + " km/h")
                , (rpm < 0 ? "–" : rpm + " rpm")
                , (coolant == Integer.MIN_VALUE ? "–" : coolant + " °C")
                , (fuel < 0 ? "–" : fuel + " %"))
                + "\n\n" + obdStatus;
    }

    private void updateValues() {
        for (TextView t : speedViews) t.setText(speed < 0 ? "– km/h" : speed + " km/h");
        for (TextView t : rpmViews) t.setText(rpm < 0 ? "– rpm" : rpm + " rpm");
        for (TextView t : coolantViews) t.setText(coolant == Integer.MIN_VALUE ? S.d("Kylv. –") : S.f(R.string.coolant_value, coolant));
        for (TextView t : fuelViews) t.setText(fuel < 0 ? S.d("Bränsle –") : S.f(R.string.fuel_value, fuel));
        for (TextView t : obdStatusViews) t.setText(obdStatus);
        String dt = dtcs.length == 0 ? "" : S.d("⚠ Felkod: ") + android.text.TextUtils.join(", ", dtcs);
        for (TextView t : dtcViews) { t.setText(dt); t.setVisibility(dt.isEmpty() ? View.GONE : View.VISIBLE); }
    }

    @Override
    public void onValues(int speedKmh, int rpm, int coolantC, int fuelPercent) {
        MAIN.post(() -> {
            speed = speedKmh; this.rpm = rpm; coolant = coolantC; fuel = fuelPercent;
            updateValues();
        });
    }

    @Override
    public void onDtcs(String[] codes) {
        MAIN.post(() -> { dtcs = codes; updateValues(); });
    }

    @Override
    public void onStatus(String text, boolean connected) {
        MAIN.post(() -> {
            obdStatus = connected ? S.d("● OBD ansluten") : text;
            if (!connected) { speed = -1; rpm = -1; coolant = Integer.MIN_VALUE; fuel = -1; }
            updateValues();
        });
    }

    /** Stiliserad kartbild: gator i temats färger och en pil för bilen. Helt egen ritning. */
    private static final class MapPreviewView extends View {
        private final Theme t;
        private final Paint p = new Paint(Paint.ANTI_ALIAS_FLAG);

        MapPreviewView(Context c, Theme t) { super(c); this.t = t; }

        @Override
        protected void onDraw(Canvas canvas) {
            int w = getWidth(), h = getHeight();
            if (w < 10 || h < 10) return;
            float r = 12 * getResources().getDisplayMetrics().density;
            p.setStyle(Paint.Style.FILL);
            p.setColor(alpha(t.background, 255));
            canvas.drawRoundRect(0, 0, w, h, r, r, p);
            // kvarter
            p.setColor(alpha(t.muted, 28));
            for (int y = 0; y < h; y += 70) for (int x = 0; x < w; x += 90) canvas.drawRect(x + 8, y + 8, x + 82, y + 62, p);
            // vatten
            p.setColor(alpha(t.accent, 45));
            canvas.drawCircle(w * 0.85f, h * 0.9f, Math.min(w, h) * 0.45f, p);
            // vägar
            p.setStyle(Paint.Style.STROKE);
            p.setStrokeCap(Paint.Cap.ROUND);
            p.setColor(alpha(t.muted, 90));
            p.setStrokeWidth(Math.max(4, h / 30f));
            canvas.drawLine(0, h * 0.62f, w, h * 0.48f, p);
            canvas.drawLine(w * 0.35f, 0, w * 0.45f, h, p);
            // rutt
            p.setColor(t.accent);
            p.setStrokeWidth(Math.max(6, h / 20f));
            canvas.drawLine(w * 0.42f, h * 0.85f, w * 0.40f, h * 0.57f, p);
            canvas.drawLine(w * 0.40f, h * 0.57f, w * 0.95f, h * 0.49f, p);
            // bilen
            p.setStyle(Paint.Style.FILL);
            p.setColor(Color.WHITE);
            android.graphics.Path arrow = new android.graphics.Path();
            float cx = w * 0.42f, cy = h * 0.8f, s = Math.max(10, h / 9f);
            arrow.moveTo(cx, cy - s);
            arrow.lineTo(cx + s * 0.7f, cy + s * 0.7f);
            arrow.lineTo(cx, cy + s * 0.35f);
            arrow.lineTo(cx - s * 0.7f, cy + s * 0.7f);
            arrow.close();
            canvas.drawPath(arrow, p);
        }
    }

    /** Bakgrund ritad av oss: toning, linjer eller prickar i temats färger. */
    private static final class WallpaperView extends View {
        private final Theme t;
        private final Paint p = new Paint(Paint.ANTI_ALIAS_FLAG);

        WallpaperView(Context c, Theme t) { super(c); this.t = t; }

        @Override
        protected void onDraw(Canvas canvas) {
            int w = getWidth(), h = getHeight();
            canvas.drawColor(t.background);
            if ("plain".equals(t.wallpaper)) return;
            if ("gradient".equals(t.wallpaper)) {
                p.setShader(new LinearGradient(0, 0, w, h, alpha(t.accent, 60), alpha(t.background, 0), Shader.TileMode.CLAMP));
                canvas.drawRect(0, 0, w, h, p);
                p.setShader(null);
            } else if ("lines".equals(t.wallpaper)) {
                p.setColor(alpha(t.accent, 26));
                p.setStrokeWidth(2);
                for (int x = -h; x < w; x += 46) canvas.drawLine(x, h, x + h, 0, p);
            } else if ("dots".equals(t.wallpaper)) {
                p.setColor(alpha(t.accent, 40));
                for (int y = 20; y < h; y += 36) for (int x = 20; x < w; x += 36) canvas.drawCircle(x, y, 2.5f, p);
            }
        }
    }
}
