using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace AddressableSystem.Examples
{
    /// <summary>
    /// Test script to demonstrate downloading remote addressables from a CDN
    /// </summary>
    public class AddressableCdnTest : MonoBehaviour
    {
        [Header("CDN Configuration")]
        [SerializeField] private string _cdnBaseUrl = "https://your-cdn-url.com/assets/";
        [SerializeField] private List<string> _remoteCatalogs = new List<string>();
        
        [Header("Remote Asset Keys")]
        [SerializeField] private List<string> _remoteAssetKeys = new List<string>();
        [SerializeField] private List<string> _preloadAssetKeys = new List<string>();
        
        [Header("UI Elements")]
        [SerializeField] private Button _downloadAllButton;
        [SerializeField] private Button _checkCacheButton;
        [SerializeField] private Button _clearCacheButton;
        [SerializeField] private Button _preloadAssetsButton;
        [SerializeField] private Text _statusText;
        [SerializeField] private Slider _progressSlider;
        
        private AddressableManager _addressableManager;
        private bool _isInitialized = false;
        
        private void Awake()
        {
            // Get singleton instance
            _addressableManager = AddressableManager.Instance;
            
            // Set the CDN URL
            //_addressableManager.SetCdnUrl(_cdnBaseUrl);
            
            // Set up UI
            if (_downloadAllButton != null)
                _downloadAllButton.onClick.AddListener(DownloadAllRemoteAssets);
            
            if (_checkCacheButton != null)
                _checkCacheButton.onClick.AddListener(CheckCachedAssets);
            
            if (_clearCacheButton != null)
                _clearCacheButton.onClick.AddListener(ClearCache);
            
            if (_preloadAssetsButton != null)
                _preloadAssetsButton.onClick.AddListener(PreloadAssets);
            
            // Configure progress UI
            if (_progressSlider != null)
            {
                _progressSlider.minValue = 0f;
                _progressSlider.maxValue = 1f;
                _progressSlider.value = 0f;
            }
            
            // Subscribe to download events
            //_addressableManager.DownloadQueue.DownloadCompleted += OnDownloadCompleted;
        }
        
        private async void Start()
        {
            // Initialize with remote catalogs
            UpdateStatus("Initializing addressables with remote catalogs...");
            
            _isInitialized = await _addressableManager.InitializeWithCatalogsAsync(_remoteCatalogs);
            
            if (_isInitialized)
            {
                UpdateStatus("Initialization complete. Ready to download remote assets.");
            }
            else
            {
                UpdateStatus("Initialization failed. Could not load remote catalogs.");
            }
        }
        
        /// <summary>
        /// Download all remote assets
        /// </summary>
        private async void DownloadAllRemoteAssets()
        {
            if (!_isInitialized)
            {
                UpdateStatus("Addressables not initialized yet. Please wait.");
                return;
            }
            
            if (_remoteAssetKeys.Count == 0)
            {
                UpdateStatus("No remote asset keys defined.");
                return;
            }
            
            UpdateStatus($"Downloading {_remoteAssetKeys.Count} remote assets...");
            
            bool allSuccess = true;
            int completedCount = 0;
            
            foreach (string key in _remoteAssetKeys)
            {
                UpdateStatus($"Downloading {key} ({completedCount + 1}/{_remoteAssetKeys.Count})...");
                
                // Monitor progress for this asset
                _ = MonitorDownloadProgress(key);
                
                // Queue this asset for download
                bool success = await _addressableManager.PreDownloadAssetAsync(key);
                
                if (!success)
                {
                    allSuccess = false;
                    UpdateStatus($"Failed to download {key}");
                }
                
                completedCount++;
            }
            
            if (allSuccess)
            {
                UpdateStatus($"Successfully downloaded all {_remoteAssetKeys.Count} remote assets.");
            }
            else
            {
                UpdateStatus($"Completed with some failures. {completedCount}/{_remoteAssetKeys.Count} assets downloaded.");
            }
        }
        
        /// <summary>
        /// Check which assets are already cached
        /// </summary>
        private async void CheckCachedAssets()
        {
            if (!_isInitialized)
            {
                UpdateStatus("Addressables not initialized yet. Please wait.");
                return;
            }
            
            List<string> allKeys = new List<string>();
            allKeys.AddRange(_remoteAssetKeys);
            allKeys.AddRange(_preloadAssetKeys);
            
            if (allKeys.Count == 0)
            {
                UpdateStatus("No asset keys defined to check.");
                return;
            }
            
            UpdateStatus("Checking cached status of assets...");
            
            List<string> cachedAssets = new List<string>();
            List<string> uncachedAssets = new List<string>();
            
            foreach (string key in allKeys)
            {
                bool isCached = await _addressableManager.IsAssetCachedAsync(key);
                
                if (isCached)
                    cachedAssets.Add(key);
                else
                    uncachedAssets.Add(key);
            }
            
            UpdateStatus($"Cache check complete: {cachedAssets.Count} assets cached, {uncachedAssets.Count} not cached.");
            
            // Log detailed results
            Debug.Log($"Cached assets: {string.Join(", ", cachedAssets)}");
            Debug.Log($"Uncached assets: {string.Join(", ", uncachedAssets)}");
        }
        
        /// <summary>
        /// Clear the addressables cache
        /// </summary>
        private async void ClearCache()
        {
            if (!_isInitialized)
            {
                UpdateStatus("Addressables not initialized yet. Please wait.");
                return;
            }
            
            UpdateStatus("Clearing addressables cache...");
            
            bool success = await _addressableManager.ClearAllCacheAsync();
            
            if (success)
            {
                UpdateStatus("Successfully cleared the addressables cache.");
            }
            else
            {
                UpdateStatus("Failed to clear the addressables cache.");
            }
        }
        
        /// <summary>
        /// Preload assets for later use
        /// </summary>
        private async void PreloadAssets()
        {
            if (!_isInitialized)
            {
                UpdateStatus("Addressables not initialized yet. Please wait.");
                return;
            }
            
            if (_preloadAssetKeys.Count == 0)
            {
                UpdateStatus("No preload asset keys defined.");
                return;
            }
            
            UpdateStatus($"Preloading {_preloadAssetKeys.Count} assets...");
            
            bool success = await _addressableManager.PreDownloadAssetsAsync(_preloadAssetKeys, DownloadPriority.Low);
            
            if (success)
            {
                UpdateStatus($"Successfully preloaded {_preloadAssetKeys.Count} assets.");
            }
            else
            {
                UpdateStatus("Preloading completed with some failures.");
            }
        }
        
        /// <summary>
        /// Monitor and display download progress for an asset
        /// </summary>
        private async Task MonitorDownloadProgress(string key)
        {
            while (true)
            {
                float progress = await _addressableManager.GetDownloadStatusAsync(key);
                
                // Update UI
                if (_progressSlider != null)
                {
                    _progressSlider.value = progress;
                }
                
                // Check if download is complete
                if (progress >= 0.99f)
                {
                    break;
                }
                
                await Task.Delay(100); // Update every 100ms
            }
        }
        
        /// <summary>
        /// Handle download completed event
        /// </summary>
        private void OnDownloadCompleted(string key, bool success)
        {
            if (success)
            {
                Debug.Log($"Download completed: {key}");
            }
            else
            {
                Debug.Log($"Failed to download {key}");
            }
        }
        
        /// <summary>
        /// Update the status text
        /// </summary>
        private void UpdateStatus(string message)
        {
            Debug.Log($"Addressables: {message}");
            
            if (_statusText != null)
            {
                _statusText.text = message;
            }
        }
        
        private void OnDestroy()
        {
            // Unsubscribe from events
            /*if (_addressableManager != null && _addressableManager.DownloadQueue != null)
            {
                //_addressableManager.DownloadQueue.DownloadCompleted -= OnDownloadCompleted;
            }*/
            
            // Remove button listeners
            if (_downloadAllButton != null)
                _downloadAllButton.onClick.RemoveListener(DownloadAllRemoteAssets);
            
            if (_checkCacheButton != null)
                _checkCacheButton.onClick.RemoveListener(CheckCachedAssets);
            
            if (_clearCacheButton != null)
                _clearCacheButton.onClick.RemoveListener(ClearCache);
            
            if (_preloadAssetsButton != null)
                _preloadAssetsButton.onClick.RemoveListener(PreloadAssets);
        }
    }
}