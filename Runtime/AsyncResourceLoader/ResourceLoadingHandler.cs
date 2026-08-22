using System;
using System.Threading;

namespace Com.Hapiga.Scheherazade.Common.AsyncResourceLoader
{
    public class ResourceLoadingHandler<ResourceType>
        where ResourceType : class
    {
        private int _cancellationRequested;

        public ResourceType Resouce { get; set; }
        public LoadingStatus LoadingStatus { get; set; }
        public ResourceStatus ResourceStatus { get; set; }
        public string ProviderSource { get; set; }
        public IAsyncResourceProvider Provider { get; set; }
        public Exception Exception { get; set; }
        public float Progress { get; set; }

        public bool IsCancellationRequested =>
            Volatile.Read(ref _cancellationRequested) != 0;

        public bool IsCompleted => LoadingStatus == LoadingStatus.Completed;

        public void Cancel(CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _cancellationRequested, 1) != 0)
            {
                return;
            }

            if (IsCompleted)
            {
                return;
            }

            Exception = new OperationCanceledException(cancellationToken);
            ResourceStatus = ResourceStatus.Canceled;
            LoadingStatus = LoadingStatus.Completed;
        }
    }
}
