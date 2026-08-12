using System;

namespace Com.Hapiga.Scheherazade.Common.ItemDatabase
{
    /// <summary>
    /// Applied to TagDefinition subclasses to declare that the tag and
    /// any ItemDefinition carrying it are managed by an external module.
    /// The ItemDatabase editor respects this to prevent manual deletion.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class OwnedByModuleAttribute : Attribute
    {
        public string ModuleName { get; }

        public OwnedByModuleAttribute(string moduleName)
        {
            ModuleName = moduleName;
        }
    }
}
