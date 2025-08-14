using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BugsnagUnity;
using ShovelTools;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;

namespace AddressableSystem
{
    /// <summary>
    /// Priority-aware Addressables download scheduler that runs entirely on
    /// the main thread and respects Unity's global MaxConcurrentWebRequests cap.
    /// Enhanced with duplicate download prevention.
    /// </summary>
    public sealed class DownloadManager : IDownloadManager
    {
        private readonly IReadOnlyDictionary<DownloadPriority, int> _cap;
        private readonly Dictionary<DownloadPriority, Queue<DownloadJob>> _queue;
        private readonly Dictionary<string, DownloadStatus> _running = new Dictionary<string, DownloadStatus>();
        private readonly Dictionary<string, Task<bool>> _pendingDownloads = new Dictionary<string, Task<bool>>();
        private readonly DLM.FeatureFlags _flag;
        private readonly IAsyncHandleRepository _repo;
        private bool _pumpActive;

        /// <summary>
        /// Event fired when a download completes, with key and success status.
        /// </summary>
        public event Action<string, bool> DownloadCompleted;

        /// <summary>
        /// Initializes a new instance of the DownloadManager class.
        /// </summary>
        /// <param name="featureFlag">Feature flag for logging.</param>
        /// <param name="handleRepository">Optional handle repository for asset management.</param>
        /// <param name="perPriorityCap">Optional custom concurrent download limits per priority.</param>
        public DownloadManager(
            DLM.FeatureFlags featureFlag = DLM.FeatureFlags.Addressables,
            IAsyncHandleRepository handleRepository = null,
            IDictionary<DownloadPriority, int> perPriorityCap = null)
        {
            _flag = featureFlag;
            _repo = handleRepository;

            // Default Unity concurrent web request limit
            int unityCap = 5;

            //Will be updated as per platform
            
            if (perPriorityCap != null)
            {
                _cap = new Dictionary<DownloadPriority, int>(perPriorityCap);
            }
            else
            {
                _cap = new Dictionary<DownloadPriority, int>
                {
                    { DownloadPriority.Critical, unityCap },
                    { DownloadPriority.High, unityCap - 1},
                    { DownloadPriority.Normal, unityCap - 2 },
                    { DownloadPriority.Low, 1 }
                };
            }

            _queue = Enum.GetValues(typeof(DownloadPriority))
                         .Cast<DownloadPriority>()
                         .ToDictionary(p => p, _ => new Queue<DownloadJob>());
        }

        /// <summary>
        /// Queues a download for a single asset with the specified priority.
        /// </summary>
        /// <param name="key">The addressable key of the asset to download.</param>
        /// <param name="pr">Download priority.</param>
        /// <param name="onProgress">Optional progress callback (0-1).</param>
        /// <param name="showDialog">Whether to show a download dialog.</param>
        /// <returns>True if download succeeded, false otherwise.</returns>
        public Task<bool> PreDownloadAssetAsync(
            string key, DownloadPriority pr,
            Action<float> onProgress = null, bool showDialog = false)
            => PreDownloadAssetAsync(key, pr, onProgress, showDialog, default);

        /// <summary>
        /// Queues downloads for multiple assets with the specified priority.
        /// </summary>
        /// <param name="keys">List of addressable keys to download.</param>
        /// <param name="pr">Download priority for all assets.</param>
        /// <param name="perKey">Optional per-key progress callback.</param>
        /// <param name="showDialog">Whether to show a download dialog.</param>
        /// <returns>True if all downloads succeeded, false otherwise.</returns>
        public Task<bool> PreDownloadAssetsAsync(
            List<string> keys, DownloadPriority pr = DownloadPriority.Normal,
            Action<string, float> perKey = null, bool showDialog = false)
            => PreDownloadAssetsAsync(keys, pr, perKey, showDialog, default);

        /// <summary>
        /// Downloads and loads an asset of type T.
        /// </summary>
        /// <typeparam name="T">The type of asset to load.</typeparam>
        /// <param name="key">The addressable key of the asset.</param>
        /// <param name="pr">Download priority.</param>
        /// <param name="r">Optional handle repository.</param>
        /// <param name="dlg">Whether to show a download dialog.</param>
        /// <param name="autoUnload">Whether to auto-unload the asset.</param>
        /// <returns>The loaded asset.</returns>
        public Task<T> QueueDownloadToLoadAsync<T>(
            string key, DownloadPriority pr,
            IAsyncHandleRepository r = null, bool dlg = false, bool autoUnload = true)
            where T : UnityEngine.Object
            => QueueDownloadToLoadAsync<T>(key, pr, r, dlg, autoUnload, default);

