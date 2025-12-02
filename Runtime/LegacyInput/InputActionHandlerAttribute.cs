using System;

namespace Com.Hapiga.Scheherazade.Common.LegacyInput
{
    /// <summary>
    /// Attribute to mark methods as input action handlers.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = true)]
    public sealed class InputActionHandlerAttribute : Attribute
    {
        public InputActionHandlerAttribute(string mapping, string name, ActionState fireState)
        {
            Mapping = mapping;
            Name = name;
            FireState = fireState;
        }

        public string Mapping { get; set; }
        public string Name { get; set; }
        public ActionState FireState { get; set; }
    }
}