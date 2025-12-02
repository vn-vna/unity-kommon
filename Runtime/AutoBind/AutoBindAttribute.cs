namespace Com.Hapiga.Scheherazade.Common.AutoBind
{
    [System.Flags]
    public enum AutoBindFromFlag
    {
        GameObject = 1 << 0,
        Children = 1 << 1,
        Parents = 1 << 2,
    }

    public class AutoBindAttribute
    {
        public AutoBindFromFlag From { get; private set; }
        public string Condition { get; private set; }
    }
}