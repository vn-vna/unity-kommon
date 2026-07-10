using System;
using System.IO;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Chrono
{
    [CreateAssetMenu(
        fileName = "PlayerPrefsChronoPersister",
        menuName = "Scheherazade/Chrono/PlayerPrefs Persister"
    )]
    public class PlayerPrefsChronoPersister : ScriptableObject, IChronoPersister
    {
        public void Save(string key, DateTime value)
        {
            PlayerPrefs.SetString(key, value.ToString("O"));
            PlayerPrefs.Save();
        }

        public DateTime? Load(string key)
        {
            string stored = PlayerPrefs.GetString(key, null);
            if (string.IsNullOrEmpty(stored))
            {
                return null;
            }

            if (DateTime.TryParse(stored, out DateTime result))
            {
                return result;
            }

            return null;
        }

        public void Delete(string key)
        {
            PlayerPrefs.DeleteKey(key);
        }
    }
}
