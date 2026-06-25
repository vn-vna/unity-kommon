using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Integration.Tracking.Editor
{
    [CustomPropertyDrawer(typeof(TrackingFeatureFilterAttribute))]
    public class TrackingFeatureFilterDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            TrackingProviderFeatures supportedFeatures = ComputeSupportedFeatures(property.serializedObject);
            TrackingProviderFeatures currentValue = (TrackingProviderFeatures)property.intValue;

            List<TrackingProviderFeatures> individualFlags = GetIndividualFlags();
            List<TrackingProviderFeatures> supportedFlags = individualFlags
                .Where(f => (supportedFeatures & f) != 0)
                .ToList();

            if (supportedFlags.Count == 0)
            {
                EditorGUI.LabelField(position, label, new GUIContent("No providers support any features"));
                return;
            }

            int currentMask = 0;
            for (int i = 0; i < supportedFlags.Count; i++)
            {
                if ((currentValue & supportedFlags[i]) != 0)
                    currentMask |= (1 << i);
            }

            int newMask = EditorGUI.MaskField(
                position, label, currentMask,
                supportedFlags.Select(f => ObjectNames.NicifyVariableName(f.ToString())).ToArray()
            );

            if (newMask != currentMask)
            {
                TrackingProviderFeatures result = TrackingProviderFeatures.None;
                for (int i = 0; i < supportedFlags.Count; i++)
                {
                    if ((newMask & (1 << i)) != 0)
                        result |= supportedFlags[i];
                }

                property.intValue = (int)result;
            }
        }

        private static TrackingProviderFeatures ComputeSupportedFeatures(SerializedObject serializedObject)
        {
            if (serializedObject.targetObject is ITrackingProvider provider)
            {
                return provider.Features;
            }

            SerializedProperty initialProvidersProp = serializedObject.FindProperty("initialProviders");
            if (initialProvidersProp == null || !initialProvidersProp.isArray)
                return TrackingProviderFeatures.AllFeatures;

            TrackingProviderFeatures supported = TrackingProviderFeatures.None;
            for (int i = 0; i < initialProvidersProp.arraySize; i++)
            {
                SerializedProperty element = initialProvidersProp.GetArrayElementAtIndex(i);
                if (element.objectReferenceValue is ITrackingProvider trackingProvider)
                {
                    supported |= trackingProvider.Features;
                }
            }

            return supported == TrackingProviderFeatures.None
                ? TrackingProviderFeatures.AllFeatures
                : supported;
        }

        private static List<TrackingProviderFeatures> GetIndividualFlags()
        {
            return Enum.GetValues(typeof(TrackingProviderFeatures))
                .Cast<TrackingProviderFeatures>()
                .Where(v => v != TrackingProviderFeatures.None && IsPowerOfTwo((int)(object)v))
                .ToList();
        }

        private static bool IsPowerOfTwo(int x)
        {
            return x > 0 && (x & (x - 1)) == 0;
        }
    }
}
