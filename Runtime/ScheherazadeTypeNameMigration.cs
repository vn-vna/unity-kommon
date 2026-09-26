using System;
using System.Reflection;

namespace Com.Scheherazade.Common
{
    /// <summary>
    /// Resolves persisted type names written before the Scheherazade namespace migration.
    /// Keep this compatibility path while shipped saves or project assets may still contain
    /// the previous namespace or assembly prefix.
    /// </summary>
    public static class ScheherazadeTypeNameMigration
    {
        private const string LegacyPrefix = "Com.Hapiga.Scheherazade";
        private const string LegacyMisspelledPrefix = "Com.Hapiga.Schehrazade";
        private const string LegacyFallAwayPrefix = "Com.Hapiga.FallAway";
        private const string CurrentPrefix = "Com.Scheherazade";
        private const string CurrentFallAwayPrefix = "Com.Scheherazade.FallAway";

        public static string Migrate(string typeName)
        {
            if (string.IsNullOrEmpty(typeName)) return typeName;

            return typeName
                .Replace(LegacyPrefix, CurrentPrefix)
                .Replace(LegacyMisspelledPrefix, CurrentPrefix)
                .Replace(LegacyFallAwayPrefix, CurrentFallAwayPrefix);
        }

        public static Type ResolveType(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName)) return null;

            Type type = Type.GetType(typeName);
            if (type != null) return type;

            string migratedTypeName = Migrate(typeName);
            type = Type.GetType(migratedTypeName);
            if (type != null) return type;

            string fullTypeName = GetFullTypeName(migratedTypeName);
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = assembly.GetType(fullTypeName);
                if (type != null) return type;
            }

            return null;
        }

        private static string GetFullTypeName(string typeName)
        {
            int assemblySeparator = typeName.IndexOf(',');
            return assemblySeparator < 0
                ? typeName
                : typeName.Substring(0, assemblySeparator).Trim();
        }
    }
}
