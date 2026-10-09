using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Com.Scheherazade.Common.DependenciesDownloader.Editor.Tests
{
    public sealed class DependenciesDownloaderTests
    {
        private string _temporaryPackagePath;
        private string _previousSession;

        [SetUp]
        public void SetUp()
        {
            _previousSession = SessionState.GetString("Scheherazade.DependenciesDownloader.Session", string.Empty);
            SessionState.SetString("Scheherazade.DependenciesDownloader.Session", string.Empty);
        }

        [TearDown]
        public void TearDown()
        {
            SessionState.SetString("Scheherazade.DependenciesDownloader.Session", _previousSession);
            if (!string.IsNullOrEmpty(_temporaryPackagePath) &&
                File.Exists(_temporaryPackagePath))
            {
                File.Delete(_temporaryPackagePath);
            }
            if (!string.IsNullOrEmpty(_temporaryPackagePath))
            {
                foreach (var path in new[]
                {
                    Path.ChangeExtension(_temporaryPackagePath, ".unitypackage"),
                    Path.ChangeExtension(_temporaryPackagePath, ".filtered.unitypackage"),
                    Path.ChangeExtension(_temporaryPackagePath, ".unitypackage.download"),
                    Path.ChangeExtension(_temporaryPackagePath, ".filtered.unitypackage.download")
                })
                {
                    if (File.Exists(path)) File.Delete(path);
                }
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
        public void ParseInstalledPackageStates_PreservesTransitiveRegistryOwnership()
        {
            var packages = PackageManifestHelper.ParseInstalledPackageStates(
                "{\"dependencies\":{\"com.google.parent\":\"1.0.0\"}}",
                "{\"dependencies\":{\"com.google.parent\":{\"version\":\"1.0.0\",\"source\":\"registry\",\"dependencies\":{\"com.google.external-dependency-manager\":\"1.2.186\"}},\"com.google.external-dependency-manager\":{\"version\":\"1.2.186\",\"source\":\"registry\",\"depth\":1}}}"
            );
            var dependency = packages["com.google.external-dependency-manager"];
            Assert.That(dependency.Version, Is.EqualTo("1.2.186"));
            Assert.That(dependency.IsDirect, Is.False);
            Assert.That(dependency.Source, Is.EqualTo("registry"));
            Assert.That(dependency.RequiredBy, Is.EquivalentTo(new[] { "com.google.parent" }));
            Assert.That(packages["com.google.parent"].IsDirect, Is.True);
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
                ["com.google.dependency"] = "2.0.0"
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
                        ["com.google.dependency"] = "2.0.0"
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

        [TestCase("v2.0.0-beta.2", "2.0.0-beta.2")]
        [TestCase("release_8_6_6", "8.6.6")]
        [TestCase("file:../Deps/com.google.sdk-13.17.0.tgz", "13.17.0")]
        [TestCase("https://github.com/sdk.git#v2.0.0-rc.1", "2.0.0-rc.1")]
        public void ExtractVersion_PreservesReleaseIdentity(string value, string expected)
        {
            Assert.That(DependencyVersionUtility.Extract(value), Is.EqualTo(expected));
        }

        [TestCase("2.0.0", "2.0.0-rc.1", 1)]
        [TestCase("2.0.0-beta.10", "2.0.0-beta.2", 1)]
        [TestCase("1.9.0", "2.0.0", -1)]
        [TestCase("6000.0.72f1", "2020.1", 1)]
        public void CompareVersions_UsesNumericAndPrereleaseOrdering(string left, string right, int sign)
        {
            Assert.That(DependencyVersionUtility.TryCompare(left, right, out var comparison), Is.True);
            Assert.That(Math.Sign(comparison), Is.EqualTo(sign));
        }

        [Test]
        public void ParseReleaseJson_ExcludesDraftsAndPreservesPrereleases()
        {
            const string json = "[{\"tag_name\":\"v3.0.0\",\"draft\":true},{\"tag_name\":\"v2.0.0-beta.1\",\"prerelease\":true},{\"tag_name\":\"v1.9.0\",\"prerelease\":false}]";
            var method = typeof(GitHubReleaseFetcher).GetMethod("ParseReleaseJson", BindingFlags.NonPublic | BindingFlags.Static);
            var releases = (List<GitHubReleaseInfo>)method.Invoke(null,
                new object[] { json, "owner", "repo", GitHubReleaseDelivery.GitPackage });
            Assert.That(releases, Has.Count.EqualTo(2));
            Assert.That(releases[0].Version, Is.EqualTo("1.9.0"));
            Assert.That(releases[1].Version, Is.EqualTo("2.0.0-beta.1"));
            Assert.That(releases[1].IsPrerelease, Is.True);
        }

        [Test]
        public void ResolveDependency_IncompatibleInstalledVersion_ReportsRequirement()
        {
            var exception = Assert.Throws<InvalidOperationException>(() => GoogleDependencyResolver.ResolveFullDependencyTree(
                "com.google.root", "2.0.0", CreateArchive(),
                new Dictionary<string, string> { ["com.google.dependency"] = "1.0.0" }));
            Assert.That(exception.Message, Does.Contain("com.google.dependency 2.0.0"));
        }

        [Test]
        public void ResolveDependency_NewerEdm_PreservesRegistryInstallation()
        {
            var archive = CreateArchive();
            archive.Packages[0].Versions[0].Dependencies[0].Name = "com.google.external-dependency-manager";
            archive.Packages[0].Versions[0].Dependencies[0].Version = "1.2.182";
            var entries = GoogleDependencyResolver.ResolveFullDependencyTree("com.google.root", "2.0.0", archive,
                new Dictionary<string, string> { ["com.google.external-dependency-manager"] = "1.2.186" });
            Assert.That(entries, Has.Count.EqualTo(1));
        }

        [Test]
        public void ResolveDependency_ConflictingVersionsInOneTree_RejectsPlan()
        {
            var archive = CreateArchive();
            archive.Packages[0].Versions[0].Dependencies.Add(new GooglePackageDependency
            {
                Name = "com.google.dependency", Version = "1.0.0", TarballUrl = "https://example.com/dependency-1.0.0.tgz"
            });
            Assert.Throws<InvalidOperationException>(() => GoogleDependencyResolver.ResolveFullDependencyTree("com.google.root", "2.0.0", archive));
        }

        [Test]
        public void BuildPlan_TransitiveRemoval_ExplainsParentOwnership()
        {
            var state = new InstalledPackageState { Name = "com.google.dependency", Version = "2.0.0", IsDirect = false };
            var plan = DependencyChangePlanner.Build(CreateArchive(), new Dictionary<string, string>(),
                new HashSet<string> { state.Name }, new Dictionary<string, InstalledPackageState> { [state.Name] = state }, "6000.0");
            Assert.That(plan.Errors, Has.Count.EqualTo(1));
            Assert.That(plan.Errors[0], Does.Contain("parent packages"));
        }

        [Test]
        public void BuildPlan_RemovalStillRequiredByParent_IsBlocked()
        {
            var state = new InstalledPackageState { Name = "com.google.dependency", Version = "2.0.0", IsDirect = true };
            state.RequiredBy.Add("com.google.parent");
            var plan = DependencyChangePlanner.Build(CreateArchive(), new Dictionary<string, string>(),
                new HashSet<string> { state.Name }, new Dictionary<string, InstalledPackageState> { [state.Name] = state }, "6000.0");
            Assert.That(plan.Errors[0], Does.Contain("com.google.parent"));
        }

        [Test]
        public void BuildPlan_RequiredDependencyQueuedForRemoval_IsBlocked()
        {
            var state = new InstalledPackageState { Name = "com.google.dependency", Version = "2.0.0", IsDirect = true };
            var plan = DependencyChangePlanner.Build(CreateArchive(), new Dictionary<string, string> { ["com.google.root"] = "2.0.0" },
                new HashSet<string> { state.Name }, new Dictionary<string, InstalledPackageState> { [state.Name] = state }, "6000.0");
            Assert.That(plan.Errors.Exists(error => error.Contains("queued for removal")), Is.True);
        }

        [Test]
        public void BuildPlan_UnityVersionTooOld_IsBlocked()
        {
            var archive = CreateArchive();
            archive.Packages[0].Versions[0].MinUnityVersion = "2022.3";
            var plan = DependencyChangePlanner.Build(archive, new Dictionary<string, string> { ["com.google.root"] = "2.0.0" },
                new HashSet<string>(), new Dictionary<string, InstalledPackageState>(), "2020.1");
            Assert.That(plan.Errors[0], Does.Contain("needs Unity 2022.3"));
        }

        [Test]
        public void BrowseUnavailableInstalledVersion_DoesNotQueueUpgrade()
        {
            var window = ScriptableObject.CreateInstance<DependenciesDownloaderWindow>();
            try
            {
                var flags = BindingFlags.NonPublic | BindingFlags.Instance;
                var method = typeof(DependenciesDownloaderWindow).GetMethod("GetSelectedGoogleVersion", flags);
                var selected = method.Invoke(window, new object[] { CreateArchive().Packages[0], "1.0.0" });
                Assert.That(selected, Is.EqualTo("1.0.0"));
                var installs = (HashSet<string>)typeof(DependenciesDownloaderWindow).GetField("_queuedInstalls", flags).GetValue(window);
                Assert.That(installs, Is.Empty);
            }
            finally { UnityEngine.Object.DestroyImmediate(window); }
        }

        [Test]
        public void ClearQueue_RemovesStaleConflictAndPinnedTargets()
        {
            var window = ScriptableObject.CreateInstance<DependenciesDownloaderWindow>();
            try
            {
                var flags = BindingFlags.NonPublic | BindingFlags.Instance;
                var type = typeof(DependenciesDownloaderWindow);
                type.GetField("_dependencyConflictMessage", flags).SetValue(window, "Old conflict");
                var targets = (Dictionary<string, GitHubReleaseInfo>)type.GetField("_queuedGitHubTargets", flags).GetValue(window);
                targets["Adjust"] = new GitHubReleaseInfo { Version = "1.0.0" };
                type.GetMethod("ClearQueue", flags).Invoke(window, null);
                Assert.That(type.GetField("_dependencyConflictMessage", flags).GetValue(window), Is.EqualTo(string.Empty));
                Assert.That(targets, Is.Empty);
            }
            finally { UnityEngine.Object.DestroyImmediate(window); }
        }

        [Test]
        public void PinnedRelease_IsIndependentOfRefreshedCache()
        {
            var flags = BindingFlags.Static | BindingFlags.NonPublic;
            var original = new GitHubReleaseInfo { Version = "1.0.0", TagName = "v1.0.0", DownloadUrl = "https://example.com/old.unitypackage" };
            var pinned = (GitHubReleaseInfo)typeof(DependenciesDownloaderWindow).GetMethod("CloneRelease", flags).Invoke(null, new object[] { original });
            original.Version = "2.0.0";
            original.DownloadUrl = "https://example.com/new.unitypackage";
            Assert.That(pinned.Version, Is.EqualTo("1.0.0"));
            Assert.That(pinned.DownloadUrl, Does.Contain("old.unitypackage"));
        }

        [Test]
        public void SessionState_RoundTrip_PreservesQueueAndVerificationTargets()
        {
            var state = new DownloaderSessionState
            {
                AdjustAction = 1,
                AdjustTarget = new GitHubReleaseInfo { Version = "2.0.0-beta.1", TagName = "v2.0.0-beta.1" },
                VerificationPending = true
            };
            state.GoogleChanges.Add(new QueuedGoogleChange { PackageName = "com.google.root", Version = "2.0.0" });
            state.Verification.Add(new PackageVerification { PackageName = "com.google.root", Version = "2.0.0", Applied = true });
            state.Save();
            var restored = DownloaderSessionState.Load();
            Assert.That(restored.GoogleChanges[0].Version, Is.EqualTo("2.0.0"));
            Assert.That(restored.AdjustTarget.TagName, Is.EqualTo("v2.0.0-beta.1"));
            Assert.That(restored.Verification[0].Applied, Is.True);
        }

        [Test]
        public void VerifyInstallation_ManifestWrittenButPackageUnregistered_DoesNotReportSuccess()
        {
            var expected = new PackageVerification { PackageName = "sdk", Version = "2.0.0", ManifestValue = "2.0.0" };
            Assert.That(DependencyInstallationVerifier.GetFailure(expected, new Dictionary<string, RegisteredDependency>(), "2.0.0"), Is.Not.Null);
        }

        [TestCase("1.0.0", "v2.0.0", "Expected 2.0.0")]
        [TestCase("2.0.0", "v1.0.0", "Expected Git revision")]
        public void VerifyInstallation_WrongResolvedVersionOrRevision_IsRejected(string version, string revision, string message)
        {
            var expected = new PackageVerification { PackageName = "sdk", Version = "2.0.0", ManifestValue = "repo#v2.0.0", GitRevision = "v2.0.0" };
            var actual = new Dictionary<string, RegisteredDependency> { ["sdk"] = new RegisteredDependency { Version = version, GitRevision = revision } };
            Assert.That(DependencyInstallationVerifier.GetFailure(expected, actual, expected.ManifestValue), Does.Contain(message));
        }

        [Test]
        public void VerifyInstallation_MatchingResolvedState_Succeeds()
        {
            var expected = new PackageVerification { PackageName = "sdk", Version = "2.0.0", ManifestValue = "repo#v2.0.0", GitRevision = "v2.0.0" };
            var actual = new Dictionary<string, RegisteredDependency> { ["sdk"] = new RegisteredDependency { Version = "2.0.0", GitRevision = "v2.0.0" } };
            Assert.That(DependencyInstallationVerifier.GetFailure(expected, actual, expected.ManifestValue), Is.Null);
        }

        [Test]
        public void VerifyRemoval_PackageStillRegistered_IsReported()
        {
            var expected = new PackageVerification { PackageName = "sdk", Remove = true };
            Assert.That(DependencyInstallationVerifier.GetFailure(expected,
                new Dictionary<string, RegisteredDependency> { ["sdk"] = new RegisteredDependency { Version = "2.0.0" } }, string.Empty), Does.Contain("Still registered"));
        }

        [Test]
        public void TrackedFiles_MissingAsset_IsReportedWithoutDeletingAnything()
        {
            var record = new UnityPackageInstallRecord { TrackedFiles = new List<string> { $"Assets/DependencyDownloaderTests-{Guid.NewGuid():N}/missing.txt" } };
            Assert.That(UnityPackageTracker.GetMissingFiles(record), Has.Count.EqualTo(1));
        }

        [Test]
        public void RelativeDownloadFolder_IsResolvedAgainstProjectRoot()
        {
            Assert.That(PackageManifestHelper.TryGetPathInsideProject("LocalPackages", out var path), Is.True);
            Assert.That(path, Does.EndWith("/LocalPackages"));
            Assert.That(PackageManifestHelper.GetProjectRelativePath(path), Is.EqualTo("LocalPackages"));
            Assert.That(PackageManifestHelper.IsPathInsideProject("../outside-project"), Is.False);
        }

        [Test]
        public void ValidateTarball_MetadataMustMatchTheQueuedPackage()
        {
            _temporaryPackagePath = Path.Combine(Path.GetTempPath(), $"dependency-tarball-{Guid.NewGuid():N}.tgz");
            using (var file = File.Create(_temporaryPackagePath))
            using (var gzip = new GZipStream(file, CompressionMode.Compress))
            {
                WriteTarEntry(gzip, "package/package.json", "{\"name\":\"com.google.root\",\"version\":\"2.0.0\"}");
                gzip.Write(new byte[1024], 0, 1024);
            }
            Assert.That(UnityPackageTracker.ValidateTarballPackage(_temporaryPackagePath, "com.google.root", "2.0.0"), Is.True);
            Assert.That(UnityPackageTracker.ValidateTarballPackage(_temporaryPackagePath, "com.google.other", "2.0.0"), Is.False);
            Assert.That(UnityPackageTracker.ValidateTarballPackage(_temporaryPackagePath, "com.google.root", "1.0.0"), Is.False);
        }

        [Test]
        public void BuildPlan_ChangingSharedSdkModule_RequiresCompatibleParentUpdates()
        {
            var root = new InstalledPackageState { Name = "com.google.root", Version = "1.0.0", IsDirect = true };
            root.RequiredBy.Add("com.google.parent");
            var parent = new InstalledPackageState { Name = "com.google.parent", Version = "1.0.0", IsDirect = true };
            parent.Dependencies[root.Name] = "1.0.0";
            var plan = DependencyChangePlanner.Build(CreateArchive(), new Dictionary<string, string> { [root.Name] = "2.0.0" },
                new HashSet<string>(), new Dictionary<string, InstalledPackageState> { [root.Name] = root, [parent.Name] = parent }, "6000.0");
            Assert.That(plan.Errors.Exists(error => error.Contains("com.google.parent still requires")), Is.True);
        }

        [Test]
        public void TrackedFingerprint_ModifiedAsset_IsDetectedAndPreservedPathsAreSkipped()
        {
            const string path = "Assets/Plugins/Scheherazade/Editor/DependenciesDownloader/GooglePackageModels.cs";
            var record = new UnityPackageInstallRecord
            {
                Fingerprints = new List<TrackedAssetFingerprint> { new TrackedAssetFingerprint { Path = path, Hash = "different-content" } }
            };
            Assert.That(UnityPackageTracker.GetModifiedFiles(record), Is.EquivalentTo(new[] { path }));
            Assert.That(UnityPackageTracker.GetModifiedFiles(record, new HashSet<string> { path }), Is.Empty);
        }

        [Test]
        public void DownloadFile_PreCanceledOperation_DoesNotStartRequestOrCreateCache()
        {
            var window = ScriptableObject.CreateInstance<DependenciesDownloaderWindow>();
            try
            {
                var flags = BindingFlags.NonPublic | BindingFlags.Instance;
                var type = typeof(DependenciesDownloaderWindow);
                ((CancellationTokenSource)type.GetField("_operationCancellation", flags).GetValue(window)).Cancel();
                _temporaryPackagePath = Path.Combine(Path.GetTempPath(), $"dependency-canceled-{Guid.NewGuid():N}.tgz");
                var task = (Task<bool>)type.GetMethod("DownloadFileAsync", flags).Invoke(window,
                    new object[] { "http://127.0.0.1:1/should-not-be-requested", _temporaryPackagePath, 0, 1 });
                Assert.That(task.IsCanceled, Is.True);
                Assert.That(File.Exists(_temporaryPackagePath), Is.False);
                Assert.That(File.Exists(_temporaryPackagePath + ".download"), Is.False);
            }
            finally { UnityEngine.Object.DestroyImmediate(window); }
        }

        [Test]
        public void CloseAndReopenWindow_RestoresPendingGoogleTarget()
        {
            var flags = BindingFlags.NonPublic | BindingFlags.Instance;
            var type = typeof(DependenciesDownloaderWindow);
            var first = ScriptableObject.CreateInstance<DependenciesDownloaderWindow>();
            try
            {
                var installs = (HashSet<string>)type.GetField("_queuedInstalls", flags).GetValue(first);
                var versions = (Dictionary<string, string>)type.GetField("_packageVersions", flags).GetValue(first);
                installs.Add("com.google.root");
                versions["com.google.root"] = "2.0.0";
            }
            finally { UnityEngine.Object.DestroyImmediate(first); }

            var reopened = ScriptableObject.CreateInstance<DependenciesDownloaderWindow>();
            try
            {
                Assert.That((HashSet<string>)type.GetField("_queuedInstalls", flags).GetValue(reopened), Does.Contain("com.google.root"));
                Assert.That(((Dictionary<string, string>)type.GetField("_packageVersions", flags).GetValue(reopened))["com.google.root"], Is.EqualTo("2.0.0"));
            }
            finally { UnityEngine.Object.DestroyImmediate(reopened); }
        }

        [Test]
        public void RetryFailedVerification_RestoresUnverifiedTransitiveDownload()
        {
            var window = ScriptableObject.CreateInstance<DependenciesDownloaderWindow>();
            try
            {
                var flags = BindingFlags.NonPublic | BindingFlags.Instance;
                var type = typeof(DependenciesDownloaderWindow);
                var state = new DownloaderSessionState();
                state.Verification.Add(new PackageVerification { PackageName = "com.google.dependency", Version = "2.0.0", Applied = true });
                state.Results.Add(new PackageOperationResult { PackageName = "com.google.dependency", Message = "Not registered yet", Succeeded = false });
                type.GetField("_session", flags).SetValue(window, state);
                type.GetMethod("RestoreFailedVerificationChanges", flags).Invoke(window, null);
                Assert.That((HashSet<string>)type.GetField("_queuedInstalls", flags).GetValue(window), Does.Contain("com.google.dependency"));
                Assert.That(((Dictionary<string, string>)type.GetField("_packageVersions", flags).GetValue(window))["com.google.dependency"], Is.EqualTo("2.0.0"));
            }
            finally { UnityEngine.Object.DestroyImmediate(window); }
        }

        [Test]
        public void LegacyRecordMigration_PreservesTimestampAndUnknownFingerprintBaseline()
        {
            var name = "DownloaderMigrationTest-" + Guid.NewGuid().ToString("N");
            var root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var legacyPath = Path.Combine(root, "Temp", "DependenciesDownloader", name + "_install.json");
            var migratedPath = Path.Combine(root, "ProjectSettings", "Scheherazade", "DependenciesDownloader", name + "_install.json");
            Directory.CreateDirectory(Path.GetDirectoryName(legacyPath));
            try
            {
                File.WriteAllText(legacyPath, JsonUtility.ToJson(new UnityPackageInstallRecord
                {
                    PackageName = name,
                    Version = "1.0.0",
                    InstalledAt = "2020-01-01T00:00:00Z",
                    TrackedFiles = new List<string> { "Assets/Plugins/Scheherazade/Editor/DependenciesDownloader/GooglePackageModels.cs" }
                }));
                UnityPackageTracker.GetInstallRecord(name);
                var migrated = UnityPackageTracker.GetInstallRecord(name);
                Assert.That(migrated.InstalledAt, Is.EqualTo("2020-01-01T00:00:00Z"));
                Assert.That(migrated.Fingerprints, Is.Null.Or.Empty);
                Assert.That(migrated.HasFingerprints, Is.False);
            }
            finally
            {
                if (File.Exists(legacyPath)) File.Delete(legacyPath);
                if (File.Exists(migratedPath)) File.Delete(migratedPath);
            }
        }

        [Test]
        public void ParseFacebookRelease_SelectsOfficialSdkZipAndPreservesDeliveryType()
        {
            const string json = "[{\"tag_name\":\"sdk-version-18.1.1\",\"assets\":[{\"name\":\"facebook-unity-sdk-18.1.1.zip\",\"browser_download_url\":\"https://example.com/facebook-unity-sdk-18.1.1.zip\"},{\"name\":\"symbols.zip\",\"browser_download_url\":\"https://example.com/symbols.zip\"}]}]";
            var method = typeof(GitHubReleaseFetcher).GetMethod("ParseReleaseJson", BindingFlags.NonPublic | BindingFlags.Static);
            var releases = (List<GitHubReleaseInfo>)method.Invoke(null,
                new object[] { json, "facebook", "facebook-sdk-for-unity", GitHubReleaseDelivery.UnityPackageZipAsset });
            Assert.That(releases, Has.Count.EqualTo(1));
            Assert.That(releases[0].Version, Is.EqualTo("18.1.1"));
            Assert.That(releases[0].IsZipArchive, Is.True);
            Assert.That(releases[0].DownloadUrl, Does.EndWith("facebook-unity-sdk-18.1.1.zip"));
        }

        [Test]
        public void ParseFacebookRelease_MismatchedAssetVersion_IsIgnored()
        {
            const string json = "[{\"tag_name\":\"sdk-version-18.1.1\",\"assets\":[{\"name\":\"facebook-unity-sdk-18.0.0.zip\",\"browser_download_url\":\"https://example.com/facebook-unity-sdk-18.0.0.zip\"}]}]";
            var method = typeof(GitHubReleaseFetcher).GetMethod("ParseReleaseJson", BindingFlags.NonPublic | BindingFlags.Static);
            var releases = (List<GitHubReleaseInfo>)method.Invoke(null,
                new object[] { json, "facebook", "facebook-sdk-for-unity", GitHubReleaseDelivery.UnityPackageZipAsset });
            Assert.That(releases, Is.Empty);
        }

        [Test]
        public void FacebookSession_RestoresPinnedZipTargetAndAssetsVerification()
        {
            var state = new DownloaderSessionState
            {
                FacebookAction = 1,
                FacebookTarget = new GitHubReleaseInfo
                {
                    Version = "18.1.1", TagName = "sdk-version-18.1.1", IsZipArchive = true,
                    DownloadUrl = "https://example.com/facebook-unity-sdk-18.1.1.zip"
                }
            };
            state.Save();
            var window = ScriptableObject.CreateInstance<DependenciesDownloaderWindow>();
            try
            {
                var flags = BindingFlags.NonPublic | BindingFlags.Instance;
                var type = typeof(DependenciesDownloaderWindow);
                Assert.That(type.GetField("_facebookAction", flags).GetValue(window).ToString(), Is.EqualTo("Install"));
                Assert.That(type.GetProperty("QueuedChangeCount", flags).GetValue(window), Is.EqualTo(1));
                Assert.That(type.GetProperty("RequiresDownloadFolder", flags).GetValue(window), Is.False);
                var verification = (List<PackageVerification>)type.GetMethod("CreateVerificationPlan", flags).Invoke(window,
                    new object[] { Path.GetFullPath(Path.Combine(Application.dataPath, "..", "LocalPackages")) });
                Assert.That(verification[0].PackageName, Is.EqualTo("Facebook"));
                Assert.That(verification[0].AssetsImport, Is.True);
                Assert.That(verification[0].ReleaseTarget.IsZipArchive, Is.True);
                type.GetMethod("ClearQueue", flags).Invoke(window, null);
                Assert.That(DownloaderSessionState.Load().FacebookAction, Is.EqualTo(0));
                Assert.That(DownloaderSessionState.Load().FacebookTarget, Is.Null);
            }
            finally { UnityEngine.Object.DestroyImmediate(window); }
        }

        [Test]
        public void FacebookRemoval_IsAnAssetsOperationWithoutDownloadFolderRequirement()
        {
            new DownloaderSessionState { FacebookAction = 2 }.Save();
            var window = ScriptableObject.CreateInstance<DependenciesDownloaderWindow>();
            try
            {
                var flags = BindingFlags.NonPublic | BindingFlags.Instance;
                var type = typeof(DependenciesDownloaderWindow);
                Assert.That(type.GetProperty("RequiresDownloadFolder", flags).GetValue(window), Is.False);
                type.GetMethod("RemoveVerifiedChange", flags).Invoke(window,
                    new object[] { new PackageVerification { PackageName = "Facebook", AssetsImport = true, Remove = true } });
                Assert.That(type.GetField("_facebookAction", flags).GetValue(window).ToString(), Is.EqualTo("None"));
            }
            finally { UnityEngine.Object.DestroyImmediate(window); }
        }

        [Test]
        public void FacebookZip_ValidPayload_IsExtractedWithBundledResolverWhenUnmanaged()
        {
            CreateFacebookZipFixture();
            Assert.That(FacebookSdkArchive.IsUsable(_temporaryPackagePath), Is.True);
            var payload = FacebookSdkArchive.PrepareForImport(_temporaryPackagePath, "18.1.1", false, null);
            var paths = UnityPackageTracker.EnumerateFilesInPackage(payload);
            Assert.That(paths, Does.Contain("Assets/FacebookSDK/Plugins/Facebook.Unity.dll"));
            Assert.That(paths, Does.Contain("Assets/ExternalDependencyManager/Editor/1.2.188/Google.JarResolver.dll"));
        }

        [Test]
        public void FacebookZip_UpmResolver_IsExcludedWhileOtherSdkAssetsArePreserved()
        {
            CreateFacebookZipFixture();
            var payload = FacebookSdkArchive.PrepareForImport(_temporaryPackagePath, "18.1.1", true, "1.2.188");
            var paths = UnityPackageTracker.EnumerateFilesInPackage(payload);
            Assert.That(paths, Does.Contain("Assets/FacebookSDK/Plugins/Facebook.Unity.dll"));
            Assert.That(paths, Does.Contain("Assets/IAPiOS/IAP/Editor/iOS/IAPUnityInterface.h"));
            Assert.That(paths.Exists(path => path.StartsWith("Assets/ExternalDependencyManager")), Is.False);
            Assert.That(FacebookSdkArchive.IsUsable(_temporaryPackagePath), Is.True, "The original release ZIP should remain intact.");
        }

        [Test]
        public void FacebookZip_IncompatibleUpmResolver_ExplainsRequiredUpdate()
        {
            CreateFacebookZipFixture();
            var exception = Assert.Throws<InvalidOperationException>(() =>
                FacebookSdkArchive.PrepareForImport(_temporaryPackagePath, "18.1.1", true, "1.2.186"));
            Assert.That(exception.Message, Does.Contain("EDM 1.2.188"));
            Assert.That(exception.Message, Does.Contain("Update EDM in Package Manager"));
        }

        [Test]
        public void FacebookZip_WrongQueuedVersion_IsRejectedBeforeImport()
        {
            CreateFacebookZipFixture();
            Assert.Throws<InvalidDataException>(() => FacebookSdkArchive.PrepareForImport(_temporaryPackagePath, "18.0.0", false, null));
        }

        [Test]
        public void FacebookZip_MultipleUnityPackages_IsRejectedAsAmbiguous()
        {
            CreateFacebookZipFixture(true);
            Assert.That(FacebookSdkArchive.IsUsable(_temporaryPackagePath), Is.False);
            Assert.Throws<InvalidDataException>(() => FacebookSdkArchive.PrepareForImport(_temporaryPackagePath, "18.1.1", false, null));
        }

        [Test]
        public void FacebookZip_PreCanceledPreparation_DoesNotExtractPayload()
        {
            CreateFacebookZipFixture();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            Assert.Throws<OperationCanceledException>(() => FacebookSdkArchive.PrepareForImport(
                _temporaryPackagePath, "18.1.1", false, null, cancellation.Token));
            Assert.That(File.Exists(Path.ChangeExtension(_temporaryPackagePath, ".unitypackage")), Is.False);
        }

        private void CreateFacebookZipFixture(bool extraPayload = false)
        {
            _temporaryPackagePath = Path.Combine(Path.GetTempPath(), $"facebook-downloader-test-{Guid.NewGuid():N}.zip");
            using var payload = new MemoryStream();
            using (var gzip = new GZipStream(payload, CompressionMode.Compress, leaveOpen: true))
            {
                WriteTarEntry(gzip, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/pathname", "Assets/FacebookSDK/Plugins/Facebook.Unity.dll");
                WriteTarEntry(gzip, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/asset", "Facebook SDK fixture");
                WriteTarEntry(gzip, "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb/pathname", "Assets/ExternalDependencyManager/Editor/1.2.188/Google.JarResolver.dll");
                WriteTarEntry(gzip, "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb/asset", "EDM fixture");
                WriteTarEntry(gzip, "cccccccccccccccccccccccccccccccc/pathname", "Assets/IAPiOS/IAP/Editor/iOS/IAPUnityInterface.h");
                WriteTarEntry(gzip, "cccccccccccccccccccccccccccccccc/asset", "Facebook native IAP fixture");
                gzip.Write(new byte[1024], 0, 1024);
            }
            using var file = File.Create(_temporaryPackagePath);
            using var archive = new ZipArchive(file, ZipArchiveMode.Create);
            using (var output = archive.CreateEntry("facebook-unity-sdk-18.1.1/FacebookSDK/facebook-unity-sdk-18.1.1.unitypackage").Open())
            {
                var bytes = payload.ToArray();
                output.Write(bytes, 0, bytes.Length);
            }
            if (extraPayload)
            {
                using var output = archive.CreateEntry("other.unitypackage").Open();
                output.WriteByte(1);
            }
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
