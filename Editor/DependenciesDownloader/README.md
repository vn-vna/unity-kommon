# Dependencies Downloader

Open **Dev Menu → Tools → Dependencies Downloader**.

## Workflow

1. Refresh the registry or SDK releases when the cached data is missing or old.
2. Browse versions and use **Queue install/update/downgrade**, **Queue reinstall**, or **Queue removal**.
3. Review the pending changes and required dependencies. **Undo** removes an individual queued change; **Discard** clears the queue.
4. Apply the changes. Downloads are validated first. All tarball/Git changes are written to the manifest in one batch, followed by one UPM resolution/refresh and compilation barrier.
5. SDK `.unitypackage` files then open **one at a time in Unity's import dialog**. Select assets and import (or cancel). The next dialog opens only after the current import, selected-asset tracking, script compilation, and assembly reload finish successfully.
6. Review **Last operation results**. Compilation errors or cancelled imports pause the sequence. Fix the error and use **Resume sequence**, or **Stop sequence** to retain the remaining queue for retry.

Browsing does not automatically queue updates. A queued GitHub target retains its release tag, version, and URL when release lists are refreshed. Changing its version explicitly edits the queued target.

## Package ownership

- Rows show the registered version, delivery source, and direct/transitive dependency role.
- Transitive dependencies cannot be removed independently of their parents.
- UPM-managed Adjust/AppLovin installations are opened in Package Manager for changes.
- Existing Assets installation records are preserved when UPM ownership is detected, so duplicate installations can be inspected.
- Firebase module requirements are checked for aligned versions. Existing EDM installations satisfy an equal or newer minimum version without changing their registry ownership.

## Progress and recovery

Downloads and registry refreshes have inline progress and cancellation. Closing the window cancels work that is still downloading. Once the installation sequence starts, an Editor-owned coordinator keeps its checkpoints across window closure and assembly reloads. It does not lock reloads across imports.

**Cancel remaining sequence** stops before the next package after the current import/compilation completes. Finish or cancel an already-open Unity import dialog. A cancelled native dialog preserves that package and all later packages in the queue.

Queue, operation checkpoints, and results are stored in Unity `SessionState`: they survive window closure and assembly reloads within the current Editor session, but not an Editor restart.

## Settings and tracked assets

**Settings** contains a project-scoped, project-relative **Tarball folder**. Tarballs remain there because UPM's manifest links must persist. A valid folder is required only for tarball downloads.

SDK `.unitypackage` archives and Facebook ZIP wrappers are always staged in `Temp/DependenciesDownloader/Downloads/`; they do not use the tarball folder. Only the SDK assets selected in Unity's import dialog are newly tracked. Previously tracked assets retained by the new package remain tracked during an update.

Assets SDK installation records live in `ProjectSettings/Scheherazade/DependenciesDownloader/`. New installations record file and `.meta` fingerprints. Local modifications are reported and require explicit review before an update/removal. Legacy records remain readable, but cannot identify historical modifications; fingerprint tracking starts at the next successful installation. **Inspect tracked paths** and **Copy tracked paths** expose the affected files.

## Facebook SDK

The **Facebook SDK** tab reads releases from Meta's official `facebook/facebook-sdk-for-unity` repository. Select a version, queue an installation/update/removal, and apply through the same tracked Assets workflow as the other `.unitypackage` SDKs.

Facebook distributes `facebook-unity-sdk-<version>.zip`, containing one `.unitypackage`. The downloader validates and extracts that payload, and verifies the package version before import.

Facebook bundles External Dependency Manager (EDM). If EDM is already UPM-managed, its installed version must meet the bundled minimum; the bundled Assets copy is excluded from the import and Facebook's tracking record. Use **Open EDM in Package Manager** to update it when required. For example, Facebook 18.1.1 bundles EDM 1.2.188. If EDM is not UPM-managed, the bundle is imported and tracked with the SDK.

## Verification

The EditMode test assembly is `Com.Scheherazade.DependenciesDownloader.EditorTests`. It covers ownership, dependency planning, version identities, pinned targets, session recovery, cancellation, archive metadata, file fingerprints, and installed-state verification without installing production SDKs.
