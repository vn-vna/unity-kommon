using System;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Inspector
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, Inherited = true)]
    public class ShowInInspectorAttribute : PropertyAttribute
    {
        public bool ReadOnly { get; set; }
        public bool LiveReload { get; set; }
    }
}