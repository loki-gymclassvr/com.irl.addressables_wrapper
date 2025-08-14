using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using ShovelTools;

namespace AddressableSystem
{
   	/// <summary>
    /// Manages the Addressables content cache
    /// </summary>
    public class CacheManager : ICacheManager
    {
        private readonly DLM.FeatureFlags _featureFlag;
        
        public CacheManager(DLM.FeatureFlags featureFlag = DLM.FeatureFlags.Addressables)
        {
            _featureFlag = featureFlag;
        }
        
        /// <summary>
        /// Get the size of the cached data for a specific key
        /// </summary>
        public async Task<long> GetCachedSizeAsync(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                DLM.LogError(_featureFlag, "Asset key cannot be null or empty");
                return 0;
            }
            
            try
            {
                var sizeOperation = Addressables.GetDownloadSizeAsync(key);
                
                // Wait for the operation to complete
                while (!sizeOperation.IsDone)
                {
                    await Task.Yield();
                }
                
                // If size is 0, it means the asset is already cached
                if (sizeOperation.Status == AsyncOperationStatus.Succeeded)
                {
                    long downloadSize = sizeOperation.Result;
                    Addressables.Release(sizeOperation);
                    
                    // If download size is 0, the asset is fully cached
                    // We can't directly get the cached size, but we can report it as cached
                    if (downloadSize == 0)
                    {
                        return -1; // -1 indicates "cached but size unknown"
                    }
                    
                    // If download size > 0, asset is not cached
                    return 0;
                }
                
                Addressables.Release(sizeOperation);
                return 0;
            }
            catch (Exception ex)
            {
                DLM.LogError(_featureFlag, $"Error getting cached size for {key}: {ex.Message}");
                return 0;
            }
        }
        
        /// <summary>
        /// Get the total size of the Addressables cache
        /// </summary>
        public async Task<long> GetTotalCacheSizeAsync()
        {
            // Unfortunately, Addressables doesn't provide a direct API to get total cache size
            // We would need to implement a custom solution using Unity's Caching API
            // This is a placeholder implementation
            DLM.Log(_featureFlag, "GetTotalCacheSizeAsync called - functionality requires custom implementation");
            
            // Unity's built-in caching API doesn't provide a simple way to get total cache size
            // For actual implementation, you might want to track asset sizes as they're downloaded
            long cacheSize = 0;
            
            // Return an estimated size or -1 to indicate "unknown"
            return cacheSize;
        }
        
        /// <summary>
        /// Clear the dependency cache for an asset by passing its address
        /// </summary>
        public async Task<bool> ClearCacheForAssetAsync(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                DLM.LogError(_featureFlag, "Asset key cannot be null or empty");
                return false;
            }

            try
            {
                DLM.Log(_featureFlag, $"Clearing cache for asset: {key}");

                // ✅ Call the correct overload
                AsyncOperationHandle<bool> operation = Addressables.ClearDependencyCacheAsync(key, false);

                await operation.Task;

                bool success = operation.Status == AsyncOperationStatus.Succeeded && operation.Result;
                Addressables.Release(operation);

                if (success)
                {
                    DLM.Log(_featureFlag, $"Successfully cleared cache for asset: {key}");
                }
                else
                {
                    DLM.LogError(_featureFlag, $"Failed to clear cache for asset {key}");
                }

                return success;
            }
            catch (Exception ex)
            {
                DLM.LogError(_featureFlag, $"Exception clearing cache for asset {key}: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// /// Clear the dependency cache for a set of assets by passing a label
        /// </summary>
        /// <param name="label"></param>
        /// <returns></returns>
        public async Task<bool> ClearCacheForLabelAsync(string label) {
            if (string.IsNullOrEmpty(label)) {
                DLM.LogWarning(_featureFlag, "Label cannot be null or empty");
                return false;
            }

            DLM.Log(_featureFlag, $"Clearing cache for label '{label}'...");

            // 1) Load all locations that carry this label
            var locHandle = Addressables.LoadResourceLocationsAsync(label, typeof(UnityEngine.Object));
            await locHandle.Task;
            if (locHandle.Status != AsyncOperationStatus.Succeeded || locHandle.Result.Count == 0) {
                DLM.LogWarning(_featureFlag, $"No assets found for label '{label}'");
                Addressables.Release(locHandle);
                return false;
            }
            var locations = locHandle.Result;
            Addressables.Release(locHandle);

            // 2) Clear the dependency cache for all of them
            var clearHandle = Addressables.ClearDependencyCacheAsync(locations, autoReleaseHandle: false);
            await clearHandle.Task;
            bool success = clearHandle.Status == AsyncOperationStatus.Succeeded && clearHandle.Result;
            Addressables.Release(clearHandle);

            if (success)
                DLM.Log(_featureFlag, $"Successfully cleared cache for label '{label}'");
            else
                DLM.LogError(_featureFlag, $"Failed to clear cache for label '{label}'");

            return success;
        }
        
        /// <summary>
        /// Clear the dependency cache for multiple assets
        /// </summary>
        public async Task<bool> ClearCacheForAssetsAsync(List<string> keys)
        {
            if (keys == null || keys.Count == 0)
            {
                DLM.LogWarning(_featureFlag, "No keys provided for cache clearing");
                return true;
            }
            
            bool allSucceeded = true;
            
            foreach (var key in keys)
            {
                bool success = await ClearCacheForAssetAsync(key);
                if (!success)
                {
                    allSucceeded = false;
                }
            }
            
            return allSucceeded;
        }
        
        /// <summary>
        /// Clear the entire Addressables cache
        /// </summary>
        public async Task<bool> ClearAllCacheAsync()
        {
            try
            {
                DLM.Log(_featureFlag, "Clearing all Addressables cache");
                
                var operation = Addressables.CleanBundleCache();
                
                // Wait for the operation to complete
                while (!operation.IsDone)
                {
                    await Task.Yield();
                }
                
                bool success = operation.Status == AsyncOperationStatus.Succeeded;
                Addressables.Release(operation);
                
                if (success)
                {
                    DLM.Log(_featureFlag, "Successfully cleared all Addressables cache");
                }
                else
                {
                    DLM.LogError(_featureFlag, "Failed to clear all Addressables cache");
                }
                
                return success;
            }
            catch (Exception ex)
            {
                DLM.LogError(_featureFlag, $"Exception clearing all cache: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Check if an asset is cached locally
        /// </summary>
        public async Task<bool> IsAssetCachedAsync(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                DLM.LogError(_featureFlag, "Asset key cannot be null or empty");
                return false;
            }
            
            try
            {
                var sizeOperation = Addressables.GetDownloadSizeAsync(key);
                
                // Wait for the operation to complete
                while (!sizeOperation.IsDone)
                {
                    await Task.Yield();
                }
                
                bool isCached = false;
                
                if (sizeOperation.Status == AsyncOperationStatus.Succeeded)
                {
                    // If download size is 0, the asset is fully cached
                    isCached = sizeOperation.Result == 0;
                }
                
                Addressables.Release(sizeOperation);
                return isCached;
            }
            catch (Exception ex)
            {
                DLM.LogError(_featureFlag, $"Error checking if asset is cached {key}: {ex.Message}");
                return false;
            }
        }
    }
}