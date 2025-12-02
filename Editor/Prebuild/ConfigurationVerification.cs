using System;
using System.Linq;
using Com.Hapiga.Scheherazade.Common.Integration.Ads;
using Com.Hapiga.Scheherazade.Common.Integration.Tracking;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Prebuild
{
    public class ConfigurationVerification : IPreprocessBuildWithReport
    {
        public int callbackOrder => -100;

        public void OnPreprocessBuild(BuildReport report)
        {
#if TRACKING_ADJUST
            AdjustConfiguration adjustConfig = AssetDatabase.FindAssets("")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<AdjustConfiguration>)
                .Where(c => c != null)
                .FirstOrDefault();

            VerifyAdjustConfiguration(adjustConfig);
#endif

#if APPLOVIN_MAX
            ApplovinMaxAdsConfiguration maxConfig = AssetDatabase.FindAssets("")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<ApplovinMaxAdsConfiguration>)
                .Where(c => c != null)
                .FirstOrDefault();

            VerifyAppLovinMaxConfiguration(maxConfig);
#endif

#if TRACKING_APPMETRICA
            AppMetricaTrackingConfiguration appMetricaConfig = AssetDatabase.FindAssets("")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<AppMetricaTrackingConfiguration>)
                .Where(c => c != null)
                .FirstOrDefault();

            VerifyAppMetricaConfiguration(appMetricaConfig);
#endif

#if FIREBASE_ANALYTICS
            FirebaseTrackingConfiguration firebaseConfig = AssetDatabase.FindAssets("")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<FirebaseTrackingConfiguration>)
                .Where(c => c != null)
                .FirstOrDefault();

            VerifyFirebaseTrackingConfiguration(firebaseConfig);
#endif
        }

#if TRACKING_ADJUST
        private void VerifyAdjustConfiguration(AdjustConfiguration adjustConfig)
        {
            if (adjustConfig == null)
            {
                bool proceed = EditorUtility.DisplayDialog(
                    "Adjust Configuration Missing",
                    "Adjust Configuration asset is MISSING.\n" +
                    "Adjust tracking will NOT be initialized in the build.\n\n" +
                    "Do you want to proceed with the build?",
                    "Proceed",
                    "Cancel Build"
                );
                if (!proceed)
                {
                    throw new OperationCanceledException(
                        "Build cancelled by user due to missing Adjust configuration."
                    );
                }
                return;
            }

            if (adjustConfig.Environment == AdjustEnvironment.Sandbox)
            {
                bool proceed = EditorUtility.DisplayDialog(
                    "Adjust Configuration - Sandbox Environment",
                    "The Adjust Configuration is set to SANDBOX environment.\n" +
                    "This build is not for production use.\n\n" +
                    "Do you want to proceed with the build?",
                    "Proceed",
                    "Cancel Build"
                );

                if (!proceed)
                {
                    throw new OperationCanceledException(
                        "Build cancelled by user due to Adjust configuration."
                    );
                }
            }
        }
#endif

