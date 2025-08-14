using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BugsnagUnity;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.SceneManagement;
using ShovelTools;
using UnityEngine.AddressableAssets.ResourceLocators;

namespace AddressableSystem
{
    public enum TargetDevice
    {
        Quest,
        Mobile_Android,
        Mobile_iOS,
        PC
    }
    
    /// <summary>
    /// Singleton ScriptableObject that manages the addressable asset system.
    /// Acts as the facade for the addressable subsystem with optional CDN/catalog capabilities.
    /// </summary>
    [CreateAssetMenu(fileName = "AddressableManager", menuName = "Addressables_Wrapper/Addressable Manager")]
    public class AddressableManager : ScriptableObject
    {
        private static AddressableManager _instance;

        [Header("CDN")]
        [SerializeField] private bool _enableRemoteCDN = false;
        [SerializeField] private string _cdnUrl = "https://your-cdn-url.com/";
        [SerializeField] private string _catalogVersion = "1.0.0";
        [SerializeField] private string _buildVersion = "1.0.0";
        [SerializeField] private List<string> _defaultCatalogPaths = new List<string>();

        [Header("Profiles")]
        [SerializeField] private TargetDevice _selectedTargetDevice = TargetDevice.Quest;
        
        [SerializeField] private DLM.FeatureFlags _loggingFeatureFlag = DLM.FeatureFlags.Addressables;
        [SerializeField] private bool _logDetailedErrors = true;

        public const string ENDPOINT_CDN_SIGNED_CATALOG = "https://api.gymclassvr.com/v1/deploy-lambda-v1-GymClassCdnSignUrlGenerator-oXDCvnP8OtEi?";
        
        public bool EnableRemoteCDN
        {
            get { return _enableRemoteCDN; }
            set { _enableRemoteCDN = value; }
        }

        public string CdnUrl
        {
            get { return _cdnUrl; }
            set { _cdnUrl = value; }
        }
        
        public string CatalogVersion
        {
            get { return _catalogVersion; }
            set { _catalogVersion = value; }
        }
        
        public string BuildVersion
        {
            get { return _buildVersion; }
            set { _buildVersion = value; }
        }

        public List<string> DefaultCatalogPaths
        {
            get { return _defaultCatalogPaths; }
            set { _defaultCatalogPaths = value; }
        }

        public TargetDevice SelectedTargetDevice
        {
            get { return _selectedTargetDevice; }
            set { _selectedTargetDevice = value; }
        }

        // Track the initialization state
        private static bool _initializing = false;
        private static bool _coreInitialized = false;        // NEW: only core Addressables
        private static bool _initialized = false;            // full (core + catalogs)
        private static TaskCompletionSource<bool> _initializationTask;
        // at the top of AddressableManager
        private readonly List<string> _loadedObbCatalogs = new List<string>();
        private string _signedCatalogUrl;
        
        private Task _catalogRefreshTask;
        private readonly object _catalogLock = new object();

