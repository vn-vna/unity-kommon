using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using UnityEngine;

namespace Com.Scheherazade.Common.DependenciesDownloader.Editor.Tests
{
    public sealed class DependenciesDownloaderTests
    {
        private string _temporaryPackagePath;

        [TearDown]
        public void TearDown()
        {
            if (!string.IsNullOrEmpty(_temporaryPackagePath) &&
                File.Exists(_temporaryPackagePath))
            {
                File.Delete(_temporaryPackagePath);
            }
        }

        [Test]
        public void ParseArchive_FlattenedFeed_ParsesVersionsAndDependencies()
        {
            const string archiveText =
                "## Firebase\n" +
                "### Analytics\n" +
                "Firebase Analytics package.\n" +
                "`com.google.firebase.analytics`\n" +
                "Version Publish Date Minimum Unity Version Download Dependencies " +
                "13.0.0 2025-07 2020.1 " +
                "[.unitypackage](https://example.com/FirebaseAnalytics.unitypackage) " +
                "[.tgz](https://example.com/com.google.firebase.analytics-13.0.0.tgz) " +
                "Included " +
                "[com.google.external-dependency-manager]" +
                "(https://example.com/com.google.external-dependency-manager-1.2.186.tgz) " +
                "12.0.0 2024-05 2019.1 " +
                "[.tgz](https://example.com/com.google.firebase.analytics-12.0.0.tgz) None";

            var parseMethod = typeof(GoogleArchiveParser).GetMethod(
                "ParseArchive",
                BindingFlags.NonPublic | BindingFlags.Static
            );
            Assert.That(parseMethod, Is.Not.Null);

            var packages = (List<GooglePackageInfo>)parseMethod.Invoke(
                null,
                new object[] { archiveText }
            );

            Assert.That(packages, Has.Count.EqualTo(1));
            Assert.That(packages[0].Name, Is.EqualTo("com.google.firebase.analytics"));
            Assert.That(packages[0].Versions, Has.Count.EqualTo(2));
            Assert.That(packages[0].Versions[0].Version, Is.EqualTo("13.0.0"));
            Assert.That(packages[0].Versions[0].Dependencies, Has.Count.EqualTo(1));
            Assert.That(
                packages[0].Versions[0].Dependencies[0].Version,
                Is.EqualTo("1.2.186")
            );
        }

        [Test]
        public void ParseVersionFromTag_UnderscoreReleaseTag_ReturnsSemanticVersion()
        {
            var parseMethod = typeof(GitHubReleaseFetcher).GetMethod(
                "ParseVersionFromTag",
                BindingFlags.NonPublic | BindingFlags.Static
            );
            Assert.That(parseMethod, Is.Not.Null);

            var version = parseMethod.Invoke(
                null,
                new object[] { "release_8_6_6" }
            );

            Assert.That(version, Is.EqualTo("8.6.6"));
        }

        [Test]
        public void ParseGitPackageRelease_BuildsTaggedGitUrl()
        {
            const string releaseJson =
                "[{\"tag_name\":\"v6.10.0\",\"published_at\":\"2026-07-31\",\"body\":\"Notes\",\"assets\":[]}]";
            var parseMethod = typeof(GitHubReleaseFetcher).GetMethod(
                "ParseReleaseJson",
                BindingFlags.NonPublic | BindingFlags.Static
            );
            Assert.That(parseMethod, Is.Not.Null);

            var releases = (List<GitHubReleaseInfo>)parseMethod.Invoke(
                null,
                new object[]
                {
                    releaseJson,
                    "appmetrica",
                    "appmetrica-unity-plugin",
                    GitHubReleaseDelivery.GitPackage
                }
            );

            Assert.That(releases, Has.Count.EqualTo(1));
            Assert.That(releases[0].Version, Is.EqualTo("6.10.0"));
            Assert.That(
                releases[0].GitUrl,
                Is.EqualTo(
                    "https://github.com/appmetrica/appmetrica-unity-plugin.git#v6.10.0"
                )
            );
        }

        [Test]
        public void GetCurrentlyInstalledGooglePackages_IncludesLockedOpenUpmDependency()
        {
            var packages =
                PackageManifestHelper.GetCurrentlyInstalledGooglePackages();

            Assert.That(
                packages.ContainsKey("com.google.external-dependency-manager"),
                Is.True
            );
            Assert.That(
                packages["com.google.external-dependency-manager"],
                Is.Not.Empty
            );
        }

        [Test]
        public void ResolveFullDependencyTree_InstalledRootUpgrade_IncludesRoot()
        {
            var archive = CreateArchive();
            var installed = new Dictionary<string, string>
            {
                ["com.google.root"] =
                    "file:../LocalPackages/com.google.root-1.0.0.tgz"
            };

            var entries = GoogleDependencyResolver.ResolveFullDependencyTree(
                "com.google.root",
                "2.0.0",
                archive,
                installed
            );

            Assert.That(
                entries.Exists(entry =>
                    entry.PackageName == "com.google.root" &&
                    entry.Version == "2.0.0"),
                Is.True
            );
        }

        [Test]
        public void ResolveFullDependencyTree_InstalledDependency_SkipsDependency()
        {
            var archive = CreateArchive();
            var installed = new Dictionary<string, string>
            {
                ["com.google.dependency"] = "1.0.0"
            };

            var entries = GoogleDependencyResolver.ResolveFullDependencyTree(
                "com.google.root",
                "2.0.0",
                archive,
                installed
            );

            Assert.That(
                entries.Exists(entry =>
                    entry.PackageName == "com.google.dependency"),
                Is.False
            );
        }

