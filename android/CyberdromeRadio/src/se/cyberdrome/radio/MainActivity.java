// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

package se.cyberdrome.radio;

import android.app.Activity;
import android.content.Intent;
import android.graphics.Color;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.os.Bundle;
import android.text.InputType;
import android.view.Gravity;
import android.view.View;
import android.view.ViewGroup;
import android.widget.Button;
import android.widget.EditText;
import android.widget.LinearLayout;
import android.widget.Switch;
import android.widget.TextView;

/** Appens egen skärm: stationslista, stor stationsruta, knappar, OBD-data och volym efter fart. */
public final class MainActivity extends Activity implements RadioEngine.Listener {
    private static final int BG = Color.rgb(15, 23, 42);
    private static final int CARD = Color.rgb(30, 41, 59);
    private static final int ACCENT = Color.rgb(37, 99, 235);
    private static final int TEXT = Color.WHITE;
    private static final int MUTED = Color.rgb(148, 163, 184);

    private RadioEngine engine;
    private final Button[] stationButtons = new Button[Station.ALL.length];
    private TextView title, freq, state, speed, rpm, obdStatus, gala;
    private Button playButton;
    private Switch galaSwitch;
    private EditText address;

    @Override
    protected void onCreate(Bundle saved) {
        super.onCreate(saved);
        S.init(this);
        engine = RadioEngine.get(this);
        startService(new Intent(this, RadioService.class));
        setContentView(build());
        engine.addListener(this);
        onChanged();
    }

    @Override
    protected void onDestroy() {
        engine.removeListener(this);
        super.onDestroy();
    }

    // ---------------- Layout ----------------

    private int dp(int v) { return Math.round(v * getResources().getDisplayMetrics().density); }

    private GradientDrawable box(int color, int radius) {
        GradientDrawable g = new GradientDrawable();
        g.setColor(color);
        g.setCornerRadius(dp(radius));
        return g;
    }

    private TextView text(String s, int sp, int color, boolean bold) {
        TextView t = new TextView(this);
        t.setText(S.d(s));
        t.setTextSize(sp);
        t.setTextColor(color);
        if (bold) t.setTypeface(Typeface.DEFAULT_BOLD);
        return t;
    }

    private Button button(String s, View.OnClickListener click) {
        Button b = new Button(this);
        b.setText(S.d(s));
        b.setAllCaps(false);
        b.setTextSize(18);
        b.setTextColor(TEXT);
        b.setBackground(box(CARD, 12));
        b.setOnClickListener(click);
        return b;
    }

    private View build() {
        LinearLayout root = new LinearLayout(this);
        root.setOrientation(LinearLayout.HORIZONTAL);
        root.setBackgroundColor(BG);
        root.setPadding(dp(16), dp(16), dp(16), dp(16));

        // Vänster: stationer
        LinearLayout list = new LinearLayout(this);
        list.setOrientation(LinearLayout.VERTICAL);
        list.addView(text("Stationer", 16, MUTED, true));
        for (int i = 0; i < Station.ALL.length; i++) {
            final int idx = i;
            Button b = button(Station.ALL[i].name + "\n" + Station.ALL[i].frequency, v -> { engine.select(idx); engine.play(); });
            b.setGravity(Gravity.START | Gravity.CENTER_VERTICAL);
            b.setSingleLine(false);
            b.setMaxLines(2);
            b.setTextSize(16);
            LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f);
            lp.topMargin = dp(8);
            list.addView(b, lp);
            stationButtons[i] = b;
        }
        root.addView(list, new LinearLayout.LayoutParams(dp(220), ViewGroup.LayoutParams.MATCH_PARENT));

        // Höger: stor ruta + knappar + OBD
        LinearLayout right = new LinearLayout(this);
        right.setOrientation(LinearLayout.VERTICAL);
        right.setPadding(dp(16), 0, 0, 0);

