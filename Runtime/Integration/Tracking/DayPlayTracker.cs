using System;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Integration.Tracking
{
    public static class DayPlayTracker
    {
        private const string FirstLaunchKey = "dayplay_first_launch_utc";
        private static DateTime? _firstLaunchUtc;

        public static int DayPlay
        {
            get
            {
                EnsureInitialized();
                var now = DateTime.UtcNow;
                var days = (now.Date - _firstLaunchUtc.Value.Date).Days;
                return Math.Max(0, days);
            }
        }

        public static void Initialize()
        {
            if (!PlayerPrefs.HasKey(FirstLaunchKey))
            {
                var now = DateTime.UtcNow;
                PlayerPrefs.SetString(FirstLaunchKey, now.ToString("o"));
                PlayerPrefs.Save();
                _firstLaunchUtc = now;
            }
            else
            {
                string s = PlayerPrefs.GetString(FirstLaunchKey);
                if (DateTime.TryParse(s, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
                {
                    _firstLaunchUtc = dt;
                }
                else
                {
                    var now = DateTime.UtcNow;
                    _firstLaunchUtc = now;
                    PlayerPrefs.SetString(FirstLaunchKey, now.ToString("o"));
                    PlayerPrefs.Save();
                }
            }
        }

        private static void EnsureInitialized()
        {
            if (!_firstLaunchUtc.HasValue)
            {
                if (PlayerPrefs.HasKey(FirstLaunchKey))
                {
                    var s = PlayerPrefs.GetString(FirstLaunchKey);
                    if (DateTime.TryParse(s, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
                    {
                        _firstLaunchUtc = dt;
                    }
                }

                if (!_firstLaunchUtc.HasValue)
                {
                    var now = DateTime.UtcNow;
                    _firstLaunchUtc = now;
                    PlayerPrefs.SetString(FirstLaunchKey, now.ToString("o"));
                    PlayerPrefs.Save();
                }
            }
        }
    }
}