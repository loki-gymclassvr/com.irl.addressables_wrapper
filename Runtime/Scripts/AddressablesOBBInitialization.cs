using System;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.ResourceManagement.AsyncOperations;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using BugsnagUnity;
using ShovelTools;
using UnityEngine.Networking;

namespace AddressableSystem
{
    /// <summary>
    /// Static utility class that handles Addressables initialization with OBB support.
    /// Sets up InternalIdTransformFunc for proper OBB path resolution and catalog discovery.
    /// Does not handle catalog loading - this is managed by AddressableManager.
    /// </summary>
    public static class AddressablesOBBInitialization
    {
        // Catalog file name constants
        private const string PatchOBBCatalogFileName = "catalog.json";
        private const string MainOBBCatalogFileName = "mainobb_catalog.json";

        // Path construction elements
        private const string JAR_FILE_PREFIX = "jar:file://";
        private const string ASSETS_AA_PATH = "!/assets/aa/";

        // OBB paths
        private static string mainObbPath = null;
        private static string patchObbPath = null;
        private static string mlbObbPath = null;

        // Initialization tracking
        private static bool coreInitialized = false;
        private static bool obbSetupCompleted = false;
        private static bool initializationStarted = false;
        private static TaskCompletionSource<bool> initializationTask = new TaskCompletionSource<bool>();
        
        // Shared StringBuilder for path construction
        private static StringBuilder _pathBuilder = new StringBuilder(256);
        
        // Path transformation cache
        private static Dictionary<string, string> _obbPathByMarker = new Dictionary<string, string>(10);

        /// <summary>
        /// Determines if Addressables core initialization and OBB setup have completed.
        /// </summary>
        public static bool IsInitialized => coreInitialized && obbSetupCompleted;

        /// <summary>
        /// Returns a task that completes when initialization finishes.
        /// The task result will be true if initialization was successful, false otherwise.
        /// </summary>
        /// <returns>Task that completes when initialization is done</returns>
        public static Task<bool> WaitForInitializationAsync()
        {
            if (IsInitialized)
                return Task.FromResult(true);

            if (!initializationStarted)
                InitializeAddressables();

            return initializationTask.Task;
        }

        /// <summary>
        /// Initializes the Addressables system by setting up OBB paths,
        /// assigning the InternalIdTransformFunc, and starting the initialization sequence.
        /// </summary>
        public static void InitializeAddressables()
        {
            if (initializationStarted)
                return;

            initializationStarted = true;

            // Set up OBB paths before anything else
            FindObbPaths();
            
            InternalIdTransformer.InitializeObbPaths(patchObbPath, mainObbPath, mlbObbPath);
            
            Addressables.InternalIdTransformFunc = loc => InternalIdTransformer.Transform(loc);

            if (DLM.ShouldLog)
            {
                DLM.Log(DLM.FeatureFlags.Addressables,
                    "[AddressableInitializer]: Addressables.InternalIdTransformFunc assigned.");

                if (!string.IsNullOrEmpty(mainObbPath))
                {
                    DLM.Log(DLM.FeatureFlags.Addressables, $"[AddressableInitializer]: Using Main OBB: {mainObbPath}");
                }

                if (!string.IsNullOrEmpty(patchObbPath))
                {
                    DLM.Log(DLM.FeatureFlags.Addressables,
                        $"[AddressableInitializer]: Using Patch OBB: {patchObbPath}");
                }
                else
                {
#if UNITY_ANDROID && !UNITY_EDITOR
                DLM.LogWarning(DLM.FeatureFlags.Addressables, "[AddressableInitializer]: Patch OBB path was not found. Patch content may fail to load.");
#else
                    DLM.Log(DLM.FeatureFlags.Addressables,
                        "[AddressableInitializer]: Not on Android device or no Patch OBB found. Patch path is null.");
#endif
                }
            }

            obbSetupCompleted = true;
            initializationTask.SetResult(true);
        }

