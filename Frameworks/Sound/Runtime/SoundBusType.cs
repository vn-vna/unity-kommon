namespace Com.Hapiga.Scheherazade.Common.Sound
{
    /// <summary>
    /// Which mix bus a sound definition belongs to.
    /// Sfx = one-shot / looped gameplay sounds (pooled player provider).
    /// Bgm = looping background music tracks.
    /// </summary>
    public enum SoundBusType
    {
        Sfx,
        Bgm
    }
}
