using System;
using ShovelTools;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace AddressableSystem
{
    /// <summary>
    /// Improved unloader class for releasing addressable assets.
    /// Follows the Single Responsibility Principle by focusing solely on unloading assets.
    /// </summary>
    public class AddressableUnloader : IAddressableUnloader
    {
        private readonly IAsyncHandleRepository _handleRepository;

        public AddressableUnloader(IAsyncHandleRepository handleRepository)
        {
            _handleRepository = handleRepository ?? throw new ArgumentNullException(nameof(handleRepository));
        }

        /// <summary>
        /// Unloads an addressable asset by key.
        /// </summary>
        /// <param name="key">The addressable key to unload.</param>
        public void UnloadHandle(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                if(DLM.ShouldLog)
                {
                    DLM.LogError(DLM.FeatureFlags.Addressables, $"[AddressableWrapper]: Cannot unload handle with null or empty key");
                }
                return;
            }

            if (_handleRepository.TryGetHandle(key, out AsyncOperationHandle handle))
            {
                if (handle.IsValid())
                {
                    try
                    {
                        if(DLM.ShouldLog)
                        {
                            DLM.Log(DLM.FeatureFlags.Addressables, $"[AddressableWrapper]: Releasing handle for key {key}, Status: {handle.Status}, " +
                                    $"IsDone: {handle.IsDone}, Type: {(handle.Result != null ? handle.Result.GetType().Name : "null")}");
                        }
                        
                        Addressables.Release(handle);
                    }
                    catch (Exception ex)
                    {
                        if(DLM.ShouldLog)
                        {
                            DLM.LogError(DLM.FeatureFlags.Addressables, $"[AddressableWrapper]: Error releasing handle for key {key}: {ex.Message}");
                        }
                    }
                }
                else
                {
                    if(DLM.ShouldLog)
                    {
                        DLM.LogWarning(DLM.FeatureFlags.Addressables, $"[AddressableWrapper]: Handle for key {key} is not valid anymore.");
                    }
                }
                
                // Remove from repository after releasing
                _handleRepository.RemoveHandle(key);
            }
            else
            {
                if(DLM.ShouldLog)
                {
                    DLM.LogWarning(DLM.FeatureFlags.Addressables, $"[AddressableWrapper]: No handle found for key: {key}");
                }
            }
        }

        /// <summary>
        /// Unloads all addressable assets.
        /// </summary>
        public void UnloadAllHandles()
        {
            if(DLM.ShouldLog)
            {
                DLM.Log(DLM.FeatureFlags.Addressables, $"[AddressableWrapper]: Unloading all handles");
            }
            
            // Get all keys that need to be unloaded
            string[] keys = _handleRepository.GetAutoUnloadKeys();
            
            // Track errors for reporting
            int successCount = 0;
            int failureCount = 0;
            
            foreach (string key in keys)
            {
                try
                {
                    UnloadHandle(key);
                    successCount++;
                }
                catch (Exception ex)
                {
                    failureCount++;
                    if(DLM.ShouldLog)
                    {
                        DLM.LogError(DLM.FeatureFlags.Addressables, $"[AddressableWrapper]: Error unloading handle {key}: {ex.Message}");
                    }
                }
            }
            
            if(DLM.ShouldLog)
            {
                DLM.Log(DLM.FeatureFlags.Addressables, $"[AddressableWrapper]: Unloaded {successCount} handles successfully, {failureCount} failures");
            }
        }

        /// <summary>
        /// Unloads only the addressable assets marked for auto-unload.
        /// </summary>
        public void UnloadAutoUnloadHandles()
        {
            if(DLM.ShouldLog)
            {
                DLM.Log(DLM.FeatureFlags.Addressables, $"[AddressableWrapper]: Unloading auto-unload handles");
            }
            
            // Get the keys for auto-unload handles
            string[] autoUnloadKeys = _handleRepository.GetAutoUnloadKeys();
            
            if (autoUnloadKeys.Length == 0)
            {
                if(DLM.ShouldLog)
                {
                    DLM.Log(DLM.FeatureFlags.Addressables, $"[AddressableWrapper]: No auto-unload handles found");
                }
                return;
            }
            
            if(DLM.ShouldLog)
            {
                DLM.Log(DLM.FeatureFlags.Addressables, $"[AddressableWrapper]: Found {autoUnloadKeys.Length} auto-unload handles");
            }
            
            foreach (string key in autoUnloadKeys)
            {
                UnloadHandle(key);
            }
        }
    }
}