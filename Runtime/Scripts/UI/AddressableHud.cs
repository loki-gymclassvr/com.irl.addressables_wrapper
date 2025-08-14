using UnityEngine;
using TMPro;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ShovelTools;
using UnityEngine.SceneManagement;

namespace AddressableSystem
{
    /// <summary>
    /// Simplified HUD that only shows download status without progress tracking.
    /// Much more performance-friendly for multiplayer scenarios.
    /// </summary>
    public class AddressableHud : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private GameObject _hudContainer;
        [SerializeField] private TextMeshProUGUI _statusText;

        [Header("Display Settings")]
        [SerializeField] private float _minimumDisplayTime = 1.5f;
        [SerializeField] private float _completedDisplayTime = 1.0f;
        [SerializeField] private float _errorDisplayTime = 2.0f;
        [SerializeField] private DLM.FeatureFlags _loggingFeatureFlag = DLM.FeatureFlags.Addressables;

        [Header("Status Messages")]
        [SerializeField] private string _downloadingText = "Downloading Content...";
        [SerializeField] private string _loadingText = "Loading Content...";
        [SerializeField] private string _completedText = "Download Complete";
        [SerializeField] private string _failedText = "Download Failed";
        [SerializeField] private string _cancelledText = "Download Cancelled";

        // State tracking using HashSet for reliability
        private readonly HashSet<string> _activeDownloads = new HashSet<string>();
        private float _lastStatusChangeTime;
        private bool _isInitialized = false;
        private bool _isHudOn = true;

        #region Unity Lifecycle

        private void Awake()
        {
            if (_hudContainer != null)
            {
                ShowStatus("");
                _hudContainer.SetActive(false);
            }
            
            if (_statusText == null)
            {
                DLM.LogWarning(_loggingFeatureFlag, "[AddressableHud] Status Text is not assigned!");
            }
            
            _isInitialized = true;
        }

        private void OnEnable()
        {
            ForceHide();
            
            // Unsubscribe first to prevent duplicates
            AddressableDownloadHudNotifier.OnDownloadStarted -= OnDownloadStarted;
            AddressableDownloadHudNotifier.OnDownloadStarted += OnDownloadStarted;
            
            AddressableDownloadHudNotifier.OnDownloadCompleted -= OnDownloadCompleted;
            AddressableDownloadHudNotifier.OnDownloadCompleted += OnDownloadCompleted;
            
            AddressableDownloadHudNotifier.OnDownloadFailed -= OnDownloadFailed;
            AddressableDownloadHudNotifier.OnDownloadFailed += OnDownloadFailed;
            
            AddressableDownloadHudNotifier.OnLoadStarted -= OnLoadStarted;
            AddressableDownloadHudNotifier.OnLoadStarted += OnLoadStarted;
            
            AddressableDownloadHudNotifier.OnDownloadCancelled -= OnDownloadCancelled;
            AddressableDownloadHudNotifier.OnDownloadCancelled += OnDownloadCancelled;
            
            AddressableDownloadHudNotifier.OnSwitchStatusHud -= SwitchHudVisibility;
            AddressableDownloadHudNotifier.OnSwitchStatusHud += SwitchHudVisibility;
            
            SceneManager.activeSceneChanged -= OnActiveSceneChanged;
            SceneManager.activeSceneChanged += OnActiveSceneChanged;
        }

        private void OnDisable()
        {
            ForceHide();
            AddressableDownloadHudNotifier.OnDownloadStarted -= OnDownloadStarted;
            AddressableDownloadHudNotifier.OnDownloadCompleted -= OnDownloadCompleted;
            AddressableDownloadHudNotifier.OnDownloadFailed -= OnDownloadFailed;
            AddressableDownloadHudNotifier.OnLoadStarted -= OnLoadStarted;
            AddressableDownloadHudNotifier.OnDownloadCancelled -= OnDownloadCancelled;
            AddressableDownloadHudNotifier.OnSwitchStatusHud -= SwitchHudVisibility;
            SceneManager.activeSceneChanged -= OnActiveSceneChanged;
        }

