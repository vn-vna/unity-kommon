using System;
using System.Collections.Generic;

namespace Com.Hapiga.Scheherazade.Common.LoadingManager
{
    /// <summary>
    /// Per-loading event listeners carried by a <see cref="LoadingBuilder"/>
    /// (via <c>WithStartListener</c> / <c>WithProgressListener</c> /
    /// <c>WithCompleteListener</c>). Ephemeral: wired on <c>Start</c> and
    /// released in <c>FinishLoading</c>.
    /// </summary>
    public sealed class LoadingSessionListeners
    {
        #region Fields
        public Action<IReadOnlyList<ILoadingTaskGroup>> OnStart;
        public Action<string, float, float> OnProgress;
        public Action OnComplete;
        #endregion
    }
}
