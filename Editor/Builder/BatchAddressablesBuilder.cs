using UnityEngine;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using System;
using System.IO;
using Addressables_Wrapper.Editor.CDN;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;

namespace Addressables_Wrapper.Editor
{
    /// <summary>
    /// Provides command-line/batch mode building capabilities for Addressables
    /// </summary>
    public static class BatchAddressablesBuilder
    {
        /// <summary>
        /// Entry point for customizable batch mode builds with command-line parameters
        /// </summary>
        public static void BuildAddressablesWithParams()
        {
            try
            {
                Debug.Log("[Addressables Build]: Starting BatchAddressablesBuilder.BuildAddressablesWithParams()");
                
                // Parse command line arguments 
                string targetDeviceStr = BatchModeUtils.GetCommandLineArgValue("-targetDevice");
                string environmentStr = BatchModeUtils.GetCommandLineArgValue("-environment");
                string releaseVersion = BatchModeUtils.GetCommandLineArgValue("-releaseVersion");
                string catalogVersion = BatchModeUtils.GetCommandLineArgValue("-catalogVersion");
                string addressablesProfile = BatchModeUtils.GetCommandLineArgValue("-addressablesProfile");
                bool assetBundleUpdate = BatchModeUtils.GetBoolArgValue("-assetBundleUpdate", false);
                
                Debug.Log($"[Addressables Build]: Target device: {targetDeviceStr ?? "not specified"}");
                Debug.Log($"[Addressables Build]: Environment: {environmentStr ?? "not specified"}");
                Debug.Log($"[Addressables Build]: Release version: {releaseVersion ?? "not specified"}");
                Debug.Log($"[Addressables Build]: Addressables Profile: {addressablesProfile ?? "not specified"}");
                Debug.Log($"[Addressables Build]: AssetBundle Update Mode: {assetBundleUpdate}");
                
                // Get remote bundles parameter
                bool enableRemoteBundles = BatchModeUtils.GetBoolArgValue("-enableRemoteBundles", false);
                Debug.Log($"[Addressables Build]: Enable remote bundles: {enableRemoteBundles}");
                
                bool inheritBuildFlag = BatchModeUtils.GetBoolArgValue("-inheritDlc", false);
                Debug.Log($"[Addressables Build]: Inherit DLC/Patch OBB: {inheritBuildFlag}");
                
                // Convert string parameters to enums if provided
                BuildTargetDevice targetDevice = BatchModeUtils.GetEnumArgValue("-targetDevice", BuildTargetDevice.Quest);
                EnvironmentType environment = BatchModeUtils.GetEnumArgValue("-environment", EnvironmentType.Development);
                
                Debug.Log($"[Addressables Build]: Parsed target device enum: {targetDevice}");
                Debug.Log($"[Addressables Build]: Parsed environment enum: {environment}");
                
                // Load and update the AddressableToolData with command-line parameters
                Debug.Log("[Addressables Build]: Loading AddressableToolData...");
                var data = LoadToolData();
                if (data == null)
                {
                    Debug.LogError("Failed to find AddressableToolData. Please create one in your project.");
                    BatchModeUtils.ExitWithCode(false);
                    return;
                }
                
                Debug.Log($"[Addressables Build]: Found AddressableToolData: {AssetDatabase.GetAssetPath(data)}");
                
                // Update the data with command-line values
                data.buildTargetDevice = targetDevice;
                data.environment = environment;
                if (!string.IsNullOrEmpty(releaseVersion))
                {
                    data.releaseVersion = releaseVersion;
                }
                
                Debug.Log($"[Addressables Build]: Updated AddressableToolData - Target: {data.buildTargetDevice}, Environment: {data.environment}, Version: {data.releaseVersion}");
                
                EditorUtility.SetDirty(data);
                AssetDatabase.SaveAssetIfDirty(data);
                
                // Set the active profile if provided
                var settings = AddressableAssetSettingsDefaultObject.Settings;
                if (settings != null)
                {
                    if (enableRemoteBundles)
                    {
                        string projectRoot = Path.GetDirectoryName(Application.dataPath);
                        string catalogFilePath = Path.Combine(projectRoot, "BuildUtils", "latest_catalog_name.txt");
                        if (File.Exists(catalogFilePath))
                        {
                            catalogVersion = File.ReadAllText(catalogFilePath).Trim();
                            Debug.Log($"[Addressables Build]: Loaded catalogVersion from file: {catalogVersion}");
                        }
                        else
                        {
                            Debug.LogWarning($"[Addressables Build]: latest_catalog_name.txt not found at {catalogFilePath}, using command-line catalogVersion: {catalogVersion}");
                        }
                    }
                    
                    Debug.Log($"[Addressables Build]: Setting Addressables profile. Profile: {addressablesProfile}, Target: {targetDevice}, Environment: {environment}");
                    BatchModeUtils.SetAddressablesProfile(settings, addressablesProfile, targetDevice, environment);
                    
                    // Update the profile's Remote Load Path with the version if provided
                    if (!string.IsNullOrEmpty(releaseVersion))
                    {
                        Debug.Log($"[Addressables Build]: Updating Remote Load Path with version: {releaseVersion}");
                        UpdateProfileRemotePaths(settings, releaseVersion, catalogVersion, targetDevice, environment);
                    }
                }
                else
                {
                    Debug.LogWarning("AddressableAssetSettings not found. Unable to set profile.");
                }
                
                Debug.Log("[Addressables Build]: Overriding Addressable settings...");
                AddressablesEditorManager.OverrideAddressableSettings(enableRemoteBundles);

                // Exclude assets based on platform/environment
                if (data != null)
                {
                    var excluded = AddressableExclusionUtility.ApplyPlatformExclusions((TargetDevice)data.buildTargetDevice, data.environment);
                    if (excluded.Count > 0)
                    {
                        Debug.Log($"[Addressables Build]: Excluded {excluded.Count} assets for platform {data.buildTargetDevice} and environment {data.environment}.\n" + string.Join("\n", excluded));
                    }
                    else
                    {
                        Debug.Log("[Addressables Build] No assets excluded for this platform/environment.");
                    }

                    // Remove empty groups after exclusion
                    AddressablesEditorManager.DeleteEmptyGroups();
                }
                
                Debug.Log("[Addressables Build]: Cleaning remote addressables...");
                AddressablesEditorManager.CleanRemoteAddressables();
                
                // Debug.Log("[Addressables Build]: Running duplicate isolation using analyze...");
                // AddressablesEditorManager.IsolateDuplicatesUsingAnalyze();
                
                // Update the remote/local settings if specified
                if (enableRemoteBundles)
                {
                    Debug.Log("[Addressables Build]: Configuring groups for remote bundles...");
                    AddressablesEditorManager.RunGroupConfig();
                }
                else
                {
                    Debug.Log("[Addressables Build]: Making all groups local...");
                    AddressablesEditorManager.MakeGroupsLocal();
                }
                
                Debug.Log("[Addressables Build]: Overriding all addressable group settings...");
                AddressableSettingsOverrider.OverrideAllAddressableGroupSettings();

                // if (inheritBuildFlag)
                // {
                //     Debug.Log("[Addressables Build]: Running configuration for inheriting patch OBB...");
                //     //When inheriting patch obb we ignore including the the default group
                //     RunConfigForInheritingPatchOBB();
                // }
                // else
                // {
                //     Debug.Log("[Addressables Build]: Not inheriting patch OBB, all local asset bundles will be loaded from main OBB");
                //     //all local asset bundles will be loaded from main obb
                // }
                
                // 6. Force garbage collection before building
                Debug.Log("[Addressables Build]: Clearing old Addressables build cache...");
                AddressableAssetSettings.CleanPlayerContent();
                
                System.GC.Collect();
                System.GC.WaitForPendingFinalizers();
                System.Threading.Thread.Sleep(500); // Small delay to ensure files are released

                // Replace your existing try-catch block with this:
                try
                {
                    
                    if (assetBundleUpdate)
                    {
                        Console.WriteLine("[Addressables Build]: Running in UPDATE mode – looking for previous content state…");

                        //Compute where we dropped the .bin (next to Library, under BuildUtils)
                        string projectRoot           = Path.GetDirectoryName(Application.dataPath);
                        string buildUtilsRoot        = Path.Combine(projectRoot, "BuildUtils");
                        string platformFolder        = targetDevice == BuildTargetDevice.Quest
                            ? "Android"
                            : "";
                        string binFolder             = string.IsNullOrEmpty(platformFolder)
                            ? buildUtilsRoot
                            : Path.Combine(buildUtilsRoot, platformFolder);
                        string contentStateDataPath  = Path.Combine(binFolder, "addressables_content_state.bin");

                        Console.WriteLine($"[Addressables Build]: Looking for content state file at: {contentStateDataPath}");

                        //If it exists, do an incremental update…
                        if (File.Exists(contentStateDataPath))
                        {
                            Console.WriteLine("[Addressables Build]: Found previous content state file – performing UPDATE build…");
                            var result = ContentUpdateScript.BuildContentUpdate(settings, contentStateDataPath);
                            if (!string.IsNullOrEmpty(result.Error))
                            {
                                Console.WriteLine($"[Addressables Build]: Update FAILED: {result.Error}");
                                Debug.LogError($"Addressables update build failed: {result.Error}");
                                BatchModeUtils.ExitWithCode(false);
                                return;
                            }
                            else
                            {
                                Console.WriteLine("[Addressables Build]: Content update succeeded!");
                            }
                        }
                        //otherwise fall back to a clean build
                        else
                        {
                            Console.WriteLine($"[Addressables Build]: No state file at {contentStateDataPath}; performing full clean build");
                            AddressablesPlayerBuildResult buildResult;
                            AddressableAssetSettings.BuildPlayerContent(out buildResult);
                            if (!string.IsNullOrEmpty(buildResult.Error))
                            {
                                Console.WriteLine($"[Addressables Build]: Clean build FAILED: {buildResult.Error}");
                                Debug.LogError($"Addressables clean build failed: {buildResult.Error}");
                                BatchModeUtils.ExitWithCode(false);
                                return;
                            }
                        }
                    }
                    else
                    {
                        Console.WriteLine("[Addressables Build]: Running in CLEAN build mode…");
                        AddressablesPlayerBuildResult buildResult;
                        AddressableAssetSettings.BuildPlayerContent(out buildResult);
                        if (!string.IsNullOrEmpty(buildResult.Error))
                        {
                            Console.WriteLine($"[Addressables Build]: Clean build FAILED: {buildResult.Error}");
                            Debug.LogError($"Addressables clean build failed: {buildResult.Error}");
                            BatchModeUtils.ExitWithCode(false);
                            return;
                        }
                    }

                    if (data != null)
                    {
                        Console.WriteLine($"[Addressables Build]: Addressable Asset Build complete. Release: {data.releaseVersion} | Environment: {data.environment}");
                    }
                    Debug.Log("[Addressables Build]: Batch mode Addressables build with parameters completed successfully.");
                    BatchModeUtils.ExitWithCode(true);
                }
                catch (Exception e)
                {
                    Debug.LogError($"Failed to build addressable assets: {e.Message}");
                    BatchModeUtils.ExitWithCode(false);
                    return;
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Batch mode Addressables build failed: {e.Message}");
                Debug.LogException(e);
                BatchModeUtils.ExitWithCode(false);
            }
        }

        private static void RunConfigForInheritingPatchOBB()
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings != null)
            {
                //Exclude the Default Group from the build as its inherited patch obb
                var group = settings.FindGroup("Default Local Group");
                if (group == null)
                {
                    Debug.LogWarning("Default Local Group not found. Unable to exclude from build.");
                    return;
                }
            
                Debug.Log("[Addressables Build]: Excluding Default Local Group from build for inherited patch OBB");
            
                // Get or add the BundledAssetGroupSchema
                var bundledSchema = group.GetSchema<BundledAssetGroupSchema>();
                if (bundledSchema != null)
                {
                    Debug.Log($"[Addressables Build]: Setting IncludeInBuild to false for Default Local Group (previous value: {bundledSchema.IncludeInBuild})");
                    bundledSchema.IncludeInBuild = false;
                }
                else
                {
                    Debug.LogWarning("BundledAssetGroupSchema not found on Default Local Group");
                }
            
                EditorUtility.SetDirty(bundledSchema);
            }
            else
            {
                Debug.LogWarning("AddressableAssetSettings not found. Unable to configure for inheriting patch OBB.");
                return;
            }
        }
        
