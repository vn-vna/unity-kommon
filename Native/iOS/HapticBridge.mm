//
//  HapticBridge.mm
//  Scheherazade Haptics — iOS native bridge.
//
//  Strategies:
//   - iOS 13+  : CHHapticEngine for continuous + amplitude control; fallback
//                UIImpact/Notification/Selection generators for discrete impacts.
//   - Older iOS: legacy AudioServicesPlaySystemSound impact ids 1519/1520/1521.
//
//  Simulator has no Taptic Engine: isAvailable() returns false and all other
//  calls no-op (capability checks never crash).
//

#import <Foundation/Foundation.h>
#import <UIKit/UIKit.h>
#import <AudioToolbox/AudioToolbox.h>
#import <CoreHaptics/CoreHaptics.h>

// WaveformType enum (must match HapticWaveformType in C#).
typedef NS_ENUM(NSInteger, HapticWaveformType) {
    LightImpact = 0,
    MediumImpact = 1,
    HeavyImpact = 2,
    NotificationSuccess = 3,
    NotificationWarning = 4,
    NotificationError = 5,
    SelectionTick = 6,
    VibrationBurst = 7,
    Custom = 8
};

// Legacy AudioServices impact ids.
static const SystemSoundID kHapticLegacyLight = 1519;
static const SystemSoundID kHapticLegacyMedium = 1520;
static const SystemSoundID kHapticLegacyHeavy = 1521;

static UIImpactFeedbackGenerator *_lightGenerator;
static UIImpactFeedbackGenerator *_mediumGenerator;
static UIImpactFeedbackGenerator *_heavyGenerator;
static UINotificationFeedbackGenerator *_notificationGenerator;
static UISelectionFeedbackGenerator *_selectionGenerator;
static CHHapticEngine *_engine;

static NSMutableDictionary<NSNumber *, CHHapticPatternPlayer *> *_continuousPlayers;

static BOOL HapticRuntimeIsReady() {
    if (@available(iOS 13.0, *)) {
        return [CHHapticEngine capabilitiesForHardware].supportsHaptics;
    }
    return NO;
}

static void EnsureGenerators() {
    static dispatch_once_t onceToken;
    dispatch_once(&onceToken, ^{
        _lightGenerator = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleLight];
        _mediumGenerator = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleMedium];
        _heavyGenerator = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleHeavy];
        _notificationGenerator = [[UINotificationFeedbackGenerator alloc] init];
        _selectionGenerator = [[UISelectionFeedbackGenerator alloc] init];
    });
}

static CHHapticEngine *EnsureEngine() {
    if (_engine != nil) {
        return _engine;
    }
    if (@available(iOS 13.0, *)) {
        NSError *error = nil;
        _engine = [[CHHapticEngine alloc] initAndReturnError:&error];
        if (error != nil) {
            _engine = nil;
        }
    }
    return _engine;
}

static void PlayLegacy(SystemSoundID soundId) {
    AudioServicesPlaySystemSound(soundId);
}

static void PlayDiscreteImpact(HapticWaveformType type, float intensity) {
    EnsureGenerators();

    switch (type) {
        case NotificationSuccess:
            [_notificationGenerator notificationOccurred:UINotificationFeedbackTypeSuccess];
            break;
        case NotificationWarning:
            [_notificationGenerator notificationOccurred:UINotificationFeedbackTypeWarning];
            break;
        case NotificationError:
            [_notificationGenerator notificationOccurred:UINotificationFeedbackTypeError];
            break;
        case SelectionTick:
            [_selectionGenerator selectionChanged];
            break;
        case LightImpact:
            [_lightGenerator impactOccurredWithIntensity:intensity];
            break;
        case MediumImpact:
            [_mediumGenerator impactOccurredWithIntensity:intensity];
            break;
        case HeavyImpact:
            if (@available(iOS 13.0, *)) {
                if ([CHHapticEngine capabilitiesForHardware].supportsHaptics) {
                    [_heavyGenerator impactOccurredWithIntensity:intensity];
                } else {
                    // Downgrade Heavy -> Medium on devices without heavy support.
                    [_mediumGenerator impactOccurredWithIntensity:intensity];
                }
            } else {
                [_mediumGenerator impactOccurredWithIntensity:intensity];
            }
            break;
        default:
            [_mediumGenerator impactOccurredWithIntensity:intensity];
            break;
    }
}

