using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BugsnagUnity;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using ShovelTools;
using UnityEngine.AddressableAssets.ResourceLocators;

namespace AddressableSystem
{
    /// <summary>
    /// Implementation for managing Addressable catalogs
    /// </summary>
    public class CatalogManager : ICatalogManager
    {
        private readonly DLM.FeatureFlags _featureFlag;
        private string _catalogUrl;
        private Dictionary<string, bool> _loadedCatalogs = new Dictionary<string, bool>();
        
        private readonly Dictionary<string, AsyncOperationHandle<IResourceLocator>> _catalogHandles = new Dictionary<string, AsyncOperationHandle<IResourceLocator>>();
        
        private readonly HashSet<string> _registeredCatalogs = new HashSet<string>();
        private readonly Dictionary<string, DateTime> _catalogExpiryTimes = new Dictionary<string, DateTime>();
        
        public CatalogManager(DLM.FeatureFlags featureFlag = DLM.FeatureFlags.Addressables, string initialCatalogUrl = null)
        {
            _featureFlag = featureFlag;
            _catalogUrl = initialCatalogUrl;
            
            UrlExpiryManager.Initialize();
            UrlExpiryManager.OnUrlExpired += OnCatalogExpired;
        }
        
        private async void OnCatalogExpired(string catalogPath)
        {
            // only care about this catalog
            if (catalogPath != _catalogUrl)
                return;

            DLM.Log(_featureFlag, $"[CatalogManager] Catalog '{catalogPath}' expired → reloading…");

            // unload the old catalog
            try
            {
                UnloadCatalog(catalogPath);
            }
            catch (Exception e)
            {
                DLM.LogWarning(_featureFlag, $"Failed to unload expired catalog: {e.Message}");
            }

            // fetch & load again
            bool ok = await LoadCatalogAsync(catalogPath);
            if (!ok)
                DLM.LogError(_featureFlag, $"Failed to reload catalog '{catalogPath}'.");
            else
                DLM.Log(_featureFlag, $"Successfully reloaded catalog '{catalogPath}'.");
        }