        /// <summary>
        /// Gets a list of valid OBB catalogs that have been verified to exist.
        /// This method checks both patch and main OBB catalogs using UnityWebRequest.
        /// </summary>
        /// <returns>Task that resolves to a list of valid catalog paths</returns>
        public static async Task<List<string>> GetValidatedObbCatalogsAsync()
        {
            // Create a list to hold both catalog paths
            List<string> validCatalogs = new List<string>(2); // Pre-allocate capacity
            
            #if UNITY_ANDROID && !UNITY_EDITOR
            if (!string.IsNullOrEmpty(patchObbPath))
            {
                string patchCatalogPath = GetCatalogPathFromObb(patchObbPath, false);
                
                // Check if catalog exists using UnityWebRequest
                bool patchExists = await DoesCatalogExistAsync(patchCatalogPath);
                if (patchExists)
                {
                    validCatalogs.Add(patchCatalogPath);
                    if (DLM.ShouldLog) DLM.Log(DLM.FeatureFlags.Addressables, $"[AddressableInitializer]: Found valid patch catalog: {patchCatalogPath}");
                }
                else
                {
                    if (DLM.ShouldLog) DLM.LogWarning(DLM.FeatureFlags.Addressables, $"[AddressableInitializer]: Patch catalog not found at path: {patchCatalogPath}");
                }
            }
            
            if (!string.IsNullOrEmpty(mainObbPath))
            {
                string mainCatalogPath = GetCatalogPathFromObb(mainObbPath, true);
                
                // Check if catalog exists using UnityWebRequest
                bool mainExists = await DoesCatalogExistAsync(mainCatalogPath);
                if (mainExists)
                {
                    validCatalogs.Add(mainCatalogPath);
                    if (DLM.ShouldLog) DLM.Log(DLM.FeatureFlags.Addressables, $"[AddressableInitializer]: Found valid main catalog: {mainCatalogPath}");
                }
                else
                {
                    if (DLM.ShouldLog) DLM.LogWarning(DLM.FeatureFlags.Addressables, $"[AddressableInitializer]: Main catalog not found at path: {mainCatalogPath}");
                }
            }
            #endif
            
            return validCatalogs;
        }

        /// <summary>
        /// Asynchronously checks if a catalog file exists at the given path using a simple UnityWebRequest.
        /// </summary>
        /// <param name="catalogPath">Path to the catalog to check</param>
        /// <returns>Task with bool result indicating if catalog exists</returns>
        private static async Task<bool> DoesCatalogExistAsync(string catalogPath)
        {
            try
            {
                if (string.IsNullOrEmpty(catalogPath))
                {
                    if (DLM.ShouldLog)
                        DLM.LogWarning(DLM.FeatureFlags.Addressables,
                            "[AddressableInitializer]: Empty catalog path provided");
                    return false;
                }

                if (DLM.ShouldLog)
                    DLM.Log(DLM.FeatureFlags.Addressables,
                        $"[AddressableInitializer]: Checking if catalog exists at: {catalogPath}");

                // Use a regular GET request - simpler approach that works with jar: URLs
                using (UnityEngine.Networking.UnityWebRequest request = 
                       UnityEngine.Networking.UnityWebRequest.Get(catalogPath))
                {
                    // Send request and await completion
                    var asyncOperation = request.SendWebRequest();

                    // Wait for completion using Task
                    while (!asyncOperation.isDone)
                    {
                        await Task.Yield();
                    }

                    // Check if request was successful and returned data
                    bool success = !request.isNetworkError && !request.isHttpError && 
                                  !string.IsNullOrEmpty(request.downloadHandler.text);

                    if (DLM.ShouldLog)
                    {
                        if (success)
                        {
                            DLM.Log(DLM.FeatureFlags.Addressables,
                                $"[AddressableInitializer]: Catalog found at path: {catalogPath}");
                        }
                        else
                        {
                            DLM.LogWarning(DLM.FeatureFlags.Addressables,
                                $"[AddressableInitializer]: Catalog not found at path: {catalogPath}. " +
                                $"Error: {request.error}, Code: {request.responseCode}");
                        }
                    }

                    return success;
                }
            }
            catch (System.Exception e)
            {
                if (DLM.ShouldLog)
                    DLM.LogError(DLM.FeatureFlags.Addressables,
                        $"[AddressableInitializer]: Error checking if catalog exists: {e.Message}\n{e.StackTrace}");
                return false;
            }
        }

