using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AddressableSystem
{
    /// <summary>
    /// Interface for cache management operations
    /// </summary>
    public interface ICacheManager
    {
        /// <summary>
        /// Get the size of the cached data for a specific key
        /// </summary>
        Task<long> GetCachedSizeAsync(string key);
        
        /// <summary>
        /// Get the total size of the Addressables cache
        /// </summary>
        Task<long> GetTotalCacheSizeAsync();
        
        /// <summary>
        /// Clear the dependency cache for an asset
        /// </summary>
        Task<bool> ClearCacheForAssetAsync(string key);
        
        /// <summary>
        /// Clear the dependency cache for multiple assets
        /// </summary>
        Task<bool> ClearCacheForAssetsAsync(List<string> keys);
        
        /// <summary>
        /// Clear the entire Addressables cache
        /// </summary>
        Task<bool> ClearAllCacheAsync();
        
        /// <summary>
        /// Check if an asset is cached locally
        /// </summary>
        Task<bool> IsAssetCachedAsync(string key);
    }
}