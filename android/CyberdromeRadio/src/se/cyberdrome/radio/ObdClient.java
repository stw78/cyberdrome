// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

package se.cyberdrome.radio;

import java.io.InputStream;
import java.io.OutputStream;
import java.net.InetSocketAddress;
import java.net.Socket;
import java.nio.charset.StandardCharsets;

/**
 * Enkel ELM327-klient över Wi-Fi/TCP. Standard: 10.0.2.2:35000 (datorn sett från Android-emulatorn).
 * Läser hastighet (010D) och varvtal (010C) ungefär två gånger per sekund och återansluter av sig själv.
 */
final class ObdClient {
    interface Listener {
        void onValues(int speedKmh, int rpm);
        void onStatus(String text, boolean connected);
    }

    private final Listener listener;
    private volatile String host = "10.0.2.2";
    private volatile int port = 35000;
    private volatile boolean running;
    private volatile Socket socket;
    private Thread thread;

    ObdClient(Listener listener) { this.listener = listener; }

    String address() { return host + ":" + port; }

    /** "värd:port", t.ex. "10.0.2.2:35000". Kopplar om. */
    void setAddress(String hostPort) {
        String[] p = hostPort.trim().split(":");
        host = p[0];
        if (p.length > 1) {
            try { port = Integer.parseInt(p[1]); } catch (NumberFormatException ignored) { }
        }
        closeSocket();
    }

    synchronized void start() {
        if (running) return;
        running = true;
        thread = new Thread(this::loop, "ObdClient");
        thread.start();
    }

    synchronized void stop() {
        running = false;
        closeSocket();
    }

    private void closeSocket() {
        Socket s = socket;
        if (s != null) try { s.close(); } catch (Exception ignored) { }
    }

    private void loop() {
        while (running) {
            try (Socket s = new Socket()) {
                listener.onStatus(S.f(R.string.connecting_to, address()), false);
                s.connect(new InetSocketAddress(host, port), 3000);
                s.setSoTimeout(3000);
                socket = s;
                InputStream in = s.getInputStream();
                OutputStream out = s.getOutputStream();

                // Robust start: ATZ, vänta in prompten, sedan inställningar.
                command(in, out, "ATZ");
                command(in, out, "ATE0");
                command(in, out, "ATH0");
                command(in, out, "ATS1");
                listener.onStatus(S.f(R.string.connected_to, address()), true);

                while (running) {
                    int speed = parse(command(in, out, "010D"), "41 0D", 1);
                    int rpmRaw = parse(command(in, out, "010C"), "41 0C", 2);
                    listener.onValues(speed, rpmRaw < 0 ? -1 : rpmRaw / 4);
                    Thread.sleep(450);
                }
            } catch (Exception e) {
                socket = null;
                if (!running) break;
                listener.onStatus(S.f(R.string.no_obd_retry, address()), false);
                try { Thread.sleep(2000); } catch (InterruptedException ignored) { }
            }
        }
        listener.onStatus(S.d("OBD avstängd"), false);
    }

    /** Skickar ett kommando och läser svaret fram till prompten '>'. */
    static String command(InputStream in, OutputStream out, String cmd) throws Exception {
        out.write((cmd + "\r").getBytes(StandardCharsets.US_ASCII));
        out.flush();
        StringBuilder sb = new StringBuilder();
        int c;
        while ((c = in.read()) != -1) {
            if (c == '>') break;
            sb.append((char) c);
        }
        if (c == -1) throw new java.io.EOFException();
        return sb.toString();
    }

    /** Hittar t.ex. "41 0D 36" i svaret och läser 1 eller 2 databyte. -1 om det saknas. */
    static int parse(String reply, String prefix, int bytes) {
        String r = reply.replace("\r", " ").replace("\n", " ").toUpperCase();
        int i = r.indexOf(prefix);
        if (i < 0) return -1;
        String[] p = r.substring(i + prefix.length()).trim().split("\\s+");
        if (p.length < bytes) return -1;
        try {
            int v = 0;
            for (int k = 0; k < bytes; k++) v = (v << 8) | Integer.parseInt(p[k], 16);
            return v;
        } catch (NumberFormatException e) {
            return -1;
        }
    }
}