        /// <summary>
        /// Downloads and loads an asset with callbacks.
        /// </summary>
        /// <typeparam name="T">The type of asset to load.</typeparam>
        /// <param name="key">The addressable key of the asset.</param>
        /// <param name="pr">Download priority.</param>
        /// <param name="ok">Success callback with loaded asset.</param>
        /// <param name="fail">Optional failure callback.</param>
        /// <param name="r">Optional handle repository.</param>
        /// <param name="dlg">Whether to show a download dialog.</param>
        public void QueueDownloadToLoadAsync<T>(
            string key, DownloadPriority pr,
            Action<T> ok, Action<Exception> fail = null,
            IAsyncHandleRepository r = null, bool dlg = false)
            where T : UnityEngine.Object
            => QueueDownloadToLoadAsync<T>(key, pr, ok, fail, r, dlg, default);

        /// <summary>
        /// Downloads and loads a scene.
        /// </summary>
        /// <param name="key">The addressable key of the scene.</param>
        /// <param name="mode">Scene loading mode.</param>
        /// <param name="pr">Download priority.</param>
        /// <returns>The loaded scene instance.</returns>
        public Task<SceneInstance> QueueSceneDownloadToLoadAsync(
            string key, LoadSceneMode mode, DownloadPriority pr)
            => QueueSceneDownloadToLoadAsync(key, mode, pr, default);

        /// <summary>
        /// Queues a download for a single asset with cancellation support.
        /// </summary>
        /// <param name="key">The addressable key of the asset to download.</param>
        /// <param name="pr">Download priority.</param>
        /// <param name="onProgress">Optional progress callback (0-1).</param>
        /// <param name="showDialog">Whether to show a download dialog.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>True if download succeeded, false otherwise.</returns>
        public async Task<bool> PreDownloadAssetAsync(
            string key, DownloadPriority pr,
            Action<float> onProgress, bool showDialog,
            CancellationToken ct)
        {
            // Check if already downloading or in queue
            Task<bool> existingTask = null;
            lock (_pendingDownloads)
            {
                if (_pendingDownloads.TryGetValue(key, out existingTask))
                {
                    DLM.Log(_flag, $"[DownloadManager]: Download for '{key}' already in progress, returning existing task");
                }
            }
            
            if (existingTask != null)
            {
                return await existingTask;
            }

            // Check if already cached before queuing
            try
            {
                var downloadSize = await GetDownloadSizeAsync(key);
                if (downloadSize == 0)
                {
                    DLM.Log(_flag, $"[DownloadManager]: Asset '{key}' already cached, skipping download");
                    onProgress?.Invoke(1f);
                    return true;
                }
            }
            catch (Exception ex)
            {
                DLM.LogError(_flag, $"[DownloadManager]: Error checking cache status for '{key}': {ex.Message}");
                Bugsnag.Notify(new System.Exception($"[DownloadManager]: Error checking cache status for '{key}': {ex.Message}"));
            }

            var tcs = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            // Register the pending download
            lock (_pendingDownloads)
            {
                _pendingDownloads[key] = tcs.Task;
            }

            // Clean up pending downloads when complete
            tcs.Task.ContinueWith(t =>
            {
                lock (_pendingDownloads)
                {
                    _pendingDownloads.Remove(key);
                }
            }, TaskScheduler.Default);

            var job = new DownloadJob(key, pr, onProgress, showDialog, ct, tcs);
            lock (_queue) _queue[pr].Enqueue(job);
            EnsurePump();
            return await tcs.Task;
        }

        /// <summary>
        /// Queues downloads for multiple assets with cancellation support.
        /// </summary>
        /// <param name="keys">List of addressable keys to download.</param>
        /// <param name="pr">Download priority for all assets.</param>
        /// <param name="perKey">Optional per-key progress callback.</param>
        /// <param name="showDialog">Whether to show a download dialog.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>True if all downloads succeeded, false otherwise.</returns>
        public async Task<bool> PreDownloadAssetsAsync(
            List<string> keys, DownloadPriority pr,
            Action<string, float> perKey, bool showDialog,
            CancellationToken ct)
        {
            bool allOk = true;
            foreach (string k in keys)
            {
                allOk &= await PreDownloadAssetAsync(
                             k, pr,
                             p => perKey?.Invoke(k, p),
                             showDialog, ct);
            }
            return allOk;
        }

