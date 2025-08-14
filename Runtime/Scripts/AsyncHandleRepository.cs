using System.Collections.Generic;
using ShovelTools;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace AddressableSystem
{
    /// <summary>
    /// Improved repository class for storing and managing async operation handles.
    /// Prevents accidental handle release and provides better handle lifecycle management.
    /// </summary>
    public class AsyncHandleRepository : IAsyncHandleRepository
    {
        // Dictionary to store all handles
        private readonly Dictionary<string, AsyncOperationHandle> _handles = new Dictionary<string, AsyncOperationHandle>();
        
        // List to track which handles should be auto-unloaded
        private readonly HashSet<string> _autoUnloadKeys = new HashSet<string>();

        /// <summary>
        /// Adds a handle to the repository.
        /// </summary>
        /// <param name="key">The addressable key.</param>
        /// <param name="handle">The async operation handle.</param>
        /// <param name="autoUnload">Whether this handle should be auto-unloaded on scene changes.</param>
        public void AddHandle(string key, AsyncOperationHandle handle, bool autoUnload)
        {
            if (string.IsNullOrEmpty(key))
            {
                if(DLM.ShouldLog)
                {
                    DLM.LogError(DLM.FeatureFlags.Addressables, $"[AddressableWrapper]: Cannot add handle with null or empty key");
                }
                return;
            }

            // If we already have a handle with this key
            if (_handles.TryGetValue(key, out AsyncOperationHandle existingHandle))
            {
                // Only release if they're different handles - use ReferenceEquals for accurate comparison
                if (existingHandle.IsValid() && !ReferenceEquals(existingHandle, handle))
                {
                    if(DLM.ShouldLog)
                    {
                        DLM.Log(DLM.FeatureFlags.Addressables, $"[AddressableWrapper]: Replacing different handle for key {key}");
                    }
                    
                    Addressables.Release(existingHandle);
                }
                
                _handles.Remove(key);
                
                if (_autoUnloadKeys.Contains(key))
                {
                    _autoUnloadKeys.Remove(key);
                }
            }

            // Add the new handle
            _handles.Add(key, handle);
            
            // Add to auto-unload list if needed
            if (autoUnload)
            {
                _autoUnloadKeys.Add(key);
                if(DLM.ShouldLog)
                {
                    DLM.Log(DLM.FeatureFlags.Addressables, $"[AddressableWrapper]: Handle for key {key} is marked for auto-unload");
                }
            }
            
            if(DLM.ShouldLog)
            {
                DLM.Log(DLM.FeatureFlags.Addressables, $"[AddressableWrapper]: Handle added for key {key}");
            }
        }
        
        /// <summary>
        /// Updates the auto-unload flag for an existing handle.
        /// </summary>
        /// <param name="key">The addressable key.</param>
        /// <param name="autoUnload">The new auto-unload value.</param>
        /// <returns>Whether the update was successful.</returns>
        public bool UpdateAutoUnloadFlag(string key, bool autoUnload)
        {
            if (string.IsNullOrEmpty(key))
            {
                return false;
            }
            
            if (!_handles.ContainsKey(key))
            {
                return false;
            }
            
            if (autoUnload)
            {
                _autoUnloadKeys.Add(key);
            }
            else
            {
                _autoUnloadKeys.Remove(key);
            }
            
            return true;
        }

        /// <summary>
        /// Removes a handle from the repository without releasing it.
        /// </summary>
        /// <param name="key">The addressable key.</param>
        /// <returns>Whether the handle was found and removed.</returns>
        public bool RemoveHandle(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                if(DLM.ShouldLog)
                {
                    DLM.LogError(DLM.FeatureFlags.Addressables, $"[AddressableWrapper]: Cannot remove handle with null or empty key");
                }
                return false;
            }

            bool result = _handles.Remove(key);
            
            if (_autoUnloadKeys.Contains(key))
            {
                _autoUnloadKeys.Remove(key);
            }
            
            if (result && DLM.ShouldLog)
            {
                DLM.Log(DLM.FeatureFlags.Addressables, $"[AddressableWrapper]: Handle removed for key {key}");
            }
            
            return result;
        }

        /// <summary>
        /// Tries to get a handle by key.
        /// </summary>
        /// <param name="key">The addressable key.</param>
        /// <param name="handle">The output handle if found.</param>
        /// <returns>Whether the handle was found.</returns>
        public bool TryGetHandle(string key, out AsyncOperationHandle handle)
        {
            if (string.IsNullOrEmpty(key))
            {
                handle = default;
                return false;
            }
            
            return _handles.TryGetValue(key, out handle);
        }

        /// <summary>
        /// Clears all handles from the repository without releasing them.
        /// </summary>
        public void ClearAllHandles()
        {
            _handles.Clear();
            _autoUnloadKeys.Clear();
            
            if(DLM.ShouldLog)
            {
                DLM.Log(DLM.FeatureFlags.Addressables, $"[AddressableWrapper]: Cleared all handles from repository");
            }
        }

        /// <summary>
        /// Gets an array of keys for handles that should be auto-unloaded.
        /// </summary>
        /// <returns>Array of keys for auto-unload handles.</returns>
        public string[] GetAutoUnloadKeys()
        {
            string[] keys = new string[_autoUnloadKeys.Count];
            _autoUnloadKeys.CopyTo(keys);
            return keys;
        }
        
        /// <summary>
        /// Checks if an operation is in progress (not yet complete) for the given key.
        /// </summary>
        /// <param name="key">The addressable key.</param>
        /// <returns>Whether an operation is in progress.</returns>
        public bool IsOperationInProgress(string key)
        {
            if (TryGetHandle(key, out AsyncOperationHandle handle))
            {
                return handle.IsValid() && !handle.IsDone;
            }
            
            return false;
        }
        
        /// <summary>
        /// Checks if a key exists in the repository.
        /// </summary>
        /// <param name="key">The addressable key.</param>
        /// <returns>Whether the key exists.</returns>
        public bool ContainsKey(string key)
        {
            return !string.IsNullOrEmpty(key) && _handles.ContainsKey(key);
        }
    }
}