using System;

namespace Com.Hapiga.Scheherazade.Common.AsyncResourceLoader
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class AsyncResourceManagerHostAttribute : Attribute
    {
        public string SettingsPath { get; }

        public AsyncResourceManagerHostAttribute(string settingsPath)
        {
            SettingsPath = settingsPath;
        }
    }
}