        /// <summary>
        /// Downloads and loads an asset with cancellation support.
        /// </summary>
        /// <typeparam name="T">The type of asset to load.</typeparam>
        /// <param name="key">The addressable key of the asset.</param>
        /// <param name="pr">Download priority.</param>
        /// <param name="repo">Optional handle repository.</param>
        /// <param name="dlg">Whether to show a download dialog.</param>
        /// <param name="autoUnload">Whether to auto-unload the asset.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The loaded asset.</returns>
        public async Task<T> QueueDownloadToLoadAsync<T>(
            string key, DownloadPriority pr,
            IAsyncHandleRepository repo, bool dlg,
            bool autoUnload, CancellationToken ct)
            where T : UnityEngine.Object
        {
            bool ok = await PreDownloadAssetAsync(key, pr, null, dlg, ct);
            if (!ok) throw new InvalidOperationException($"Download failed for '{key}'");

            var tcs = new TaskCompletionSource<T>();
            var loader = new AddressableLoader();

            loader.LoadAssetAsync<T>(
                key,
                asset => tcs.SetResult(asset),
                ex => tcs.SetException(ex),
                autoUnload,
                repo ?? _repo);

            return await tcs.Task;
        }

        /// <summary>
        /// Downloads and loads an asset with callbacks and cancellation support.
        /// </summary>
        /// <typeparam name="T">The type of asset to load.</typeparam>
        /// <param name="key">The addressable key of the asset.</param>
        /// <param name="pr">Download priority.</param>
        /// <param name="onSuccess">Success callback with loaded asset.</param>
        /// <param name="onFail">Optional failure callback.</param>
        /// <param name="repo">Optional handle repository.</param>
        /// <param name="dlg">Whether to show a download dialog.</param>
        /// <param name="ct">Cancellation token.</param>
        public void QueueDownloadToLoadAsync<T>(
            string key, DownloadPriority pr,
            Action<T> onSuccess, Action<Exception> onFail,
            IAsyncHandleRepository repo, bool dlg,
            CancellationToken ct)
            where T : UnityEngine.Object
        {
            _ = RunDownloadAndLoadAsync(
                    key, pr, onSuccess, onFail,
                    repo ?? _repo, dlg, ct);
        }

        /// <summary>
        /// Downloads and loads a scene with cancellation support.
        /// </summary>
        /// <param name="key">The addressable key of the scene.</param>
        /// <param name="mode">Scene loading mode.</param>
        /// <param name="pr">Download priority.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The loaded scene instance.</returns>
        public async Task<SceneInstance> QueueSceneDownloadToLoadAsync(
            string key, LoadSceneMode mode, DownloadPriority pr,
            CancellationToken ct)
        {
            bool cached = await PreDownloadAssetAsync(key, pr, null, false, ct);
            if (!cached)
                throw new InvalidOperationException($"Scene download failed for '{key}'");

            var h = Addressables.LoadSceneAsync(key, mode, true);
            await h.Task;

            if (h.Status != AsyncOperationStatus.Succeeded)
            {
                string msg = h.OperationException?.Message ?? "Unknown";
                Addressables.Release(h);
                throw new Exception($"LoadSceneAsync('{key}') failed: {msg}");
            }
            return h.Result;
        }

        /// <summary>
        /// Gets the download progress for a specific asset.
        /// </summary>
        /// <param name="key">The addressable key to check.</param>
        /// <returns>Progress value between 0 and 1.</returns>
        public async Task<float> GetDownloadStatusAsync(string key)
        {
            if (_running.ContainsKey(key)) return 0f;

            var sz = Addressables.GetDownloadSizeAsync(key);
            await sz.Task;
            float p = sz.Status == AsyncOperationStatus.Succeeded && sz.Result == 0
                    ? 1f : 0f;
            Addressables.Release(sz);
            return p;
        }

