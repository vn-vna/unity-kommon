using System.Linq;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Economy
{
    public interface IManagableScriptableDatabase<T>
        where T : ScriptableObject
    {
        T[] Items { get; set; }

#if UNITY_EDITOR
        void RefreshDatabase()
        {
            Items = UnityEditor.AssetDatabase.FindAssets("")
                .Select(UnityEditor.AssetDatabase.GUIDToAssetPath)
                .Select(UnityEditor.AssetDatabase.LoadAssetAtPath<T>)
                .Where(asset => asset != null)
                .ToArray();
        }
#endif
    }
}