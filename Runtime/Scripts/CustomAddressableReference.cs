using System;
using ShovelTools;
using UnityEngine;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;

namespace AddressableSystem
{
    /// <summary>
    /// Custom addressable asset reference that stores GUID, name and type information.
    /// Provides convenient load and unload methods that integrate with the AddressableLoader and AddressableUnloader.
    /// </summary>
    [Serializable]
    public class CustomAddressableReference
    {
        // Asset identification
        [SerializeField] private string _assetGUID;
        [SerializeField] private string _assetAddress;
        [SerializeField] private string _assetName;
        [SerializeField] private string _assetType;
        [SerializeField] private string _subObjectName;
        
        // Additional metadata for UI display
        [SerializeField] private string _editorAssetPath;
        [SerializeField] private bool _hasSubObjects;
        
        /// <summary>
        /// The GUID of the referenced addressable asset.
        /// </summary>
        public string AssetGUID => _assetGUID;
        
        /// <summary>
        /// The address of the referenced addressable asset.
        /// </summary>
        public string AssetAddress => _assetAddress;
        
        /// <summary>
        /// The name of the referenced addressable asset.
        /// </summary>
        public string AssetName => _assetName;
        
        /// <summary>
        /// The type of the referenced addressable asset.
        /// </summary>
        public string AssetType => _assetType;
        
        /// <summary>
        /// The path of the asset in the editor (for preview purposes).
        /// </summary>
        public string EditorAssetPath => _editorAssetPath;
        
        /// <summary>
        /// Whether the asset has sub-objects.
        /// </summary>
        public bool HasSubObjects => _hasSubObjects;
        
        /// <summary>
        /// The name of the sub-object if this references a sub-asset.
        /// </summary>
        public string SubObjectName => _subObjectName;
        
        /// <summary>
        /// Whether this reference has been initialized with a valid asset.
        /// </summary>
        public bool IsValid => !string.IsNullOrEmpty(_assetGUID);
        
        /// <summary>
        /// Load the referenced asset asynchronously.
        /// </summary>
        /// <typeparam name="T">The type of asset to load.</typeparam>
        /// <param name="loader">The addressable asset loader to use.</param>
        /// <param name="onSuccess">Callback when loading succeeds.</param>
        /// <param name="onFail">Callback when loading fails.</param>
        /// <param name="autoUnload">Whether to automatically unload this asset on scene changes.</param>
        /// <param name="handleRepository">The repository to store the async handle.</param>
        public void LoadAssetAsync<T>(
            IAddressableLoader loader,
            Action<T> onSuccess = null,
            Action<Exception> onFail = null,
            bool autoUnload = true,
            IAsyncHandleRepository handleRepository = null)
        {
            if (!IsValid)
            {
                onFail?.Invoke(new InvalidOperationException("CustomAddressableReference is not valid."));
                return;
            }
            
            if (loader == null)
            {
                onFail?.Invoke(new ArgumentNullException(nameof(loader)));
                return;
            }
            
            // Use the GUID as the addressable key
            loader.LoadAssetAsync<T>(_assetGUID, onSuccess, onFail, autoUnload, handleRepository);
        }
        
        /// <summary>
        /// Load a scene asynchronously.
        /// </summary>
        /// <param name="loader">The addressable asset loader to use.</param>
        /// <param name="loadMode">The scene load mode.</param>
        /// <param name="activateOnLoad">Whether to activate the scene immediately after loading.</param>
        /// <param name="onSuccess">Callback when loading succeeds.</param>
        /// <param name="onFail">Callback when loading fails.</param>
        /// <param name="handleRepository">The repository to store the async handle.</param>
        public void LoadSceneAsync(
            IAddressableLoader loader,
            LoadSceneMode loadMode = LoadSceneMode.Single,
            bool activateOnLoad = true,
            Action<SceneInstance> onSuccess = null,
            Action<Exception> onFail = null,
            IAsyncHandleRepository handleRepository = null)
        {
            if (!IsValid)
            {
                onFail?.Invoke(new InvalidOperationException("CustomAddressableReference is not valid."));
                return;
            }
            
            if (loader == null)
            {
                onFail?.Invoke(new ArgumentNullException(nameof(loader)));
                return;
            }
            
            // Use the GUID as the addressable key
            loader.LoadSceneAsync(_assetGUID, loadMode, activateOnLoad, onSuccess, onFail, handleRepository);
        }
        
        /// <summary>
        /// Unload the loaded asset from memory
        /// </summary>
        public void Unload()
        {
            if (!string.IsNullOrEmpty(AssetAddress))
            {
                AddressableManager.Instance.UnloadAsset(AssetAddress);
            }
            else
            {
                DLM.LogWarning(DLM.FeatureFlags.Addressables,"CustomAddressableReference is empty.");
            }
        }

    }
    
    /// <summary>
    /// Specialized version of CustomAddressableReference for prefabs and GameObjects.
    /// </summary>
    [Serializable]
    public class PrefabReference : CustomAddressableReference
    {
        public void LoadAsync(
            Action<GameObject> onSuccess = null,
            Action<Exception> onFail = null,
            bool autoUnload = true)
        {
            if(!string.IsNullOrEmpty(AssetAddress))
            {
                AddressableManager.Instance.LoadAssetAsync(AssetAddress, onSuccess, onFail, autoUnload);
            }
            else
            {
                DLM.LogWarning(DLM.FeatureFlags.Addressables,"CustomAddressableReference is empty.");
            }
        }
    }
    