        /// <summary>
        /// Gets the download size in bytes for a specific asset.
        /// </summary>
        /// <param name="key">The addressable key to check.</param>
        /// <returns>Download size in bytes.</returns>
        public async Task<long> GetDownloadSizeAsync(string key)
        {
            var op = Addressables.GetDownloadSizeAsync(key);
            await op.Task;
            long s = op.Status == AsyncOperationStatus.Succeeded ? op.Result : 0;
            Addressables.Release(op);
            return s;
        }

        /// <summary>
        /// Gets comprehensive information about an asset.
        /// </summary>
        /// <param name="key">The addressable key to check.</param>
        /// <returns>Tuple containing existence, progress, and size information.</returns>
        public async Task<(bool exists, float progress, long sizeBytes)>
            GetAssetInfoAsync(string key)
        {
            var loc = Addressables.LoadResourceLocationsAsync(key);
            await loc.Task;
            if (loc.Status != AsyncOperationStatus.Succeeded || loc.Result.Count == 0)
            {
                Addressables.Release(loc);
                return (false, 0f, 0L);
            }

            long size = await GetDownloadSizeAsync(key);
            float prog = await GetDownloadStatusAsync(key);
            Addressables.Release(loc);
            return (true, prog, size);
        }

        /// <summary>
        /// Gets the current queue status.
        /// </summary>
        /// <returns>Tuple containing active and queued download counts.</returns>
        public (int activeDownloads, int queuedDownloads) GetQueueStatus()
        {
            lock (_queue)
                return (_running.Count, _queue.Sum(kv => kv.Value.Count));
        }

        /// <summary>
        /// Releases a loaded asset.
        /// </summary>
        /// <param name="asset">The asset to release.</param>
        public void ReleaseAsset(UnityEngine.Object asset)
        {
            if (asset != null) Addressables.Release(asset);
        }

        /// <summary>
        /// Releases a loaded scene.
        /// </summary>
        /// <param name="si">The scene instance to release.</param>
        /// <returns>Task that completes when the scene is unloaded.</returns>
        public async Task ReleaseSceneAsync(SceneInstance si)
        {
            if (!si.Scene.IsValid()) return;
            var op = Addressables.UnloadSceneAsync(si);
            await op.Task;
        }

        /// <summary>
        /// Cancels a specific download if it's active.
        /// </summary>
        /// <param name="key">The addressable key of the download to cancel.</param>
        /// <returns>True if the download was cancelled, false if not found.</returns>
        public bool CancelDownload(string key)
        {
            if (_running.TryGetValue(key, out var st))
            {
                st.Cts.Cancel();
                return true;
            }
            return false;
        }

        /// <summary>
        /// Cancels all active downloads.
        /// </summary>
        public void CancelAllDownloads()
        {
            AddressableDownloadHudNotifier.NotifyDownloadCancelled();
            foreach (var st in _running.Values) st.Cts.Cancel();
        }

        /// <summary>
        /// Sets the maximum concurrent downloads for a priority level.
        /// </summary>
        /// <param name="priority">The priority level to configure.</param>
        /// <param name="max">Maximum concurrent downloads.</param>
        public void SetConcurrency(DownloadPriority priority, int max)
        {
            DLM.LogWarning(_flag, "[DownloadManager]: SetConcurrency not implemented in this version");
        }

        private async Task RunDownloadAndLoadAsync<T>(
            string key, DownloadPriority pr,
            Action<T> ok, Action<Exception> fail,
            IAsyncHandleRepository repo, bool dlg,
            CancellationToken ct) where T : UnityEngine.Object
        {
            try
            {
                var obj = await QueueDownloadToLoadAsync<T>(
                              key, pr, repo, dlg, true, ct);
                ok?.Invoke(obj);
            }
            catch (Exception ex) { fail?.Invoke(ex); }
        }

        private void EnsurePump()
        {
            if (_pumpActive) return;
            _pumpActive = true;
            _ = PumpAsync();
        }

        private async Task PumpAsync()
        {
            try
            {
                while (true)
                {
                    foreach (var pr in _queue.Keys.OrderByDescending(p => p))
                    {
                        int running = _running.Values.Count(s => s.Priority == pr);
                        int cap = _cap[pr];

                        while (running < cap && _queue[pr].Count > 0)
                        {
                            DownloadJob job;
                            lock (_queue)
                            {
                                if (_queue[pr].Count == 0)
                                    break;
                                job = _queue[pr].Dequeue();
                            }
                            
                            _ = RunJobAsync(job);
                            running++;
                        }
                    }

                    lock (_queue)
                    {
                        if (_running.Count == 0 && _queue.All(kv => kv.Value.Count == 0))
                            return;
                    }

                    await Task.Yield();
                }
            }
            finally 
            { 
                _pumpActive = false; 
            }
        }