        private void OnDestroy()
        {
            // Ensure cleanup on destruction
            ForceHide();
        }

        #endregion

        #region Event Handlers
        
        private void OnActiveSceneChanged(Scene oldScene, Scene newScene)
        {
            // Hide HUD on main scene transitions
            if (oldScene.name != newScene.name)
            {
                ForceHide();
            }
        }

        private void OnDownloadStarted(string assetKey, string displayName)
        {
            if (!_isHudOn || !_isInitialized) return;

            _activeDownloads.Add(assetKey);
            _lastStatusChangeTime = Time.time;

            ShowStatus(_downloadingText);
        }

        private void OnLoadStarted(string assetKey, string displayName)
        {
            if (!_isHudOn || !_isInitialized) return;

            _activeDownloads.Add(assetKey);
            _lastStatusChangeTime = Time.time;

            ShowStatus(_loadingText);
        }

        private void OnDownloadCompleted(string assetKey)
        {
            if (!_isInitialized) return;

            _activeDownloads.Remove(assetKey);

            if (_isHudOn)
            {
                // Only show completed if no other downloads are active
                if (_activeDownloads.Count == 0)
                {
                    ShowStatus(_completedText);
                    // Hide immediately after showing completed status
                    Invoke(nameof(HideHud), 0.5f);
                }
            }
            else
            {
                ForceHide();
            }
        }

        private void OnDownloadFailed(string assetKey, string error)
        {
            if (!_isInitialized) return;

            _activeDownloads.Remove(assetKey);

            if (_isHudOn)
            {
                // Always show errors
                ShowStatus(_failedText);

                // Hide after showing error if no other downloads
                if (_activeDownloads.Count == 0)
                {
                    Invoke(nameof(HideHud), 0.5f);
                }
            }
            else
            {
                ForceHide();
            }
        }

        private void OnDownloadCancelled()
        {
            if (!_isInitialized) return;
            
            // Clear all downloads on cancel
            _activeDownloads.Clear();
            
            if (_isHudOn)
            {
                // Show cancelled status briefly
                ShowStatus(_cancelledText);
                
                // Hide quickly
                Invoke(nameof(HideHud), 0.5f);
            }
            else
            {
                ForceHide();
            }
        }

        #endregion

        #region Display Methods

        private void SwitchHudVisibility(bool status)
        {
            _isHudOn = status;
            
            // If turning off, hide immediately
            if (!_isHudOn && _hudContainer != null && _hudContainer.activeInHierarchy)
            {
                ForceHide();
            }
        }

        private void ShowStatus(string message)
        {
            // Show container
            if (_hudContainer != null && !_hudContainer.activeInHierarchy)
            {
                _hudContainer.SetActive(true);
            }
            
            // Update text
            if (_statusText != null)
            {
                _statusText.SetText(message);
            }
        }

        private void HideHud()
        {
            if (_hudContainer != null && _hudContainer.activeInHierarchy)
            {
                _hudContainer.SetActive(false);
            }
        }

        private void ForceHide()
        {
            CancelInvoke(); // Cancel any pending Invoke calls
            _activeDownloads.Clear();
            HideHud();
        }

        #endregion

        #region Public API

        /// <summary>
        /// Check if there are any active downloads
        /// </summary>
        public bool HasActiveDownloads()
        {
            return _activeDownloads.Count > 0;
        }

        /// <summary>
        /// Get count of active downloads
        /// </summary>
        public int GetActiveDownloadCount()
        {
            return _activeDownloads.Count;
        }

        /// <summary>
        /// Get list of active download keys
        /// </summary>
        public List<string> GetActiveDownloadKeys()
        {
            return new List<string>(_activeDownloads);
        }

        /// <summary>
        /// Manually verify and cleanup stuck HUD
        /// </summary>
        public void VerifyAndCleanup()
        {
            if (_activeDownloads.Count == 0 && _hudContainer != null && _hudContainer.activeInHierarchy)
            {
                DLM.Log(_loggingFeatureFlag, "[AddressableHud] Manual cleanup - hiding HUD");
                ForceHide();
            }
        }

        #endregion
    }
}