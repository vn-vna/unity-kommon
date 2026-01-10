using System;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Inspector
{
    /// <summary>
    /// Attribute to mark fields or properties for display in the Unity Inspector.
    /// </summary>
    /// <remarks>
    /// This attribute allows private fields and properties to be shown in the Unity Inspector
    /// without making them public or serialized. Provides options for read-only display and live reloading.
    /// </remarks>
    /// <example>
    /// <code>
    /// public class MyComponent : MonoBehaviour
    /// {
    ///     [ShowInInspector(ReadOnly = true)]
    ///     private int calculatedValue;
    ///     
    ///     [ShowInInspector(LiveReload = true)]
    ///     private string debugInfo;
    /// }
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, Inherited = true)]
    public class ShowInInspectorAttribute : PropertyAttribute
    {
        /// <summary>
        /// Gets or sets whether the field should be displayed as read-only in the Inspector.
        /// </summary>
        public bool ReadOnly { get; set; }
        
        /// <summary>
        /// Gets or sets whether the field should automatically update in the Inspector during play mode.
        /// </summary>
        public bool LiveReload { get; set; }
    }
}