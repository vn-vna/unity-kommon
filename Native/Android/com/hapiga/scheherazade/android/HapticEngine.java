package com.hapiga.scheherazade.android;

import android.content.Context;
import android.os.Build;
import android.os.VibrationEffect;
import android.os.Vibrator;

import java.util.HashMap;
import java.util.Map;

/**
 * Android haptic bridge for the Scheherazade Haptics framework.
 *
 * API-level strategy:
 *  - API >= 26 : VibrationEffect.createOneShot / createWaveform (full amplitude)
 *  - API 21-25 : Vibrator.vibrate(durationMs) (legacy amplitude handling)
 *  - API < 21  : Vibrator.vibrate(durationMs)
 *
 * Requires <uses-permission android:name="android.permission.VIBRATE"/>.
 * Missing permission = silent no-op on device.
 */
public final class HapticEngine {

    private static final Map<Integer, ContinuousVibration> CONTINUOUS =
            new HashMap<Integer, ContinuousVibration>();

    private static final class ContinuousVibration {
        final Vibrator vibrator;
        final long durationMs;
        int amplitude;

        ContinuousVibration(Vibrator vibrator, long durationMs, int amplitude) {
            this.vibrator = vibrator;
            this.durationMs = durationMs;
            this.amplitude = amplitude;
        }
    }

    private HapticEngine() {
    }

    public static boolean isAvailable(Context context) {
        Vibrator v = (Vibrator) context.getSystemService(Context.VIBRATOR_SERVICE);
        return v != null && v.hasVibrator();
    }

    public static void cue(Context context, int durationMs, int amplitude, int waveformType) {
        Vibrator v = (Vibrator) context.getSystemService(Context.VIBRATOR_SERVICE);
        if (v == null) return;

        int clamped = clamp(amplitude, 0, 255);

        if (Build.VERSION.SDK_INT >= 26) {
            v.vibrate(VibrationEffect.createOneShot(durationMs, clamped));
        } else {
            v.vibrate(durationMs);
        }
    }

    public static void beginContinuous(Context context, int tokenId, int durationMs, int amplitude) {
        Vibrator v = (Vibrator) context.getSystemService(Context.VIBRATOR_SERVICE);
        if (v == null) return;

        // Stop any previous continuous vibration before starting a new one.
        if (CONTINUOUS.containsKey(tokenId)) {
            endContinuous(tokenId);
        }

        int clamped = clamp(amplitude, 0, 255);

        if (Build.VERSION.SDK_INT >= 26) {
            v.vibrate(VibrationEffect.createOneShot(durationMs, clamped));
        } else {
            v.vibrate(durationMs);
        }

        CONTINUOUS.put(tokenId, new ContinuousVibration(v, durationMs, clamped));
    }

    public static void updateContinuous(Context context, int tokenId, int durationMs, int amplitude) {
        ContinuousVibration existing = CONTINUOUS.get(tokenId);
        if (existing == null) return;

        int clamped = clamp(amplitude, 0, 255);
        existing.amplitude = clamped;

        // Android has no true "retune" for a running one-shot; re-issue with the
        // new amplitude so the driver feels responsive.
        existing.vibrator.cancel();
        if (Build.VERSION.SDK_INT >= 26) {
            existing.vibrator.vibrate(VibrationEffect.createOneShot(durationMs, clamped));
        } else {
            existing.vibrator.vibrate(durationMs);
        }
    }

    public static void endContinuous(int tokenId) {
        ContinuousVibration existing = CONTINUOUS.remove(tokenId);
        if (existing == null) return;
        existing.vibrator.cancel();
    }

    public static void cancelAll(Context context) {
        Vibrator v = (Vibrator) context.getSystemService(Context.VIBRATOR_SERVICE);
        if (v != null) {
            v.cancel();
        }
        CONTINUOUS.clear();
    }

    private static int clamp(int value, int min, int max) {
        return value < min ? min : (value > max ? max : value);
    }
}
