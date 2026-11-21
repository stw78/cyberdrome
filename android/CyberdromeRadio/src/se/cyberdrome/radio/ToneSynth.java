// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

package se.cyberdrome.radio;

import android.media.AudioAttributes;
import android.media.AudioFormat;
import android.media.AudioTrack;

/** Spelar stationens jingel som mjuka sinustoner i en slinga, via AudioTrack. */
final class ToneSynth {
    private static final int RATE = 22050;

    private AudioTrack track;
    private Thread thread;
    private volatile boolean running;
    private volatile Station station;
    private volatile float volume = 0.4f;

    synchronized void play(Station s) {
        station = s;
        if (running) return;
        int min = AudioTrack.getMinBufferSize(RATE, AudioFormat.CHANNEL_OUT_MONO, AudioFormat.ENCODING_PCM_16BIT);
        // Bufferten måste vara ett helt antal ramar (2 byte per ram för 16-bitars mono).
        int size = Math.max(min, RATE);
        size -= size % 2;
        track = new AudioTrack.Builder()
                .setAudioAttributes(new AudioAttributes.Builder()
                        .setUsage(AudioAttributes.USAGE_MEDIA)
                        .setContentType(AudioAttributes.CONTENT_TYPE_MUSIC)
                        .build())
                .setAudioFormat(new AudioFormat.Builder()
                        .setEncoding(AudioFormat.ENCODING_PCM_16BIT)
                        .setSampleRate(RATE)
                        .setChannelMask(AudioFormat.CHANNEL_OUT_MONO)
                        .build())
                .setBufferSizeInBytes(size)
                .setTransferMode(AudioTrack.MODE_STREAM)
                .build();
        track.setVolume(volume);
        track.play();
        running = true;
        thread = new Thread(this::loop, "ToneSynth");
        thread.start();
    }

    synchronized void stop() {
        running = false;
        if (thread != null) {
            try { thread.join(500); } catch (InterruptedException ignored) { }
            thread = null;
        }
        if (track != null) {
            try { track.stop(); } catch (Exception ignored) { }
            track.release();
            track = null;
        }
    }

    void setStation(Station s) { station = s; }

    void setVolume(float v) {
        volume = Math.max(0f, Math.min(1f, v));
        AudioTrack t = track;
        if (t != null) t.setVolume(volume);
    }

    private void loop() {
        int note = 0;
        while (running) {
            Station s = station;
            double f = s.melody[note % s.melody.length];
            int n = RATE * s.noteMs / 1000;
            short[] buf = new short[n];
            for (int i = 0; i < n; i++) {
                double t = (double) i / RATE;
                // mjuk in-/uttoning så att det inte knäpper mellan tonerna
                double env = Math.min(1.0, Math.min(i, n - i) / (RATE * 0.02));
                double v = Math.sin(2 * Math.PI * f * t) * 0.6 + Math.sin(2 * Math.PI * f * 2 * t) * 0.15;
                buf[i] = (short) (v * env * 9000);
            }
            AudioTrack tr = track;
            if (tr == null) break;
            tr.write(buf, 0, n);
            note++;
        }
    }
}
