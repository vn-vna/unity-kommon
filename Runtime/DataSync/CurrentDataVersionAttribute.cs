using System;

namespace Com.Hapiga.Scheherazade.Common.DataSync
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public class CurrentDataVersionAttribute : Attribute
    {
        public VersionTag Version { get; }

        public CurrentDataVersionAttribute(string version)
        {
            Version = VersionTag.Parse(version);
        }
    }
}