        [Test]
        public void ResolveAllEntries_InstalledDependency_IsRecordedForDisplay()
        {
            var window = ScriptableObject.CreateInstance<
                DependenciesDownloaderWindow
            >();
            try
            {
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var type = typeof(DependenciesDownloaderWindow);
                type.GetField("_archive", flags).SetValue(
                    window,
                    CreateArchive()
                );
                type.GetField("_installedCache", flags).SetValue(
                    window,
                    new Dictionary<string, string>
                    {
                        ["com.google.dependency"] = "1.0.0"
                    }
                );
                type.GetField("_installedCacheTime", flags).SetValue(
                    window,
                    double.MaxValue
                );

                var queuedInstalls = (HashSet<string>)type.GetField(
                    "_queuedInstalls",
                    flags
                ).GetValue(window);
                queuedInstalls.Add("com.google.root");

                var packageVersions = (Dictionary<string, string>)type.GetField(
                    "_packageVersions",
                    flags
                ).GetValue(window);
                packageVersions["com.google.root"] = "2.0.0";

                var resolve = type.GetMethod(
                    "ResolveAllEntries",
                    flags
                );
                resolve.Invoke(window, null);

                var dependencies = (System.Collections.IDictionary)type.GetField(
                    "_installedDependencies",
                    flags
                ).GetValue(window);
                Assert.That(
                    dependencies.Contains("com.google.dependency"),
                    Is.True
                );
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void IsPathInsideProject_InvalidAndSiblingPaths_ReturnFalse()
        {
            var projectRoot = Path.GetFullPath(Path.Combine(
                Application.dataPath,
                ".."
            ));
            var sibling = projectRoot + "_backup";

            Assert.That(PackageManifestHelper.IsPathInsideProject(string.Empty), Is.False);
            Assert.That(PackageManifestHelper.IsPathInsideProject("\0"), Is.False);
            Assert.That(PackageManifestHelper.IsPathInsideProject(sibling), Is.False);
            Assert.That(
                PackageManifestHelper.IsPathInsideProject(
                    Path.Combine(projectRoot, "LocalPackages")
                ),
                Is.True
            );
        }

        [Test]
        public void EnumerateFilesInPackage_ReadsPathnamePayloadsSequentially()
        {
            _temporaryPackagePath = Path.Combine(
                Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Temp")),
                $"dependency-downloader-test-{Guid.NewGuid():N}.unitypackage"
            );
            WriteUnityPackage(
                _temporaryPackagePath,
                "Assets/TestSdk/Runtime/TestSdk.cs"
            );

            var paths = UnityPackageTracker.EnumerateFilesInPackage(
                _temporaryPackagePath
            );

            Assert.That(
                paths,
                Is.EquivalentTo(new[] { "Assets/TestSdk/Runtime/TestSdk.cs" })
            );
        }

        private static GoogleArchiveCache CreateArchive()
        {
            return new GoogleArchiveCache
            {
                Packages = new List<GooglePackageInfo>
                {
                    new GooglePackageInfo
                    {
                        Name = "com.google.root",
                        Versions = new List<GooglePackageVersion>
                        {
                            new GooglePackageVersion
                            {
                                Version = "2.0.0",
                                TarballUrl = "https://example.com/root-2.0.0.tgz",
                                Dependencies = new List<GooglePackageDependency>
                                {
                                    new GooglePackageDependency
                                    {
                                        Name = "com.google.dependency",
                                        Version = "2.0.0",
                                        TarballUrl =
                                            "https://example.com/dependency-2.0.0.tgz"
                                    }
                                }
                            }
                        }
                    },
                    new GooglePackageInfo
                    {
                        Name = "com.google.dependency",
                        Versions = new List<GooglePackageVersion>
                        {
                            new GooglePackageVersion
                            {
                                Version = "2.0.0",
                                TarballUrl =
                                    "https://example.com/dependency-2.0.0.tgz",
                                Dependencies = new List<GooglePackageDependency>()
                            }
                        }
                    }
                }
            };
        }

        private static void WriteUnityPackage(string path, string assetPath)
        {
            using var file = File.Create(path);
            using var gzip = new GZipStream(file, CompressionMode.Compress);
            WriteTarEntry(gzip, "0123456789abcdef/pathname", assetPath);
            WriteTarEntry(
                gzip,
                "fedcba9876543210/pathname",
                "Assets/../ProjectSettings/ProjectSettings.asset"
            );
            WriteTarEntry(
                gzip,
                "0011223344556677/pathname",
                "Assets/Invalid\0Name.txt"
            );
            gzip.Write(new byte[1024], 0, 1024);
        }

        private static void WriteTarEntry(
            Stream stream,
            string entryName,
            string content)
        {
            var contentBytes = Encoding.UTF8.GetBytes(content);
            var header = new byte[512];
            Encoding.ASCII.GetBytes(entryName).CopyTo(header, 0);

            var sizeText = Convert.ToString(contentBytes.Length, 8)
                .PadLeft(11, '0');
            Encoding.ASCII.GetBytes(sizeText).CopyTo(header, 124);
            header[135] = 0;
            header[156] = (byte)'0';

            stream.Write(header, 0, header.Length);
            stream.Write(contentBytes, 0, contentBytes.Length);

            var padding = (512 - contentBytes.Length % 512) % 512;
            if (padding > 0)
            {
                stream.Write(new byte[padding], 0, padding);
            }
        }
    }
}
