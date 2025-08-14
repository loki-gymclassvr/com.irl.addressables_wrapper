//
// AddressableSystem.Active.cs
//
using System.Threading;

namespace AddressableSystem
{
    /// <summary>
    /// Tracks an in-flight download together with its cancellation source.
    /// </summary>
    internal sealed class DownloadStatus
    {
        public readonly DownloadPriority Priority;
        public readonly CancellationTokenSource Cts;

        public DownloadStatus(DownloadPriority priority, CancellationTokenSource cts)
        {
            Priority = priority;
            Cts = cts;
        }
    }
}