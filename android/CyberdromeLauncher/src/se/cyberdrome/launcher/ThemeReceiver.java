// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

package se.cyberdrome.launcher;

import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;
import android.util.Base64;

import java.nio.charset.StandardCharsets;

/**
 * Tar emot temabyte från Cyberdrome:
 *   --es id vw-golf8          (inbyggt tema)
 *   --es json64 &lt;base64&gt;     (eget tema: theme.json kodad som base64)
 */
public final class ThemeReceiver extends BroadcastReceiver {
    static final String PREFS = "theme";

    @Override
    public void onReceive(Context ctx, Intent intent) {
        String json64 = intent.getStringExtra("json64");
        String id = intent.getStringExtra("id");
        android.content.SharedPreferences.Editor e = ctx.getSharedPreferences(PREFS, Context.MODE_PRIVATE).edit();
        if (json64 != null) {
            String json = new String(Base64.decode(json64, Base64.DEFAULT), StandardCharsets.UTF_8);
            try {
                Theme.fromJson(json); // kontrollera att det går att läsa
                e.putString("json", json).remove("id");
            } catch (Exception ex) {
                setResultData("FEL: " + ex.getMessage());
                return;
            }
        } else if (id != null) {
            e.putString("id", id).remove("json");
        }
        // OBD-adressen sett inifrån Android (10.0.2.2 i emulatorn, WSL2-gatewayen i Cuttlefish).
        String obd = intent.getStringExtra("obd");
        if (obd != null) e.putString("obd", obd);
        e.apply();
        setResultData("OK");
        if (obd != null) HomeActivity.obdChanged(obd);
        HomeActivity.themeChanged();
    }

    /** Tema som gäller just nu. */
    static Theme current(Context ctx) {
        android.content.SharedPreferences p = ctx.getSharedPreferences(PREFS, Context.MODE_PRIVATE);
        String json = p.getString("json", null);
        if (json != null) {
            try { return Theme.fromJson(json); } catch (Exception ignored) { }
        }
        return Theme.builtIn(p.getString("id", "vw-golf8"));
    }
}
