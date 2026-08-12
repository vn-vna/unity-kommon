using System;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.ItemDatabase
{
    /// <summary>
    /// Items with this tag can be stacked.
    /// Runtime data: StackableData { count }.
    /// </summary>
    public class StackableTag : TagDefinition
    {
#if UNITY_EDITOR
        [Tooltip("Maximum items per stack")]
#endif
        [SerializeField]
        private int _maxStack = 99999999;

        public int MaxStack
        {
            get => _maxStack;
            set => _maxStack = value;
        }
    }

    /// <summary>
    /// Stackable data: tracks count within a stack.
    /// Linked to StackableTag via [TagData].
    /// </summary>
    [Serializable]
    [TagData(typeof(StackableTag))]
    public class StackableData : ITagData
    {
        public int count = 1;
    }
}
