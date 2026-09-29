//
//  HapticBridge.mm
//  Scheherazade Haptics — iOS native bridge.
//
//  Strategies:
//   - iOS 13+ : CHHapticEngine for continuous + amplitude control.
//   - UIImpact/Notification/Selection generators for discrete impacts.
//
//  Simulator has no Taptic Engine: isAvailable() returns false and all other
//  calls no-op (capability checks never crash).
//

#import <Foundation/Foundation.h>
#import <UIKit/UIKit.h>
#import <CoreHaptics/CoreHaptics.h>
#import <TargetConditionals.h>

#if __has_feature(objc_arc)
#define HAPTIC_RELEASE(object)
#else
#define HAPTIC_RELEASE(object) [object release]
#endif

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

static UIImpactFeedbackGenerator *_lightGenerator;
static UIImpactFeedbackGenerator *_mediumGenerator;
static UIImpactFeedbackGenerator *_heavyGenerator;
static UINotificationFeedbackGenerator *_notificationGenerator;
static UISelectionFeedbackGenerator *_selectionGenerator;
static CHHapticEngine *_engine;
static NSUInteger _engineGeneration;

static NSMutableDictionary<NSNumber *, id<CHHapticPatternPlayer>> *_continuousPlayers;

static void ClearEngine() {
    ++_engineGeneration;
    HAPTIC_RELEASE(_engine);
    _engine = nil;
}

static BOOL HapticRuntimeIsAvailable() {
#if TARGET_OS_SIMULATOR
    return NO;
#else
    return YES;
#endif
}

static BOOL HapticRuntimeSupportsContinuous() {
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
        if (_engine == nil || error != nil) {
            ClearEngine();
            return nil;
        }

        NSUInteger generation = ++_engineGeneration;
        _engine.stoppedHandler = ^(CHHapticEngineStoppedReason reason) {
            (void)reason;
            dispatch_async(dispatch_get_main_queue(), ^{
                if (_engineGeneration != generation) return;
                [_continuousPlayers removeAllObjects];
                ClearEngine();
            });
        };
        _engine.resetHandler = ^{
            dispatch_async(dispatch_get_main_queue(), ^{
                if (_engineGeneration != generation) return;
                [_continuousPlayers removeAllObjects];
                ClearEngine();
            });
        };

        if (![_engine startAndReturnError:&error] || error != nil) {
            ClearEngine();
        }
    }
    return _engine;
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
    return HapticRuntimeIsAvailable();
}

bool haptic_supportsHeavy() {
    if (@available(iOS 13.0, *)) {
        return [CHHapticEngine capabilitiesForHardware].supportsHaptics;
    }
    return NO;
}

bool haptic_supportsContinuous() {
    return HapticRuntimeSupportsContinuous();
}

void haptic_cue(int type, float intensity) {
    if (!HapticRuntimeIsAvailable()) return;

    float clamped = intensity < 0 ? 0 : (intensity > 1 ? 1 : intensity);
    PlayDiscreteImpact((HapticWaveformType)type, clamped);
}

void haptic_beginContinuous(int tokenId, float intensity) {
    if (!HapticRuntimeSupportsContinuous()) return;

    if (_continuousPlayers == nil) {
        _continuousPlayers = [[NSMutableDictionary alloc] init];
    }

    CHHapticEngine *engine = EnsureEngine();
    if (engine == nil) return;

    if (_continuousPlayers[@(tokenId)] != nil) {
        [_continuousPlayers[@(tokenId)] stopAtTime:0 error:nil];
        [_continuousPlayers removeObjectForKey:@(tokenId)];
    }

    float clamped = intensity < 0 ? 0 : (intensity > 1 ? 1 : intensity);

    CHHapticEventParameter *intensityParameter =
        [[CHHapticEventParameter alloc] initWithParameterID:CHHapticEventParameterIDHapticIntensity
                                                     value:clamped];
    CHHapticEventParameter *sharpnessParameter =
        [[CHHapticEventParameter alloc] initWithParameterID:CHHapticEventParameterIDHapticSharpness
                                                     value:0.5f];
    CHHapticEvent *event = [[CHHapticEvent alloc] initWithEventType:CHHapticEventTypeHapticContinuous
                                                         parameters:@[intensityParameter, sharpnessParameter]
                                                           relativeTime:0
                                                               duration:1.0];
    HAPTIC_RELEASE(intensityParameter);
    HAPTIC_RELEASE(sharpnessParameter);

    NSError *error = nil;
    CHHapticPattern *pattern = [[CHHapticPattern alloc] initWithEvents:@[event] parameters:@[] error:&error];
    HAPTIC_RELEASE(event);
    if (pattern == nil || error != nil) {
        HAPTIC_RELEASE(pattern);
        return;
    }

    id<CHHapticAdvancedPatternPlayer> player =
        [engine createAdvancedPlayerWithPattern:pattern error:&error];
    HAPTIC_RELEASE(pattern);
    if (player == nil || error != nil) return;

    player.loopEnabled = YES;
    if (![player startAtTime:0 error:&error] || error != nil) return;

    _continuousPlayers[@(tokenId)] = player;
}

void haptic_updateContinuous(int tokenId, float intensity) {
    if (!HapticRuntimeSupportsContinuous() || _continuousPlayers == nil) return;

    id<CHHapticPatternPlayer> player = _continuousPlayers[@(tokenId)];
    if (player == nil) return;

    float clamped = intensity < 0 ? 0 : (intensity > 1 ? 1 : intensity);

    CHHapticDynamicParameter *param =
        [[CHHapticDynamicParameter alloc] initWithParameterID:CHHapticDynamicParameterIDHapticIntensityControl
                                                        value:clamped
                                                  relativeTime:0];
    [player sendParameters:@[param] atTime:0 error:nil];
    HAPTIC_RELEASE(param);
}

void haptic_endContinuous(int tokenId) {
    if (_continuousPlayers == nil) return;

    id<CHHapticPatternPlayer> player = _continuousPlayers[@(tokenId)];
    if (player == nil) return;

    [player stopAtTime:0 error:nil];
    [_continuousPlayers removeObjectForKey:@(tokenId)];
}

void haptic_cancelAll() {
    [_continuousPlayers enumerateKeysAndObjectsUsingBlock:^(NSNumber *key, id<CHHapticPatternPlayer> player, BOOL *stop) {
        (void)key;
        (void)stop;
        [player stopAtTime:0 error:nil];
    }];
    [_continuousPlayers removeAllObjects];
}

} // extern "C"
