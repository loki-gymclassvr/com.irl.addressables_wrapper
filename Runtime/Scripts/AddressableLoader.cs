using System;
using ShovelTools;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;

namespace AddressableSystem
{
    /// <summary>
    /// Improved loader class for loading addressable assets asynchronously.
    /// Prevents duplicate loading operations and properly manages handles.
    /// </summary>
    public class AddressableLoader : IAddressableLoader
    {
        /// <summary>
        /// Loads an addressable asset asynchronously.
        /// </summary>
        /// <typeparam name="T">The type of asset to load.</typeparam>
        /// <param name="key">The addressable key.</param>
        /// <param name="onSuccess">Callback when loading succeeds.</param>
        /// <param name="onFail">Callback when loading fails.</param>
        /// <param name="autoUnload">Whether to automatically unload this asset on scene changes.</param>
        /// <param name="handleRepository">The repository to store the async handle.</param>
        public void LoadAssetAsync<T>(string key, Action<T> onSuccess = null, Action<Exception> onFail = null, 
            bool autoUnload = false, IAsyncHandleRepository handleRepository = null)
        {
            if (string.IsNullOrEmpty(key))
            {
                if(DLM.ShouldLog)
                {
                    DLM.LogError(DLM.FeatureFlags.Addressables, $"[AddressableSystem]: Cannot load asset with null or empty key");
                }
                onFail?.Invoke(new ArgumentNullException(nameof(key)));
                return;
            }

            // Skip if repository is null - can't track handles
            if (handleRepository == null)
            {
                if(DLM.ShouldLog)
                {
                    DLM.LogError(DLM.FeatureFlags.Addressables, $"[AddressableSystem]: HandleRepository is null, can't track handles");
                }
                
                // Load directly without tracking
                AsyncOperationHandle<T> handle = Addressables.LoadAssetAsync<T>(key);
                handle.Completed += operation => 
                {
                    if (operation.Status == AsyncOperationStatus.Succeeded)
                    {
                        onSuccess?.Invoke(operation.Result);
                    }
                    else
                    {
                        onFail?.Invoke(operation.OperationException);
                    }
                };
                return;
            }

            // First check: Is the handle already in our repository and valid?
            if (handleRepository.TryGetHandle(key, out AsyncOperationHandle existingHandle))
            {
                // If the handle exists and is valid and completed
                if (existingHandle.IsValid() && existingHandle.IsDone && existingHandle.Status == AsyncOperationStatus.Succeeded)
                {
                    if (existingHandle.Result is T result)
                    {
                        if(DLM.ShouldLog)
                        {
                            DLM.Log(DLM.FeatureFlags.Addressables, $"[AddressableSystem]: Using existing completed handle for key {key}");
                        }
                        onSuccess?.Invoke(result);
                        
                        // Update auto-unload flag if needed
                        if (autoUnload)
                        {
                            handleRepository.UpdateAutoUnloadFlag(key, true);
                        }
                        return;
                    }
                    else
                    {
                        if(DLM.ShouldLog)
                        {
                            DLM.LogWarning(DLM.FeatureFlags.Addressables, 
                                $"[AddressableSystem]: Handle exists for key {key} but result is of wrong type. Expected {typeof(T).Name}, got {existingHandle.Result?.GetType().Name ?? "null"}");
                        }
                        
                        // The existing handle is of the wrong type - we'll need to release it and load again
                        Addressables.Release(existingHandle);
                        handleRepository.RemoveHandle(key);
                    }
                }
                else if (existingHandle.IsValid() && !existingHandle.IsDone)
                {
                    // The operation is still in progress, register our callback
                    if(DLM.ShouldLog)
                    {
                        DLM.Log(DLM.FeatureFlags.Addressables, $"[AddressableSystem]: Operation in progress for key {key}, adding callback");
                    }
                    
                    // Add completion callback for this incomplete operation
                    existingHandle.Completed += operation => 
                    {
                        if (operation.Status == AsyncOperationStatus.Succeeded && operation.Result is T result)
                        {
                            onSuccess?.Invoke(result);
                        }
                        else
                        {
                            onFail?.Invoke(operation.OperationException ?? new Exception($"Failed to load asset: {key}"));
                        }
                    };
                    
                    // Update auto-unload flag if needed
                    if (autoUnload)
                    {
                        handleRepository.UpdateAutoUnloadFlag(key, true);
                    }
                    return;
                }
                else
                {
                    // The handle exists but is invalid or failed - remove it and try again
                    if(DLM.ShouldLog)
                    {
                        DLM.LogWarning(DLM.FeatureFlags.Addressables, $"[AddressableSystem]: Handle exists for key {key} but is invalid or failed. Removing and reloading.");
                    }
                    
                    if (existingHandle.IsValid())
                    {
                        Addressables.Release(existingHandle);
                    }
                    handleRepository.RemoveHandle(key);
                }
            }

            // If we get here, we need to start a new load operation
            if(DLM.ShouldLog)
            {
                DLM.Log(DLM.FeatureFlags.Addressables, $"[AddressableSystem]: Starting new load operation for key {key}");
            }
            
            // Start the async operation
            AsyncOperationHandle<T> newHandle = Addressables.LoadAssetAsync<T>(key);
            
            // IMMEDIATELY add it to the repository to prevent duplicate loads
            handleRepository.AddHandle(key, newHandle, autoUnload);
            
            // Set up completion callback
            newHandle.Completed += operation => 
            {
                if (operation.Status == AsyncOperationStatus.Succeeded)
                {
                    if(DLM.ShouldLog)
                    {
                        DLM.Log(DLM.FeatureFlags.Addressables, $"[AddressableSystem]: Successfully loaded handle with key {key}");
                    }
                    onSuccess?.Invoke(operation.Result);
                }
                else
                {
                    if(DLM.ShouldLog)
                    {
                        DLM.LogError(DLM.FeatureFlags.Addressables, $"[AddressableSystem]: Failed to load addressable asset: {key}. Error: {operation.OperationException}");
                    }
                    onFail?.Invoke(operation.OperationException);
                    
                    // Remove failed handles from the repository
                    handleRepository.RemoveHandle(key);
                }
            };
        }

