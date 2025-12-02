package com.hapiga.scheherazade.android;

import android.app.Activity;
import android.graphics.Color;
import android.view.View;
import android.view.ViewGroup;
import android.widget.FrameLayout;

/**
 * Helper to add/remove a full-screen white cover view on the activity window.
 * Safe to call multiple times from any thread (it marshals to UI thread).
 */
public final class PrivacyCoverManager {
    private static final String COVER_TAG = "PRIVACY_WHITE_COVER_TAG";

    private PrivacyCoverManager() { /* no-op */ }

    private static View createCoverView(Activity activity) {
        FrameLayout cover = new FrameLayout(activity);
        cover.setBackgroundColor(Color.WHITE);
        cover.setTag(COVER_TAG);
        cover.setLayoutParams(new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT,
                ViewGroup.LayoutParams.MATCH_PARENT));
        // don't intercept input/back press
        cover.setFocusable(false);
        cover.setClickable(false);
        return cover;
    }

    private static View findCover(Activity activity) {
        View decorView = activity.getWindow() != null ? activity.getWindow().getDecorView() : null;
        if (!(decorView instanceof ViewGroup)) return null;
        ViewGroup decor = (ViewGroup) decorView;
        for (int i = 0; i < decor.getChildCount(); i++) {
            View child = decor.getChildAt(i);
            if (child != null && COVER_TAG.equals(child.getTag())) {
                return child;
            }
        }
        return null;
    }

    public static void showCover(final Activity activity) {
        if (activity == null) return;
        activity.runOnUiThread(new Runnable() {
            @Override
            public void run() {
                View decorView = activity.getWindow() != null ? activity.getWindow().getDecorView() : null;
                if (!(decorView instanceof ViewGroup)) return;
                ViewGroup decor = (ViewGroup) decorView;
                if (findCover(activity) != null) return; // already added
                View cover = createCoverView(activity);
                // add as last child so it sits on top
                decor.addView(cover);
            }
        });
    }

    public static void removeCover(final Activity activity) {
        if (activity == null) return;
        activity.runOnUiThread(new Runnable() {
            @Override
            public void run() {
                View decorView = activity.getWindow() != null ? activity.getWindow().getDecorView() : null;
                if (!(decorView instanceof ViewGroup)) return;
                ViewGroup decor = (ViewGroup) decorView;
                View cover = findCover(activity);
                if (cover != null) {
                    decor.removeView(cover);
                }
            }
        });
    }
}