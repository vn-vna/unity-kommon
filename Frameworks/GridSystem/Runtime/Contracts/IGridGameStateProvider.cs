namespace Com.Hapiga.Scheherazade.Common.Frameworks.GridSystem
{
    /// <summary>
    /// Game-state gates the board honors while ticking. Null-safe: when a board
    /// has no provider assigned, interaction is always considered active.
    /// </summary>
    public interface IGridGameStateProvider
    {
        bool IsPaused { get; }
        bool IsInteractionActive { get; }
    }
}
