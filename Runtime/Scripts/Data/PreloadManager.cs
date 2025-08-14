using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ShovelTools;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AddressableSystem
{
    public abstract class PreloadManager : ScriptableObject
    {
        [SerializeField] protected DownloadPriority _priority = DownloadPriority.Critical;
        [SerializeField] protected string[] _preloadKeys;
        
        public string[] PreloadKeys => _preloadKeys;

        public void SetPriority(int priority)
        {
            _priority = (DownloadPriority)priority;
        }
        
        protected void SetPreloadKeys(string[] keys)
        {
            _preloadKeys = keys;
        }
        
        /// <summary>
        /// Preloads all resources defined in _preloadKeys array in parallel using the Addressable system.
        /// Only executes in builds (not in editor) and when remote CDN is enabled.
        /// </summary>
        /// <param name="OnComplete">Optional callback invoked when all resources have been successfully preloaded</param>
        /// <typeparam name="T">The type of Unity Object to preload</typeparam>
        public virtual async Task PreDownloadResourcesAsync(Action OnComplete = null)
        {
            #if !UNITY_EDITOR
            if (AddressableManager.Instance.EnableRemoteCDN)
            {
                var downloadTasks = new List<Task<bool>>();
        
                DLM.Log(DLM.FeatureFlags.Addressables, $"Starting preload of {_preloadKeys.Length} scene resources in async.");
                
                for (int i = 0; i < _preloadKeys.Length; i++)
                {
                    if (!string.IsNullOrEmpty(_preloadKeys[i]))
                    {
                        var task = AddressableManager.Instance.DownloadAssetAsync<Object>(_preloadKeys[i], 
                            DownloadPriority.Normal, 
                            true);
                        downloadTasks.Add(task);
                    }
                }
        
                // Wait for all downloads to complete
                await Task.WhenAll(downloadTasks);
                
                DLM.Log(DLM.FeatureFlags.Addressables, $"Finished preloading {_preloadKeys.Length} scene resources in async.");
                
                OnComplete?.Invoke();
            }
            #else
            OnComplete?.Invoke();
            #endif
        }
        
        /// <summary>
        /// Preloads all resources defined in _preloadKeys array in parallel using the Addressable system.
        /// Only executes in builds (not in editor) and when remote CDN is enabled.
        /// </summary>
        /// <param name="OnComplete">Optional callback invoked when all resources have been successfully preloaded</param>
        /// <typeparam name="T">The type of Unity Object to preload</typeparam>
        public virtual async Task PreloadResourcesAsync(Action OnComplete = null)
        {
            #if !UNITY_EDITOR
            if (AddressableManager.Instance.EnableRemoteCDN)
            {
                var downloadTasks = new List<Task<Object>>();
        
                DLM.Log(DLM.FeatureFlags.Addressables, $"Starting preload of {_preloadKeys.Length} scene resources in async.");
                
                for (int i = 0; i < _preloadKeys.Length; i++)
                {
                    if (!string.IsNullOrEmpty(_preloadKeys[i]))
                    {
                        var task = AddressableManager.Instance.DownloadAndLoadAssetAsync<Object>(_preloadKeys[i], 
                            DownloadPriority.Critical, 
                            true);
                        downloadTasks.Add(task);
                    }
                }
        
                // Wait for all downloads to complete
                await Task.WhenAll(downloadTasks);
                
                DLM.Log(DLM.FeatureFlags.Addressables, $"Finished preloading {_preloadKeys.Length} scene resources in async.");
                
                OnComplete?.Invoke();
            }
            #else
            OnComplete?.Invoke();
            #endif
        }
    }
}