#if APPLOVIN_MAX
        private void VerifyAppLovinMaxConfiguration(ApplovinMaxAdsConfiguration maxConfig)
        {
            if (maxConfig == null)
            {
                bool proceed = EditorUtility.DisplayDialog(
                    "AppLovin MAX Configuration Missing",
                    "AppLovin MAX Configuration asset is MISSING.\n" +
                    "AppLovin MAX SDK will NOT be initialized in the build.\n\n" +
                    "Do you want to proceed with the build?",
                    "Proceed",
                    "Cancel Build"
                );
                if (!proceed)
                {
                    throw new OperationCanceledException(
                        "Build cancelled by user due to missing AppLovin MAX configuration."
                    );
                }
            }

            if (maxConfig.EnabledAds == ApplovinMaxAdsEnabledAds.None)
            {
                bool proceed = EditorUtility.DisplayDialog(
                    "AppLovin MAX Configuration - No Ads Enabled",
                    "No Ad formats are enabled in the AppLovin MAX Configuration.\n" +
                    "AppLovin MAX SDK will NOT be initialized in the build.\n\n" +
                    "Do you want to proceed with the build?",
                    "Proceed",
                    "Cancel Build"
                );

                if (!proceed)
                {
                    throw new OperationCanceledException(
                        "Build cancelled by user due to AppLovin MAX configuration."
                    );
                }
            }

            if (maxConfig.UnitIdsMapping.Count == 0)
            {
                bool proceed = EditorUtility.DisplayDialog(
                    "AppLovin MAX Configuration - No Ad Unit IDs",
                    "No Ad Unit IDs are configured in the AppLovin MAX Configuration.\n" +
                    "AppLovin MAX SDK will NOT be initialized in the build.\n\n" +
                    "Do you want to proceed with the build?",
                    "Proceed",
                    "Cancel Build"
                );

                if (!proceed)
                {
                    throw new OperationCanceledException(
                        "Build cancelled by user due to AppLovin MAX configuration."
                    );
                }
            }
        }
#endif

#if TRACKING_APPMETRICA
        private void VerifyAppMetricaConfiguration(AppMetricaTrackingConfiguration appMetricaConfig)
        {
            if (appMetricaConfig == null)
            {
                bool proceed = EditorUtility.DisplayDialog(
                    "AppMetrica Tracking Configuration Missing",
                    "AppMetrica Tracking Configuration asset is MISSING.\n" +
                    "AppMetrica tracking will NOT be initialized in the build.\n\n" +
                    "Do you want to proceed with the build?",
                    "Proceed",
                    "Cancel Build"
                );
                if (!proceed)
                {
                    throw new OperationCanceledException(
                        "Build cancelled by user due to missing AppMetrica Tracking configuration."
                    );
                }
            }

            if (string.IsNullOrEmpty(appMetricaConfig.ApiKey))
            {
                bool proceed = EditorUtility.DisplayDialog(
                    "AppMetrica Tracking Configuration - Missing API Key",
                    "The AppMetrica Tracking Configuration is missing the API Key.\n" +
                    "AppMetrica tracking will NOT be initialized in the build.\n\n" +
                    "Do you want to proceed with the build?",
                    "Proceed",
                    "Cancel Build"
                );

                if (!proceed)
                {
                    throw new OperationCanceledException(
                        "Build cancelled by user due to AppMetrica Tracking configuration."
                    );
                }
            }
        }
#endif

#if FIREBASE_ANALYTICS
        private void VerifyFirebaseTrackingConfiguration(FirebaseTrackingConfiguration firebaseConfig)
        {
            if (firebaseConfig == null)
            {
                bool proceed = EditorUtility.DisplayDialog(
                    "Firebase Tracking Configuration Missing",
                    "Firebase Tracking Configuration asset is MISSING.\n" +
                    "Firebase tracking will NOT be initialized in the build.\n\n" +
                    "Do you want to proceed with the build?",
                    "Proceed",
                    "Cancel Build"
                );
                if (!proceed)
                {
                    throw new OperationCanceledException(
                        "Build cancelled by user due to missing Firebase Tracking configuration."
                    );
                }
            }

            if (firebaseConfig.RevenueTrackingConfig == null ||
                firebaseConfig.RevenueTrackingConfig.Length == 0)
            {
                bool proceed = EditorUtility.DisplayDialog(
                    "Firebase Tracking Configuration - No Revenue Tracking Configured",
                    "No revenue tracking events are configured in the Firebase Tracking Configuration.\n" +
                    "Firebase revenue tracking will NOT send any revenue events in the build.\n\n" +
                    "Do you want to proceed with the build?",
                    "Proceed",
                    "Cancel Build"
                );

                if (!proceed)
                {
                    throw new OperationCanceledException(
                        "Build cancelled by user due to Firebase Tracking configuration."
                    );
                }
            }
        }
#endif

    }
}