        /// <summary>
        /// Global singleton instance (loaded from Resources/AddressableManager).
        /// </summary>
        public static AddressableManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = Resources.Load<AddressableManager>("AddressableManager");
                    if (_instance == null)
                    {
                        _instance = CreateInstance<AddressableManager>();
                        #if UNITY_EDITOR
                        UnityEditor.AssetDatabase.CreateAsset(_instance, "Assets/Resources/AddressableManager.asset");
                        UnityEditor.AssetDatabase.SaveAssets();
                        #endif
                    }
                }
                return _instance;
            }
        }

        // Core Addressables subsystems
        private IAddressableLoader     _loader;
        private IAsyncHandleRepository _handleRepository;
        private IAddressableUnloader   _unloader;

        // CDN-enhanced subsystems
        private ICatalogManager  _catalogManager;
        private IDownloadManager _downloadManager;
        private ICacheManager    _cacheManager;
        private bool             _cdnCapabilitiesInitialized = false;

        /// <summary>Expose CDN download API for external subscription.</summary>
        public IDownloadManager DownloadManager => _downloadManager;

        /// <summary>Expose the cache manager.</summary>
        public ICacheManager CacheManager => _cacheManager;

        /// <summary>True if Addressables (and catalogs, if enabled) have finished initializing.</summary>
        public bool IsInitialized => _initialized;

        private void OnEnable()
        {
#if UNITY_EDITOR
            // In-editor only: wire up the pure Addressables loader/unloader
            _loader           = new AddressableLoader();
            _handleRepository = new AsyncHandleRepository();
            _unloader         = new AddressableUnloader(_handleRepository);
#endif
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            if (_initializing || _initialized)
                return;

            _initializing = true;
            _initializationTask = new TaskCompletionSource<bool>();

            // Force creation
            var inst = Instance;

            // Core Addressables
            inst._loader           = new AddressableLoader();
            inst._handleRepository = new AsyncHandleRepository();
            inst._unloader         = new AddressableUnloader(inst._handleRepository);

            // Start initialization of AddressablesOBBInitialization explicitly
            // This is the centralized point for initializing both systems
            StartAddressablesInitialization();
            
            // Register scene unload listener
            SceneManager.sceneUnloaded += inst.OnSceneUnloaded;
        }

        /// <summary>
        /// Starts the initialization of the AddressablesOBBInitialization.
        /// This method serves as the centralized entry point for initializing both
        /// the AddressablesOBBInitialization and AddressableManager systems.
        /// </summary>
        private static void StartAddressablesInitialization()
        {
            // First, call AddressablesOBBInitialization to set up the necessary transforms and catalogs
            // The static class will return a Task we can await
            var inst = Instance;
            
            try
            {
                if(inst.SelectedTargetDevice == TargetDevice.Quest)
                {
                    #if UNITY_ANDROID && !UNITY_EDITOR
                    // Only use AddressablesOBBInitialization on Android devices
                    
                    AddressablesOBBInitialization.InitializeAddressables();
                    ContinueWithAddressableManagerInitialization();
                    #else
                    // In Editor or non-Android platforms, skip OBB initialization
                    DLM.Log(inst._loggingFeatureFlag, "Skipping OBB initialization in Editor/non-Android platform");
                    inst.InitializeAddressablesAsync();
                    #endif
                }
                else
                {
                    Addressables.InternalIdTransformFunc = loc => InternalIdTransformer.Transform(loc);
                    DLM.Log(inst._loggingFeatureFlag, "Skipping OBB initialization in Editor/non-Quest device");
                    inst.InitializeAddressablesAsync();
                }
            }
            catch (Exception ex)
            {
                // If AddressablesOBBInitialization fails or isn't available, log and fall back to our own initialization
                DLM.LogWarning(inst._loggingFeatureFlag, $"Error starting AddressablesOBBInitialization: {ex.Message}. Falling back to direct initialization.");
                Bugsnag.Notify(new System.Exception($"[Addressables]: Error starting AddressablesOBBInitialization: {ex.Message}. Falling back to direct initialization."));
                // Continue with our own initialization
                inst.InitializeAddressablesAsync();
            }
        }

        /// <summary>
        /// Initializes the Addressables system asynchronously.
        /// Handles both core initialization and optional CDN/OBB capabilities based on configuration.
        /// Integrates with AddressablesOBBInitialization to use validated OBB catalogs when appropriate.
        /// </summary>
        private async void InitializeAddressablesAsync()
        {
            try
            {
                // If AddressablesOBBInitialization didn't initialize core, we need to do it
                if (!_coreInitialized)
                {
                    DLM.Log(_loggingFeatureFlag, "Starting Addressables core initialization...");
                    bool coreOk = await InitializeAddressablesCoreAsync();
                    _coreInitialized = coreOk;
                    
                    if (!coreOk)
                    {
                        DLM.LogError(_loggingFeatureFlag, "Failed to initialize Addressables core");
                        _initializationTask.SetResult(false);
                        _initializing = false;
                        return;
                    }
                }
                else
                {
                    DLM.Log(_loggingFeatureFlag, "Addressables core already initialized");
                }

                if (SelectedTargetDevice == TargetDevice.Quest)
                {
                    // Handle OBB catalogs for Quest devices
                    List<string> obbCatalogsToLoad = new List<string>();
                    
                    #if UNITY_ANDROID && !UNITY_EDITOR
                    try
                    {
                        // Try to get validated OBB catalogs from AddressablesOBBInitialization
                        List<string> validatedObbCatalogs = await AddressablesOBBInitialization.GetValidatedObbCatalogsAsync();
                        if (validatedObbCatalogs != null && validatedObbCatalogs.Count > 0)
                        {
                            DLM.Log(_loggingFeatureFlag, $"Found {validatedObbCatalogs.Count} validated OBB catalogs");
                            obbCatalogsToLoad.AddRange(validatedObbCatalogs);
                        }
                    }
                    catch (Exception ex)
                    {
                        // If AddressablesOBBInitialization isn't available or fails, log and continue
                        DLM.LogWarning(_loggingFeatureFlag, $"Could not get validated OBB catalogs: {ex.Message}");
                    }
                    #endif
                    
                    // Handle OBB catalogs without CDN
                    if (obbCatalogsToLoad.Count > 0)
                    {
                        DLM.Log(_loggingFeatureFlag, $"Loading {obbCatalogsToLoad.Count} OBB catalogs directly via Addressables");
                            
                        bool ok = await InitializeWithCatalogsAsync(obbCatalogsToLoad);
                        if (ok)
                        {
                            _loadedObbCatalogs.AddRange(obbCatalogsToLoad);
                            DLM.Log(_loggingFeatureFlag, $"Successfully loaded obb catalogs via CatalogManager");
                            await InternalIdTransformer.PrewarmCacheAsync();
                        }
                        else
                        {
                            DLM.LogError(_loggingFeatureFlag, $"Failed to load obb catalog via CatalogManager");
                        }
                    }
                    else
                    {
                        DLM.Log(_loggingFeatureFlag, "No OBB catalogs to load and CDN disabled");
                    }
                }
                else
                {
                    await InternalIdTransformer.PrewarmCacheAsync();
                }

                _initialized = true;
                _initializing = false;
                _initializationTask.SetResult(true);
                DLM.Log(_loggingFeatureFlag, "AddressableManager initialization completed successfully");
            }
            catch (Exception ex)
            {
                DLM.LogError(_loggingFeatureFlag, $"Error during AddressableManager initialization: {ex.Message}");
                if (_logDetailedErrors)
                    DLM.LogError(_loggingFeatureFlag, ex.StackTrace);
                _initialized = false;
                _initializing = false;
                _initializationTask.SetResult(false);
            }
        }
        
        /// <summary>
        /// Remove all remote urls
        /// </summary>
        private void ScrubUnsignedCloudflareLocators()
        {
            const string CDN_URL_MARKER = "r2.cloudflarestorage.com/";
            const string BUNDLE_MARKER  = ".bundle";
            var   logFlag              = _loggingFeatureFlag;

            // snapshot on main thread
            var locatorsToScan = new List<IResourceLocator>(Addressables.ResourceLocators);

            Task.Run(() =>
                {
                    var toRemove = new List<IResourceLocator>();

                    foreach (var locator in locatorsToScan)
                    {
                        bool hasUnsigned = false;
                        foreach (var key in locator.Keys)
                        {
                            locator.Locate(key, null, out var locations);
                            foreach (var loc in locations)
                            {
                                var id = loc.InternalId.Replace('\\','/');
                                if (id.IndexOf(CDN_URL_MARKER, StringComparison.Ordinal) >= 0 &&
                                    id.EndsWith(BUNDLE_MARKER, StringComparison.OrdinalIgnoreCase))
                                {
                                    hasUnsigned = true;
                                    break;
                                }
                            }
                            if (hasUnsigned) break;
                        }

                        if (hasUnsigned)
                            toRemove.Add(locator);
                    }

                    return toRemove;
                })
                .ContinueWith(task =>
                {
                    // continuation scheduled on Unity's main thread via TaskScheduler
                    foreach (var locator in task.Result)
                    {
                        Addressables.RemoveResourceLocator(locator);
                        DLM.Log(logFlag, $"[AddressableInitializer] Removed catalog locator: {locator.LocatorId}");
                    }
                }, TaskScheduler.FromCurrentSynchronizationContext());
        }
        
        /// <summary>
        /// Returns true if the catalog was already valid or was successfully initialized/refreshed,
        /// false if the refresh threw.
        /// </summary>
        public async Task<bool> EnsureCatalogValidAsync()
        {
            if (_cdnCapabilitiesInitialized
                && _downloadManager != null
                && _catalogManager.IsCatalogStillValid(_signedCatalogUrl))
            {
                return true;
            }

            Task toAwait;
            lock (_catalogLock)
            {
                if (_catalogRefreshTask == null || _catalogRefreshTask.IsCompleted)
                    _catalogRefreshTask = InitializeRemoteCatalogAsync();
                toAwait = _catalogRefreshTask;
            }

            try
            {
                await toAwait;// await the single in-flight init
                return true;// success
            }
            catch
            {
                return false;// failure
            }
        }

        /// <summary>
        /// Post auth config initialization for cdn
        /// </summary>
        /// <param name="apiKey"></param>
        /// <param name="deviceId"></param>
        /// <param name="oculusOrgId"></param>
        /// <param name="oculusAccessToken"></param>
        /// <returns></returns>
        public async Task<bool> InitializeCdnConfig(string deviceType, string apiKey, string oculusOrgId, string oculusAccessToken)
        {
            CdnConfig.ApiKey            = apiKey;
            CdnConfig.DeviceId          = SystemInfo.deviceUniqueIdentifier;
            CdnConfig.DeviceType        = deviceType;
            CdnConfig.OculusOrgId       = oculusOrgId;
            CdnConfig.OculusAccessToken = oculusAccessToken;
            CdnConfig.Version           = Application.version;
            
            DLM.Log(_loggingFeatureFlag, $"APIKey: {CdnConfig.ApiKey} \n" +
                                         $"DeviceID: {CdnConfig.DeviceId} \n" +
                                         $"DeviceType: {CdnConfig.DeviceType} \n" +
                                         $"OculusOrgId: {CdnConfig.OculusOrgId} \n" +
                                         $"OculusAccessToken: {CdnConfig.OculusAccessToken} \n" +
                                         $"Version: {Application.version}");
            
            var ok = await EnsureCatalogValidAsync();
            
            return ok;
        }

        /// <summary>
        /// Fetches your signed catalog from the backend and loads it into Addressables.
        /// Returns true if the catalog loaded successfully.
        /// </summary>
        /// <param name="apiKey">Your API key</param>
        /// <param name="deviceId">Your device ID</param>
        /// <param name="oculusOrgId">Your Oculus Org ID</param>
        /// <param name="oculusAccessToken">Your Oculus Access Token</param>
        /// <param name="version">Your game version</param>
        private async Task<bool> InitializeRemoteCatalogAsync()
        {
            if (!EnableRemoteCDN)
            {
                DLM.Log(_loggingFeatureFlag, "Remote CDN is disabled; skipping catalog fetch.");
                return false;
            }
            
            // Build the signed-catalog URL:
            var sb = new StringBuilder(ENDPOINT_CDN_SIGNED_CATALOG);
            sb.Append($"apiKey={Uri.EscapeDataString(CdnConfig.ApiKey)}");
            sb.Append($"&deviceId={Uri.EscapeDataString(CdnConfig.DeviceId)}");
            //TODO: We need to have one unified parameter for device types
            if (SelectedTargetDevice == TargetDevice.Quest)
            {
                sb.Append($"&deviceType={Uri.EscapeDataString(CdnConfig.DeviceType)}");
            }
            else
            {
                sb.Append($"&platform={Uri.EscapeDataString(CdnConfig.DeviceType)}");
            }
            sb.Append($"&oculusOrgId={Uri.EscapeDataString(CdnConfig.OculusOrgId)}");
            sb.Append($"&oculusAccessToken={Uri.EscapeDataString(CdnConfig.OculusAccessToken)}");
            sb.Append($"&version={Uri.EscapeDataString(CdnConfig.Version)}");
            _signedCatalogUrl = sb.ToString();

            DLM.Log(_loggingFeatureFlag, $"Loading signed Addressables catalog: {_signedCatalogUrl}");

            // Wire up the CDN managers with only this URL
            _catalogManager  = new CatalogManager(_loggingFeatureFlag, _signedCatalogUrl);
            _downloadManager = new DownloadManager(_loggingFeatureFlag, _handleRepository);
            _cacheManager    = new CacheManager(_loggingFeatureFlag);
            
            // Load the catalog
            bool ok = await InitializeWithCatalogsAsync(new List<string> { _signedCatalogUrl });
            if (ok)
            {
                if (_loadedObbCatalogs.Count > 0)
                {
                    for (int i = 0; i < _loadedObbCatalogs.Count; i++)
                    {
                        _catalogManager.UnloadCatalog(_loadedObbCatalogs[i]);
                    }
                }
                
                DLM.Log(_loggingFeatureFlag, "Remote Addressables catalog loaded successfully.");
                ScrubUnsignedCloudflareLocators();
                
                await Task.Delay(500);
                _initialized = true;
                _cdnCapabilitiesInitialized = true;
            }
            else
            {
                DLM.LogError(_loggingFeatureFlag, "Failed to load remote Addressables catalog.");
            }
            return ok;
        }

        /// <summary>
        /// Continues the initialization process after AddressablesOBBInitialization completes.
        /// Waits for the AddressablesOBBInitialization to finish, then proceeds with 
        /// AddressableManager initialization, handling success or failure appropriately.
        /// </summary>
        private static async void ContinueWithAddressableManagerInitialization()
        {
            var inst = Instance;
            DLM.Log(inst._loggingFeatureFlag, "Waiting for AddressablesOBBInitialization to complete…");
            bool obbSuccess = await AddressablesOBBInitialization.WaitForInitializationAsync();
            
            if (!obbSuccess)
                DLM.LogWarning(inst._loggingFeatureFlag, "OBB init failed, continuing anyway…");
            
            try
            {
                // Wait for AddressablesOBBInitialization to complete
                DLM.Log(inst._loggingFeatureFlag, "Waiting for AddressablesOBBInitialization to complete...");
                bool success = await AddressablesOBBInitialization.WaitForInitializationAsync();
                
                if (success)
                {
                    DLM.Log(inst._loggingFeatureFlag, "AddressablesOBBInitialization completed successfully");
                    //_coreInitialized = true;
                    
                    // Continue with CDN initialization
                    inst.InitializeAddressablesAsync();
                }
                else
                {
                    DLM.LogWarning(inst._loggingFeatureFlag, "AddressablesOBBInitialization completed with errors, falling back to direct initialization");
                    
                    // Fall back to our own initialization
                    inst.InitializeAddressablesAsync();
                }
            }
            catch (Exception ex)
            {
                DLM.LogError(inst._loggingFeatureFlag, $"Error waiting for AddressablesOBBInitialization: {ex.Message}");
                if (inst._logDetailedErrors)
                    DLM.LogError(inst._loggingFeatureFlag, ex.StackTrace);
                    
                // Fall back to direct initialization
                inst.InitializeAddressablesAsync();
            }
        }
        
        private async Task<bool> InitializeAddressablesCoreAsync()
        {
            try
            {
                DLM.Log(_loggingFeatureFlag, "Initializing Addressables core...");
                var op = Addressables.InitializeAsync(false);
                while (!op.IsDone)
                    await Task.Yield();

                bool ok = op.Status == AsyncOperationStatus.Succeeded;
                if (!ok && _logDetailedErrors)
                {
                    string msg = op.OperationException?.Message ?? "Unknown error";
                    DLM.LogError(_loggingFeatureFlag, $"Addressables core init failed: {msg}");
                }
                else
                {
                    _catalogManager = new CatalogManager(_loggingFeatureFlag);
                    DLM.Log(_loggingFeatureFlag, "Addressables core initialized");
                }

                Addressables.Release(op);
                return ok;
            }
            catch (Exception ex)
            {
                DLM.LogError(_loggingFeatureFlag, $"Exception during core init: {ex.Message}");
                if (_logDetailedErrors) DLM.LogError(_loggingFeatureFlag, ex.StackTrace);
                return false;
            }
        }

        private void OnSceneUnloaded(Scene scene) => _unloader.UnloadAutoUnloadHandles();

        public async Task<bool> IsSceneKeyAsync(string key)
        {
            var handle = Addressables.LoadResourceLocationsAsync(key);
            await handle.Task;
            bool result = handle.Status == AsyncOperationStatus.Succeeded
                       && handle.Result.Any(loc => loc.ResourceType == typeof(SceneInstance));
            Addressables.Release(handle);
            return result;
        }

        //──────────────────────────────────────────────────────────────────────────
        // Original Addressables facade (loading / unloading assets & scenes)
        //──────────────────────────────────────────────────────────────────────────

        public async void LoadAssetAsync<T>(
            string key,
            Action<T> onSuccess = null,
            Action<Exception> onFail = null,
            bool autoUnload = true,
            bool showLoadingDial = false) where T : UnityEngine.Object
        {
            try
            {
                if (!_initialized) await WaitForInitializationAsync();

                Action<T> wrappedSuccess = asset => {
                    if (showLoadingDial) AddressableDownloadHudNotifier.NotifyDownloadCompleted(key);
                    onSuccess?.Invoke(asset);
                };

                Action<Exception> wrappedFail = exception => {
                    if (showLoadingDial) AddressableDownloadHudNotifier.NotifyDownloadFailed(key, exception.Message);
                    onFail?.Invoke(exception);
                };

                _loader.LoadAssetAsync<T>(key, wrappedSuccess, wrappedFail, autoUnload, _handleRepository);
            }
            catch (Exception ex)
            {
                if (showLoadingDial) AddressableDownloadHudNotifier.NotifyDownloadFailed(key, ex.Message);
                onFail?.Invoke(ex);
            }
        }

        public async void LoadSceneAsync(
            string key,
            LoadSceneMode loadMode = LoadSceneMode.Single,
            bool activateOnLoad = true,
            Action<SceneInstance> onSuccess = null,
            Action<Exception> onFail = null
        )
        {
            if (!_initialized)
            {
                try { await WaitForInitializationAsync(); }
                catch { onFail?.Invoke(new InvalidOperationException("Addressables init failed")); return; }
            }
            _loader.LoadSceneAsync(key, loadMode, activateOnLoad, onSuccess, onFail, _handleRepository);
        }

        public void UnloadAsset(string key)
            => _unloader.UnloadHandle(key);

        public void UnloadAllAssets()
            => _unloader.UnloadAllHandles();

        //──────────────────────────────────────────────────────────────────────────
        // CDN Catalogs & Download API
        //──────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Awaitable entrypoint: returns true once Addressables (and optional CDN catalogs) are ready.
        /// </summary>
        public static Task<bool> WaitForInitializationAsync()
        {
            if (_initialized)
                return Task.FromResult(true);

            if (!_initializing)
                Initialize();

            return _initializationTask.Task;
        }
        
        public void SetCatalogVersion(string catalogVersion)
        {
            CatalogVersion = catalogVersion;
        }

        /// <summary>
        /// Initializes Addressables with the specified catalog paths.
        /// This method is used for loading content catalogs from both OBB and CDN sources.
        /// It waits for core initialization if needed and handles editor-specific catalog paths.
        /// </summary>
        /// <param name="catalogPaths">List of catalog paths to load</param>
        /// <returns>True if all catalogs loaded successfully, false otherwise</returns>
        public async Task<bool> InitializeWithCatalogsAsync(List<string> catalogPaths)
        {
            #if UNITY_EDITOR
            // always include the local catalog in editor
            var local = GetEditorLocalCatalogPath();
            if (File.Exists(local))
                catalogPaths.Add(local);
            #endif
            
            if (!_coreInitialized)
            {
                DLM.Log(_loggingFeatureFlag, "Waiting for Addressables core before loading catalogs...");
                await WaitForInitializationAsync();
            }

            if (_catalogManager == null)
            {
                DLM.LogError(_loggingFeatureFlag, "CatalogManager is null");
                return false;
            }

            bool allOk = true;
            foreach (var path in catalogPaths)
            {
                try
                {
                    DLM.Log(_loggingFeatureFlag, $"Loading catalog: {path}");
                    bool ok = await _catalogManager.LoadCatalogAsync(path);
                    if (!ok)
                    {
                        allOk = false;
                        DLM.LogError(_loggingFeatureFlag, $"Failed to load: {path}");
                    }
                }
                catch (Exception ex)
                {
                    allOk = false;
                    DLM.LogError(_loggingFeatureFlag, $"Exception loading {path}: {ex.Message}");
                    if (_logDetailedErrors) DLM.LogError(_loggingFeatureFlag, ex.StackTrace);
                }
            }
            return allOk;
        }
        

