using System;
using UnityEngine.ResourceManagement.ResourceProviders;

namespace AddressableSystem
{
    /// <summary>
    /// Interface for loading addressable assets.
    /// Following the Interface Segregation Principle.
    /// </summary>
    public interface IAddressableLoader
    {
        void LoadAssetAsync<T>(string key, Action<T> onSuccess = null, Action<Exception> onFail = null, 
            bool autoUnload = false, IAsyncHandleRepository handleRepository = null);
            
        void LoadSceneAsync(string key, UnityEngine.SceneManagement.LoadSceneMode loadMode = UnityEngine.SceneManagement.LoadSceneMode.Single, 
            bool activateOnLoad = true, Action<SceneInstance> onSuccess = null, Action<Exception> onFail = null, 
            IAsyncHandleRepository handleRepository = null);
    }
}