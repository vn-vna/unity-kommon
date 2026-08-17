using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Frameworks.GridSystem
{
    /// <summary>
    /// Scriptable-delivered placeable shape config (drop-car
    /// <c>PlaceableObject</c> MonoBehaviour ported to a plain ScriptableObject).
    /// </summary>
    [CreateAssetMenu(menuName = "Scheherazade/Grid System/Placeable Object")]
    public class PlaceableObject : ScriptableObject, IPlaceableObject
    {
        #region Serialized Fields

#if UNITY_EDITOR
        [Tooltip("Unique identifier used by level sources and spawners.")]
#endif
        [SerializeField]
        private string _id;

#if UNITY_EDITOR
        [Tooltip("Shape anchor offset in the grid. The hooked cell matches the shape's origin cell.")]
#endif
        [SerializeField]
        private GridCoord _offset;

#if UNITY_EDITOR
        [Tooltip("Occupied-cell shape. Cells >= 0 are occupied parts; -1 is empty.")]
#endif
        [SerializeField]
        private PlaceableObjectGrid _grid = new PlaceableObjectGrid(1, 1);

        #endregion

        #region Properties

        public string Id => _id;
        public GridCoord Offset { get => _offset; set => _offset = value; }
        public PlaceableObjectGrid Grid { get => _grid; set => _grid = value; }

        #endregion
    }
}
