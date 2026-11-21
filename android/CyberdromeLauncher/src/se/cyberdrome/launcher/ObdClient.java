// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

package se.cyberdrome.launcher;

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
        void onValues(int speedKmh, int rpm, int coolantC, int fuelPercent);
        void onStatus(String text, boolean connected);
        void onDtcs(String[] codes);
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
                listener.onStatus("Ansluter till " + address() + " …", false);
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
                listener.onStatus("Ansluten till " + address(), true);

                int loops = 0;
                while (running) {
                    if (loops++ % 12 == 0) listener.onDtcs(parseDtcs(command(in, out, "03")));
                    int speed = parse(command(in, out, "010D"), "41 0D", 1);
                    int rpmRaw = parse(command(in, out, "010C"), "41 0C", 2);
                    int coolRaw = parse(command(in, out, "0105"), "41 05", 1);
                    int fuelRaw = parse(command(in, out, "012F"), "41 2F", 1);
                    listener.onValues(speed, rpmRaw < 0 ? -1 : rpmRaw / 4, coolRaw < 0 ? Integer.MIN_VALUE : coolRaw - 40, fuelRaw < 0 ? -1 : Math.round(fuelRaw * 100f / 255));
                    Thread.sleep(400);
                }
            } catch (Exception e) {
                socket = null;
                if (!running) break;
                listener.onStatus("Ingen OBD-anslutning (" + address() + ") – försöker igen …", false);
                try { Thread.sleep(2000); } catch (InterruptedException ignored) { }
            }
        }
        listener.onStatus("OBD avstängd", false);
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

    /** Felkoder ur ett mode 03-svar, t.ex. "43 01 03 00" → P0300. */
    static String[] parseDtcs(String reply) {
        String r = reply.replace("\r", " ").replace("\n", " ").toUpperCase();
        int i = r.indexOf("43");
        if (i < 0) return new String[0];
        String[] p = r.substring(i + 2).trim().split("\\s+");
        java.util.List<String> codes = new java.util.ArrayList<>();
        int start = p.length % 2 == 1 ? 1 : 0; // CAN: första byten är antal
        for (int k = start; k + 1 < p.length; k += 2) {
            try {
                int v = (Integer.parseInt(p[k], 16) << 8) | Integer.parseInt(p[k + 1], 16);
                if (v == 0) continue;
                codes.add("PCBU".charAt((v >> 14) & 3) + String.format("%04X", v & 0x3FFF));
            } catch (NumberFormatException e) { break; }
        }
        return codes.toArray(new String[0]);
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
