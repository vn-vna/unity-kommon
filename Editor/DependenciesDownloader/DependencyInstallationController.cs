using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Com.Scheherazade.Common.DependenciesDownloader.Editor
{
    public sealed class PackageManagerInstallationSnapshot
    {
        public bool Ready;
        public string Error;
        public Dictionary<string, RegisteredDependency> Packages = new Dictionary<string, RegisteredDependency>();
    }

    public interface IDependencyInstallationHost
    {
        bool IsBusy { get; }
        int Generation { get; }
        bool ManifestMatches(IEnumerable<PackageVerification> expectations);
        bool ApplyManifest(DependencyInstallationState state);
        void RefreshPackageManager();
        PackageManagerInstallationSnapshot PollPackageManager();
        string GetManifestValue(string packageName);
        void PrepareImport(StagedSdkImport import);
        void OpenImportDialog(string packagePath);
        string FinalizeImport(StagedSdkImport import);
        string RemoveAssets(StagedSdkRemoval removal);
        void RequestCompilation();
    }

    public sealed class DependencyInstallationController
    {
        private readonly DownloaderSessionState _session;
        private readonly IDependencyInstallationHost _host;
        private readonly Action _checkpoint;
        private readonly DependencyInstallationState _state;

        public DependencyInstallationController(DownloaderSessionState session, IDependencyInstallationHost host, Action checkpoint)
        {
            _session = session;
            _host = host;
            _checkpoint = checkpoint;
            _state = session.Installation;
            if (_host.Generation > _state.BarrierGeneration) _state.ReloadPending = false;
        }

        public void Tick()
        {
            if (!_state.IsActive || _host.IsBusy) return;
            try
            {
                switch (_state.Phase)
                {
                    case DependencyInstallationPhase.ApplyingManifest: ApplyManifest(); break;
                    case DependencyInstallationPhase.WaitingForPackageManager: WaitForPackageManager(); break;
                    case DependencyInstallationPhase.ReadyToImport: OpenNextImport(); break;
                    case DependencyInstallationPhase.FinalizingImport: FinalizeImport(); break;
                    case DependencyInstallationPhase.WaitingForCompilation: WaitForCompilation(); break;
                    case DependencyInstallationPhase.RemovingAssets: RemoveNextAssets(); break;
                }
            }
            catch (Exception ex)
            {
                Pause(_state.Phase, ex.Message);
            }
        }

        private void ApplyManifest()
        {
            if (StopIfCancelled()) return;
            ResetCompilationObservation();
            Save();
            var expected = _session.Verification.Where(item => !item.AssetsImport).ToList();
            if (!_state.ManifestApplied && !_host.ManifestMatches(expected) && !_host.ApplyManifest(_state))
                throw new InvalidOperationException("Could not apply the Package Manager batch. SDK imports have not started.");
            _state.ManifestApplied = true;
            foreach (var item in expected) item.Applied = true;
            _session.VerificationPending = expected.Count > 0;
            Transition(DependencyInstallationPhase.WaitingForPackageManager, "Resolving the Package Manager batch before SDK imports...");
        }

        private void WaitForPackageManager()
        {
            if (!_state.ResolutionRequested)
            {
                _state.ResolutionRequested = true;
                Save();
                _host.RefreshPackageManager();
                return;
            }
            var snapshot = _host.PollPackageManager();
            if (!string.IsNullOrEmpty(snapshot.Error)) throw new InvalidOperationException(snapshot.Error);
            if (!snapshot.Ready) { CheckTimeout(); return; }
            var failures = new List<string>();
            foreach (var expected in _session.Verification.Where(item => !item.AssetsImport))
            {
                var failure = DependencyInstallationVerifier.GetFailure(expected, snapshot.Packages, _host.GetManifestValue(expected.PackageName));
                if (failure != null) failures.Add(expected.PackageName + ": " + failure);
            }
            if (failures.Count > 0)
            {
                _state.Status = "Waiting for UPM: " + string.Join("; ", failures);
                CheckTimeout();
                return;
            }
            foreach (var expected in _session.Verification.Where(item => !item.AssetsImport))
            {
                _session.RecordResult(expected.PackageName, expected.Remove ? "UPM removal verified." : $"UPM {expected.Version} verified.", true);
                _session.RemoveQueuedChange(expected);
            }
            UpdateVerificationPending();
            BeginCompilationBarrier(DependencyInstallationPhase.ReadyToImport);
        }

        private void OpenNextImport()
        {
            if (StopIfCancelled()) return;
            if (_state.ImportIndex >= _state.Imports.Count)
            {
                Transition(DependencyInstallationPhase.RemovingAssets, "Applying tracked SDK removals...");
                return;
            }
            var import = _state.Imports[_state.ImportIndex];
            _host.PrepareImport(import);
            if (import.CandidatePaths.Count == 0) throw new InvalidDataException("The staged SDK package has no importable assets.");
            import.SelectionReported = false;
            import.ImportStarted = false;
            import.SelectedPaths.Clear();
            ResetCompilationObservation();
            Transition(DependencyInstallationPhase.WaitingForImport,
                $"Import ({_state.ImportIndex + 1}/{_state.Imports.Count}) {import.Entry.PackageName}: choose assets in Unity's import dialog.");
            _host.OpenImportDialog(import.PackagePath);
        }

        public void ImportStarted(string packageName)
        {
            if (_state.Phase != DependencyInstallationPhase.WaitingForImport) return;
            CurrentImport.ImportStarted = IsCurrentPackage(packageName);
            Save();
        }

        public void ImportItemsCompleted(string[] paths)
        {
            if (_state.Phase != DependencyInstallationPhase.WaitingForImport || !CurrentImport.ImportStarted) return;
            CurrentImport.SelectedPaths = paths.Select(path => path.Replace('\\', '/'))
                .Where(path => CurrentImport.CandidatePaths.Contains(path)).Distinct().ToList();
            CurrentImport.SelectionReported = true;
            CompleteImport();
        }

        public void ImportCompleted(string packageName)
        {
            if (_state.Phase != DependencyInstallationPhase.WaitingForImport || !IsCurrentPackage(packageName)) return;
            // Unity's selected-items callback may follow the package-name callback.
            CurrentImport.ImportStarted = true;
            if (CurrentImport.SelectionReported) CompleteImport();
            else Save();
        }

        private void CompleteImport()
        {
            if (CurrentImport.SelectedPaths.Count == 0)
            {
                Pause(DependencyInstallationPhase.ReadyToImport, "No SDK assets were selected. The remaining imports are paused.");
                return;
            }
            var expected = _session.Verification.Find(item => item.PackageName == CurrentImport.Entry.PackageName);
            if (expected != null) expected.Applied = true;
            _session.VerificationPending = true;
            Transition(DependencyInstallationPhase.FinalizingImport, "Recording selected SDK assets and cleaning obsolete tracked files...");
        }

        public void ImportFailed(string packageName, string error)
        {
            if (_state.Phase == DependencyInstallationPhase.WaitingForImport && IsCurrentPackage(packageName))
                Pause(DependencyInstallationPhase.ReadyToImport, CurrentImport.Entry.PackageName + ": " + error);
        }

        private void FinalizeImport()
        {
            var error = _host.FinalizeImport(CurrentImport);
            if (!string.IsNullOrEmpty(error)) throw new InvalidOperationException(error);
            // Request the final compilation after stale-file cleanup, even when the
            // import itself already triggered an earlier compilation/reload.
            ResetCompilationObservation();
            BeginCompilationBarrier(DependencyInstallationPhase.ReadyToImport);
        }

        private void BeginCompilationBarrier(DependencyInstallationPhase next)
        {
            _state.AfterCompilation = next;
            _state.IdleObservations = 0;
            Transition(DependencyInstallationPhase.WaitingForCompilation, "Waiting for script compilation and assembly reload before continuing...");
        }

        private void WaitForCompilation()
        {
            if (!_state.CompilationRequested && !_state.CompilationObserved)
            {
                _state.CompilationRequested = true;
                Save();
                _host.RequestCompilation();
                return;
            }
            if (_state.CompilationFailed)
            {
                Pause(DependencyInstallationPhase.WaitingForCompilation, "Script compilation failed. Fix the Console errors, then resume the installation sequence.");
                return;
            }
            if (!_state.CompilationFinished || _state.ReloadPending ||
                (_state.AssemblyCompiled && _host.Generation <= _state.BarrierGeneration))
            {
                CheckTimeout();
                return;
            }
            if (++_state.IdleObservations < 2) return;
            if (_state.AfterCompilation == DependencyInstallationPhase.ReadyToImport && _state.ImportIndex < _state.Imports.Count &&
                CurrentImport.SelectionReported)
            {
                var expected = _session.Verification.Find(item => item.PackageName == CurrentImport.Entry.PackageName);
                _session.RecordResult(CurrentImport.Entry.PackageName,
                    $"Imported {CurrentImport.SelectedPaths.Count} selected asset(s) from {CurrentImport.Entry.Version}; compilation completed.", true);
                if (expected != null) _session.RemoveQueuedChange(expected);
                _state.ImportIndex++;
            }
            UpdateVerificationPending();
            if (StopIfCancelled()) return;
            Transition(_state.AfterCompilation, "Compilation completed. Continuing the installation sequence...");
        }

        public void CompilationStarted()
        {
            if (!_state.IsActive) return;
            _state.CompilationObserved = true;
            _state.CompilationFinished = false;
            _state.CompilationFailed = false;
            _state.IdleObservations = 0;
            Save();
        }

        public void AssemblyCompilationFinished(bool failed)
        {
            if (!_state.IsActive) return;
            _state.AssemblyCompiled = true;
            _state.CompilationFailed |= failed;
            Save();
        }

        public void CompilationFinished()
        {
            if (!_state.IsActive) return;
            _state.CompilationFinished = true;
            Save();
        }

        public void BeforeAssemblyReload()
        {
            if (!_state.IsActive) return;
            _state.ReloadPending = true;
            Save();
        }

        private void RemoveNextAssets()
        {
            if (StopIfCancelled()) return;
            if (_state.RemovalIndex >= _state.Removals.Count)
            {
                _session.Interrupted = false;
                UpdateVerificationPending();
                Transition(DependencyInstallationPhase.Completed, "Package Manager changes and selected SDK imports completed and compiled.");
                return;
            }
            var removal = _state.Removals[_state.RemovalIndex];
            var error = _host.RemoveAssets(removal);
            if (!string.IsNullOrEmpty(error)) throw new InvalidOperationException(error);
            var expected = _session.Verification.Find(item => item.PackageName == removal.PackageName);
            if (expected != null)
            {
                expected.Applied = true;
                _session.RemoveQueuedChange(expected);
            }
            _session.RecordResult(removal.PackageName, "Tracked SDK removal completed.", true);
            _state.RemovalIndex++;
            ResetCompilationObservation();
            BeginCompilationBarrier(DependencyInstallationPhase.RemovingAssets);
        }

        public void Cancel()
        {
            if (_state.Phase == DependencyInstallationPhase.Paused)
            {
                Transition(DependencyInstallationPhase.Cancelled, "Sequence stopped. Remaining changes are preserved for retry.");
                return;
            }
            _state.CancelAfterCurrent = true;
            _state.Status = "Stopping after the current import/compilation. Finish or cancel any open import dialog.";
            Save();
        }

        public void Resume()
        {
            if (_state.Phase != DependencyInstallationPhase.Paused || _host.IsBusy) return;
            _state.CancelAfterCurrent = false;
            if (_state.ResumePhase == DependencyInstallationPhase.WaitingForCompilation) ResetCompilationObservation();
            Transition(_state.ResumePhase, "Resuming the installation sequence...");
        }

        private void Pause(DependencyInstallationPhase resumePhase, string error)
        {
            _state.ResumePhase = resumePhase;
            _session.RecordResult(CurrentPackageName, error, false);
            Transition(DependencyInstallationPhase.Paused, error);
        }

        private bool StopIfCancelled()
        {
            if (!_state.CancelAfterCurrent) return false;
            _session.Interrupted = false;
            Transition(DependencyInstallationPhase.Cancelled, "Sequence cancelled. Verified changes are retained; remaining changes stay queued.");
            return true;
        }

        private void ResetCompilationObservation()
        {
            _state.BarrierGeneration = _host.Generation;
            _state.CompilationRequested = false;
            _state.CompilationObserved = false;
            _state.CompilationFinished = false;
            _state.AssemblyCompiled = false;
            _state.CompilationFailed = false;
            _state.ReloadPending = false;
            _state.IdleObservations = 0;
        }

        private void UpdateVerificationPending()
        {
            _session.VerificationPending = _session.Verification.Any(item => item.Applied &&
                !_session.Results.Any(result => result.PackageName == item.PackageName && result.Succeeded));
        }

        private void CheckTimeout()
        {
            if (_state.PhaseStartedUtcTicks > 0 && DateTime.UtcNow.Ticks - _state.PhaseStartedUtcTicks > TimeSpan.FromMinutes(5).Ticks)
                throw new TimeoutException("This stage has not completed. Inspect Unity's package/compilation status, then resume the sequence.");
        }

        private StagedSdkImport CurrentImport => _state.Imports[_state.ImportIndex];
        private string CurrentPackageName => _state.Phase == DependencyInstallationPhase.ApplyingManifest ||
            _state.Phase == DependencyInstallationPhase.WaitingForPackageManager ? "Package Manager"
            : _state.ImportIndex < _state.Imports.Count ? CurrentImport.Entry.PackageName : "Installation sequence";

        private bool IsCurrentPackage(string name)
        {
            const string extension = ".unitypackage";
            var fileName = Path.GetFileName(name ?? string.Empty);
            if (fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                fileName = fileName.Substring(0, fileName.Length - extension.Length);
            return string.Equals(fileName, Path.GetFileNameWithoutExtension(CurrentImport.PackagePath), StringComparison.OrdinalIgnoreCase);
        }

        private void Transition(DependencyInstallationPhase phase, string status)
        {
            _state.Phase = phase;
            _state.Status = status;
            _state.PhaseStartedUtcTicks = DateTime.UtcNow.Ticks;
            Save();
        }

        private void Save()
        {
            _session.Revision++;
            _checkpoint();
        }
    }
}
