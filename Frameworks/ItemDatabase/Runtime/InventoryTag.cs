using UnityEngine.Scripting;

namespace Com.Hapiga.Scheherazade.Common.ItemDatabase
{
    /// <summary>
    /// Marker interface for runtime tag data classes.
    /// The [TagData] attribute links each implementation to its TagDefinition.
    /// </summary>
    [RequireImplementors]
    public interface ITagData
    {
    }
}
