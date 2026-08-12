using System;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.ItemDatabase
{
    /// <summary>
    /// Example tag with schema fields. Shows how to create custom
    /// TagDefinitions that carry editor-defined configuration.
    /// </summary>
    public class ArmorTag : TagDefinition
    {
#if UNITY_EDITOR
        [Tooltip("Required class to equip this armor")]
#endif
        [SerializeField]
        private string _requiredClass;

#if UNITY_EDITOR
        [Tooltip("HP range rolled for this armor")]
#endif
        [SerializeField]
        private Vector2Int _hpRange = new Vector2Int(10, 50);

#if UNITY_EDITOR
        [Tooltip("Defense range rolled for this armor")]
#endif
        [SerializeField]
        private Vector2Int _defRange = new Vector2Int(5, 30);

        public string RequiredClass
        {
            get => _requiredClass;
            set => _requiredClass = value;
        }

        public Vector2Int HpRange
        {
            get => _hpRange;
            set => _hpRange = value;
        }

        public Vector2Int DefRange
        {
            get => _defRange;
            set => _defRange = value;
        }
    }

    /// <summary>
    /// Example runtime data for ArmorTag. Linked via [TagData].
    /// </summary>
    [Serializable]
    [TagData(typeof(ArmorTag))]
    public class ArmorData : ITagData
    {
        public int hp;
        public int def;
    }
}
