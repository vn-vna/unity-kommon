using System;

namespace Com.Hapiga.Scheherazade.Common.LocalSave
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public class MigratorVersionAttribute : Attribute
    {
        public VersionTag Version { get; }
        public VersionTag TargetVersion { get; }

        public MigratorVersionAttribute(string version, string targetVersion)
        {
            Version = VersionTag.Parse(version);
            TargetVersion = VersionTag.Parse(targetVersion);
        }

        public MigratorVersionAttribute(VersionTag version, VersionTag targetVersion)
        {
            Version = version;
            TargetVersion = targetVersion;
        }
    }


}