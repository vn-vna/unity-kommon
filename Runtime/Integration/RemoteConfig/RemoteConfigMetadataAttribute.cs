using System;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Integration.RemoteConfig
{
    /// <summary>
    /// Optional metadata for a remote config property, used by editor tooling
    /// (e.g. the Remote Config settings preview tab) for debugging purposes.
    /// When omitted, the editor derives display names from the property name
    /// and falls back to default value formatting.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public class RemoteConfigMetadataAttribute : Attribute
    {
        public string DisplayName { get; set; }
        public Type Formatter { get; set; }
    }
}
