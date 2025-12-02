using System;
using System.Linq;
using UnityEngine;
using UnityEditor;

namespace Com.Hapiga.Scheherazade.Common.Integration.RemoteConfig.Editor
{

    public class RemoteConfigIntegrationHelper
    {
#if UNITY_EDITOR
        [MenuItem("Dev Menu/Integrations/Auto Resolve Remote Config Providers")]
        public static void AutoResolveRemoteConfigProviders()
        {
            // Find firebase dlls
            var firebaseDlls = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => a.FullName.Contains("Firebase.RemoteConfig"))
                .ToList();

            if (firebaseDlls.Count > 0)
            {
                Debug.Log("Firebase Remote Config found, registering provider.");
            }
        }
#endif
    }

}