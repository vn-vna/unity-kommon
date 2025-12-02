using System;
using System.Collections.Generic;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.LegacyInput
{
    /// <summary>
    /// Represents a mapping of input actions.
    /// </summary>
    [CreateAssetMenu(
        fileName = "InputActionMapping",
        menuName = "Dev Menu/Legacy Input Controller/Input Action Mapping",
        order = 0
    )]
    public class InputActionMapping : ScriptableObject
    {
        [field: SerializeField]
        public string Mapping { get; set; }

        [field: SerializeField]
        public List<InputActionEntry> Actions { get; set; }

        public void Deconstruct(out string mapping, out List<InputActionEntry> actions)
        {
            mapping = Mapping;
            actions = Actions;
        }
    }
}