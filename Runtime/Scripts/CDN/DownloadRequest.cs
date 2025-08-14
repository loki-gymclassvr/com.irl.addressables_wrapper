using System;
using System.Threading.Tasks;
using UnityEngine.SceneManagement;

namespace AddressableSystem
{
    /// <summary>
    /// Represents a download request in the queue
    /// </summary>
    public class DownloadRequest
    {
        public string Key { get; }
        public DownloadPriority Priority { get; }
        public TaskCompletionSource<object> TaskCompletionSource { get; }
        public Type AssetType { get; }
        public DateTime QueueTime { get; }
        public bool IsScene { get; }
        public LoadSceneMode? SceneLoadMode { get; }
        
        public DownloadRequest(string key, DownloadPriority priority, Type assetType, bool isScene = false, LoadSceneMode? sceneLoadMode = null)
        {
            Key = key;
            Priority = priority;
            AssetType = assetType;
            IsScene = isScene;
            SceneLoadMode = sceneLoadMode;
            TaskCompletionSource = new TaskCompletionSource<object>();
            QueueTime = DateTime.Now;
        }
    }
}