        private async Task RunJobAsync(DownloadJob job)
        {
            var cts = CancellationTokenSource.CreateLinkedTokenSource(job.Ct);
            var status = new DownloadStatus(job.Priority, cts);
            _running[job.Key] = status;

            bool success = false;
            try
            {
                success = await DownloadAsync(job, cts.Token);
                job.Tcs.TrySetResult(success);
            }
            catch (OperationCanceledException)
            {
                job.Tcs.TrySetCanceled();
            }
            catch (Exception ex)
            {
                DLM.LogError(_flag, ex.Message);
                job.Tcs.TrySetException(ex);
            }
            finally
            {
                _running.Remove(job.Key);
                cts.Dispose();
                DownloadCompleted?.Invoke(job.Key, success);
            }
        }

        private async Task<bool> DownloadAsync(DownloadJob job, CancellationToken ct)
        {
            var locHandle = Addressables.LoadResourceLocationsAsync(job.Key);
            await locHandle.Task;
            if (locHandle.Status != AsyncOperationStatus.Succeeded || locHandle.Result.Count == 0)
            {
                DLM.LogError(_flag, $"[DownloadManager]: No locations for '{job.Key}'");
                Addressables.Release(locHandle);
                return false;
            }

            // Check size
            var sizeHandle = Addressables.GetDownloadSizeAsync(locHandle.Result);
            await sizeHandle.Task;
            long bytes = sizeHandle.Status == AsyncOperationStatus.Succeeded ? sizeHandle.Result : 0;
            Addressables.Release(sizeHandle);

            if (bytes == 0)
            {
                job.Progress?.Invoke(1f);
                Addressables.Release(locHandle);
                return true;
            }

            if (job.ShowDialog)
                AddressableDownloadHudNotifier.NotifyDownloadStarted(job.Key);

            var dlHandle = Addressables.DownloadDependenciesAsync(locHandle.Result, false);
            double last = 0;

            try
            {
                // progress pump
                while (!dlHandle.IsDone)
                {
                    ct.ThrowIfCancellationRequested();
                    double now = Time.realtimeSinceStartupAsDouble;
                    if (now - last > 0.25)
                    {
                        last = now;
                        job.Progress?.Invoke(dlHandle.PercentComplete);
                    }
                    await Task.Yield();
                }

                // after loop, check status
                if (dlHandle.Status != AsyncOperationStatus.Succeeded)
                {
                    var errMsg = dlHandle.OperationException?.Message ?? "Unknown download error";
                    if (errMsg.Contains("403"))
                    {
                        OnBundleForbidden(job.Key, errMsg);
                    }
                    else
                    {
                        Bugsnag.Notify(dlHandle.OperationException ?? new Exception(errMsg));
                    }
                    return false;
                }

                // success
                job.Progress?.Invoke(1f);
                return true;
            }
            catch (OperationCanceledException)
            {
                // let cancellation bubble up if you want
                throw;
            }
            catch (Exception ex)
            {
                // any other unexpected
                var msg = ex.Message;
                if (msg.Contains("403"))
                    OnBundleForbidden(job.Key, msg);
                else
                    Bugsnag.Notify(ex);
                return false;
            }
            finally
            {
                Addressables.Release(dlHandle);
                Addressables.Release(locHandle);
            }
        }

        // 403 callback
        private async void OnBundleForbidden(string key, string message)
        {
            DLM.LogError(_flag, $"[DownloadManager]: Bundle '{key}' forbidden (403): {message}");

            bool ok = await AddressableManager.Instance.EnsureCatalogValidAsync();
            if (!ok)
            {
                DLM.LogError(_flag, "[DownloadManager]: Catalog refresh failed after a 403—check your network or credentials.");
                Bugsnag.Notify(new System.Exception($"[DownloadManager]: Catalog refresh failed after a 403—check your network or credentials. key: {key} \n {message}"));
            }
        }

    }
}