using Com.Hapiga.Scheherazade.Common.AsyncResourceLoader;

namespace Com.Hapiga.Scheherazade.Common.Frameworks.PuzzleLevels
{
    public interface IPuzzleLevelData
    {
        string LevelId { get; }
        DataType Type { get; }
        bool IsLoaded { get; }
        string GetText();
        byte[] GetBytes();
        T GetParsed<T>();
    }
}
