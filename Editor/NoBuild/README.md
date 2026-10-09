# NoBuild

Open **Dev Menu → Tools → NoBuild** or use **Manage…** in the NoBuild toolbar menu.

## Scene and define workflows

- **Open Scene Set** replaces the current scene setup after Unity's modified-scene save prompt. The first enabled, valid scene becomes active.
- **Capture Open Scenes** captures saved, loaded scenes with the active scene first. Save untitled scenes before capturing them.
- **Copy Scene Order to Build Override** gives builds their own editable scene order.
- Green state indicators and **Matches Editor** filters are based on actual open scenes or configured Player Settings symbols. A selected scene set also supplies the shortcut-combination context; opening a combination can intentionally produce a different scene setup.
- Define application merges into the displayed Editor target: enabled symbols are added, disabled symbols are removed, and symbols outside the set are preserved. Editing an applied set makes Apply available again when its symbols differ.
- Scene and define selections are session-local; define context is tracked per target group. Tabs, selection, search, expansion, and scroll survive assembly reloads.

## Quick menu and shortcuts

Search the toolbar popup, then use **Up/Down** to select and **Enter** to execute. **Escape** clears a search. Full names are available in tooltips.

Scene sets use the existing `NoBuild/Switch To Scene Set 1…12` bindings. Combinations use fixed slots `1…9`: disabling or invalidating slot 1 does not move slot 2 onto binding 1. The settings UI and tooltips display the actual bindings from Unity's Shortcut Manager.

## Builds

Toolbar and settings actions open the same confirmation with target, backend, mode, artifact, scenes, define changes, and the exact output path including its extension.

Preflight reports missing references, invalid/conflicting symbols, incompatible Android output options, missing build support, and unknown template tokens. **None** explicitly preserves existing defines; a missing define-set reference is an error. If a profile removes a currently enabled symbol, use **Apply Defines & Compile**, wait for Unity, then build again.

Use **Insert Token…** for templates, including named flag tokens generated from the configured flag IDs. Flag IDs must be unique. File-name values containing path separators are normalized to avoid accidental subfolders; relative build folders are resolved from the project root.

The most recent result is shown in settings with **Reveal Last Build** and **Copy Output Path**. Native builds continue to use Unity's own build progress UI. Build & Run reports outcomes per device.

## Android devices

- Device refresh uses cached, asynchronous `adb devices -l` results and shows authorization/offline guidance.
- **Select APK/AAB…** displays the selected artifact and verifies its package. APK inspection uses Android SDK `aapt`/`aapt2`; AAB inspection uses Java and bundletool.
- Installation verifies artifact identity again before recovery can uninstall anything. **Uninstall & Reinstall** is offered for version/signature conflicts and explicitly removes local app data.
- Wireless discovery, pairing, install, and launch show status and support **Cancel Device Operation**. Only one NoBuild device operation runs at a time.
- **Tool Paths** exposes local ADB, Java, and bundletool overrides. Missing bundletool is downloaded into `Library/NoBuild`; failures leave an actionable result, and partially downloaded files are cleaned up.
- The Launch Package field is explicit and updates when an artifact is inspected. Remembered device preferences are project-scoped.

## Verification

Run the EditMode assembly `Com.Scheherazade.NoBuild.EditorTests`. It covers preflight, output paths, define/flag authority, state persistence, device parsing, recovery policy, scene-reference stability, and process timeout/cancellation.
