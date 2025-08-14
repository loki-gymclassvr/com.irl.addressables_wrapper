using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;

namespace AddressableSystem
{
    /// <summary>
    /// Interface for download management — on-demand cache of bundles and then load.
    /// Enhanced with real-time progress tracking and asset information methods.
    /// </summary>
    public interface IDownloadManager
    {
        /// <summary>
        /// Raised when a PreDownloadAssetAsync completes (key, success).
        /// </summary>
        event Action<string, bool> DownloadCompleted;

        #region Download and Load Operations

        /// <summary>
        /// Download all bundles for the given key, then load the asset of type T.
        /// </summary>
        Task<T> QueueDownloadToLoadAsync<T>(
            string key,
            DownloadPriority priority,
            IAsyncHandleRepository handleRepository,
            bool showLoadingDialog = false,
            bool autoUnload = true
        ) where T : UnityEngine.Object;

        /// <summary>
        /// Download bundles for the key, then load the asset and invoke your callback.
        /// </summary>
        void QueueDownloadToLoadAsync<T>(
            string key,
            DownloadPriority priority,
            Action<T> onSuccess = null,
            Action<Exception> onFail = null,
            IAsyncHandleRepository handleRepository = null,
            bool showLoadingDialog = false
        ) where T : UnityEngine.Object;

        /// <summary>
        /// Download bundles for the scene key, then load it.
        /// </summary>
        Task<SceneInstance> QueueSceneDownloadToLoadAsync(
            string key,
            LoadSceneMode loadMode,
            DownloadPriority priority
        );

        #endregion

        #region Progress and Status Methods

        /// <summary>
        /// Returns real-time progress (0 to 1) of the download for the given key.
        /// For active downloads, returns actual progress; for cached content returns 1.0.
        /// </summary>
        Task<float> GetDownloadStatusAsync(string key);

        /// <summary>
        /// Gets the download size in bytes for the given key.
        /// Returns 0 if already cached or if the key doesn't exist.
        /// </summary>
        Task<long> GetDownloadSizeAsync(string key);

        /// <summary>
        /// Gets comprehensive information about an asset including existence, progress, and size.
        /// Returns (exists, progress 0-1, sizeInBytes).
        /// </summary>
        Task<(bool exists, float progress, long sizeBytes)> GetAssetInfoAsync(string key);

        /// <summary>
        /// Returns (activeDownloads, queuedDownloads) for monitoring download queue status.
        /// Enhanced to return actual counts of tracked downloads.
        /// </summary>
        (int activeDownloads, int queuedDownloads) GetQueueStatus();

        #endregion

        #region Asset and Scene Management

        /// <summary>
        /// Release a loaded asset from memory.
        /// </summary>
        void ReleaseAsset(UnityEngine.Object asset);

        /// <summary>
        /// Unload a loaded scene instance.
        /// </summary>
        Task ReleaseSceneAsync(SceneInstance sceneInstance);

        #endregion

        #region Download Control

        /// <summary>
        /// Cancel all pending and active downloads.
        /// Enhanced to actually cancel tracked downloads.
        /// </summary>
        void CancelAllDownloads();

        /// <summary>
        /// Cancel a specific download by key.
        /// Enhanced to actually cancel the download if it's active.
        /// </summary>
        /// <param name="key">Key of download to cancel.</param>
        /// <returns>True if download was found and cancelled, false otherwise.</returns>
        bool CancelDownload(string key);

        #endregion

        #region Pre-Download (Caching) Operations

        /// <summary>
        /// Download and cache all bundles for the given key without loading the asset.
        /// Enhanced with real-time progress reporting during download.
        /// </summary>
        /// <param name="key">Addressable key to cache.</param>
        /// <param name="priority">Download priority level.</param>
        /// <param name="onProgress">Optional progress callback (0–1) called during download.</param>
        /// <returns>True if cached successfully or already present, false on failure.</returns>
        Task<bool> PreDownloadAssetAsync(string key, DownloadPriority priority, Action<float> onProgress = null, bool showLoadingDialog = false);

        /// <summary>
        /// Batch version of PreDownloadAssetAsync for multiple keys.
        /// Reports progress for each key individually.
        /// </summary>
        /// <param name="keys">List of addressable keys to cache.</param>
        /// <param name="priority">Download priority level.</param>
        /// <param name="onProgress">Optional callback reporting progress per key (key, progress 0-1).</param>
        /// <returns>True if all keys cached successfully, false if any failed.</returns>
        Task<bool> PreDownloadAssetsAsync(List<string> keys, DownloadPriority priority, Action<string, float> onProgress = null, bool showLoadingDialog = false);

        #endregion
    }
}