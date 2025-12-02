using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Com.Hapiga.Scheherazade.Common.Extensions;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.LegacyInput
{

    /// <summary>
    /// Controller for handling legacy input actions.
    /// </summary>
    /// <remarks>
    /// This class of mine is not prefect. Feel free to improve it.
    /// </remarks>
    [AddComponentMenu("Scheherazade/Legacy Input Controller")]
    public class LegacyInputController : MonoBehaviour
    {
        public static readonly Dictionary<string, KeyCode> KeyCodeMappings = new Dictionary<string, KeyCode>()
        {
            { "A", KeyCode.A },
            { "B", KeyCode.B },
            { "C", KeyCode.C },
            { "D", KeyCode.D },
            { "E", KeyCode.E },
            { "F", KeyCode.F },
            { "G", KeyCode.G },
            { "H", KeyCode.H },
            { "I", KeyCode.I },
            { "J", KeyCode.J },
            { "K", KeyCode.K },
            { "L", KeyCode.L },
            { "M", KeyCode.M },
            { "N", KeyCode.N },
            { "O", KeyCode.O },
            { "P", KeyCode.P },
            { "Q", KeyCode.Q },
            { "R", KeyCode.R },
            { "S", KeyCode.S },
            { "T", KeyCode.T },
            { "U", KeyCode.U },
            { "V", KeyCode.V },
            { "W", KeyCode.W },
            { "X", KeyCode.X },
            { "Y", KeyCode.Y },
            { "Z", KeyCode.Z },
            { "Alpha0", KeyCode.Alpha0 },
            { "Alpha1", KeyCode.Alpha1 },
            { "Alpha2", KeyCode.Alpha2 },
            { "Alpha3", KeyCode.Alpha3 },
            { "Alpha4", KeyCode.Alpha4 },
            { "Alpha5", KeyCode.Alpha5 },
            { "Alpha6", KeyCode.Alpha6 },
            { "Alpha7", KeyCode.Alpha7 },
            { "Alpha8", KeyCode.Alpha8 },
            { "Alpha9", KeyCode.Alpha9 },
            { "F1", KeyCode.F1 },
            { "F2", KeyCode.F2 },
            { "F3", KeyCode.F3 },
            { "F4", KeyCode.F4 },
            { "F5", KeyCode.F5 },
            { "F6", KeyCode.F6 },
            { "F7", KeyCode.F7 },
            { "F8", KeyCode.F8 },
            { "F9", KeyCode.F9 },
            { "F10", KeyCode.F10 },
            { "F11", KeyCode.F11 },
            { "F12", KeyCode.F12 },
            { "F13", KeyCode.F13 },
            { "F14", KeyCode.F14 },
            { "F15", KeyCode.F15 },
            { "UpArrow", KeyCode.UpArrow },
            { "DownArrow", KeyCode.DownArrow },
            { "LeftArrow", KeyCode.LeftArrow },
            { "RightArrow", KeyCode.RightArrow },
            { "Return", KeyCode.Return },
            { "LShift", KeyCode.LeftShift },
            { "RShift", KeyCode.RightShift },
            { "Escape", KeyCode.Escape },
            { "Space", KeyCode.Space },
            { "Tab", KeyCode.Tab },
            { "CapsLock", KeyCode.CapsLock },
            { "LeftControl", KeyCode.LeftControl },
            { "RightControl", KeyCode.RightControl },
            { "LeftAlt", KeyCode.LeftAlt },
            { "RightAlt", KeyCode.RightAlt },
            { "LeftCommand", KeyCode.LeftCommand },
            { "RightCommand", KeyCode.RightCommand },
            { "Backspace", KeyCode.Backspace },
            { "Delete", KeyCode.Delete },
            { "Insert", KeyCode.Insert },
            { "Home", KeyCode.Home },
            { "End", KeyCode.End },
            { "PageUp", KeyCode.PageUp },
            { "PageDown", KeyCode.PageDown },
            { "Numlock", KeyCode.Numlock },
            { "Print", KeyCode.Print },
            { "ScrollLock", KeyCode.ScrollLock },
            { "Pause", KeyCode.Pause },
            { "Numpad0", KeyCode.Keypad0 },
            { "Numpad1", KeyCode.Keypad1 },
            { "Numpad2", KeyCode.Keypad2 },
            { "Numpad3", KeyCode.Keypad3 },
            { "Numpad4", KeyCode.Keypad4 },
            { "Numpad5", KeyCode.Keypad5 },
            { "Numpad6", KeyCode.Keypad6 },
            { "Numpad7", KeyCode.Keypad7 },
            { "Numpad8", KeyCode.Keypad8 },
            { "Numpad9", KeyCode.Keypad9 },
            { "NumpadDivide", KeyCode.KeypadDivide },
            { "NumpadMultiply", KeyCode.KeypadMultiply },
            { "NumpadMinus", KeyCode.KeypadMinus },
            { "NumpadPlus", KeyCode.KeypadPlus },
            { "NumpadEnter", KeyCode.KeypadEnter },
            { "NumpadPeriod", KeyCode.KeypadPeriod },
            { "LeftBracket", KeyCode.LeftBracket },
            { "RightBracket", KeyCode.RightBracket },
            { "Semicolon", KeyCode.Semicolon },
            { "Quote", KeyCode.Quote },
            { "Comma", KeyCode.Comma },
            { "Period", KeyCode.Period },
            { "Slash", KeyCode.Slash },
            { "Backslash", KeyCode.Backslash },
            { "Minus", KeyCode.Minus },
            { "Equals", KeyCode.Equals },
            { "Tilde", KeyCode.Tilde },
            { "LeftMeta", KeyCode.LeftMeta },
            { "RightMeta", KeyCode.RightMeta }
        };

        public static readonly Dictionary<string, int> MouseButtonMappings = new Dictionary<string, int>()
        {
            { "LeftButton", 0 },
            { "RightButton", 1 },
            { "MiddleButton", 2 },
            { "ForwardButton", 3 },
            { "BackButton", 4 }
        };

        public static readonly Dictionary<string, Comparer<float>> MouseScrollMappings = new Dictionary<string, Comparer<float>>()
        {
            { "Up", Comparer<float>.Create((x, y) => x > y ? 1 : 0) },
            { "Down", Comparer<float>.Create((x, y) => x < y ? 1 : 0) }
        };

        public static readonly Dictionary<string, Func<Vector2, bool>> MouseDisplaceMappings = new Dictionary<string, Func<Vector2, bool>>()
        {
            { "Up", delta => delta.y > 0 },
            { "Down", delta => delta.y < 0 },
            { "Left", delta => delta.x < 0 },
            { "Right", delta => delta.x > 0 }
        };

        public static readonly Dictionary<string, string> ControllerButtonMappings = new Dictionary<string, string>()
        {
            { "A", "joystick button 0" },
            { "B", "joystick button 1" },
            { "X", "joystick button 2" },
            { "Y", "joystick button 3" },
            { "LB", "joystick button 4" },
            { "RB", "joystick button 5" },
            { "Back", "joystick button 6" },
            { "Start", "joystick button 7" },
            { "LStick", "joystick button 8" },
            { "RStick", "joystick button 9" }
        };

        [SerializeField]
        private List<InputActionMapping> inputActionMappings;

        [SerializeField]
        private List<Component> inputActionListeners;

        private bool _enabled;
        private Vector2 _mouseDelta;
        private Vector2 _prevMousePosition;

        private Queue<Action> _inputActionQueue;

        private List<(InputActionDescription, InputActionInfo)> _inputActions;

        private Dictionary<Component, List<(Action<InputActionInfo>, InputActionHandlerAttribute)>> _cachedListeners;

        private List<(string, bool)> InputActionStatusView => _inputActions.Select(x => (x.Item1.Name, x.Item2.Enabled)).ToList();

        private void Awake()
        {
            _inputActions = new List<(InputActionDescription, InputActionInfo)>();
            _inputActionQueue = new Queue<Action>();
            _cachedListeners = new Dictionary<Component, List<(Action<InputActionInfo>, InputActionHandlerAttribute)>>();
            InitializeInputMappings();
        }

        private void Start()
        {
            _mouseDelta = Input.mousePosition;
        }

        private void Update()
        {
            if (!_enabled) return;
            _mouseDelta = Input.mousePosition.GetXY() - _prevMousePosition;
            _prevMousePosition = Input.mousePosition;
            foreach (var (action, info) in _inputActions) action.ScanCallback(info, action);
            if (_inputActionQueue.Count == 0) return;
            ResolveActionQueue();
        }

        public void Enable()
        {
            _enabled = true;
        }

        public void Disable()
        {
            _enabled = false;
        }

        public void SwitchMapping(string name)
        {
            foreach (var (action, info) in _inputActions)
                info.Enabled = action.Mapping == name;
        }

        public void DiableAllMappings()
        {
            foreach (var (_, info) in _inputActions) info.Disable();
        }

        public void EnableMapping(string name)
        {
            foreach (var (action, info) in _inputActions)
                if (action.Mapping == name)
                    info.Enable();
        }

        public void DisbaleMapping(string name)
        {
            foreach (var (action, info) in _inputActions)
                if (action.Mapping == name)
                    info.Disable();
        }

        public void EnableAction(string mapping, string name)
        {
            foreach (var (action, info) in _inputActions)
                if (action.Mapping == mapping && action.Name == name)
                    info.Enable();
        }

        public void DisableAction(string mapping, string name)
        {
            foreach (var (action, info) in _inputActions)
                if (action.Mapping == mapping && action.Name == name)
                    info.Disable();
        }

        /// <summary>
        /// Initializes the input mappings.
        /// </summary>
        private LegacyInputController InitializeInputMappings()
        {
            foreach (var (mapping, actions) in inputActionMappings)
                foreach (var action in actions)
                {
                    var tokens = action.Path.Split("::");
                    var device = tokens[0];
                    var input = tokens[1];

                    switch (device)
                    {
                        case "Keyboard":
                            AppendKeyboardAction(mapping, action.Name, KeyCodeMappings[input]);
                            break;
                        case "MouseButton":
                            AppendMouseAction(mapping, action.Name, MouseButtonMappings[input]);
                            break;
                        case "MouseScroll":
                            AppendMouseScrollAction(mapping, action.Name, MouseScrollMappings[input]);
                            break;
                        case "Gamepad":
                            AppendGamepadAction(mapping, action.Name, (KeyCode)Enum.Parse(typeof(KeyCode), input));
                            break;
                        case "MouseDisplace":
                            AppendMouseDisplaceAction(mapping, action.Name, MouseDisplaceMappings[input]);
                            break;
                        case "ControllerButton":
                            AppendControllerButtonAction(mapping, action.Name, ControllerButtonMappings[input]);
                            break;
                        default:
                            throw new ArgumentException($"Invalid device \"{device}\".");
                    }
                }

            foreach (var listener in inputActionListeners)
            {
                CacheListenerMethods(listener);
                foreach (var (action, attribute) in _cachedListeners[listener])
                {
                    AppendListener(attribute.Mapping, attribute.Name, attribute.FireState, action);
                }
            }

            return this;
        }

        /// <summary>
        /// Caches the methods of a listener component.
        /// </summary>
        /// <param name="listener">The listener component.</param>
        private void CacheListenerMethods(Component listener)
        {
            if (_cachedListeners.ContainsKey(listener)) return;

            var methods = listener.GetType().GetMethods(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
            var cachedMethods = new List<(Action<InputActionInfo>, InputActionHandlerAttribute)>();

            foreach (var method in methods)
            {
                var attributes = method.GetCustomAttributes(typeof(InputActionHandlerAttribute), false);
                if (attributes.Length == 0) continue;

                foreach (InputActionHandlerAttribute attribute in attributes.Cast<InputActionHandlerAttribute>())
                {
                    var action = ConstructListenerMethod(listener, method);
                    cachedMethods.Add((action, attribute));
                }
            }

            _cachedListeners[listener] = cachedMethods;
        }

        private Action<InputActionInfo> ConstructListenerMethod(Component listener, MethodInfo method)
        {
            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length == 0)
            {
                Action action = (Action)Delegate.CreateDelegate(typeof(Action), listener, method);
                return (_) => { action.Invoke(); };
            }
            else if (parameters.Length == 1 && parameters[0].ParameterType == typeof(InputActionInfo))
            {
                return (Action<InputActionInfo>)Delegate.CreateDelegate(typeof(Action<InputActionInfo>), listener, method);
            }
            else
            {
                throw new ArgumentException($"Method {method.Name} in {listener.GetType().Name} does not match expected signature.");
            }
        }

        /// <summary>
        /// Appends a listener for an input action.
        /// </summary>
        /// <param name="mapping">The mapping of the input action.</param>
        /// <param name="name">The name of the input action.</param>
        /// <param name="fireState">The state in which the listener should be fired.</param>
        /// <param name="listener">The listener to be appended.</param>
        private LegacyInputController AppendListener(string mapping, string name, ActionState fireState,
            Action<InputActionInfo> listener)
        {
            foreach (var (action, _) in _inputActions)
                if (action.Mapping == mapping && action.Name == name)
                {
                    if ((fireState & ActionState.Started) != 0) action.StartListeners += listener;
                    if ((fireState & ActionState.Performed) != 0) action.PerformListeners += listener;
                    if ((fireState & ActionState.Canceled) != 0) action.CancelListeners += listener;

                    return this;
                }

            throw new ArgumentException($"No action with mapping {mapping} and name {name} found.");
        }

        /// <summary>
        /// Appends a keyboard action.
        /// </summary>
        /// <param name="mapping">The mapping of the input action.</param>
        /// <param name="name">The name of the input action.</param>
        /// <param name="key">The key code for the keyboard action.</param>
        private LegacyInputController AppendKeyboardAction(string mapping, string name, KeyCode key)
        {
            var action =
                new InputActionDescription(mapping, name, (info, desc) => KeyboardScanCallback(key, info, desc));
            _inputActions.Add((action, new InputActionInfo()));
            return this;
        }

        /// <summary>
        /// Appends a mouse action.
        /// </summary>
        /// <param name="mapping">The mapping of the input action.</param>
        /// <param name="name">The name of the input action.</param>
        /// <param name="button">The mouse button for the action.</param>
        private LegacyInputController AppendMouseAction(string mapping, string name, int button)
        {
            var action =
                new InputActionDescription(mapping, name, (info, desc) => MouseScanCallback(button, info, desc));
            _inputActions.Add((action, new InputActionInfo()));
            return this;
        }

        /// <summary>
        /// Appends a mouse scroll action.
        /// </summary>
        /// <param name="mapping">The mapping of the input action.</param>
        /// <param name="name">The name of the input action.</param>
        /// <param name="comparer">The comparer for the mouse scroll action.</param>
        private LegacyInputController AppendMouseScrollAction(string mapping, string name, Comparer<float> comparer)
        {
            var action = new InputActionDescription(mapping, name,
                (info, desc) => MouseScrollScanCallback(info, desc, comparer));
            _inputActions.Add((action, new InputActionInfo()));
            return this;
        }

        /// <summary>
        /// Appends a gamepad action.
        /// </summary>
        /// <param name="mapping">The mapping of the input action.</param>
        /// <param name="name">The name of the input action.</param>
        /// <param name="key">The key code for the gamepad action.</param>
        private LegacyInputController AppendGamepadAction(string mapping, string name, KeyCode key)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Appends a mouse displacement action.
        /// </summary>
        /// <param name="mapping">The mapping of the input action.</param>
        /// <param name="name">The name of the input action.</param>
        /// <param name="displaceFunc">The function to determine the displacement direction.</param>
        private LegacyInputController AppendMouseDisplaceAction(string mapping, string name, Func<Vector2, bool> displaceFunc)
        {
            var action = new InputActionDescription(mapping, name, (info, desc) => MouseDisplaceScanCallback(info, desc, displaceFunc));
            _inputActions.Add((action, new InputActionInfo()));
            return this;
        }

        /// <summary>
        /// Appends a controller button action.
        /// </summary>
        /// <param name="mapping">The mapping of the input action.</param>
        /// <param name="name">The name of the input action.</param>
        /// <param name="button">The controller button for the action.</param>
        private LegacyInputController AppendControllerButtonAction(string mapping, string name, string button)
        {
            var action = new InputActionDescription(mapping, name, (info, desc) => ControllerButtonScanCallback(button, info, desc));
            _inputActions.Add((action, new InputActionInfo()));
            return this;
        }

        /// <summary>
        /// Queues an action to be executed.
        /// </summary>
        /// <param name="action">The action to be queued.</param>
        private void QueueAction(Action action)
        {
            _inputActionQueue.Enqueue(action);
        }

        /// <summary>
        /// Resolves and executes all actions in the queue.
        /// </summary>
        private void ResolveActionQueue()
        {
            while (_inputActionQueue.Count > 0) _inputActionQueue.Dequeue().Invoke();
        }

        /// <summary>
        /// Callback for keyboard input scanning.
        /// </summary>
        /// <param name="key">The key code for the keyboard action.</param>
        /// <param name="info">The input action information.</param>
        /// <param name="desc">The input action description.</param>
        private void KeyboardScanCallback(KeyCode key, InputActionInfo info, InputActionDescription desc)
        {
            switch (info.ActionState)
            {
                case ActionState.None:
                    if (Input.GetKeyDown(key)) info.SetState(ActionState.Started);
                    break;
                case ActionState.Started:
                    if (Input.GetKey(key))
                        info.SetState(ActionState.Performed);
                    else
                        info.SetState(ActionState.Canceled);
                    break;
                case ActionState.Performed:
                    if (Input.GetKeyUp(key)) info.SetState(ActionState.Canceled);
                    break;
                case ActionState.Canceled:
                    if (!Input.GetKey(key))
                        info.SetState(ActionState.None);
                    else
                        info.SetState(ActionState.Started);
                    break;
            }

            if (info.ActionState != ActionState.None && info.Enabled)
            {
                QueueAction(() => desc.InvokeListeners(info));
            }
        }

        /// <summary>
        /// Callback for mouse button input scanning.
        /// </summary>
        /// <param name="button">The mouse button for the action.</param>
        /// <param name="info">The input action information.</param>
        /// <param name="desc">The input action description.</param>
        private void MouseScanCallback(int button, InputActionInfo info, InputActionDescription desc)
        {
            switch (info.ActionState)
            {
                case ActionState.None:
                    if (Input.GetMouseButtonDown(button)) info.SetState(ActionState.Started);
                    break;
                case ActionState.Started:
                    if (Input.GetMouseButton(button))
                        info.SetState(ActionState.Performed);
                    else
                        info.SetState(ActionState.Canceled);
                    break;
                case ActionState.Performed:
                    if (Input.GetMouseButtonUp(button)) info.SetState(ActionState.Canceled);
                    break;
                case ActionState.Canceled:
                    if (!Input.GetMouseButton(button))
                        info.SetState(ActionState.None);
                    else
                        info.SetState(ActionState.Started);
                    break;
            }

            if (info.ActionState != ActionState.None && info.Enabled) QueueAction(() => desc.InvokeListeners(info));
        }

        /// <summary>
        /// Callback for mouse scroll input scanning.
        /// </summary>
        /// <param name="info">The input action information.</param>
        /// <param name="desc">The input action description.</param>
        /// <param name="comparer">The comparer for the mouse scroll action.</param>
        private void MouseScrollScanCallback(InputActionInfo info, InputActionDescription desc,
            Comparer<float> comparer)
        {
            switch (info.ActionState)
            {
                case ActionState.None:
                    if (comparer.Compare(Input.mouseScrollDelta.y, 0) > 0) info.SetState(ActionState.Started);
                    break;
                case ActionState.Started:
                    if (comparer.Compare(Input.mouseScrollDelta.y, 0) > 0)
                        info.SetState(ActionState.Performed);
                    else
                        info.SetState(ActionState.Canceled);
                    break;
                case ActionState.Performed:
                    if (comparer.Compare(Input.mouseScrollDelta.y, 0) == 0) info.SetState(ActionState.Canceled);
                    break;
                case ActionState.Canceled:
                    if (comparer.Compare(Input.mouseScrollDelta.y, 0) == 0)
                        info.SetState(ActionState.None);
                    else
                        info.SetState(ActionState.Started);
                    break;
            }

            if (info.ActionState != ActionState.None && info.Enabled) QueueAction(() => desc.InvokeListeners(info));
        }

        /// <summary>
        /// Callback for mouse displacement input scanning.
        /// </summary>
        /// <param name="info">The input action information.</param>
        /// <param name="desc">The input action description.</param>
        /// <param name="displaceFunc">The function to determine the displacement direction.</param>
        private void MouseDisplaceScanCallback(InputActionInfo info, InputActionDescription desc, Func<Vector2, bool> displaceFunc)
        {
            switch (info.ActionState)
            {
                case ActionState.None:
                    if (displaceFunc(_mouseDelta)) info.SetState(ActionState.Started);
                    break;
                case ActionState.Started:
                    if (displaceFunc(_mouseDelta))
                        info.SetState(ActionState.Performed);
                    else
                        info.SetState(ActionState.Canceled);
                    break;
                case ActionState.Performed:
                    if (!displaceFunc(_mouseDelta)) info.SetState(ActionState.Canceled);
                    break;
                case ActionState.Canceled:
                    if (!displaceFunc(_mouseDelta))
                        info.SetState(ActionState.None);
                    else
                        info.SetState(ActionState.Started);
                    break;
            }

            if (info.ActionState != ActionState.None && info.Enabled) QueueAction(() => desc.InvokeListeners(info));
        }

        /// <summary>
        /// Callback for controller button input scanning.
        /// </summary>
        /// <param name="button">The controller button for the action.</param>
        /// <param name="info">The input action information.</param>
        /// <param name="desc">The input action description.</param>
        private void ControllerButtonScanCallback(string button, InputActionInfo info, InputActionDescription desc)
        {
            switch (info.ActionState)
            {
                case ActionState.None:
                    if (Input.GetKeyDown(button)) info.SetState(ActionState.Started);
                    break;
                case ActionState.Started:
                    if (Input.GetKey(button))
                        info.SetState(ActionState.Performed);
                    else
                        info.SetState(ActionState.Canceled);
                    break;
                case ActionState.Performed:
                    if (Input.GetKeyUp(button)) info.SetState(ActionState.Canceled);
                    break;
                case ActionState.Canceled:
                    if (!Input.GetKey(button))
                        info.SetState(ActionState.None);
                    else
                        info.SetState(ActionState.Started);
                    break;
            }

            if (info.ActionState != ActionState.None && info.Enabled) QueueAction(() => desc.InvokeListeners(info));
        }
    }
}