namespace Com.Hapiga.Scheherazade.Common.Frameworks.GridSystem
{
    /// <summary>
    /// Minimal base for model generation (drop-car <c>ModelGenerator</c>).
    /// Implementations may be plain classes or Unity components; the core only
    /// ever calls the two methods.
    /// </summary>
    public interface IModelGenerator
    {
        void GenerateModel();
        void UpdateModel();
    }
}
