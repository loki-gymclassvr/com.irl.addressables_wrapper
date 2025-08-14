using System;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace AddressableSystem
{
    /// <summary>
    /// Interface for repository that stores async operation handles.
    /// </summary>
    public interface IAsyncHandleRepository
    {
        void AddHandle(string key, AsyncOperationHandle handle, bool autoUnload);
        bool RemoveHandle(string key);
        bool TryGetHandle(string key, out AsyncOperationHandle handle);
        void ClearAllHandles();
        string[] GetAutoUnloadKeys();
        bool IsOperationInProgress(string key);
        bool ContainsKey(string key);
        bool UpdateAutoUnloadFlag(string key, bool autoUnload);
    }
}