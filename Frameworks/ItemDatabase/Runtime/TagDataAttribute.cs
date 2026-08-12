using System;

namespace Com.Hapiga.Scheherazade.Common.ItemDatabase
{
    /// <summary>
    /// Declares that a data class is the runtime payload for a TagDefinition.
    /// Apply to [Serializable] classes that hold instance-level values.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public class TagDataAttribute : Attribute
    {
        public Type TagDefinitionType { get; }

        public TagDataAttribute(Type tagDefinitionType)
        {
            TagDefinitionType = tagDefinitionType;
        }
    }
}
