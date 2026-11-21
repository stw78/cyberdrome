// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

package se.cyberdrome.radio;

import android.content.Context;
import android.media.AudioAttributes;
import android.media.AudioFocusRequest;
import android.media.AudioManager;
import android.media.MediaMetadata;
import android.media.session.MediaSession;
import android.media.session.PlaybackState;
import android.os.Handler;
import android.os.Looper;

import java.util.concurrent.CopyOnWriteArrayList;

/**
 * Appens hjärta (en instans per process): stationer, uppspelning, MediaSession (rattknappar
 * och bilens mediagränssnitt), OBD-data och volym efter fart.
 */
final class RadioEngine implements ObdClient.Listener {
    interface Listener { void onChanged(); }

    /** Volym efter fart: +1 steg per 30 km/h, högst +5. */
    static final int KMH_PER_STEP = 30;
    static final int MAX_STEPS = 5;
    static final float BASE_VOLUME = 0.35f;
    static final float VOLUME_PER_STEP = 0.12f;

    private static RadioEngine instance;

    static synchronized RadioEngine get(Context ctx) {
        if (instance == null) instance = new RadioEngine(ctx.getApplicationContext());
        return instance;
    }

    private final Context ctx;
    private final Handler main = new Handler(Looper.getMainLooper());
    private final ToneSynth synth = new ToneSynth();
    private final ObdClient obd = new ObdClient(this);
    private final CopyOnWriteArrayList<Listener> listeners = new CopyOnWriteArrayList<>();
    private final MediaSession session;
    private final AudioManager audio;
    private AudioFocusRequest focus;

    private int index;
    private boolean playing;
    private boolean galaEnabled = true;
    private int speed = -1, rpm = -1;
    private String obdStatus = "OBD startar …";
    private boolean obdConnected;

    private RadioEngine(Context ctx) {
        this.ctx = ctx;
        audio = (AudioManager) ctx.getSystemService(Context.AUDIO_SERVICE);
        session = new MediaSession(ctx, "CyberdromeRadio");
        session.setCallback(new MediaSession.Callback() {
            @Override public void onPlay() { play(); }
            @Override public void onPause() { pause(); }
            @Override public void onStop() { pause(); }
            @Override public void onSkipToNext() { next(); }
            @Override public void onSkipToPrevious() { previous(); }
            @Override public void onPlayFromMediaId(String mediaId, android.os.Bundle extras) {
                for (int i = 0; i < Station.ALL.length; i++)
                    if (Station.ALL[i].id.equals(mediaId)) { select(i); play(); return; }
            }
        });
        session.setActive(true);
        updateSession();
        obd.start();
    }

    MediaSession.Token token() { return session.getSessionToken(); }

    // ---------------- Lyssnare ----------------

    void addListener(Listener l) { listeners.add(l); }
    void removeListener(Listener l) { listeners.remove(l); }

    private void changed() {
        updateSession();
        main.post(() -> { for (Listener l : listeners) l.onChanged(); });
    }

    // ---------------- Uppspelning ----------------

    Station station() { return Station.ALL[index]; }
    int index() { return index; }
    boolean isPlaying() { return playing; }

    void select(int i) {
        index = ((i % Station.ALL.length) + Station.ALL.length) % Station.ALL.length;
        synth.setStation(station());
        changed();
    }

    void next() { select(index + 1); }
    void previous() { select(index - 1); }

    void play() {
        if (playing) return;
        if (focus == null) {
            focus = new AudioFocusRequest.Builder(AudioManager.AUDIOFOCUS_GAIN)
                    .setAudioAttributes(new AudioAttributes.Builder()
                            .setUsage(AudioAttributes.USAGE_MEDIA)
                            .setContentType(AudioAttributes.CONTENT_TYPE_MUSIC).build())
                    .setOnAudioFocusChangeListener(change -> {
                        if (change == AudioManager.AUDIOFOCUS_LOSS) pause();
                    })
                    .build();
        }
        audio.requestAudioFocus(focus);
        try {
            synth.play(station());
        } catch (RuntimeException e) {
            // Ljudfel ska aldrig krascha appen.
            android.util.Log.e("CyberdromeRadio", "Kunde inte starta ljudet", e);
            audio.abandonAudioFocusRequest(focus);
            return;
        }
        playing = true;
        applyVolume();
        changed();
    }

    void pause() {
        if (!playing) return;
        synth.stop();
        playing = false;
        if (focus != null) audio.abandonAudioFocusRequest(focus);
        changed();
    }

    void togglePlay() { if (playing) pause(); else play(); }

    // ---------------- Volym efter fart ----------------

    boolean galaEnabled() { return galaEnabled; }

    void setGalaEnabled(boolean on) {
        galaEnabled = on;
        applyVolume();
        changed();
    }

    /** Antal volymsteg som läggs till för en viss fart. */
    static int stepsFor(int speedKmh) {
        if (speedKmh <= 0) return 0;
        return Math.min(MAX_STEPS, speedKmh / KMH_PER_STEP);
    }

    int galaSteps() { return galaEnabled ? stepsFor(speed) : 0; }

    float volume() { return Math.min(1f, BASE_VOLUME + galaSteps() * VOLUME_PER_STEP); }

    private void applyVolume() { synth.setVolume(volume()); }

    // ---------------- OBD ----------------

    int speed() { return speed; }
    int rpm() { return rpm; }
    String obdStatus() { return obdStatus; }
    boolean obdConnected() { return obdConnected; }
    String obdAddress() { return obd.address(); }
    void setObdAddress(String hostPort) { obd.setAddress(hostPort); }

    @Override public void onValues(int speedKmh, int rpm) {
        main.post(() -> {
            int oldSteps = galaSteps();
            this.speed = speedKmh;
            this.rpm = rpm;
            if (galaSteps() != oldSteps) applyVolume();
            changed();
        });
    }

    @Override public void onStatus(String text, boolean connected) {
        main.post(() -> {
            obdStatus = text;
            obdConnected = connected;
            if (!connected) { speed = -1; rpm = -1; applyVolume(); }
            changed();
        });
    }

    // ---------------- MediaSession ----------------

    private void updateSession() {
        Station s = station();
        session.setMetadata(new MediaMetadata.Builder()
                .putString(MediaMetadata.METADATA_KEY_MEDIA_ID, s.id)
                .putString(MediaMetadata.METADATA_KEY_TITLE, s.name)
                .putString(MediaMetadata.METADATA_KEY_ARTIST, s.frequency)
                .putString(MediaMetadata.METADATA_KEY_ALBUM, "Cyberdrome Radio")
                .build());
        session.setPlaybackState(new PlaybackState.Builder()
                .setActions(PlaybackState.ACTION_PLAY | PlaybackState.ACTION_PAUSE | PlaybackState.ACTION_PLAY_PAUSE
                        | PlaybackState.ACTION_SKIP_TO_NEXT | PlaybackState.ACTION_SKIP_TO_PREVIOUS
                        | PlaybackState.ACTION_PLAY_FROM_MEDIA_ID | PlaybackState.ACTION_STOP)
                .setState(playing ? PlaybackState.STATE_PLAYING : PlaybackState.STATE_PAUSED,
                        PlaybackState.PLAYBACK_POSITION_UNKNOWN, 1f)
                .build());
    }
}
