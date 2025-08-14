//
// AddressableSystem.DownloadJob.cs
//
using System;
using System.Threading;
using System.Threading.Tasks;

namespace AddressableSystem
{
    /// <summary>
    /// Immutable record representing one queued download request.
    /// </summary>
    internal readonly struct DownloadJob
    {
        public readonly string Key;
        public readonly DownloadPriority Priority;
        public readonly Action<float> Progress;
        public readonly bool ShowDialog;
        public readonly CancellationToken Ct;
        public readonly TaskCompletionSource<bool> Tcs;

        public DownloadJob(
            string key,
            DownloadPriority priority,
            Action<float> progress,
            bool showDialog,
            CancellationToken ct,
            TaskCompletionSource<bool> tcs)
        {
            Key = key;
            Priority = priority;
            Progress = progress;
            ShowDialog = showDialog;
            Ct = ct;
            Tcs = tcs;
        }
    }
}