        private static void RenameCatalog(string prefix)
        {
            Debug.Log($"[Addressables Build]: Starting catalog renaming process with prefix: {prefix}");
            
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                Debug.LogError("Addressables settings not found!");
                return;
            }
            
            // Get the build path from the profile
            string buildPath = settings.profileSettings.GetValueByName(settings.activeProfileId, "LocalBuildPath");
            Debug.Log($"[Addressables Build]: Local build path from profile: {buildPath}");
            
            string catalogDirectory = Path.Combine(Application.dataPath, "../Library/com.unity.addressables/aa/Android");
            Debug.Log($"[Addressables Build]: Looking for catalog files in directory: {catalogDirectory}");
            
            // Find the platform subfolder
            if (!Directory.Exists(catalogDirectory))
            {
                Debug.LogError($"Platform build path not found: {catalogDirectory}");
                return;
            }
            
            // Source files
            string catalogJsonPath = Path.Combine(catalogDirectory, "catalog.json");
            string settingsJsonPath = Path.Combine(catalogDirectory, "settings.json");
            
            // Destination files
            string newCatalogName = $"{prefix}_catalog.json";
            string newCatalogJsonPath = Path.Combine(catalogDirectory, newCatalogName);
            string newSettingsJsonPath = Path.Combine(catalogDirectory, $"{prefix}_settings.json");
            
