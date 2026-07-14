using System;

namespace Com.Hapiga.Scheherazade.Common.Integration.MPRL
{
    /// <summary>
    /// Marks a ScriptableObject class as a discoverable resource provider template
    /// for the Async Resource Manager editor UI. Used reflectively to populate
    /// the "Built-in Templates" section of the Providers tab.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class ResourceProviderAttribute : Attribute
    {
        public string DisplayName { get; }
        public string Description { get; }
        public string[] RequiredDefines { get; set; }

        public ResourceProviderAttribute(string displayName, string description)
        {
            DisplayName = displayName;
            Description = description;
        }
    }
}