        /// <summary>
        /// Load a content catalog from the given path
        /// </summary>
        public async Task<bool> LoadCatalogAsync(string catalogPath)
        {
            if (string.IsNullOrEmpty(catalogPath))
            {
                DLM.LogError(_featureFlag, "Catalog path cannot be null or empty");
                return false;
            }
            if (_loadedCatalogs.TryGetValue(catalogPath, out var already) && already)
            {
                DLM.Log(_featureFlag, $"Catalog already loaded: {catalogPath}");
                return true;
            }

            //Build a safe URI
            string url;
            if (catalogPath.IndexOf("://", StringComparison.Ordinal) >= 0)
            {
                // Anything:// (http, https, jar:file, file, etc.) is already absolute
                url = catalogPath;
            }
            else if (Path.IsPathRooted(catalogPath))
            {
                // Local filesystem path → file://
                var fullPath = Path.GetFullPath(catalogPath).Replace("\\", "/");
                url = $"file://{fullPath}";
            }
            else
            {
                // Truly relative → use your CDN base (if any)
                var baseUrl = (_catalogUrl ?? string.Empty).TrimEnd('/') + "/";
                url = baseUrl + catalogPath.TrimStart('/');
            }

            DLM.Log(_featureFlag, $"Loading catalog: {url}");

            try
            {
                //Kick off the load
                var handle = Addressables.LoadContentCatalogAsync(url, autoReleaseHandle: false);
                await handle.Task;

                if (handle.Status == AsyncOperationStatus.Succeeded)
                {
                    _loadedCatalogs[catalogPath]     = true;
                    _catalogHandles[catalogPath]     = handle;
                    DLM.Log(_featureFlag, $"Successfully loaded catalog: {catalogPath}");
                    
                    RegisterBundleExpiryForCatalog(catalogPath, handle.Result);
                    return true;
                }
                else
                {
                    DLM.LogError(_featureFlag,
                        $"Failed to load catalog {catalogPath}: {handle.OperationException?.Message}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                DLM.LogError(_featureFlag,
                    $"Exception while loading catalog {catalogPath}: {ex.Message}");
                _loadedCatalogs[catalogPath] = false;
                
                Bugsnag.Notify(new System.Exception($"[Addressables]: Exception while loading catalog {catalogPath}: {ex.Message}"));
                return false;
            }
        }

        /// <summary>
        /// Scan this catalog, find the first signed‐bundle URL, compute its expiry,
        /// schedule a reload via UrlExpiryManager.Register, and store that expiry in-memory.
        /// </summary>
        private void RegisterBundleExpiryForCatalog(string catalogKey, IResourceLocator locator)
        {
            // Only register once per load
            if (_registeredCatalogs.Contains(catalogKey))
                return;

            // Snapshot raw IDs on main thread
            var rawIds = new List<string>();
            foreach (var key in locator.Keys)
            {
                locator.Locate(key, null, out var locations);
                foreach (var loc in locations)
                {
                    rawIds.Add(loc.InternalId.Replace('\\', '/'));
                }
            }

            // Grab the main-thread scheduler so we can log when done
            var uiScheduler = TaskScheduler.FromCurrentSynchronizationContext();
            string baseUrl = _catalogUrl != null ? _catalogUrl.TrimEnd('/') + "/" : string.Empty;

            Task.Run(() =>
            {
                // We only need the first signed URL we find:
                foreach (var normalized in rawIds)
                {
                    if (normalized.IndexOf("X-Amz-", StringComparison.Ordinal) < 0)
                        continue;

                    // Build full URL
                    string bundleUrl = normalized.IndexOf("://", StringComparison.Ordinal) >= 0
                        ? normalized
                        : baseUrl + normalized;

                    // Decode
                    bundleUrl = Uri.UnescapeDataString(bundleUrl);

                    // Parse expiry
                    DateTime? expiryUtc = AwsSignedUrlChecker.GetUrlExpiryTime(bundleUrl);
                    if (expiryUtc.HasValue)
                    {
                        // Log the discovered expiry for diagnostics:
                        DLM.Log(
                            _featureFlag,
                            $"[CatalogManager] Discovered first signed‐bundle expiry={expiryUtc.Value:O} for catalog='{catalogKey}' url='{bundleUrl}'"
                        );

                        // Schedule a single reload just before that moment:
                        UrlExpiryManager.Register(catalogKey, expiryUtc.Value);

                        // Store in memory so IsCatalogStillValid can return quickly later:
                        lock (_catalogExpiryTimes)
                        {
                            _catalogExpiryTimes[catalogKey] = expiryUtc.Value;
                        }

                        return true; // we found and registered exactly one expiry
                    }
                }

                return false;
            })
            .ContinueWith(t =>
            {
                if (t.Result)
                {
                    DLM.Log(_featureFlag,
                        $"[CatalogManager] Registered bundle expiry for catalog '{catalogKey}'");
                    _registeredCatalogs.Add(catalogKey);
                }
                else
                {
                    DLM.LogWarning(_featureFlag,
                        $"[CatalogManager] No signed‐bundle URL found in '{catalogKey}', skipping expiry registration");
                }
            }, uiScheduler);
        }
        
        /// <summary>
        /// Returns true if the catalogKey’s previously‐registered expiry hasn’t passed yet.
        /// If we never registered an expiry (or the catalog is not loaded), returns false.
        /// </summary>
        public bool IsCatalogStillValid(string catalogKey)
        {
            const DLM.FeatureFlags logFlag = DLM.FeatureFlags.Addressables;
            if (!_loadedCatalogs.ContainsKey(catalogKey) || !_loadedCatalogs[catalogKey])
            {
                DLM.Log(logFlag, $"[CatalogManager] IsCatalogStillValid: '{catalogKey}' not loaded → invalid");
                return false;
            }

            lock (_catalogExpiryTimes)
            {
                if (!_catalogExpiryTimes.TryGetValue(catalogKey, out var expiryUtc))
                {
                    // never registered an expiry
                    DLM.Log(logFlag, $"[CatalogManager] IsCatalogStillValid: no expiry on '{catalogKey}' → treat as invalid");
                    return false;
                }

                var now = DateTime.UtcNow;
                bool still = now < expiryUtc;
                DLM.Log(logFlag, $"[CatalogManager] IsCatalogStillValid('{catalogKey}') → expiry={expiryUtc:O}, now={now:O}, stillValid={still}");
                return still;
            }
        }
        
        /// <summary>
        /// Unload a catalog that was previously loaded.
        /// This will remove its locator so Addressables will no longer serve from it.
        /// </summary>
        public void UnloadCatalog(string catalogPath)
        {
            if (_catalogHandles.TryGetValue(catalogPath, out var handle))
            {
                //unregister the locator
                Addressables.RemoveResourceLocator(handle.Result);
                //release the handle (frees memory)
                Addressables.Release(handle);
                _catalogHandles.Remove(catalogPath);
                _loadedCatalogs.Remove(catalogPath);
                DLM.Log(_featureFlag, $"Unloaded catalog: {catalogPath}");
                _registeredCatalogs.Remove(catalogPath);
            }
        }

        /// <summary>
        /// Unload *all* catalogs except the one you want to keep (e.g. your remote one).
        /// </summary>
        public void UnloadAllExceptRemote()
        {
            foreach (var path in _catalogHandles.Keys.ToArray())
            {
                if (!path.Contains("http") && !path.Contains("catalog"))
                {
                    UnloadCatalog(path);
                }
            }
        }
        
        /// <summary>
        /// Get the current CDN URL
        /// </summary>
        public string GetCatalogUrl()
        {
            return _catalogUrl;
        }
        
        /// <summary>
        /// Builds the full catalog path by combining CDN URL if available
        /// </summary>
        private string BuildCatalogPath(string catalogPath)
        {
            if (string.IsNullOrEmpty(_catalogUrl))
            {
                return catalogPath;
            }
            
            // If catalog path is already a full URL, return as is
            if (catalogPath.StartsWith("http://") || catalogPath.StartsWith("https://"))
            {
                return catalogPath;
            }
            
            // If catalog path starts with slash, remove it to avoid double slashes
            if (catalogPath.StartsWith("/"))
            {
                catalogPath = catalogPath.Substring(1);
            }
            
            return $"{_catalogUrl}{catalogPath}";
        }
    }
}