            Debug.Log($"[Addressables Build]: Source catalog file: {catalogJsonPath}");
            Debug.Log($"[Addressables Build]: Source settings file: {settingsJsonPath}");
            Debug.Log($"[Addressables Build]: Target catalog file: {newCatalogJsonPath}");
            Debug.Log($"[Addressables Build]: Target settings file: {newSettingsJsonPath}");
            
            // Check source files exist
            if (!File.Exists(catalogJsonPath))
            {
                Debug.LogError($"Catalog file not found: {catalogJsonPath}");
                return;
            }
            
            if (!File.Exists(settingsJsonPath))
            {
                Debug.LogError($"Settings file not found: {settingsJsonPath}");
                return;
            }
            
            try
            {
                // Rename catalog.json to main_catalog.json
                File.Move(catalogJsonPath, newCatalogJsonPath);
                Debug.Log($"[Addressables Build]: Renamed {catalogJsonPath} to {newCatalogJsonPath}");

                // Rename settings.json to main_settings.json
                File.Move(settingsJsonPath, newSettingsJsonPath);
                Debug.Log($"[Addressables Build]: Renamed {settingsJsonPath} to {newSettingsJsonPath}");
                
                string settingsContent = File.ReadAllText(newSettingsJsonPath);
                Debug.Log("[Addressables Build]: Updating catalog reference in settings file...");
                settingsContent = settingsContent.Replace("catalog.json", newCatalogName);
                
                File.WriteAllText(newSettingsJsonPath, settingsContent);
                Debug.Log("[Addressables Build]: Settings file updated with new catalog name");
                
                Debug.Log($"[Addressables Build]: Successfully renamed catalog files with prefix: {prefix}");
                Debug.Log($"[Addressables Build]: New catalog: {newCatalogJsonPath}");
                Debug.Log($"[Addressables Build]: New settings: {newSettingsJsonPath}");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Error renaming catalog files: {e.Message}");
                Debug.LogException(e);
            }
        }

        /// <summary>
        /// Updates the remote load path in the active profile
        /// </summary>
        private static void UpdateProfileRemotePaths(
            AddressableAssetSettings settings, 
            string releaseVersion, 
            string catalogVersion,
            BuildTargetDevice targetDevice,
            EnvironmentType environment)
        {
            string profileId = settings.activeProfileId;
            string profileName = settings.profileSettings.GetProfileName(profileId);
            
            Debug.Log($"[Addressables Build]: Updating Remote.LoadPath for profile: {profileName}");
            
            // Get CDN Config
            Debug.Log($"[Addressables Build]: Looking for CDN configuration for target device: {targetDevice}");
            CDNConfig cdnConfig = BatchModeUtils.FindCDNConfig(targetDevice);
            
            if (cdnConfig != null)
            {
                Debug.Log($"[Addressables Build]: Found CDN config: {cdnConfig.name}");
                
                // Get the CDN URL
                string cdnUrl = cdnConfig.GetCdnUrl(environment);
                Debug.Log($"[Addressables Build]: CDN URL for environment {environment}: {cdnUrl}");
                
                // Build the remote load path with version
                string remoteLoadPath = $"{cdnUrl}/{releaseVersion}";
                
                Debug.Log($"[Addressables Build]: Setting Remote.LoadPath to: {remoteLoadPath}");
                
                Debug.Log($"[Addressables Build]: Setting Remote.BuildPath to: {cdnConfig.GetFormattedRemotePath(releaseVersion, environment)}");
                
                // Update the remote load path
                settings.profileSettings.SetValue(profileId, "Remote.BuildPath", cdnConfig.GetFormattedRemotePath(releaseVersion, environment));
                
                settings.profileSettings.SetValue(profileId, "Remote.LoadPath", remoteLoadPath);
                
                //Update player version override to patch the catalog name
                settings.OverridePlayerVersion = Path.GetFileNameWithoutExtension(catalogVersion)
                    .Replace("catalog_", "")   // if you only want the timestamp+suffix
                    .Replace("v", "");
                Debug.Log($"[Addressables Build]: Remote catalog name is catalog_{settings.OverridePlayerVersion}.json");
                
                // Save settings
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssetIfDirty(settings);
                Debug.Log("[Addressables Build]: Remote.LoadPath updated successfully");
            }
            else
            {
                Debug.LogWarning($"CDN configuration not found for target device {targetDevice}, unable to update Remote.LoadPath with version");
            }
        }
        
        /// <summary>
        /// Loads the AddressableToolData from the project
        /// </summary>
        private static AddressableToolData LoadToolData()
        {
            Debug.Log("[Addressables Build]: Searching for AddressableToolData assets...");
            // Find all AddressableToolData assets in the project
            string[] guids = AssetDatabase.FindAssets("t:AddressableToolData");
            if (guids.Length == 0)
            {
                Debug.LogError("No AddressableToolData found in project. Please create one first.");
                return null;
            }
            
            Debug.Log($"[Addressables Build]: Found {guids.Length} AddressableToolData asset(s)");
            
            // Load the first found instance
            string path = AssetDatabase.GUIDToAssetPath(guids[0]);
            Debug.Log($"[Addressables Build]: Loading AddressableToolData from path: {path}");
            
            var data = AssetDatabase.LoadAssetAtPath<AddressableToolData>(path);
            
            if (data == null)
            {
                Debug.LogError($"Failed to load AddressableToolData at path: {path}");
                return null;
            }
            
            Debug.Log($"[Addressables Build]: Successfully loaded AddressableToolData: {data.name}");
            return data;
        }
    }
}