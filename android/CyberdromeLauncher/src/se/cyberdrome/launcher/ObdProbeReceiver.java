// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

package se.cyberdrome.launcher;

import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;
import android.util.Log;

import java.io.InputStream;
import java.io.OutputStream;
import java.net.InetSocketAddress;
import java.net.Socket;
import java.nio.charset.StandardCharsets;

/**
 * Utvecklarverktyg: skickar OBD-kommandon inifrån Android till OBD-porten och skriver svaren i logcat
 * (tagg "CyberdromeProbe") och som broadcast-resultat. Används för att kontrollera simulatorn från Android:
 *   am broadcast -n se.cyberdrome.launcher/.ObdProbeReceiver -a se.cyberdrome.launcher.PROBE --es cmds "ATZ;ATE0;ATSH7C0;221234"
 * Valfritt: --es host 10.0.2.2 --ei port 35000
 */
public final class ObdProbeReceiver extends BroadcastReceiver {
    static final String TAG = "CyberdromeProbe";

    @Override
    public void onReceive(Context ctx, Intent intent) {
        final String cmds = intent.getStringExtra("cmds");
        final String host = intent.hasExtra("host") ? intent.getStringExtra("host") : "10.0.2.2";
        final int port = intent.getIntExtra("port", 35000);
        if (cmds == null) { setResultData("FEL: --es cmds saknas"); return; }
        final PendingResult pending = goAsync();
        new Thread(() -> {
            StringBuilder all = new StringBuilder();
            try (Socket s = new Socket()) {
                s.connect(new InetSocketAddress(host, port), 3000);
                s.setSoTimeout(5000);
                OutputStream out = s.getOutputStream();
                InputStream in = s.getInputStream();
                for (String c : cmds.split(";")) {
                    c = c.trim();
                    if (c.isEmpty()) continue;
                    out.write((c + "\r").getBytes(StandardCharsets.US_ASCII));
                    out.flush();
                    StringBuilder r = new StringBuilder();
                    int b;
                    while ((b = in.read()) >= 0 && b != '>') r.append((char) b);
                    String reply = r.toString().replace("\r", " ").replace("\n", " ").trim().replaceAll(" +", " ");
                    Log.i(TAG, c + " => " + reply);
                    all.append(c).append(" => ").append(reply).append(" | ");
                }
            } catch (Exception ex) {
                Log.w(TAG, "fel: " + ex);
                all.append("FEL: ").append(ex.getMessage());
            }
            pending.setResultData(all.toString());
            pending.finish();
        }).start();
    }
}
