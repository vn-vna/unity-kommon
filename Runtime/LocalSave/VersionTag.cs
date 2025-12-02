using System;

namespace Com.Hapiga.Scheherazade.Common.LocalSave
{
    public struct VersionTag : IComparable<VersionTag>
    {
        public int Major;
        public int Minor;
        public int Patch;

        public VersionTag(int major, int minor, int patch)
        {
            Major = major;
            Minor = minor;
            Patch = patch;
        }

        public override readonly string ToString()
        {
            return $"{Major}.{Minor}.{Patch}";
        }

        public static VersionTag Parse(string version)
        {
            string[] parts = version.Split('.');
            if (parts.Length != 3) throw new FormatException($"Invalid version format: {version}");
            return new VersionTag(int.Parse(parts[0]), int.Parse(parts[1]), int.Parse(parts[2]));
        }

        public static bool operator >(VersionTag a, VersionTag b)
        {
            if (a.Major != b.Major) return a.Major > b.Major;
            if (a.Minor != b.Minor) return a.Minor > b.Minor;
            return a.Patch > b.Patch;
        }

        public static bool operator <(VersionTag a, VersionTag b)
        {
            if (a.Major != b.Major) return a.Major < b.Major;
            if (a.Minor != b.Minor) return a.Minor < b.Minor;
            return a.Patch < b.Patch;
        }

        public static bool operator >=(VersionTag a, VersionTag b)
        {
            return a > b || a == b;
        }

        public static bool operator <=(VersionTag a, VersionTag b)
        {
            return a < b || a == b;
        }

        public static bool operator ==(VersionTag a, VersionTag b)
        {
            return a.Major == b.Major && a.Minor == b.Minor && a.Patch == b.Patch;
        }

        public static bool operator !=(VersionTag a, VersionTag b)
        {
            return !(a == b);
        }

        public override bool Equals(object obj)
        {
            if (obj is VersionTag other)
            {
                return this == other;
            }
            return false;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Major, Minor, Patch);
        }

        public int CompareTo(VersionTag other)
        {
            if (this > other) return 1;
            if (this < other) return -1;
            return 0;
        }
    }


}