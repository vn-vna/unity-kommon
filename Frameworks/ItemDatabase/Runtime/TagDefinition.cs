using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.ItemDatabase
{
    /// <summary>
    /// Abstract base for all item tag definitions.
    /// Extend this to create custom tag types.
    /// The concrete C# type serves as the discriminator at runtime.
    /// </summary>
    public abstract class TagDefinition : ScriptableObject
    {
    }
}
