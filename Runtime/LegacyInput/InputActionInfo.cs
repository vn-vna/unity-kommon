namespace Com.Hapiga.Scheherazade.Common.LegacyInput
{
    /// <summary>
    /// Contains information about an input action, including its state and whether it is enabled.
    /// </summary>
    public class InputActionInfo
    {

        public ActionState ActionState { get; private set; } = ActionState.None;
        public bool Enabled { get; set; }
        public object Value { get; set; }

        public void Reset()
        {
            ActionState = 0;
        }

        public void SetState(ActionState state)
        {
            ActionState = state;
        }

        public void Enable()
        {
            Enabled = true;
        }

        public void Disable()
        {
            Enabled = false;
        }
    }
}