        /// <summary>
        /// Constructs the appropriate catalog path based on OBB type.
        /// Optimized to use StringBuilder and avoid string allocations.
        /// </summary>
        /// <param name="obbPath">Path to the OBB file</param>
        /// <param name="isMainObb">Whether this is a main OBB or not</param>
        /// <returns>Full path to the catalog file within the OBB</returns>
        private static string GetCatalogPathFromObb(string obbPath, bool isMainObb)
        {
            _pathBuilder.Length = 0;
            _pathBuilder.Append(JAR_FILE_PREFIX)
                        .Append(obbPath)
                        .Append(ASSETS_AA_PATH);
            
            if (isMainObb)
            {
                _pathBuilder.Append(MainOBBCatalogFileName);
            }
            else
            {
                _pathBuilder.Append(PatchOBBCatalogFileName);
            }
            
            return _pathBuilder.ToString();
        }

        /// <summary>
        /// Locates and identifies the main and patch OBB files on Android devices.
        /// Selects the appropriate version-specific OBB if available.
        /// </summary>
        private static void FindObbPaths()
        {
        #if UNITY_ANDROID && !UNITY_EDITOR
        mainObbPath = null;
        patchObbPath = null;
        mlbObbPath = null;
        
        try
        {
            string packageName = Application.identifier;
            int versionCode = GetBundleVersionCode();

            AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            AndroidJavaObject currentActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            AndroidJavaClass environment = new AndroidJavaClass("android.os.Environment");
            string storagePath = environment.CallStatic<AndroidJavaObject>("getExternalStorageDirectory").Call<string>("getAbsolutePath");
            string obbDirectory = Path.Combine(storagePath, "Android/obb", packageName);

            if(DLM.ShouldLog) DLM.Log(DLM.FeatureFlags.Addressables, $"[AddressableInitializer]: Looking for OBB in: {obbDirectory}");

            if (Directory.Exists(obbDirectory))
            {
                string[] files = Directory.GetFiles(obbDirectory);
                if(DLM.ShouldLog) DLM.Log(DLM.FeatureFlags.Addressables, $"[AddressableInitializer]: Found {files.Length} files in OBB directory");
                
                // Pre-allocate collection sizes
                List<string> mlbObbFiles = new List<string>(files.Length);
                List<string> mainObbFiles = new List<string>(files.Length);
                List<string> patchObbFiles = new List<string>(files.Length);
                
                // Manual filtering to avoid LINQ allocations
                for (int i = 0; i < files.Length; i++)
                {
                    string fileName = Path.GetFileName(files[i]);
                    
                    if (fileName.EndsWith(".obb"))
                    {
                        if (fileName.StartsWith("mlb."))
                        {
                            mlbObbFiles.Add(files[i]);
                        }
                        else if (fileName.StartsWith("main."))
                        {
                            mainObbFiles.Add(files[i]);
                        }
                        else if (fileName.StartsWith("patch."))
                        {
                            patchObbFiles.Add(files[i]);
                        }
                    }
                }

                if (DLM.ShouldLog) 
                {
                    DLM.Log(DLM.FeatureFlags.Addressables, $"[AddressableInitializer]: Found {mainObbFiles.Count} main OBB files.");
                    DLM.Log(DLM.FeatureFlags.Addressables, $"[AddressableInitializer]: Found {mlbObbFiles.Count} mlb OBB files.");
                    DLM.Log(DLM.FeatureFlags.Addressables, $"[AddressableInitializer]: Found {patchObbFiles.Count} patch OBB files.");
                }

                // Find version-specific OBB file helper method to reduce code duplication
                string FindVersionSpecificObb(List<string> obbFiles, string obbType)
                {
                    if (obbFiles.Count == 0)
                        return null;
                        
                    string versionMarker = $".{versionCode}.";
                    
                    // Manual search instead of LINQ
                    for (int i = 0; i < obbFiles.Count; i++)
                    {
                        if (Path.GetFileName(obbFiles[i]).IndexOf(versionMarker, StringComparison.Ordinal) >= 0)
                        {
                            if (DLM.ShouldLog) DLM.Log(DLM.FeatureFlags.Addressables, 
                                $"[AddressableInitializer]: Using version-specific {obbType} OBB (v{versionCode}): {obbFiles[i]}");
                            return obbFiles[i];
                        }
                    }
                    
                    // Fallback to first
                    if (DLM.ShouldLog) DLM.LogWarning(DLM.FeatureFlags.Addressables, 
                        $"[AddressableInitializer]: No {obbType} OBB found for version code {versionCode}. Using fallback: {obbFiles[0]}");
                    return obbFiles[0];
                }
                
                // Process main OBB
                if (mainObbFiles.Count > 0)
                {
                    mainObbPath = FindVersionSpecificObb(mainObbFiles, "main");
                }
                else if (DLM.ShouldLog)
                {
                    DLM.LogWarning(DLM.FeatureFlags.Addressables, $"[AddressableInitializer]: No main OBB files found in {obbDirectory}!");
                }
                
                // Process mlb OBB
                if (mlbObbFiles.Count > 0)
                {
                    mlbObbPath = FindVersionSpecificObb(mlbObbFiles, "mlb");
                }
                else if (DLM.ShouldLog)
                {
                    DLM.LogWarning(DLM.FeatureFlags.Addressables, $"[AddressableInitializer]: No MLB OBB files found in {obbDirectory}!");
                }

                // Process patch OBB
                if (patchObbFiles.Count > 0)
                {
                    patchObbPath = FindVersionSpecificObb(patchObbFiles, "patch");
                }
                else if (DLM.ShouldLog)
                {
                    DLM.LogWarning(DLM.FeatureFlags.Addressables, $"[AddressableInitializer]: No patch OBB files found in {obbDirectory}!");
                }
            }
            else
            {
                if (DLM.ShouldLog) DLM.LogError(DLM.FeatureFlags.Addressables, $"[AddressableInitializer]: OBB directory not found: {obbDirectory}");
                Bugsnag.Notify(new System.Exception($"[AddressableInitializer]: OBB directory not found: {obbDirectory} for version {versionCode}."));
            }
        }
        catch (System.Exception e)
        {
            mainObbPath = null;
            patchObbPath = null;
            mlbObbPath = null;
            if (DLM.ShouldLog) DLM.LogError(DLM.FeatureFlags.Addressables, $"[AddressableInitializer]: Error finding OBB path: {e.Message}\n{e.StackTrace}");
            Bugsnag.Notify(new System.Exception($"[AddressableInitializer]: Error finding OBB path: {e.Message}", e));
        }
        #else
            mainObbPath = null;
            patchObbPath = null;
            mlbObbPath = null;
        #endif
        }

        /// <summary>
        /// Gets the application's bundle version code on Android devices.
        /// Used to match OBB files with the correct version.
        /// </summary>
        /// <returns>Bundle version code as an integer, or 0 on non-Android platforms</returns>
        public static int GetBundleVersionCode()
        {
            #if UNITY_ANDROID && !UNITY_EDITOR
            using (var activityClass = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var context = activityClass.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var packageManager = context.Call<AndroidJavaObject>("getPackageManager"))
            using (var packageName = context.Call<AndroidJavaObject>("getPackageName"))
            using (var packageInfo = packageManager.Call<AndroidJavaObject>("getPackageInfo", packageName, 0))
            {
                return packageInfo.Get<int>("versionCode");
            }
            #else
                return 0; // Default for non-Android platforms
            #endif
        }
    }
}