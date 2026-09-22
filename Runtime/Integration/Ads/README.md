# Scheherazade Ads invocation API

Every ad presentation command returns an `AdsInvocationHandler`. Lifecycle commands (`Initialize` and `Shutdown`) remain manager lifecycle methods.

```csharp
AdsInvocationHandler invocation = Integration.AdsManager.ShowRewardAds("revive");
IDisposable observation = invocation.Observe(current =>
{
    if (current.Status == AdsInvocationStatus.Showing) { /* shown */ }
    if (!current.IsTerminal) return;

    bool grantReward = current.Status == AdsInvocationStatus.Succeeded
        && current.RewardEarned;
});
```

`Observe` reports the current snapshot immediately by default, so already-terminal failures and skipped interstitials are observable without races. Observers are isolated from one another; dispose an observation when an external timeout or owner lifecycle stops waiting. Terminal handlers release their observers.

Statuses:
- `Pending`: accepted but not displayed.
- `Showing`: a native display was confirmed.
- `Succeeded`: the format-specific operation completed. Rewarded success must also have `RewardEarned` before granting gameplay content.
- `Skipped`: intentional manager policy bypass, currently interstitial cooldown. Magima treats this as successful navigation without a display.
- `Failed`: unavailable, rejected, invalid, or failed display.
- `Cancelled`: an accepted invocation ended without its success condition.

The handler also retains `WasDisplayed`, `WasClosed`, `RewardEarned`, placement and a diagnostic reason. Providers own monotonic state transitions; managers return the provider's handler rather than reconstructing SDK state.

## Base manager

`AdsManagerBase<T>` owns registration, lifecycle, provider selection, counters and interstitial interval. It always returns a handler:
- missing provider/provider exception/wrong handler type => terminal `Failed`;
- unmet non-forced interstitial interval => terminal `Skipped`;
- accepted provider invocation => the provider's handler.

Counters and fullscreen cooldown update on the first confirmed `Showing` snapshot, not on a show attempt. Rewarded display also restarts the interstitial interval. Shutdown rejects reentrant invocations while provider cleanup publishes terminal state.

## AppLovin MAX provider

The designated `ApplovinMaxAdsServiceProvider` supports one fullscreen invocation across rewarded, interstitial and app-open formats. It owns the handler before calling native Show, does not fall rewarded back to interstitial, preserves tracking/ILRD, and reloads only after terminal release.

Rewarded handling:
1. Record the native reward before any user-visible Showing callback.
2. Require native Hidden before normal success.
3. If Hidden arrives first, start an independent post-close grace period on the following Unity tick.
4. A late reward within the grace period succeeds; expiry cancels as closed without reward.
5. Cleanup preserves already-earned credit. Unknown native closure quarantines fullscreen ads for the session to prevent a stale callback from completing another request.

Native callback correlation uses format, unit ID and non-empty placement metadata. MAX exposes no impression ID, so quarantine is still required after an unsafe timeout.


## Banner state and size

`IAdsManager.BannerState` exposes `AdsBannerStatus`, `IsAvailable`, `IsVisible`, diagnostic reason and the current `Vector2 Size` from MAX's `GetBannerLayout` API.

- `Loading`, `Available` and `Failed` are updated from native load callbacks.
- The revenue/impression callback refreshes the measured layout and confirms the requested visible state.
- `ShowBanner` refuses to call MAX until the loaded callback has made the banner available.
- MAX exposes no banner displayed/hidden callbacks. Therefore `Showing` and `Hidden` mean the corresponding native command was accepted; later native callbacks continue to refresh availability and size.
- Size is in MAX/Unity screen coordinates and remains zero until the SDK can measure a native layout (the Editor stub reports zero).

## Sand project wiring

`Assets/Resources/Integration/Ads/AdsManager.asset` keeps its existing identity and references:
- `Assets/Resources/Integration/Ads/AdsConfiguration.asset` for the generic interval;
- `Assets/Resources/Integration/Ads/ApplovinMaxAdsServiceProvider.asset` for runtime units/provider settings.

`SandAdsConfiguration.asset` remains the canonical SDK-key, consent, unit-ID and timeout build source. `SandAdsBuildSettings.Apply` synchronizes the two runtime assets and AppLovin native settings before builds. Never log the SDK key.

The project `AdsManager` is intentionally empty. The Magima bridge retains free-ad flags, permanent no-ads entitlement and ticket debits, and translates handler snapshots to its existing fluent callback contract.

Editor tests do not prove real inventory, native failure callbacks, consent UI, ILRD or store/network dashboard configuration. Run the MAX test suite on physical Android/iOS devices before release.
