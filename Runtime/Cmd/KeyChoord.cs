using System;
using System.Collections.Generic;
using Com.Hapiga.Scheherazade.Common.Logging;
using Com.Hapiga.Scheherazade.Common.Singleton;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common
{
    [DontDestroyOnLoad]
    [AddComponentMenu("Scheherazade/Cmd/Key Choord")]
    public class KeyChoord : SingletonBehavior<KeyChoord>
    {
        [SerializeField, Min(1)]
        private int maxSequenceLength = 8;

        [SerializeField, Min(0.05f)]
        private float sequenceStepDelay = 1f;

        [SerializeField]
        private bool clearSequenceAfterTrigger = true;

        [SerializeField]
        private bool replaceShortcutWhenIdExists = true;

        [SerializeField]
        private bool immediatelyDeclineMismatchedKeyChoord;

        private static readonly Dictionary<string, KeyCode> NamedKeys =
            new(StringComparer.OrdinalIgnoreCase)
            {
                { "space", KeyCode.Space },
                { "tab", KeyCode.Tab },
                { "enter", KeyCode.Return },
                { "return", KeyCode.Return },
                { "esc", KeyCode.Escape },
                { "escape", KeyCode.Escape },
                { "backspace", KeyCode.Backspace },
                { "bs", KeyCode.Backspace },
                { "delete", KeyCode.Delete },
                { "del", KeyCode.Delete },
                { "up", KeyCode.UpArrow },
                { "down", KeyCode.DownArrow },
                { "left", KeyCode.LeftArrow },
                { "right", KeyCode.RightArrow },
                { "home", KeyCode.Home },
                { "end", KeyCode.End },
                { "pageup", KeyCode.PageUp },
                { "pagedown", KeyCode.PageDown }
            };

        private static readonly KeyCode[] SupportedKeyCodes =
        {
            KeyCode.Space,
            KeyCode.Tab,
            KeyCode.Return,
            KeyCode.Escape,
            KeyCode.Backspace,
            KeyCode.Delete,
            KeyCode.UpArrow,
            KeyCode.DownArrow,
            KeyCode.LeftArrow,
            KeyCode.RightArrow,
            KeyCode.Home,
            KeyCode.End,
            KeyCode.PageUp,
            KeyCode.PageDown,
            KeyCode.Alpha0,
            KeyCode.Alpha1,
            KeyCode.Alpha2,
            KeyCode.Alpha3,
            KeyCode.Alpha4,
            KeyCode.Alpha5,
            KeyCode.Alpha6,
            KeyCode.Alpha7,
            KeyCode.Alpha8,
            KeyCode.Alpha9,
            KeyCode.A,
            KeyCode.B,
            KeyCode.C,
            KeyCode.D,
            KeyCode.E,
            KeyCode.F,
            KeyCode.G,
            KeyCode.H,
            KeyCode.I,
            KeyCode.J,
            KeyCode.K,
            KeyCode.L,
            KeyCode.M,
            KeyCode.N,
            KeyCode.O,
            KeyCode.P,
            KeyCode.Q,
            KeyCode.R,
            KeyCode.S,
            KeyCode.T,
            KeyCode.U,
            KeyCode.V,
            KeyCode.W,
            KeyCode.X,
            KeyCode.Y,
            KeyCode.Z,
            KeyCode.F1,
            KeyCode.F2,
            KeyCode.F3,
            KeyCode.F4,
            KeyCode.F5,
            KeyCode.F6,
            KeyCode.F7,
            KeyCode.F8,
            KeyCode.F9,
            KeyCode.F10,
            KeyCode.F11,
            KeyCode.F12
        };

        private readonly Dictionary<string, ShortcutRegistration> _shortcutById =
            new(StringComparer.Ordinal);
        private readonly List<InputStep> _inputBuffer = new();
        private PendingMatch _pendingMatch;
        private float _lastInputTime = -1f;

        public static void RegisterShortcut(string id, string pattern, Action action)
        {
            EnsureInstance().RegisterShortcutInternal(id, pattern, action);
        }

        public static bool UnregisterShortcut(string id)
        {
            if (Instance == null)
            {
                return false;
            }

            return Instance.UnregisterShortcutInternal(id);
        }

        public static void ClearShortcuts()
        {
            if (Instance == null)
            {
                return;
            }

            Instance.ClearShortcutsInternal();
        }

        protected override void Awake()
        {
            base.Awake();
            if (Instance != this)
            {
                return;
            }

            maxSequenceLength = Mathf.Max(1, maxSequenceLength);
            sequenceStepDelay = Mathf.Max(0.05f, sequenceStepDelay);
        }

        private void OnValidate()
        {
            maxSequenceLength = Mathf.Max(1, maxSequenceLength);
            sequenceStepDelay = Mathf.Max(0.05f, sequenceStepDelay);
        }

        private void Update()
        {
            if (Instance != this)
            {
                return;
            }

            HandleSequenceTimeout();

            if (!TryReadCurrentInputStep(out InputStep step))
            {
                return;
            }

            bool hadActiveSequence = _inputBuffer.Count > 0;
            _inputBuffer.Add(step);
            TrimInputBufferToMaxLength();
            _lastInputTime = Time.unscaledTime;

            ResolveInputBuffer(hadActiveSequence && immediatelyDeclineMismatchedKeyChoord);
        }

        private static KeyChoord EnsureInstance()
        {
            if (Instance != null)
            {
                return Instance;
            }

            KeyChoord existingInstance = FindObjectOfType<KeyChoord>();
            if (existingInstance != null)
            {
                return existingInstance;
            }

            GameObject gameObject = new(nameof(KeyChoord));
            return gameObject.AddComponent<KeyChoord>();
        }

        private void RegisterShortcutInternal(string id, string pattern, Action action)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("Shortcut id cannot be empty.", nameof(id));
            }

            if (string.IsNullOrWhiteSpace(pattern))
            {
                throw new ArgumentException("Shortcut pattern cannot be empty.", nameof(pattern));
            }

            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            List<InputStep> parsedPattern = ParsePattern(pattern);
            if (parsedPattern.Count > maxSequenceLength)
            {
                throw new InvalidOperationException(
                    $"Shortcut '{id}' uses {parsedPattern.Count} step(s), but maxSequenceLength is {maxSequenceLength}."
                );
            }

            if (_shortcutById.ContainsKey(id) && !replaceShortcutWhenIdExists)
            {
                throw new InvalidOperationException(
                    $"Shortcut '{id}' is already registered and replacement is disabled."
                );
            }

            _shortcutById[id] = new ShortcutRegistration(id, pattern, parsedPattern, action);
        }

        private bool UnregisterShortcutInternal(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("Shortcut id cannot be empty.", nameof(id));
            }

            bool removed = _shortcutById.Remove(id);
            if (removed)
            {
                ResolveInputBuffer();
            }

            return removed;
        }

        private void ClearShortcutsInternal()
        {
            _shortcutById.Clear();
            ClearBufferedInput();
        }

        private void HandleSequenceTimeout()
        {
            if (_inputBuffer.Count == 0 || _lastInputTime < 0f)
            {
                return;
            }

            if (Time.unscaledTime - _lastInputTime <= sequenceStepDelay)
            {
                return;
            }

            if (_pendingMatch != null)
            {
                InvokePendingMatch();
                return;
            }

            ClearBufferedInput();
        }

        private void ResolveInputBuffer(bool declineCurrentSequenceOnMismatch = false)
        {
            if (_inputBuffer.Count == 0)
            {
                _pendingMatch = null;
                return;
            }

            if (declineCurrentSequenceOnMismatch)
            {
                MatchState activeSequenceMatch = MatchBufferFrom(0, _inputBuffer.Count);
                if (!activeSequenceMatch.HasMatch)
                {
                    ClearBufferedInput();
                    return;
                }

                ApplyMatch(activeSequenceMatch);
                return;
            }

            MatchState bestMatch = FindBestMatch();
            if (!bestMatch.HasMatch)
            {
                ClearBufferedInput();
                return;
            }

            ApplyMatch(bestMatch);
        }

        private void ApplyMatch(MatchState bestMatch)
        {
            if (bestMatch.StartIndex > 0)
            {
                _inputBuffer.RemoveRange(0, bestMatch.StartIndex);
            }

            if (bestMatch.ExactMatches.Count > 0 && !bestMatch.HasLongerMatch)
            {
                _pendingMatch = null;
                InvokeMatches(bestMatch.ExactMatches);
                return;
            }

            if (bestMatch.ExactMatches.Count > 0)
            {
                _pendingMatch = new PendingMatch(bestMatch.ExactMatches);
                return;
            }

            _pendingMatch = null;
        }

        private MatchState FindBestMatch()
        {
            MatchState bestMatch = MatchState.NoMatch;

            for (int startIndex = 0; startIndex < _inputBuffer.Count; startIndex++)
            {
                int stepCount = _inputBuffer.Count - startIndex;
                MatchState candidate = MatchBufferFrom(startIndex, stepCount);
                if (!candidate.HasMatch)
                {
                    continue;
                }

                bestMatch = candidate;
                break;
            }

            return bestMatch;
        }

        private MatchState MatchBufferFrom(int startIndex, int stepCount)
        {
            List<ShortcutRegistration> exactMatches = new();
            bool hasLongerMatch = false;

            foreach (ShortcutRegistration registration in _shortcutById.Values)
            {
                if (registration.Steps.Count < stepCount)
                {
                    continue;
                }

                if (!MatchesRegistrationPrefix(registration.Steps, startIndex, stepCount))
                {
                    continue;
                }

                if (registration.Steps.Count == stepCount)
                {
                    exactMatches.Add(registration);
                }
                else
                {
                    hasLongerMatch = true;
                }
            }

            return new MatchState(startIndex, exactMatches, hasLongerMatch);
        }

        private bool MatchesRegistrationPrefix(
            IReadOnlyList<InputStep> registrationSteps,
            int startIndex,
            int stepCount
        )
        {
            for (int i = 0; i < stepCount; i++)
            {
                if (!registrationSteps[i].Equals(_inputBuffer[startIndex + i]))
                {
                    return false;
                }
            }

            return true;
        }

        private void InvokePendingMatch()
        {
            if (_pendingMatch == null)
            {
                return;
            }

            PendingMatch pendingMatch = _pendingMatch;
            _pendingMatch = null;
            InvokeMatches(pendingMatch.ExactMatches);
        }

        private void InvokeMatches(IReadOnlyList<ShortcutRegistration> matches)
        {
            for (int i = 0; i < matches.Count; i++)
            {
                ShortcutRegistration match = matches[i];
                try
                {
                    match.Action.Invoke();
                }
                catch (Exception ex)
                {
                    QuickLog.Error<KeyChoord>(
                        "Shortcut '{0}' threw an exception while executing pattern '{1}': {2}",
                        match.Id,
                        match.Pattern,
                        ex
                    );
                }
            }

            if (clearSequenceAfterTrigger)
            {
                ClearBufferedInput();
            }
        }

        private void TrimInputBufferToMaxLength()
        {
            if (_inputBuffer.Count <= maxSequenceLength)
            {
                return;
            }

            _inputBuffer.RemoveRange(0, _inputBuffer.Count - maxSequenceLength);
        }

        private void ClearBufferedInput()
        {
            _inputBuffer.Clear();
            _pendingMatch = null;
            _lastInputTime = -1f;
        }

        private bool TryReadCurrentInputStep(out InputStep step)
        {
            bool ctrlPressed =
                Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool altPressed =
                Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
            bool shiftPressed =
                Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

            for (int i = 0; i < SupportedKeyCodes.Length; i++)
            {
                KeyCode keyCode = SupportedKeyCodes[i];
                if (!Input.GetKeyDown(keyCode))
                {
                    continue;
                }

                step = new InputStep(keyCode, ctrlPressed, altPressed, shiftPressed);
                return true;
            }

            step = default;
            return false;
        }

        private static List<InputStep> ParsePattern(string pattern)
        {
            List<string> tokens = TokenizePattern(pattern);
            if (tokens.Count == 0)
            {
                throw new FormatException("Shortcut pattern must contain at least one step.");
            }

            List<InputStep> steps = new(tokens.Count);
            for (int i = 0; i < tokens.Count; i++)
            {
                steps.Add(ParseStep(tokens[i]));
            }

            return steps;
        }

        private static List<string> TokenizePattern(string pattern)
        {
            List<string> tokens = new();
            int index = 0;

            while (index < pattern.Length)
            {
                while (index < pattern.Length && char.IsWhiteSpace(pattern[index]))
                {
                    index++;
                }

                if (index >= pattern.Length)
                {
                    break;
                }

                if (pattern[index] == '<')
                {
                    int closingIndex = pattern.IndexOf('>', index + 1);
                    if (closingIndex < 0)
                    {
                        throw new FormatException($"Shortcut token starting at index {index} is missing '>'.");
                    }

                    tokens.Add(pattern.Substring(index, closingIndex - index + 1));
                    index = closingIndex + 1;
                    continue;
                }

                int tokenStart = index;
                while (index < pattern.Length && !char.IsWhiteSpace(pattern[index]))
                {
                    index++;
                }

                tokens.Add(pattern.Substring(tokenStart, index - tokenStart));
            }

            return tokens;
        }

        private static InputStep ParseStep(string token)
        {
            bool ctrl = false;
            bool alt = false;
            bool shift = false;
            string keyToken = token;

            if (token.StartsWith("<", StringComparison.Ordinal) &&
                token.EndsWith(">", StringComparison.Ordinal))
            {
                string innerToken = token.Substring(1, token.Length - 2).Trim();
                if (string.IsNullOrWhiteSpace(innerToken))
                {
                    throw new FormatException("Shortcut token cannot be empty.");
                }

                string[] parts = innerToken.Split('-', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 1)
                {
                    for (int i = 0; i < parts.Length - 1; i++)
                    {
                        switch (parts[i].Trim())
                        {
                            case "C":
                            case "c":
                            case "ctrl":
                            case "CTRL":
                            case "control":
                            case "CONTROL":
                                ctrl = true;
                                break;

                            case "A":
                            case "a":
                            case "alt":
                            case "ALT":
                            case "option":
                            case "OPTION":
                                alt = true;
                                break;

                            case "S":
                            case "s":
                            case "shift":
                            case "SHIFT":
                                shift = true;
                                break;

                            default:
                                throw new FormatException($"Unsupported modifier '{parts[i]}' in token '{token}'.");
                        }
                    }

                    keyToken = parts[parts.Length - 1].Trim();
                }
                else
                {
                    keyToken = innerToken;
                }
            }

            KeyCode keyCode = ParseKeyCode(keyToken, ref shift);
            return new InputStep(keyCode, ctrl, alt, shift);
        }

        private static KeyCode ParseKeyCode(string keyToken, ref bool shift)
        {
            if (string.IsNullOrWhiteSpace(keyToken))
            {
                throw new FormatException("Shortcut key token cannot be empty.");
            }

            if (NamedKeys.TryGetValue(keyToken, out KeyCode namedKeyCode))
            {
                return namedKeyCode;
            }

            if (keyToken.Length == 1)
            {
                char keyChar = keyToken[0];
                if (char.IsLetter(keyChar))
                {
                    shift |= char.IsUpper(keyChar);
                    return (KeyCode)Enum.Parse(
                        typeof(KeyCode),
                        char.ToUpperInvariant(keyChar).ToString()
                    );
                }

                if (char.IsDigit(keyChar))
                {
                    return (KeyCode)Enum.Parse(
                        typeof(KeyCode),
                        $"Alpha{keyChar}"
                    );
                }
            }

            if (Enum.TryParse(keyToken, true, out KeyCode parsedKeyCode))
            {
                return parsedKeyCode;
            }

            throw new FormatException($"Unsupported key token '{keyToken}'.");
        }

        private sealed class ShortcutRegistration
        {
            public ShortcutRegistration(
                string id,
                string pattern,
                IReadOnlyList<InputStep> steps,
                Action action
            )
            {
                Id = id;
                Pattern = pattern;
                Steps = steps;
                Action = action;
            }

            public string Id { get; }
            public string Pattern { get; }
            public IReadOnlyList<InputStep> Steps { get; }
            public Action Action { get; }
        }

        private sealed class PendingMatch
        {
            public PendingMatch(IReadOnlyList<ShortcutRegistration> exactMatches)
            {
                ExactMatches = exactMatches;
            }

            public IReadOnlyList<ShortcutRegistration> ExactMatches { get; }
        }

        private readonly struct MatchState
        {
            public static MatchState NoMatch => new(-1, Array.Empty<ShortcutRegistration>(), false);

            public MatchState(
                int startIndex,
                IReadOnlyList<ShortcutRegistration> exactMatches,
                bool hasLongerMatch
            )
            {
                StartIndex = startIndex;
                ExactMatches = exactMatches;
                HasLongerMatch = hasLongerMatch;
            }

            public int StartIndex { get; }
            public IReadOnlyList<ShortcutRegistration> ExactMatches { get; }
            public bool HasLongerMatch { get; }
            public bool HasMatch => StartIndex >= 0;
        }

        private readonly struct InputStep : IEquatable<InputStep>
        {
            public InputStep(KeyCode keyCode, bool ctrl, bool alt, bool shift)
            {
                KeyCode = keyCode;
                Ctrl = ctrl;
                Alt = alt;
                Shift = shift;
            }

            public KeyCode KeyCode { get; }
            public bool Ctrl { get; }
            public bool Alt { get; }
            public bool Shift { get; }

            public bool Equals(InputStep other)
            {
                return KeyCode == other.KeyCode
                    && Ctrl == other.Ctrl
                    && Alt == other.Alt
                    && Shift == other.Shift;
            }

            public override bool Equals(object obj)
            {
                return obj is InputStep other && Equals(other);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(KeyCode, Ctrl, Alt, Shift);
            }
        }
    }
}
