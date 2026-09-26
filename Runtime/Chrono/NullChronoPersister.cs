using System;
using UnityEngine;

namespace Com.Scheherazade.Common.Chrono
{
    [CreateAssetMenu(fileName = "NullChronoPersister", menuName = "Scheherazade/Chrono/Null Persister")]
    public class NullChronoPersister : ScriptableObject, IChronoPersister
    {
        public void Save(string key, DateTime value) { }

        public DateTime? Load(string key) => null;

        public void Delete(string key) { }
    }
}
