using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Com.Scheherazade.Common.DependenciesDownloader.Editor.Tests
{
    public sealed class DependencyInstallationPipelineTests
    {
        [TestCase("Adjust", false, ".unitypackage")]
        [TestCase("AppLovin", false, ".unitypackage")]
        [TestCase("Facebook", true, ".zip")]
        public void SdkArchives_AlwaysUseProjectTemp(string name, bool zip, string extension)
        {
            var entry = new DownloadEntry { PackageName = name, Version = "1.0.0", IsZipArchive = zip };
            var path = DependencyInstallationState.GetArchivePath(entry, "AnInvalidTarballFolder");
            Assert.That(Path.GetDirectoryName(path), Is.EqualTo(DependencyInstallationState.SdkDownloadDirectory));
            Assert.That(path, Does.EndWith(extension));
        }

        [Test]
        public void Tarballs_RemainInPersistentManifestDirectory()
        {
            var path = DependencyInstallationState.GetArchivePath(
                new DownloadEntry { PackageName = "com.google.root", Version = "2.0.0" }, "LocalPackages");
            Assert.That(path, Is.EqualTo(Path.Combine("LocalPackages", "com.google.root-2.0.0.tgz")));
        }

        [Test]
        public void UpmBatch_AppliesAndRefreshesOnceBeforeAnySdkDialog()
        {
            var session = CreateSession(true);
            var host = new FakeHost();
            var controller = CreateController(session, host);
            controller.Tick();
            controller.Tick();
            for (var i = 0; i < 5; i++) controller.Tick();
            Assert.That(host.ManifestWrites, Is.EqualTo(1));
            Assert.That(host.TarballsApplied, Is.EqualTo(2));
            Assert.That(host.Refreshes, Is.EqualTo(1));
            Assert.That(host.Opened, Is.Empty);
            Assert.That(session.Installation.Phase, Is.EqualTo(DependencyInstallationPhase.WaitingForPackageManager));
        }

        [Test]
        public void RegisteredUpmPackages_StillWaitForCompilationAndReloadBeforeSdk()
        {
            var session = CreateSession(true);
            var host = new FakeHost { PackageManagerReady = true };
            var controller = CreateController(session, host);
            controller.Tick();
            controller.Tick();
            controller.Tick();
            controller.Tick();
            controller.CompilationStarted();
            controller.AssemblyCompilationFinished(false);
            controller.CompilationFinished();
            for (var i = 0; i < 4; i++) controller.Tick();
            Assert.That(host.Opened, Is.Empty, "Compilation completion alone is not an assembly reload.");
            controller.BeforeAssemblyReload();
            host.Generation++;
            controller = CreateController(session, host);
            controller.Tick();
            controller.Tick();
            controller.Tick();
            Assert.That(host.Opened, Has.Count.EqualTo(1));
            Assert.That(host.ManifestWrites, Is.EqualTo(1));
            Assert.That(host.Refreshes, Is.EqualTo(1));
        }

        [Test]
        public void OpenImportDialog_DoesNotTimeOutOrOpenAnotherWhileUserIsChoosingAssets()
        {
            var session = CreateSession(false);
            var host = new FakeHost();
            var controller = CreateController(session, host);
            controller.Tick();
            session.Installation.PhaseStartedUtcTicks = DateTime.UtcNow.AddHours(-1).Ticks;
            for (var i = 0; i < 10; i++) controller.Tick();
            Assert.That(host.Opened, Has.Count.EqualTo(1));
            Assert.That(session.Installation.Phase, Is.EqualTo(DependencyInstallationPhase.WaitingForImport));
        }

        [Test]
        public void SelectedAssets_AreRecordedBeforeCompilationButNextSdkWaits()
        {
            var session = CreateSession(false);
            var host = new FakeHost();
            var controller = CreateController(session, host);
            controller.Tick();
            CompleteCurrentImport(controller, host);
            controller.Tick();
            Assert.That(host.FinalizedPaths, Is.EquivalentTo(new[] { "Assets/Adjust/Core.cs" }));
            Assert.That(host.Opened, Has.Count.EqualTo(1));
            Assert.That(session.AdjustAction, Is.EqualTo(1));
            host.IsBusy = true;
            for (var i = 0; i < 3; i++) controller.Tick();
            Assert.That(host.Opened, Has.Count.EqualTo(1));
            host.IsBusy = false;
            FinishCompilation(ref controller, session, host);
            controller.Tick();
            Assert.That(host.Opened, Has.Count.EqualTo(2));
            Assert.That(session.AdjustAction, Is.EqualTo(0));
            Assert.That(session.FacebookAction, Is.EqualTo(1));
        }

        [Test]
        public void ReloadCheckpoint_ResumesWithoutReopeningCompletedImportOrReapplyingUpm()
        {
            var session = CreateSession(false);
            var host = new FakeHost();
            var controller = CreateController(session, host);
            controller.Tick();
            CompleteCurrentImport(controller, host);
            controller.Tick();
            controller.Tick();
            controller.CompilationStarted();
            controller.AssemblyCompilationFinished(false);
            controller.CompilationFinished();
            controller.BeforeAssemblyReload();
            var restored = JsonUtility.FromJson<DownloaderSessionState>(JsonUtility.ToJson(session));
            host.Generation++;
            controller = CreateController(restored, host);
            controller.Tick();
            controller.Tick();
            controller.Tick();
            Assert.That(host.Opened, Has.Count.EqualTo(2));
            Assert.That(Path.GetFileName(host.Opened[1]), Does.StartWith("Facebook"));
            Assert.That(restored.Installation.ImportIndex, Is.EqualTo(1));
            Assert.That(host.ManifestWrites, Is.EqualTo(0));
            Assert.That(host.Refreshes, Is.EqualTo(0));
        }

        [Test]
        public void CompilationFailure_PausesBeforeNextPackageAndCanResumeAfterFix()
        {
            var session = CreateSession(false);
            var host = new FakeHost();
            var controller = CreateController(session, host);
            controller.Tick();
            CompleteCurrentImport(controller, host);
            controller.Tick();
            controller.Tick();
            controller.CompilationStarted();
            controller.AssemblyCompilationFinished(true);
            controller.CompilationFinished();
            controller.Tick();
            Assert.That(session.Installation.Phase, Is.EqualTo(DependencyInstallationPhase.Paused));
            Assert.That(host.Opened, Has.Count.EqualTo(1));
            Assert.That(session.AdjustAction, Is.EqualTo(1));
            controller.Resume();
            FinishCompilation(ref controller, session, host);
            controller.Tick();
            Assert.That(host.Opened, Has.Count.EqualTo(2));
        }

        [Test]
        public void CancelledNativeImport_PreservesItsQueueAndDoesNotOpenNext()
        {
            var session = CreateSession(false);
            var host = new FakeHost();
            var controller = CreateController(session, host);
            controller.Tick();
            controller.ImportFailed(Path.GetFileNameWithoutExtension(host.Opened[0]), "Import cancelled.");
            controller.Tick();
            Assert.That(session.Installation.Phase, Is.EqualTo(DependencyInstallationPhase.Paused));
            Assert.That(session.AdjustAction, Is.EqualTo(1));
            Assert.That(session.FacebookAction, Is.EqualTo(1));
            Assert.That(host.Opened, Has.Count.EqualTo(1));
        }

        [Test]
        public void CancelRemainingSequence_FinishesCurrentCompilationAndRetainsNextQueue()
        {
            var session = CreateSession(false);
            var host = new FakeHost();
            var controller = CreateController(session, host);
            controller.Tick();
            controller.Cancel();
            CompleteCurrentImport(controller, host);
            controller.Tick();
            FinishCompilation(ref controller, session, host);
            controller.Tick();
            Assert.That(session.Installation.Phase, Is.EqualTo(DependencyInstallationPhase.Cancelled));
            Assert.That(host.Opened, Has.Count.EqualTo(1));
            Assert.That(session.AdjustAction, Is.EqualTo(0));
            Assert.That(session.FacebookAction, Is.EqualTo(1));
        }

        [Test]
        public void OtherPackageCallbacks_DoNotFinalizeTheCurrentSdk()
        {
            var session = CreateSession(false);
            var host = new FakeHost();
            var controller = CreateController(session, host);
            controller.Tick();
            controller.ImportStarted("UnrelatedSdk");
            controller.ImportItemsCompleted(new[] { "Assets/Adjust/Core.cs" });
            controller.ImportCompleted("UnrelatedSdk");
            Assert.That(session.Installation.Phase, Is.EqualTo(DependencyInstallationPhase.WaitingForImport));
            Assert.That(session.Installation.Imports[0].SelectionReported, Is.False);
        }

        [Test]
        public void CancelBeforeUpmApplication_DoesNotWriteOrRefresh()
        {
            var session = CreateSession(true);
            var host = new FakeHost();
            var controller = CreateController(session, host);
            controller.Cancel();
            controller.Tick();
            Assert.That(host.ManifestWrites, Is.EqualTo(0));
            Assert.That(host.Refreshes, Is.EqualTo(0));
        }

        private static DownloaderSessionState CreateSession(bool upm)
        {
            var session = new DownloaderSessionState { AdjustAction = 1, FacebookAction = 1 };
            session.Installation = new DependencyInstallationState
            {
                Phase = upm ? DependencyInstallationPhase.ApplyingManifest : DependencyInstallationPhase.ReadyToImport,
                TarballDirectory = "LocalPackages"
            };
            foreach (var name in new[] { "Adjust", "Facebook" })
            {
                var entry = new DownloadEntry { PackageName = name, Version = "1.0.0" };
                session.Installation.Imports.Add(new StagedSdkImport
                {
                    Entry = entry, ArchivePath = DependencyInstallationState.GetArchivePath(entry, "LocalPackages")
                });
                session.Verification.Add(new PackageVerification { PackageName = name, Version = "1.0.0", AssetsImport = true });
            }
            if (upm)
            {
                foreach (var name in new[] { "com.google.one", "com.google.two" })
                {
                    session.Installation.Tarballs.Add(new DownloadEntry { PackageName = name, Version = "1.0.0" });
                    session.Verification.Add(new PackageVerification { PackageName = name, Version = "1.0.0", ManifestValue = "1.0.0" });
                    session.GoogleChanges.Add(new QueuedGoogleChange { PackageName = name, Version = "1.0.0" });
                }
            }
            return session;
        }

        private static DependencyInstallationController CreateController(DownloaderSessionState session, FakeHost host)
        {
            return new DependencyInstallationController(session, host, () => { });
        }

        private static void CompleteCurrentImport(DependencyInstallationController controller, FakeHost host)
        {
            var name = Path.GetFileNameWithoutExtension(host.Opened.Last());
            controller.ImportStarted(name);
            controller.ImportCompleted(name);
            controller.ImportItemsCompleted(new[] { "Assets/Adjust/Core.cs" });
        }

        private static void FinishCompilation(ref DependencyInstallationController controller, DownloaderSessionState session, FakeHost host)
        {
            controller.Tick();
            controller.CompilationStarted();
            controller.AssemblyCompilationFinished(false);
            controller.CompilationFinished();
            controller.BeforeAssemblyReload();
            host.Generation++;
            controller = CreateController(session, host);
            controller.Tick();
            controller.Tick();
        }

        private sealed class FakeHost : IDependencyInstallationHost
        {
            public bool IsBusy { get; set; }
            public int Generation { get; set; }
            public bool PackageManagerReady;
            public int ManifestWrites;
            public int TarballsApplied;
            public int Refreshes;
            public readonly List<string> Opened = new List<string>();
            public List<string> FinalizedPaths;
            private readonly Dictionary<string, string> _manifest = new Dictionary<string, string>();

            public bool ManifestMatches(IEnumerable<PackageVerification> expectations)
                => expectations.All(item => _manifest.TryGetValue(item.PackageName, out var value) && value == item.ManifestValue);
            public bool ApplyManifest(DependencyInstallationState state)
            {
                ManifestWrites++;
                TarballsApplied = state.Tarballs.Count;
                foreach (var entry in state.Tarballs) _manifest[entry.PackageName] = entry.Version;
                return true;
            }
            public void RefreshPackageManager() { Refreshes++; }
            public PackageManagerInstallationSnapshot PollPackageManager() => new PackageManagerInstallationSnapshot
            {
                Ready = PackageManagerReady,
                Packages = _manifest.ToDictionary(pair => pair.Key, pair => new RegisteredDependency { Version = pair.Value })
            };
            public string GetManifestValue(string name) => _manifest.TryGetValue(name, out var value) ? value : string.Empty;
            public void PrepareImport(StagedSdkImport import)
            {
                import.PackagePath = import.ArchivePath;
                import.CandidatePaths = new List<string> { "Assets/" + import.Entry.PackageName + "/Core.cs", "Assets/" + import.Entry.PackageName + "/Example.txt" };
            }
            public void OpenImportDialog(string path) { Opened.Add(path); }
            public string FinalizeImport(StagedSdkImport import) { FinalizedPaths = import.SelectedPaths.ToList(); return null; }
            public string RemoveAssets(StagedSdkRemoval removal) => null;
            public void RequestCompilation() { }
        }
    }
}
