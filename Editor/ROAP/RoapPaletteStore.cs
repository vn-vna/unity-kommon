using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.ROAP.Editor
{
    public enum RoapParameterType
    {
        String,
        Integer,
        Boolean,
        Long,
        Float,
        Uri,
    }

    [Serializable]
    public sealed class RoapLaunchParameter
    {
        public bool enabled = true;
        public string key = string.Empty;
        public string value = string.Empty;
        public RoapParameterType type = RoapParameterType.String;

        public RoapLaunchParameter Clone()
        {
            return new RoapLaunchParameter
            {
                enabled = enabled,
                key = key,
                value = value,
                type = type,
            };
        }
    }

    [Serializable]
    public sealed class RoapParameterPalette
    {
        public string name = "New Palette";
        public List<RoapLaunchParameter> parameters = new List<RoapLaunchParameter>();

        public RoapParameterPalette Clone()
        {
            RoapParameterPalette clone = new RoapParameterPalette
            {
                name = name,
            };

            foreach (RoapLaunchParameter parameter in parameters)
            {
                clone.parameters.Add(parameter.Clone());
            }

            return clone;
        }
    }

    [FilePath("UserSettings/ScheherazadeROAP.asset", FilePathAttribute.Location.ProjectFolder)]
    public sealed class RoapPaletteStore : ScriptableSingleton<RoapPaletteStore>
    {
        [SerializeField]
        private string packageIdOverride = string.Empty;

        [SerializeField]
        private string selectedDeviceSerial = string.Empty;

        [SerializeField]
        private string selectedLauncherComponent = string.Empty;

        [SerializeField]
        private List<RoapLaunchParameter> currentParameters = new List<RoapLaunchParameter>();

        [SerializeField]
        private List<RoapParameterPalette> palettes = new List<RoapParameterPalette>();

        public string PackageIdOverride
        {
            get => packageIdOverride;
            set => packageIdOverride = value ?? string.Empty;
        }

        public string SelectedDeviceSerial
        {
            get => selectedDeviceSerial;
            set => selectedDeviceSerial = value ?? string.Empty;
        }

        public string SelectedLauncherComponent
        {
            get => selectedLauncherComponent;
            set => selectedLauncherComponent = value ?? string.Empty;
        }

        public List<RoapLaunchParameter> CurrentParameters => currentParameters;
        public List<RoapParameterPalette> Palettes => palettes;

        public void EnsureInitialized(string defaultPackageId)
        {
            packageIdOverride ??= string.Empty;
            selectedDeviceSerial ??= string.Empty;
            selectedLauncherComponent ??= string.Empty;
            currentParameters ??= new List<RoapLaunchParameter>();
            palettes ??= new List<RoapParameterPalette>();

            if (string.IsNullOrWhiteSpace(packageIdOverride))
            {
                packageIdOverride = defaultPackageId ?? string.Empty;
            }
        }

        public void SaveStore()
        {
            Save(true);
        }
    }
}
