// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

package se.cyberdrome.radio;

import android.media.MediaDescription;
import android.media.browse.MediaBrowser;
import android.os.Bundle;
import android.service.media.MediaBrowserService;

import java.util.ArrayList;
import java.util.List;

/**
 * Gör stationerna synliga i bilens eget mediagränssnitt (Android Automotive Media Center)
 * och delar samma MediaSession som appen, så att rattknapparna fungerar.
 */
public final class RadioService extends MediaBrowserService {
    private static final String ROOT = "root";

    @Override
    public void onCreate() {
        super.onCreate();
        S.init(this);
        setSessionToken(RadioEngine.get(this).token());
    }

    @Override
    public BrowserRoot onGetRoot(String clientPackageName, int clientUid, Bundle rootHints) {
        return new BrowserRoot(ROOT, null);
    }

    @Override
    public void onLoadChildren(String parentId, Result<List<MediaBrowser.MediaItem>> result) {
        List<MediaBrowser.MediaItem> items = new ArrayList<>();
        if (ROOT.equals(parentId)) {
            for (Station s : Station.ALL) {
                items.add(new MediaBrowser.MediaItem(new MediaDescription.Builder()
                        .setMediaId(s.id)
                        .setTitle(s.name)
                        .setSubtitle(s.frequency)
                        .build(), MediaBrowser.MediaItem.FLAG_PLAYABLE));
            }
        }
        result.sendResult(items);
    }
}
