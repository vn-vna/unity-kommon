using System;
using System.Linq;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.LegacyInput
{
    /// <summary>
    /// Represents an entry for an input action.
    /// </summary>
    [Serializable]
    public class InputActionEntry
    {
        [field: SerializeField]
        public string Name { get; set; }

        [field: SerializeField]
        public string Path { get; set; }

        private readonly string[] _validInputDevices =
        {
            "Keyboard",
            "MouseButton",
            "MouseScroll",
            "Gamepad",
            "MouseDisplace",
            "ControllerButton"
        };

#if UNITY_EDITOR

        private bool VerifyInputPath(string path, ref string message)
        {
            if (path == null || path == string.Empty) return false;

            var tokens = path.Split("::");

            if (tokens.Length != 2)
            {
                message = "Path must be in the format of \"Device::Input\".";
                return false;
            }

            if (!_validInputDevices.Contains(tokens[0]))
            {
                message = $"Invalid device \"{tokens[0]}\".";
                return false;
            }

            if (string.IsNullOrWhiteSpace(tokens[1]))
            {
                message = "Input name cannot be empty.";
                return false;
            }

            if (tokens[0] == "Keyboard" && !VerifyKeyboardMapping(tokens[1], ref message)) return false;

            if (tokens[0] == "MouseButton" && !VerifyMouseMapping(tokens[1], ref message)) return false;

            if (tokens[0] == "Gamepad" && !VerifyGamepadMapping(tokens[1], ref message)) return false;

            if (tokens[0] == "MouseScroll" && !VerifyMouseScrollMapping(tokens[1], ref message)) return false;

            return true;
        }

        private bool VerifyKeyboardMapping(string mapping, ref string message)
        {
            if (!LegacyInputController.KeyCodeMappings.ContainsKey(mapping))
            {
                message = $"Invalid key code \"{mapping}\".";
                return false;
            }

            return true;
        }

        private bool VerifyMouseMapping(string mapping, ref string message)
        {
            if (!LegacyInputController.MouseButtonMappings.ContainsKey(mapping))
            {
                message = $"Invalid mouse button \"{mapping}\".";
                return false;
            }

            return true;
        }

        private bool VerifyGamepadMapping(string mapping, ref string message)
        {
            if (!Enum.TryParse(mapping, out KeyCode _))
            {
                message = $"Invalid key code \"{mapping}\".";
                return false;
            }

            return true;
        }

        private bool VerifyMouseScrollMapping(string mapping, ref string message)
        {
            if (!LegacyInputController.MouseScrollMappings.ContainsKey(mapping))
            {
                message = $"Invalid mouse scroll direction \"{mapping}\".";
                return false;
            }

            return true;
        }
#endif
    }
}