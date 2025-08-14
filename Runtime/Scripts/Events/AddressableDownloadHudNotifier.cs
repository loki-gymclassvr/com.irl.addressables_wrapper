using System;
using UnityEngine;

namespace ShovelTools
{
    /// <summary>
    /// Static event system for notifying about addressable download progress.
    /// Now used exclusively by the standalone AddressableHud system.
    /// </summary>
    public static class AddressableDownloadHudNotifier
    {
        public static event Action<string, string> OnDownloadStarted;
        public static event Action<string> OnDownloadCompleted;
        public static event Action<string, string> OnDownloadFailed;
        public static event Action OnDownloadCancelled;

        public static event Action<string,string> OnLoadStarted;
        
        public static event Action<bool> OnSwitchStatusHud;
        
        public static void NotifyDownloadStarted(string assetKey, string displayName = null)
        {
            OnDownloadStarted?.Invoke(assetKey, displayName ?? assetKey);
        }
        
        public static void NotifyLoadStarted(string assetKey, string displayName = null)
            => OnLoadStarted?.Invoke(assetKey, displayName ?? assetKey);

        public static void NotifyDownloadCompleted(string assetKey)
        {
            OnDownloadCompleted?.Invoke(assetKey);
        }

        public static void NotifyDownloadFailed(string assetKey, string error)
        {
            OnDownloadFailed?.Invoke(assetKey, error);
        }
        
        public static void NotifyDownloadCancelled()
        {
            OnDownloadCancelled?.Invoke();
        }
        
        public static void SwitchDownloadHudVisibility(bool show)
        {
            OnSwitchStatusHud?.Invoke(show);
        }
    }
}