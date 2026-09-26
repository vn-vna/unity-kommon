using NUnit.Framework;

namespace Com.Scheherazade.Common.AsyncResourceLoader.Editor.Tests
{
    public sealed class ScheherazadeTypeNameMigrationTests
    {
        [Test]
        public void ResolveType_LegacyAssemblyQualifiedName_ReturnsCurrentType()
        {
            string currentTypeName = typeof(ScheherazadeTypeNameMigration).AssemblyQualifiedName;
            string legacyTypeName = currentTypeName.Replace(
                "Com.Scheherazade",
                "Com.Hapiga.Scheherazade"
            );

            System.Type resolvedType = ScheherazadeTypeNameMigration.ResolveType(legacyTypeName);

            Assert.That(resolvedType, Is.EqualTo(typeof(ScheherazadeTypeNameMigration)));
        }

        [Test]
        public void Migrate_LegacyTypeAndAssemblyName_RewritesBothPrefixes()
        {
            const string legacyTypeName =
                "Com.Hapiga.Scheherazade.Common.Example, "
                + "Com.Hapiga.Scheherazade.Common, Version=0.0.0.0";

            string migratedTypeName = ScheherazadeTypeNameMigration.Migrate(legacyTypeName);

            Assert.That(
                migratedTypeName,
                Is.EqualTo(
                    "Com.Scheherazade.Common.Example, "
                    + "Com.Scheherazade.Common, Version=0.0.0.0"
                )
            );
        }
    }
}
