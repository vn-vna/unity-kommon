using System;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.ItemDatabase
{
    /// <summary>
    /// Example tag with schema fields. Shows how to create custom
    /// TagDefinitions that carry editor-defined configuration.
    /// </summary>
    public class WeaponTag : TagDefinition
    {
#if UNITY_EDITOR
        [Tooltip("Required class to equip this weapon")]
#endif
        [SerializeField]
        private string _requiredClass;

#if UNITY_EDITOR
        [Tooltip("Attack range rolled for this weapon")]
#endif
        [SerializeField]
        private Vector2Int _atkRange = new Vector2Int(5, 50);

        public string RequiredClass
        {
            get => _requiredClass;
            set => _requiredClass = value;
        }

        public Vector2Int AtkRange
        {
            get => _atkRange;
            set => _atkRange = value;
        }
    }

    /// <summary>
    /// Example runtime data for WeaponTag. Linked via [TagData].
    /// </summary>
    [Serializable]
    [TagData(typeof(WeaponTag))]
    public class WeaponData : ITagData
    {
        public int atk;
    }
}