        /// <summary>
        /// Loads a scene asynchronously.
        /// </summary>
        /// <param name="key">The addressable key for the scene.</param>
        /// <param name="loadMode">The scene load mode.</param>
        /// <param name="activateOnLoad">Whether to activate the scene immediately after loading.</param>
        /// <param name="onSuccess">Callback when loading succeeds.</param>
        /// <param name="onFail">Callback when loading fails.</param>
        /// <param name="handleRepository">The repository to store the async handle.</param>
        public void LoadSceneAsync(string key, LoadSceneMode loadMode = LoadSceneMode.Single, 
            bool activateOnLoad = true, Action<SceneInstance> onSuccess = null, Action<Exception> onFail = null,
            IAsyncHandleRepository handleRepository = null)
        {
            if (string.IsNullOrEmpty(key))
            {
                if(DLM.ShouldLog)
                {
                    DLM.LogError(DLM.FeatureFlags.Addressables, $"[AddressableSystem]: Cannot load scene with null or empty key");
                }
                onFail?.Invoke(new ArgumentNullException(nameof(key)));
                return;
            }

            // Skip if repository is null - can't track handles
            if (handleRepository == null)
            {
                if(DLM.ShouldLog)
                {
                    DLM.LogError(DLM.FeatureFlags.Addressables, $"[AddressableSystem]: HandleRepository is null, can't track handles");
                }
                
                // Load directly without tracking
                AsyncOperationHandle<SceneInstance> handle = Addressables.LoadSceneAsync(key, loadMode, activateOnLoad);
                handle.Completed += operation => 
                {
                    if (operation.Status == AsyncOperationStatus.Succeeded)
                    {
                        onSuccess?.Invoke(operation.Result);
                    }
                    else
                    {
                        onFail?.Invoke(operation.OperationException);
                    }
                };
                return;
            }

            // First check: Is the handle already in our repository and valid?
            if (handleRepository.TryGetHandle(key, out AsyncOperationHandle existingHandle))
            {
                // If the handle exists and is valid and completed
                if (existingHandle.IsValid() && existingHandle.IsDone && existingHandle.Status == AsyncOperationStatus.Succeeded)
                {
                    if (existingHandle.Result is SceneInstance result)
                    {
                        if(DLM.ShouldLog)
                        {
                            DLM.Log(DLM.FeatureFlags.Addressables, $"[AddressableSystem]: Using existing completed scene handle for key {key}");
                        }
                        onSuccess?.Invoke(result);
                        return;
                    }
                    else
                    {
                        if(DLM.ShouldLog)
                        {
                            DLM.LogWarning(DLM.FeatureFlags.Addressables, 
                                $"[AddressableSystem]: Scene handle exists for key {key} but result is of wrong type");
                        }
                        
                        // The existing handle is of the wrong type - we'll need to release it and load again
                        Addressables.Release(existingHandle);
                        handleRepository.RemoveHandle(key);
                    }
                }
                else if (existingHandle.IsValid() && !existingHandle.IsDone)
                {
                    // The operation is still in progress, register our callback
                    if(DLM.ShouldLog)
                    {
                        DLM.Log(DLM.FeatureFlags.Addressables, $"[AddressableSystem]: Scene operation in progress for key {key}, adding callback");
                    }
                    
                    // Add completion callback for this incomplete operation
                    existingHandle.Completed += operation => 
                    {
                        if (operation.Status == AsyncOperationStatus.Succeeded && operation.Result is SceneInstance result)
                        {
                            onSuccess?.Invoke(result);
                        }
                        else
                        {
                            onFail?.Invoke(operation.OperationException ?? new Exception($"Failed to load scene: {key}"));
                        }
                    };
                    return;
                }
                else
                {
                    // The handle exists but is invalid or failed - remove it and try again
                    if(DLM.ShouldLog)
                    {
                        DLM.LogWarning(DLM.FeatureFlags.Addressables, $"[AddressableSystem]: Scene handle exists for key {key} but is invalid or failed. Removing and reloading.");
                    }
                    
                    if (existingHandle.IsValid())
                    {
                        Addressables.Release(existingHandle);
                    }
                    handleRepository.RemoveHandle(key);
                }
            }

            // If we get here, we need to start a new load operation
            if(DLM.ShouldLog)
            {
                DLM.Log(DLM.FeatureFlags.Addressables, $"[AddressableSystem]: Starting new scene load operation for key {key}");
            }
            
            // Start the async operation
            AsyncOperationHandle<SceneInstance> newHandle = Addressables.LoadSceneAsync(key, loadMode, activateOnLoad);
            
            // IMMEDIATELY add it to the repository to prevent duplicate loads
            handleRepository.AddHandle(key, newHandle, false); // Scenes typically aren't auto-unloaded
            
            // Set up completion callback
            newHandle.Completed += operation => 
            {
                if (operation.Status == AsyncOperationStatus.Succeeded)
                {
                    if(DLM.ShouldLog)
                    {
                        DLM.Log(DLM.FeatureFlags.Addressables, $"[AddressableSystem]: Successfully loaded scene with key {key}");
                    }
                    onSuccess?.Invoke(operation.Result);
                }
                else
                {
                    if(DLM.ShouldLog)
                    {
                        DLM.LogError(DLM.FeatureFlags.Addressables, $"[AddressableSystem]: Failed to load addressable scene: {key}. Error: {operation.OperationException}");
                    }
                    onFail?.Invoke(operation.OperationException);
                    
                    // Remove failed handles from the repository
                    handleRepository.RemoveHandle(key);
                }
            };
        }
    }
}