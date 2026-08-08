using System;
using System.Collections.Generic;
using Com.Hapiga.Scheherazade.Common.Logging;
using Com.Hapiga.Scheherazade.Common.Singleton;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Frameworks.GridSystem
{
    /// <summary>
    /// Registry + single tick pump. Auto-created hidden GameObject; ticks every
    /// registered <see cref="GridBoard"/> in registration order. Boards never
    /// implement Update() themselves.
    /// </summary>
    [AddComponentMenu("")] // Hidden component
    public class GridManager : SingletonBehavior<GridManager>
    {
        #region Events & Delegates

        public event Action<GridBoard> BoardRegistered;
        public event Action<GridBoard> BoardUnregistered;

        #endregion

        #region Properties

        public GridManagerStatus Status { get; private set; } = GridManagerStatus.Uninitialized;

        public IReadOnlyList<GridBoard> RegisteredBoards => _boards;

        #endregion

        #region Private Fields

        private readonly List<GridBoard> _boards = new List<GridBoard>();
        private readonly Dictionary<string, GridBoard> _boardsById = new Dictionary<string, GridBoard>();

        #endregion

        #region Bootstrap

        public static GridManager EnsureCreated()
        {
            if (Instance != null) return Instance;

            GameObject go = new GameObject("[Scheherazade Grid Manager]");
            go.hideFlags = HideFlags.HideInHierarchy;
            go.AddComponent<GridManager>();
            go.AddComponent<KeepAliveComponent>();
            return Instance;
        }

        #endregion

        #region Unity Callbacks

        private void Update()
        {
            if (Status != GridManagerStatus.Ready) return;

            // Single tick source: deterministic registration order.
            for (int i = 0; i < _boards.Count; i++)
            {
                _boards[i].Tick();
            }
        }

        #endregion

        #region Public Methods

        public void Register(GridBoard board)
        {
            if (board == null || _boards.Contains(board)) return;

            _boards.Add(board);
            if (!string.IsNullOrEmpty(board.Configuration?.Id))
            {
                _boardsById[board.Configuration.Id] = board;
            }

            board.Initialize();
            Status = GridManagerStatus.Ready;
            BoardRegistered?.Invoke(board);
            QuickLog.Debug<GridManager>(
                "Board '{0}' registered. Total: {1}",
                board.Configuration?.Id,
                _boards.Count
            );
        }

        public void Unregister(GridBoard board)
        {
            if (board == null || !_boards.Remove(board)) return;

            if (!string.IsNullOrEmpty(board.Configuration?.Id))
            {
                _boardsById.Remove(board.Configuration.Id);
            }

            board.Shutdown();
            BoardUnregistered?.Invoke(board);

            if (_boards.Count == 0) Status = GridManagerStatus.Uninitialized;
            QuickLog.Debug<GridManager>(
                "Board '{0}' unregistered. Total: {1}",
                board.Configuration?.Id,
                _boards.Count
            );
        }

        public GridBoard GetGrid(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            _boardsById.TryGetValue(id, out GridBoard board);
            return board;
        }

        #endregion
    }
}
