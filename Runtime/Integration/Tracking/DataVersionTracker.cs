using System;

namespace Com.Hapiga.Scheherazade.Common.Integration.Tracking
{
    // Tracks a data_version composed from app version digits and level update index.
    // data_version format: <appVersionDigits>-<updateIndex3Digits>
    // Example: appVersion = 0.0.1.2 -> digits = 0012; update 003 => data_version = 0012-003
    public static class DataVersionTracker
    {
        private static string _appVersionDigits = "0000";
        public static string CurrentDataVersion { get; private set; } = "0000-000";

        public static event Action<string> OnDataVersionChanged;

        public static void Initialize(string appVersion, int initialUpdateIndex = 0)
        {
            _appVersionDigits = ComputeDigits(appVersion);
            SetDataVersion(initialUpdateIndex);
        }

        public static void AdvanceUpdate(int updateIndex)
        {
            SetDataVersion(updateIndex);
        }

        private static void SetDataVersion(int updateIndex)
        {
            string newVersion = Format(_appVersionDigits, updateIndex);

            if (newVersion == CurrentDataVersion) return;

            CurrentDataVersion = newVersion;
            OnDataVersionChanged?.Invoke(CurrentDataVersion);
        }

        private static string ComputeDigits(string appVersion)
        {
            if (string.IsNullOrEmpty(appVersion)) appVersion = "0.0.0.0";
            string digits = appVersion.Replace(".", string.Empty);
            // Ensure at least 4 digits, take last 4 if longer
            if (digits.Length < 4) digits = digits.PadLeft(4, '0');
            if (digits.Length > 4) digits = digits.Substring(digits.Length - 4);
            return digits;
        }

        private static string Format(string appDigits, int updateIndex)
        {
            return $"{appDigits}-{updateIndex:D3}";
        }
    }
}
