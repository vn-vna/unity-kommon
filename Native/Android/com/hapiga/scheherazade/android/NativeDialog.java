package com.hapiga.scheherazade.android;

import android.app.AlertDialog;
import android. content.DialogInterface;
import com.unity3d.player.UnityPlayer;

public class NativeDialog {
    
    public static void showAlert(final String title, final String message, final String positiveButton, final String negativeButton, final String gameObjectName, final String callbackMethod) {
        UnityPlayer.currentActivity.runOnUiThread(new Runnable() {
            @Override
            public void run() {
                AlertDialog.Builder builder = new AlertDialog.Builder(UnityPlayer.currentActivity);
                builder.setTitle(title);
                builder.setMessage(message);
                
                // Positive button
                if (positiveButton != null && !positiveButton.isEmpty()) {
                    builder.setPositiveButton(positiveButton, new DialogInterface.OnClickListener() {
                        @Override
                        public void onClick(DialogInterface dialog, int which) {
                            // Send callback to Unity
                            if (gameObjectName != null && ! gameObjectName.isEmpty() && callbackMethod != null && !callbackMethod.isEmpty()) {
                                UnityPlayer.UnitySendMessage(gameObjectName, callbackMethod, "positive");
                            }
                            dialog.dismiss();
                        }
                    });
                }
                
                // Negative button
                if (negativeButton != null && !negativeButton.isEmpty()) {
                    builder.setNegativeButton(negativeButton, new DialogInterface.OnClickListener() {
                        @Override
                        public void onClick(DialogInterface dialog, int which) {
                            // Send callback to Unity
                            if (gameObjectName != null && ! gameObjectName.isEmpty() && callbackMethod != null && !callbackMethod.isEmpty()) {
                                UnityPlayer.UnitySendMessage(gameObjectName, callbackMethod, "negative");
                            }
                            dialog.dismiss();
                        }
                    });
                }
                
                builder.setCancelable(true);
                AlertDialog dialog = builder.create();
                dialog.show();
            }
        });
    }
    
    public static void showSimpleAlert(String title, String message) {
        showAlert(title, message, "OK", "", "", "");
    }
}