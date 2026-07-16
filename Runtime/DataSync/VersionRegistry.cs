using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Com.Hapiga.Scheherazade.Common.DataSync
{
    public static class VersionRegistry
    {
        private static readonly Dictionary<(Type rootType, VersionTag version), Type> _snapshotTypeMap
            = new Dictionary<(Type, VersionTag), Type>();

        private static readonly Dictionary<Type, List<MigratorEntry>> _migratorChains
            = new Dictionary<Type, List<MigratorEntry>>();

        private static readonly Dictionary<Type, VersionTag> _currentVersions
            = new Dictionary<Type, VersionTag>();

        private static bool _initialized;

        public static void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;
            ScanAssemblies();
        }

        public static Type GetSnapshotType(Type rootType, VersionTag version)
        {
            EnsureInitialized();
            if (_snapshotTypeMap.TryGetValue((rootType, version), out var type))
                return type;

            if (version == VersionTag.Zero)
                return rootType;

            throw new MigrationException(rootType,
                $"No snapshot type registered for {rootType.Name} at version {version}");
        }

        public static VersionTag GetCurrentVersion(Type rootType)
        {
            EnsureInitialized();
            if (_currentVersions.TryGetValue(rootType, out var version))
                return version;

            return VersionTag.Zero;
        }

        public static IReadOnlyList<MigratorEntry> GetMigratorChain(Type rootType)
        {
            EnsureInitialized();
            if (_migratorChains.TryGetValue(rootType, out var chain))
                return chain.AsReadOnly();

            return Array.Empty<MigratorEntry>();
        }

        public static object MigrateToCurrent(object snapshot, Type rootType, VersionTag fromVersion)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));

            EnsureInitialized();

            var chain = GetMigratorChain(rootType);
            var ordered = chain.OrderBy(e => e.FromVersion).ToList();

            VersionTag current = fromVersion;
            object currentSnapshot = snapshot;

            for (int i = 0; i < ordered.Count; i++)
            {
                var entry = ordered[i];
                if (entry.FromVersion != current) continue;

                var migrator = Activator.CreateInstance(entry.MigratorType);
                var migrateMethod = entry.MigratorType.GetMethod(
                    "Migrate",
                    BindingFlags.Public | BindingFlags.Instance);

                if (migrateMethod == null)
                {
                    throw new MigrationException(rootType,
                        $"Migrate method not found on {entry.MigratorType.Name}");
                }

                currentSnapshot = migrateMethod.Invoke(migrator, new[] { currentSnapshot });
                current = entry.ToVersion;
            }

            return currentSnapshot;
        }

        private static void ScanAssemblies()
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();

            // Pass 1: register root types (classes with [CurrentDataVersion])
            foreach (var assembly in assemblies)
            {
                try
                {
                    foreach (var type in assembly.GetTypes())
                    {
                        if (type.IsAbstract || type.IsInterface || type.IsGenericTypeDefinition) continue;
                        RegisterCurrentVersion(type);
                    }
                }
                catch (ReflectionTypeLoadException) { }
            }

            // Pass 2: register migrators
            foreach (var assembly in assemblies)
            {
                try
                {
                    foreach (var type in assembly.GetTypes())
                    {
                        if (type.IsAbstract || type.IsInterface) continue;
                        TryRegisterMigrator(type);
                    }
                }
                catch (ReflectionTypeLoadException) { }
            }
        }

        private static void TryRegisterMigrator(Type type)
        {
            Type baseType = type.BaseType;
            if (baseType == null) return;
            if (!baseType.IsGenericType) return;
            if (baseType.GetGenericTypeDefinition() != typeof(VersionMigrator<,>)) return;

            var attr = type.GetCustomAttribute<MigratorVersionAttribute>();
            if (attr == null) return;

            Type[] genArgs = baseType.GetGenericArguments();
            Type oldType = genArgs[0];
            Type newType = genArgs[1];

            Type rootType = ResolveRootType(oldType, newType);
            if (rootType == null) return;

            _snapshotTypeMap[(rootType, attr.Version)] = oldType;
            _snapshotTypeMap[(rootType, attr.TargetVersion)] = newType;

            if (!_migratorChains.ContainsKey(rootType))
                _migratorChains[rootType] = new List<MigratorEntry>();

            _migratorChains[rootType].Add(
                new MigratorEntry(attr.Version, attr.TargetVersion, type));
        }

        private static void RegisterCurrentVersion(Type type)
        {
            var attr = type.GetCustomAttribute<CurrentDataVersionAttribute>();
            if (attr == null) return;

            _currentVersions[type] = attr.Version;
            _snapshotTypeMap[(type, attr.Version)] = type;
        }

        private static Type ResolveRootType(Type oldType, Type newType)
        {
            if (_currentVersions.ContainsKey(oldType)) return oldType;
            if (_currentVersions.ContainsKey(newType)) return newType;

            foreach (var kvp in _currentVersions)
            {
                if (kvp.Key.IsAssignableFrom(oldType)) return kvp.Key;
                if (kvp.Key.IsAssignableFrom(newType)) return kvp.Key;
            }

            return null;
        }
    }

    public struct MigratorEntry
    {
        public VersionTag FromVersion { get; }
        public VersionTag ToVersion { get; }
        public Type MigratorType { get; }

        public MigratorEntry(VersionTag fromVersion, VersionTag toVersion, Type migratorType)
        {
            FromVersion = fromVersion;
            ToVersion = toVersion;
            MigratorType = migratorType;
        }
    }
}
