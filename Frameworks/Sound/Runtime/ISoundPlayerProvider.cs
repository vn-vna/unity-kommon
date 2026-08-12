namespace Com.Hapiga.Scheherazade.Common.Sound
{
    /// <summary>
    /// Contract for a sound player provider. Implemented by ScriptableObjects;
    /// the manager stays provider-agnostic and only talks through this interface.
    /// </summary>
    public interface ISoundPlayerProvider
    {
        string ProviderId { get; }
        bool IsAvailable { get; }

        /// <summary>Called once at manager Awake.</summary>
        void Initialize();

        /// <summary>Plays a definition and returns a controllable handle.</summary>
        SoundHandle Play(SoundDefinition def, float volumeScale = 1f);

        void Stop(SoundHandle handle);
        void Pause(SoundHandle handle);
        void Resume(SoundHandle handle);
        void StopAll();

        void SetBusVolume(SoundBusType bus, float volume);
    }
}