extern "C" {

bool haptic_isAvailable() {
    return HapticRuntimeIsReady();
}

bool haptic_supportsHeavy() {
    if (@available(iOS 13.0, *)) {
        return [CHHapticEngine capabilitiesForHardware].supportsHaptics;
    }
    return NO;
}

void haptic_cue(int type, float intensity) {
    if (!HapticRuntimeIsReady()) return;

    float clamped = intensity < 0 ? 0 : (intensity > 1 ? 1 : intensity);
    PlayDiscreteImpact((HapticWaveformType)type, clamped);
}

void haptic_beginContinuous(int tokenId, float intensity) {
    if (!HapticRuntimeIsReady()) return;

    if (_continuousPlayers == nil) {
        _continuousPlayers = [NSMutableDictionary dictionary];
    }

    CHHapticEngine *engine = EnsureEngine();
    if (engine == nil) return;

    if (_continuousPlayers[@(tokenId)] != nil) {
        [_continuousPlayers[@(tokenId)] stopWithCompletionHandler:nil];
        [_continuousPlayers removeObjectForKey:@(tokenId)];
    }

    float clamped = intensity < 0 ? 0 : (intensity > 1 ? 1 : intensity);

    CHHapticEvent *event = [[CHHapticEvent alloc] initWithEventType:CHHapticEventTypeHapticContinuous
                                                         parameters:@[
                                                             [[CHHapticEventParameter alloc] initWithParameterID:CHHapticEventParameterIDHapticIntensity
                                                                                                           value:clamped],
                                                             [[CHHapticEventParameter alloc] initWithParameterID:CHHapticEventParameterIDHapticSharpness
                                                                                                           value:0.5f]
                                                         ]
                                                           relativeTime:0
                                                           duration:1000.0];

    NSError *error = nil;
    CHHapticPattern *pattern = [[CHHapticPattern alloc] initWithEvents:@[event] parameters:@[] error:&error];
    if (error != nil) return;

    id<CHHapticPatternPlayer> player = [engine createPlayerWithPattern:pattern error:&error];
    if (error != nil) return;

    _continuousPlayers[@(tokenId)] = player;
    [player startWithCompletionHandler:nil];
}

void haptic_updateContinuous(int tokenId, float intensity) {
    if (!HapticRuntimeIsReady() || _continuousPlayers == nil) return;

    id<CHHapticPatternPlayer> player = _continuousPlayers[@(tokenId)];
    if (player == nil) return;

    float clamped = intensity < 0 ? 0 : (intensity > 1 ? 1 : intensity);

    CHHapticDynamicParameter *param =
        [[CHHapticDynamicParameter alloc] initWithParameterID:CHHapticDynamicParameterIDHapticIntensityControl
                                                        value:clamped
                                                  relativeTime:0];
    [player sendParameters:@[param] atTime:0 error:nil];
}

void haptic_endContinuous(int tokenId) {
    if (_continuousPlayers == nil) return;

    id<CHHapticPatternPlayer> player = _continuousPlayers[@(tokenId)];
    if (player == nil) return;

    [player stopWithCompletionHandler:nil];
    [_continuousPlayers removeObjectForKey:@(tokenId)];
}

void haptic_cancelAll() {
    [_continuousPlayers enumerateKeysAndObjectsUsingBlock:^(NSNumber *key, id<CHHapticPatternPlayer> player, BOOL *stop) {
        [player stopWithCompletionHandler:nil];
    }];
    [_continuousPlayers removeAllObjects];
}

} // extern "C"