    /// <summary>
    /// Specialized version of CustomAddressableReference for scenes.
    /// </summary>
    [Serializable]
    public class SceneReference : CustomAddressableReference
    {
        public void LoadAsync(
            LoadSceneMode loadMode = LoadSceneMode.Single,
            bool activateOnLoad = true,
            Action<SceneInstance> onSuccess = null,
            Action<Exception> onFail = null)
        {
            if(!string.IsNullOrEmpty(AssetAddress))
            {
                AddressableManager.Instance.LoadSceneAsync(AssetAddress, loadMode, activateOnLoad, onSuccess, onFail);
            }
            else
            {
                DLM.LogWarning(DLM.FeatureFlags.Addressables,"CustomAddressableReference is empty.");
            }
        }
    }
    
    /// <summary>
    /// Specialized version of CustomAddressableReference for materials.
    /// </summary>
    [Serializable]
    public class MaterialReference : CustomAddressableReference
    {
        public void LoadAsync(
            Action<Material> onSuccess = null,
            Action<Exception> onFail = null,
            bool autoUnload = true)
        {
            if(!string.IsNullOrEmpty(AssetAddress))
            {
                AddressableManager.Instance.LoadAssetAsync(AssetAddress, onSuccess, onFail, autoUnload);
            }
            else
            {
                DLM.LogWarning(DLM.FeatureFlags.Addressables,"CustomAddressableReference is empty.");
            }
        }
    }
    
    [Serializable]
    public class ShaderReference : CustomAddressableReference
    {
        public void LoadAsync(
            Action<Shader> onSuccess = null,
            Action<Exception> onFail = null,
            bool autoUnload = true)
        {
            if(!string.IsNullOrEmpty(AssetAddress))
            {
                AddressableManager.Instance.LoadAssetAsync(AssetAddress, onSuccess, onFail, autoUnload);
            }
            else
            {
                DLM.LogWarning(DLM.FeatureFlags.Addressables,"CustomAddressableReference is empty.");
            }
        }
    }
    
    [Serializable]
    public class TextureReference : CustomAddressableReference
    {
        public void LoadAsync(
            Action<Texture> onSuccess = null,
            Action<Exception> onFail = null,
            bool autoUnload = true)
        {
            if(!string.IsNullOrEmpty(AssetAddress))
            {
                AddressableManager.Instance.LoadAssetAsync(AssetAddress, onSuccess, onFail, autoUnload);
            }
            else
            {
                DLM.LogWarning(DLM.FeatureFlags.Addressables,"CustomAddressableReference is empty.");
            }
        }
    }
    
    [Serializable]
    public class Texture2DArrayReference : CustomAddressableReference
    {
        public void LoadAsync(
            Action<Texture2DArray> onSuccess = null,
            Action<Exception> onFail = null,
            bool autoUnload = true)
        {
            if(!string.IsNullOrEmpty(AssetAddress))
            {
                AddressableManager.Instance.LoadAssetAsync(AssetAddress, onSuccess, onFail, autoUnload);
            }
            else
            {
                DLM.LogWarning(DLM.FeatureFlags.Addressables,"CustomAddressableReference is empty.");
            }
        }
    }
    
    /// <summary>
    /// Specialized version of CustomAddressableReference for textures.
    /// </summary>
    [Serializable]
    public class Texture2DReference : CustomAddressableReference
    {
        public void LoadAsync(
            Action<Texture2D> onSuccess = null,
            Action<Exception> onFail = null,
            bool autoUnload = true)
        {
            if(!string.IsNullOrEmpty(AssetAddress))
            {
                AddressableManager.Instance.LoadAssetAsync(AssetAddress, onSuccess, onFail, autoUnload);
            }
            else
            {
                DLM.LogWarning(DLM.FeatureFlags.Addressables,"CustomAddressableReference is empty.");
            }
        }
    }
    
    /// <summary>
    /// Specialized version of CustomAddressableReference for audio clips.
    /// </summary>
    [Serializable]
    public class AudioReference : CustomAddressableReference
    {
        public void LoadAsync(
            Action<AudioClip> onSuccess = null,
            Action<Exception> onFail = null,
            bool autoUnload = true)
        {
            if(!string.IsNullOrEmpty(AssetAddress))
            {
                AddressableManager.Instance.LoadAssetAsync(AssetAddress, onSuccess, onFail, autoUnload);
            }
            else
            {
                DLM.LogWarning(DLM.FeatureFlags.Addressables,"CustomAddressableReference is empty.");
            }
        }
    }
    
    /// <summary>
    /// Specialized version of CustomAddressableReference for 3D models.
    /// </summary>
    [Serializable]
    public class MeshReference : CustomAddressableReference
    {
        public void LoadAsync(
            Action<Mesh> onSuccess = null,
            Action<Exception> onFail = null,
            bool autoUnload = true)
        {
            if(!string.IsNullOrEmpty(AssetAddress))
            {
                AddressableManager.Instance.LoadAssetAsync(AssetAddress, onSuccess, onFail, autoUnload);
            }
            else
            {
                DLM.LogWarning(DLM.FeatureFlags.Addressables,"CustomAddressableReference is empty.");
            }
        }
    }
}