using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace AddressableSystem
{
    /// <summary>
    /// Extension methods for the Addressable system.
    /// </summary>
    public static class AddressableExtensions
    {
        /// <summary>
        /// Waits for an async operation to complete and returns the result.
        /// </summary>
        /// <typeparam name="T">The type of the operation result.</typeparam>
        /// <param name="handle">The async operation handle.</param>
        /// <returns>A coroutine that yields the result when complete.</returns>
        public static IEnumerator WaitForCompletion<T>(this AsyncOperationHandle<T> handle)
        {
            while (!handle.IsDone)
            {
                yield return null;
            }
            
            yield return handle.Result;
        }
        
        /// <summary>
        /// Loads multiple assets of the same type asynchronously.
        /// </summary>
        /// <typeparam name="T">The type of assets to load.</typeparam>
        /// <param name="manager">The addressable manager.</param>
        /// <param name="keys">The array of addressable keys.</param>
        /// <param name="onAllLoaded">Callback when all assets are loaded.</param>
        /// <param name="onProgress">Optional callback for load progress.</param>
        /// <param name="autoUnload">Whether to auto-unload the assets.</param>
        public static void LoadMultipleAssetsAsync<T>(this AddressableManager manager, string[] keys, 
            Action<List<T>> onAllLoaded, Action<float> onProgress = null, bool autoUnload = false) where T : UnityEngine.Object
        {
            if (keys == null || keys.Length == 0)
            {
                onAllLoaded?.Invoke(new List<T>());
                return;
            }
            
            int totalCount = keys.Length;
            int loadedCount = 0;
            List<T> results = new List<T>(totalCount);
            
            foreach (string key in keys)
            {
                manager.LoadAssetAsync<T>(key, 
                    result => 
                    {
                        results.Add(result);
                        loadedCount++;
                        
                        float progress = (float)loadedCount / totalCount;
                        onProgress?.Invoke(progress);
                        
                        if (loadedCount >= totalCount)
                        {
                            onAllLoaded?.Invoke(results);
                        }
                    },
                    exception => 
                    {
                        loadedCount++;
                        
                        float progress = (float)loadedCount / totalCount;
                        onProgress?.Invoke(progress);
                        
                        if (loadedCount >= totalCount)
                        {
                            onAllLoaded?.Invoke(results);
                        }
                    },
                    autoUnload);
            }
        }
        
        /// <summary>
        /// Loads an asset asynchronously and instantiates it.
        /// </summary>
        /// <param name="manager">The addressable manager.</param>
        /// <param name="key">The addressable key.</param>
        /// <param name="parent">Optional parent transform.</param>
        /// <param name="instantiateInWorldSpace">Whether to instantiate in world space.</param>
        /// <param name="onInstantiated">Callback when instantiation is complete.</param>
        /// <param name="onFail">Callback when loading fails.</param>
        /// <param name="autoUnload">Whether to auto-unload the asset.</param>
        public static void LoadAndInstantiateAsync(this AddressableManager manager, string key, 
            Transform parent = null, bool instantiateInWorldSpace = false, 
            Action<GameObject> onInstantiated = null, Action<Exception> onFail = null, bool autoUnload = false)
        {
            manager.LoadAssetAsync<GameObject>(key, 
                prefab => 
                {
                    GameObject instance = UnityEngine.Object.Instantiate(prefab, parent, instantiateInWorldSpace);
                    onInstantiated?.Invoke(instance);
                },
                onFail,
                autoUnload);
        }
    }
}