        LinearLayout tile = new LinearLayout(this);
        tile.setOrientation(LinearLayout.VERTICAL);
        tile.setGravity(Gravity.CENTER);
        tile.setBackground(box(ACCENT, 20));
        title = text("", 40, TEXT, true);
        freq = text("", 20, TEXT, false);
        state = text("", 16, TEXT, false);
        for (TextView t : new TextView[]{title, freq, state}) {
            t.setGravity(Gravity.CENTER);
            tile.addView(t, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));
        }
        right.addView(tile, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, 0, 1.2f));

        LinearLayout controls = new LinearLayout(this);
        controls.setOrientation(LinearLayout.HORIZONTAL);
        controls.setPadding(0, dp(10), 0, dp(10));
        Button prev = button("⏮  Föregående", v -> engine.previous());
        playButton = button("▶  Spela", v -> engine.togglePlay());
        Button next = button("Nästa  ⏭", v -> engine.next());
        LinearLayout.LayoutParams third = new LinearLayout.LayoutParams(0, dp(64), 1f);
        third.setMargins(0, 0, dp(8), 0);
        controls.addView(prev, third);
        controls.addView(playButton, third);
        controls.addView(next, new LinearLayout.LayoutParams(0, dp(64), 1f));
        right.addView(controls);

        // OBD-ruta
        LinearLayout obdBox = new LinearLayout(this);
        obdBox.setOrientation(LinearLayout.VERTICAL);
        obdBox.setBackground(box(CARD, 16));
        obdBox.setPadding(dp(14), dp(10), dp(14), dp(10));

        LinearLayout values = new LinearLayout(this);
        values.setOrientation(LinearLayout.HORIZONTAL);
        speed = text("– km/h", 30, TEXT, true);
        rpm = text("– rpm", 30, TEXT, true);
        values.addView(speed, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));
        values.addView(rpm, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));
        obdBox.addView(values);

        obdStatus = text("", 13, MUTED, false);
        obdBox.addView(obdStatus);

        LinearLayout galaRow = new LinearLayout(this);
        galaRow.setOrientation(LinearLayout.HORIZONTAL);
        galaRow.setGravity(Gravity.CENTER_VERTICAL);
        galaSwitch = new Switch(this);
        galaSwitch.setText(S.d("Volym efter fart"));
        galaSwitch.setTextColor(TEXT);
        galaSwitch.setTextSize(16);
        galaSwitch.setOnCheckedChangeListener((b, on) -> { if (on != engine.galaEnabled()) engine.setGalaEnabled(on); });
        gala = text("", 18, TEXT, true);
        gala.setPadding(dp(16), 0, 0, 0);
        galaRow.addView(galaSwitch);
        galaRow.addView(gala);
        obdBox.addView(galaRow);

        LinearLayout addrRow = new LinearLayout(this);
        addrRow.setOrientation(LinearLayout.HORIZONTAL);
        addrRow.setGravity(Gravity.CENTER_VERTICAL);
        TextView lbl = text("OBD-adress:", 14, MUTED, false);
        address = new EditText(this);
        address.setTextColor(TEXT);
        address.setTextSize(14);
        address.setSingleLine(true);
        address.setInputType(InputType.TYPE_CLASS_TEXT | InputType.TYPE_TEXT_VARIATION_URI);
        address.setText(engine.obdAddress());
        Button connect = button("Anslut", v -> engine.setObdAddress(address.getText().toString()));
        connect.setTextSize(14);
        addrRow.addView(lbl);
        addrRow.addView(address, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));
        addrRow.addView(connect);
        obdBox.addView(addrRow);

        right.addView(obdBox, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));
        root.addView(right, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MATCH_PARENT, 1f));
        return root;
    }

    // ---------------- Uppdatering ----------------

    @Override
    public void onChanged() {
        Station s = engine.station();
        title.setText(s.name);
        freq.setText(s.frequency);
        state.setText(S.d(engine.isPlaying() ? "Spelar" : "Pausad"));
        playButton.setText(S.d(engine.isPlaying() ? "⏸  Pausa" : "▶  Spela"));
        for (int i = 0; i < stationButtons.length; i++)
            stationButtons[i].setBackground(box(i == engine.index() ? ACCENT : CARD, 12));

        speed.setText(engine.speed() < 0 ? "– km/h" : engine.speed() + " km/h");
        rpm.setText(engine.rpm() < 0 ? "– rpm" : engine.rpm() + " rpm");
        obdStatus.setText(S.d(engine.obdStatus()));
        if (galaSwitch.isChecked() != engine.galaEnabled()) galaSwitch.setChecked(engine.galaEnabled());
        gala.setText(engine.galaEnabled() ? S.f(R.string.gala_on, engine.galaSteps()) : S.d("Volym efter fart: av"));
    }
}