#if UNITY_EDITOR
        private static string GetEditorLocalCatalogPath()
        {
            string platform = UnityEditor.EditorUserBuildSettings.activeBuildTarget.ToString();
            return Path.Combine(
                Application.dataPath.Replace("/Assets", ""),
                $"Library/com.unity.addressables/aa/{platform}/catalog.json"
            );
        }
#endif

        //──────────────────────────────────────────────────────────────────────────
        // CDN-powered download + load wrappers
        //──────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Downloads all required bundles for <paramref name="key"/>,
        /// then loads the asset of type <typeparamref name="T"/>.
        /// </summary>
        /// <typeparam name="T">Type of the asset to load (must inherit from UnityEngine.Object).</typeparam>
        /// <param name="key">Addressable key or label of the asset.</param>
        /// <param name="priority">Download priority level.</param>
        /// <param name="showLoadingDialog">Whether to show loading dial UI for this operation.</param>
        /// <returns>
        /// A <see cref="Task{T}"/> that completes with the loaded asset
        /// once download and load succeed, or faults if either step fails.
        /// </returns>
        public async Task<T> DownloadAndLoadAssetAsync<T>(
            string key,
            DownloadPriority priority = DownloadPriority.Normal,
            bool showLoadingDialog = false
        ) where T : UnityEngine.Object
        {
            try
            {
                await EnsureCatalogValidAsync();

                T result = await _downloadManager.QueueDownloadToLoadAsync<T>(key, priority, _handleRepository);
                
                return result;
            }
            catch (Exception ex)
            {
                // Clear cache for failed download
                DLM.LogWarning(_loggingFeatureFlag, $"[AddressableManager] Download failed for '{key}': {ex.Message}");
                Bugsnag.Notify(new System.Exception($"[AddressableManager] Download failed for '{key}': {ex.Message}"));
        
                try
                {
                    bool cacheCleared = await ClearCacheForAssetAsync(key);
                    if (cacheCleared)
                    {
                        DLM.Log(_loggingFeatureFlag, $"[AddressableManager] Cleared cache for failed download: {key}");
                    }
                    else
                    {
                        DLM.LogWarning(_loggingFeatureFlag, $"[AddressableManager] Failed to clear cache for: {key}");
                    }
                }
                catch (Exception cacheEx)
                {
                    DLM.LogWarning(_loggingFeatureFlag, $"[AddressableManager] Exception clearing cache for '{key}': {cacheEx.Message}");
                    Bugsnag.Notify(new System.Exception($"[AddressableManager] Exception clearing cache for '{key}': {cacheEx.Message}"));
                }
                
                if (showLoadingDialog) AddressableDownloadHudNotifier.NotifyDownloadFailed(key, ex.Message);
                
                throw;
            }
        }
        
        public async Task<bool> DownloadAssetAsync<T>(
            string key, 
            DownloadPriority priority = DownloadPriority.Normal,
            bool showLoadingDialog = false) where T : UnityEngine.Object
        {
            if (EnableRemoteCDN)
            {
                try
                {
                    await EnsureCatalogValidAsync();
                    
                    var assetStatus = await _downloadManager.PreDownloadAssetAsync(key, priority,null, showLoadingDialog);
            
                    if (showLoadingDialog) AddressableDownloadHudNotifier.NotifyDownloadCompleted(key);
                    
                    return assetStatus;
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    return false;
                }
            }

            return true;
        }
        
        /// <summary>
        /// 
        /// </summary>
        /// <param name="key"></param>
        /// <param name="priority"></param>
        /// <param name="showLoadingDialog"></param>
        /// <param name="autoUnload"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public async Task<T> DownloadAndLoadAssetAsync<T>(string key,
            DownloadPriority priority,
            bool showLoadingDialog = false,
            bool autoUnload = true) where T : UnityEngine.Object
        {
            try
            {
                if (EnableRemoteCDN)
                {
                    await EnsureCatalogValidAsync();

                    T asset = await _downloadManager.QueueDownloadToLoadAsync<T>(key, priority, _handleRepository, showLoadingDialog, autoUnload);
            
                    if (showLoadingDialog) AddressableDownloadHudNotifier.NotifyDownloadCompleted(key);
                    
                    return asset;
                }
                else
                {
                    // Convert callback-based LoadAssetAsync to Task-based
                    var tcs = new TaskCompletionSource<T>();
                    
                    LoadAssetAsync<T>(key, 
                        loadedAsset => {
                            if (showLoadingDialog) AddressableDownloadHudNotifier.NotifyDownloadCompleted(key);
                            tcs.SetResult(loadedAsset);
                        },
                        exception => {
                            if (showLoadingDialog) AddressableDownloadHudNotifier.NotifyDownloadFailed(key, exception.Message);
                            tcs.SetException(exception);
                        },
                        autoUnload,
                        false);
                        
                    return await tcs.Task;
                }
            }
            catch (Exception ex)
            {
                // Clear cache for failed download
                DLM.LogWarning(_loggingFeatureFlag, $"[AddressableManager] Download failed for '{key}': {ex.Message}");
                Bugsnag.Notify(new System.Exception($"[AddressableManager] Download failed for '{key}': {ex.Message}"));

                try
                {
                    bool cacheCleared = await ClearCacheForAssetAsync(key);
                    if (cacheCleared)
                    {
                        DLM.Log(_loggingFeatureFlag, $"[AddressableManager] Cleared cache for failed download: {key}");
                    }
                    else
                    {
                        DLM.LogWarning(_loggingFeatureFlag, $"[AddressableManager] Failed to clear cache for: {key}");
                    }
                }
                catch (Exception cacheEx)
                {
                    DLM.LogWarning(_loggingFeatureFlag, $"[AddressableManager] Exception clearing cache for '{key}': {cacheEx.Message}");
                    Bugsnag.Notify(new System.Exception($"[AddressableManager] Exception clearing cache for '{key}': {cacheEx.Message}"));
                }
                
                if (showLoadingDialog) AddressableDownloadHudNotifier.NotifyDownloadFailed(key, ex.Message);
                
                // Re-throw the exception to maintain async Task<T> contract
                throw;
            }
        }

        /// <summary>
        /// Downloads all required bundles for <paramref name="key"/>,
        /// then loads the asset and invokes the provided callbacks.
        /// </summary>
        /// <typeparam name="T">Type of the asset to load (must inherit from UnityEngine.Object).</typeparam>
        /// <param name="key">Addressable key or label of the asset.</param>
        /// <param name="priority">Download priority level.</param>
        /// <param name="onSuccess">Invoked with the loaded asset if both download and load succeed.</param>
        /// <param name="onFail">Invoked with an <see cref="Exception"/> if either download or load fails.</param>
        /// <param name="showLoadingDialog">Whether to show loading dial UI for this operation.</param>
        /// <param name="autoUnload">flag to auto unload the asset at scene switch</param>
        public async void DownloadAndLoadAssetAsync<T>(
            string key,
            DownloadPriority priority,
            Action<T> onSuccess,
            Action<Exception> onFail = null,
            bool showLoadingDialog = false,
            bool autoUnload = true
        ) where T : UnityEngine.Object
        {
            try
            {
                if (EnableRemoteCDN)
                {
                    await EnsureCatalogValidAsync();

                    T asset = await _downloadManager.QueueDownloadToLoadAsync<T>(key, priority, _handleRepository, showLoadingDialog, autoUnload);
            
                    if (showLoadingDialog) AddressableDownloadHudNotifier.NotifyDownloadCompleted(key);
                    onSuccess?.Invoke(asset);
                }
                else
                {
                    LoadAssetAsync<T>(key, 
                        asset => {
                            if (showLoadingDialog) AddressableDownloadHudNotifier.NotifyDownloadCompleted(key);
                            onSuccess?.Invoke(asset);
                        },
                        exception => {
                            if (showLoadingDialog) AddressableDownloadHudNotifier.NotifyDownloadFailed(key, exception.Message);
                            onFail?.Invoke(exception);
                        },
                        autoUnload,
                        false);
                }
            }
            catch (Exception ex)
            {
                // Clear cache for failed download
                DLM.LogWarning(_loggingFeatureFlag, $"[AddressableManager] Download failed for '{key}': {ex.Message}");
                Bugsnag.Notify(new System.Exception($"[AddressableManager] Download failed for '{key}': {ex.Message}"));
        
                try
                {
                    bool cacheCleared = await ClearCacheForAssetAsync(key);
                    if (cacheCleared)
                    {
                        DLM.Log(_loggingFeatureFlag, $"[AddressableManager] Cleared cache for failed download: {key}");
                    }
                    else
                    {
                        DLM.LogWarning(_loggingFeatureFlag, $"[AddressableManager] Failed to clear cache for: {key}");
                    }
                }
                catch (Exception cacheEx)
                {
                    DLM.LogWarning(_loggingFeatureFlag, $"[AddressableManager] Exception clearing cache for '{key}': {cacheEx.Message}");
                    Bugsnag.Notify(new System.Exception($"[AddressableManager] Exception clearing cache for '{key}': {cacheEx.Message}"));
                }
                
                if (showLoadingDialog) AddressableDownloadHudNotifier.NotifyDownloadFailed(key, ex.Message);
                onFail?.Invoke(ex);
            }
        }

        /// <summary>
        /// Downloads and loads all assets for the given list of <paramref name="keys"/>.
        /// </summary>
        /// <typeparam name="T">Type of the assets to load (must inherit from UnityEngine.Object).</typeparam>
        /// <param name="keys">List of addressable keys or labels to download and load.</param>
        /// <param name="priority">Download priority level.</param>
        /// <param name="showLoadingDialog">Whether to show loading dial UI for this batch operation.</param>
        /// <returns>
        /// A <see cref="Task{Dictionary}"/> that completes with a dictionary mapping each key
        /// to its loaded asset once all operations finish successfully.
        /// </returns>
        public async Task<Dictionary<string, T>> DownloadToLoadAssetsAsync<T>(
            List<string> keys,
            DownloadPriority priority = DownloadPriority.Normal,
            bool showLoadingDialog = false
        ) where T : UnityEngine.Object
        {
            try
            {
                await EnsureCatalogValidAsync();

                var result = new Dictionary<string, T>(keys.Count);
        
                for (int i = 0; i < keys.Count; i++)
                {
                    var key = keys[i];
            
                     result[key] = await _downloadManager.QueueDownloadToLoadAsync<T>(key, priority, _handleRepository, showLoadingDialog);
                }
        
                // Notify completion
                if (showLoadingDialog) 
                {
                    AddressableDownloadHudNotifier.NotifyDownloadCompleted($"Batch ({keys.Count} items)");
                }
        
                return result;
            }
            catch (Exception ex)
            {
                if (showLoadingDialog) 
                {
                    AddressableDownloadHudNotifier.NotifyDownloadFailed($"Batch ({keys.Count} items)", ex.Message);
                }
                throw;
            }
        }

        /// <summary>
        /// Downloads all required bundles for the scene <paramref name="key"/>,
        /// then loads the scene in the specified <paramref name="loadMode"/>.
        /// </summary>
        /// <param name="key">Addressable key or label of the scene.</param>
        /// <param name="loadMode">Whether to load the scene additively or single.</param>
        /// <param name="priority">Download priority level.</param>
        /// <returns>
        /// A <see cref="Task{SceneInstance}"/> that completes with the loaded scene instance
        /// once download and scene load both succeed.
        /// </returns>
        public async Task<SceneInstance> DownloadToLoadSceneAsync(
            string key,
            LoadSceneMode loadMode = LoadSceneMode.Additive,
            DownloadPriority priority = DownloadPriority.High
        )
        {
            await EnsureCatalogValidAsync();

            return await _downloadManager.QueueSceneDownloadToLoadAsync(key, loadMode, priority);
        }

        /// <summary>
        /// Gets the download progress for the specified addressable key.
        /// </summary>
        /// <param name="key">The addressable key or label to check.</param>
        /// <returns>
        /// A <see cref="Task{Float}"/> that completes with a value between 0 and 1,
        /// representing the fraction of bundles already downloaded.
        /// </returns>
        public async Task<float> GetDownloadStatusAsync(string key)
        {
            try
            {
                // If CDN is not initialized, try basic Addressables check
                if (!_cdnCapabilitiesInitialized || _downloadManager == null)
                {
                    // Check if we're initialized at all
                    if (!_initialized)
                    {
                        return 0f; // Not initialized yet
                    }
                    
                    // Use basic Addressables download size check
                    try
                    {
                        var sizeOp = Addressables.GetDownloadSizeAsync(key);
                        await sizeOp.Task;
                        
                        if (sizeOp.Status == AsyncOperationStatus.Succeeded)
                        {
                            long bytes = sizeOp.Result;
                            Addressables.Release(sizeOp);
                            // If no download needed, it's already cached (100%)
                            return bytes == 0 ? 1f : 0f;
                        }
                        
                        Addressables.Release(sizeOp);
                        return 0f;
                    }
                    catch
                    {
                        return 0f;
                    }
                }

                // Get actual download status from enhanced download manager
                float status = await _downloadManager.GetDownloadStatusAsync(key);
                return Mathf.Clamp01(status);
            }
            catch (Exception ex)
            {
                // Log the error but don't throw - return a safe value
                DLM.LogWarning(_loggingFeatureFlag, $"Error getting download status for {key}: {ex.Message}");
                
                // Return 1f to indicate "complete" so progress tracking stops
                return 1f;
            }
        }

        /// <summary>
        /// Enhanced method to check if an asset exists and get its download status:
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public async Task<(bool exists, float progress)> GetAssetDownloadInfoAsync(string key)
        {
            try
            {
                if (!_initialized)
                {
                    return (false, 0f);
                }

                // Use enhanced download manager if available
                if (_cdnCapabilitiesInitialized && _downloadManager != null)
                {
                    var (assetExists, downloadProgress, _) = await _downloadManager.GetAssetInfoAsync(key);
                    return (assetExists, downloadProgress);
                }

                // Fallback to basic Addressables check
                var locationHandle = Addressables.LoadResourceLocationsAsync(key);
                await locationHandle.Task;
        
                bool keyExists = locationHandle.Status == AsyncOperationStatus.Succeeded && 
                                 locationHandle.Result != null && 
                                 locationHandle.Result.Count > 0;
        
                Addressables.Release(locationHandle);
        
                if (!keyExists)
                {
                    return (false, 0f);
                }

                // Get basic download progress
                float assetProgress = await GetDownloadStatusAsync(key);
                return (true, assetProgress);
            }
            catch (Exception ex)
            {
                DLM.LogWarning(_loggingFeatureFlag, $"Error checking asset info for {key}: {ex.Message}");
                return (false, 0f);
            }
        }

        /// <summary>
        /// Enhanced method to get download size information:
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public async Task<long> GetDownloadSizeAsync(string key)
        {
            try
            {
                // Use enhanced download manager if available
                if (_cdnCapabilitiesInitialized && _downloadManager != null)
                {
                    return await _downloadManager.GetDownloadSizeAsync(key);
                }

                // Fallback to basic Addressables size check
                var sizeHandle = Addressables.GetDownloadSizeAsync(key);
                await sizeHandle.Task;
                
                long size = sizeHandle.Status == AsyncOperationStatus.Succeeded ? sizeHandle.Result : 0;
                Addressables.Release(sizeHandle);
                return size;
            }
            catch (Exception ex)
            {
                DLM.LogWarning(_loggingFeatureFlag, $"Error getting download size for {key}: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// Releases a previously downloaded asset from memory.
        /// </summary>
        /// <param name="asset">The asset instance to release.</param>
        public void ReleaseDownloadedAsset(UnityEngine.Object asset)
        {
            if (_cdnCapabilitiesInitialized || _downloadManager != null)
            {
                _downloadManager.ReleaseAsset(asset);
            }
        }

        /// <summary>
        /// Unloads a previously downloaded and loaded scene instance.
        /// </summary>
        /// <param name="sceneInstance">The <see cref="SceneInstance"/> to unload.</param>
        public Task ReleaseDownloadedSceneAsync(SceneInstance sceneInstance)
        {
            if (_cdnCapabilitiesInitialized)
            {
                return _downloadManager.ReleaseSceneAsync(sceneInstance);
            }
            return Task.CompletedTask;
        }

        /// <summary>
        /// Returns (activeDownloads, queuedDownloads). Always (0,0) here.
        /// </summary>
        public (int activeDownloads, int queuedDownloads) GetQueueStatus()
            => _downloadManager?.GetQueueStatus() ?? (0, 0);

        /// <summary>Cancels all pending (not yet started) downloads.</summary>
        public void CancelAllDownloads()
        {
            if (_cdnCapabilitiesInitialized || _downloadManager != null)
            {
                _downloadManager.CancelAllDownloads();
            }
        }

        /// <summary>Attempts to cancel a pending download for the specified key.</summary>
        public bool CancelDownload(string key)
            => _cdnCapabilitiesInitialized && _downloadManager.CancelDownload(key);

        /// <summary>
        /// Pre-download (cache) a single key, reporting progress.
        /// </summary>
        public Task<bool> PreDownloadAssetAsync(
            string key,
            DownloadPriority priority = DownloadPriority.Normal,
            Action<float> onProgress = null
        )
        {
            if (!_cdnCapabilitiesInitialized || _downloadManager == null)
                return EnsureCatalogValidAsync().ContinueWith(_ => _downloadManager.PreDownloadAssetAsync(key, priority, onProgress))
                    .Unwrap();

            return _downloadManager.PreDownloadAssetAsync(key, priority, onProgress);
        }

        /// <summary>
        /// Pre-download (cache) multiple keys, reporting each key's progress.
        /// </summary>
        public Task<bool> PreDownloadAssetsAsync(
            List<string> keys,
            DownloadPriority priority = DownloadPriority.Normal,
            Action<string, float> onProgress = null
        )
        {
            if (!_cdnCapabilitiesInitialized || _downloadManager == null)
                return EnsureCatalogValidAsync().ContinueWith(_ => _downloadManager.PreDownloadAssetsAsync(keys, priority, onProgress))
                    .Unwrap();

            return _downloadManager.PreDownloadAssetsAsync(keys, priority, onProgress);
        }

        /// <summary>
        /// Checks whether the bundles for the given addressable key are already cached.
        /// </summary>
        public async Task<bool> IsAssetCachedAsync(string key)
        {
            if (!_cdnCapabilitiesInitialized || _cacheManager == null)
            {
                await EnsureCatalogValidAsync();
            }
            return await _cacheManager.IsAssetCachedAsync(key);
        }

        /// <summary>Gets the total size of all data in the Addressables cache.</summary>
        public async Task<long> GetTotalCacheSizeAsync()
        {
            if (!_cdnCapabilitiesInitialized || _cacheManager == null)
            {
                await EnsureCatalogValidAsync();
            }
            return await _cacheManager.GetTotalCacheSizeAsync();
        }

        /// <summary>Clears the cached bundles for a specific key.</summary>
        public async Task<bool> ClearCacheForAssetAsync(string key)
        {
            if (!_cdnCapabilitiesInitialized || _cacheManager == null)
            {
                await EnsureCatalogValidAsync();
            }
            return await _cacheManager.ClearCacheForAssetAsync(key);
        }

        /// <summary>Clears the cached bundles for multiple keys.</summary>
        public async Task<bool> ClearCacheForAssetsAsync(List<string> keys)
        {
            if (!_cdnCapabilitiesInitialized || _cacheManager == null)
            {
                await EnsureCatalogValidAsync();
            }
            return await _cacheManager.ClearCacheForAssetsAsync(keys);
        }

        /// <summary>Clears the entire Addressables cache.</summary>
        public async Task<bool> ClearAllCacheAsync()
        {
            if (!_cdnCapabilitiesInitialized || _cacheManager == null)
            {
                await EnsureCatalogValidAsync();
            }
            return await _cacheManager.ClearAllCacheAsync();
        }

#if UNITY_EDITOR
        /// <summary>Diagnostic: logs status of addressables & CDN subsystems.</summary>
        public void LogAddressablesStatus()
        {
            DLM.Log(_loggingFeatureFlag, "=== ADDRESSABLES STATUS ===");
            DLM.Log(_loggingFeatureFlag, $"CoreInitialized: {_coreInitialized}");
            DLM.Log(_loggingFeatureFlag, $"Initialized: {_initialized}");
            DLM.Log(_loggingFeatureFlag, $"CDN Initialized: {_cdnCapabilitiesInitialized}");
            DLM.Log(_loggingFeatureFlag, $"CDN URL: {_cdnUrl}");
            DLM.Log(_loggingFeatureFlag, "============================");
        }
#endif
    }
}