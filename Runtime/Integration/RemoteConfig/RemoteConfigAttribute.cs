using System;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Integration.RemoteConfig
{
    [AttributeUsage(AttributeTargets.Property)]
    public class RemoteConfigAttribute : Attribute
    {
        public string Key { get; set; }
        public Type ParserModule { get; set; }
        public object DefaultValue { get; set; }
    } 
}