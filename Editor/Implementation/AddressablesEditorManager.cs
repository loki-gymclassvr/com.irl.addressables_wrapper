using UnityEngine;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Addressables_Wrapper.Editor; // For AddressableExclusionUtility
using Addressables_Wrapper.Editor.CDN;
using UnityEditor.AddressableAssets.Build.AnalyzeRules;

namespace Addressables_Wrapper.Editor
{
    /// <summary>
    /// Main manager class for Addressables building and management
    /// </summary>
    public class AddressablesEditorManager
    {
        /// <summary>
        /// Deletes all empty addressable groups (except "Built In Data")
        /// </summary>
        public static void DeleteEmptyGroups()
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                Debug.LogError("[Addressables Build] AddressableAssetSettings not found!");
                return;
            }

            var emptyGroups = settings.groups
                .Where(g => g != null && g.entries.Count == 0 && g.Name != "Built In Data")
                .ToList();

            foreach (var group in emptyGroups)
            {
                Debug.Log($"[Addressables Build] Removing empty group: {group.Name}");
                settings.RemoveGroup(group);
                settings.SetDirty(AddressableAssetSettings.ModificationEvent.GroupRemoved, group, true, false);
            }

            if (emptyGroups.Count > 0)
            {
                AssetDatabase.SaveAssetIfDirty(settings);
                Debug.Log($"[Addressables Build] Deleted {emptyGroups.Count} empty addressable groups.");
            }
        }

        #region Menu Items

        //[MenuItem("Tools/Addressables/Build Addressables")]
        public static void BuildAddressablesMenuItem()
        {
            // Create manager and build
            BuildAddressables();
        }

        //[MenuItem("Tools/Addressables/Update Addressables")]
        public static void UpdateAddressablesMenuItem()
        {
            // Find and load the configuration data
            var data = LoadToolData();
            if (data == null)
            {
                Debug.LogError("[Addressables Build] Failed to find AddressableToolData. Please create one in your project.");
                return;
            }

            // Create manager and update
            UpdateAddressables(data);
        }

        //[MenuItem("Tools/Addressables/Set Groups to Remote")]
        public static void SetGroupsToRemote()
        {
            MakeGroupsRemote();
            Debug.Log("[Addressables Build] All addressable groups have been set to remote.");
        }

        //[MenuItem("Tools/Addressables/Set Groups to Local")]
        public static void SetGroupsToLocal()
        {
            var data = LoadToolData();
            if (data == null)
            {
                Debug.LogError("[Addressables Build] Failed to find AddressableToolData. Please create one in your project.");
                return;
            }

            MakeGroupsLocal();
            Debug.Log("[Addressables Build] All addressable groups have been set to local.");
        }

        [MenuItem("Tools/Addressables/Isolate Duplicate Assets")]
        public static void IsolateDuplicateAssets()
        {
            IsolateDuplicatesUsingAnalyze();
            Debug.Log("[Addressables Build] Duplicate assets have been isolated.");
        }

        #endregion

        #region Core Build Process

        /// <summary>
        /// Loads the AddressableToolData from the project
        /// </summary>
        private static AddressableToolData LoadToolData()
        {
            // Find all AddressableToolData assets in the project
            string[] guids = AssetDatabase.FindAssets("t:AddressableToolData");
            if (guids.Length == 0)
            {
                Debug.LogError("[Addressables Build] No AddressableToolData found in project. Please create one first.");
                return null;
            }

            // Load the first found instance
            string path = AssetDatabase.GUIDToAssetPath(guids[0]);
            var data = AssetDatabase.LoadAssetAtPath<AddressableToolData>(path);

            if (data == null)
            {
                Debug.LogError($"[Addressables Build] Failed to load AddressableToolData at path: {path}");
                return null;
            }

            return data;
        }

        #endregion

        #region IAddressableBuilder Implementation

        /// <summary>
        /// Main build method that orchestrates the entire build process
        /// </summary>
        public static void BuildAddressables()
        {
            // 2. Update profile
            UpdateProfile();

            // 3. Override addressable and group settings
            OverrideAddressableSettings();

            // 4. Isolate duplicate assets
            IsolateDuplicatesUsingAnalyze();

            // 5. Process group configurations
            RunGroupConfig();

            // 5.5. Exclude assets based on platform/environment
            var toolData = LoadToolData();
            if (toolData != null)
            {
                var excluded = AddressableExclusionUtility.ApplyPlatformExclusions((TargetDevice)toolData.buildTargetDevice, toolData.environment);
                if (excluded.Count > 0)
                {
                    Debug.Log($"[Addressables Build]: Excluded {excluded.Count} assets for platform {toolData.buildTargetDevice} and environment {toolData.environment}.\n" + string.Join("\n", excluded));
                }
                else
                {
                    Debug.Log("[Addressables Build] No assets excluded for this platform/environment.");
                }
            }

            CleanRemoteAddressables();

            // 6. Force garbage collection before building
            System.GC.Collect();
            System.GC.WaitForPendingFinalizers();
            System.Threading.Thread.Sleep(500); // Small delay to ensure files are released

            // 6. Build addressables
            try
            {
                Debug.Log("[Addressables Build] Clearing old Addressables build cache...");
                AddressableAssetSettings.CleanPlayerContent();
                Debug.Log("[Addressables Build] Building Addressable Asset content...");
                AddressableAssetSettings.BuildPlayerContent();

                var data = LoadToolData();
                if (data != null)
                {
                    Debug.Log($"[Addressables Build] Addressable Asset Build complete. Release: {data.releaseVersion} | Environment: {data.environment}");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[Addressables Build] Failed to build addressable assets: {e.Message}");
            }
        }

        /// <summary>
        /// Delete all previous build generated remote assetbundles
        /// </summary>
        public static void CleanRemoteAddressables()
        {
            var remotePath = Application.dataPath.Replace("Assets", "ServerData");

            if (Directory.Exists(remotePath))
            {
                Directory.Delete(remotePath, true);
            }
            else
            {
                Debug.LogWarning("[Addressables Build] Remote AssetBundle Directory doesn't exist.");
            }
        }

        /// <summary>
        /// Updates existing addressables with new content
        /// </summary>
        public static void UpdateAddressables(AddressableToolData data)
        {
            Debug.Log($"[Addressables Build] Starting Addressable Asset Update... Release: {data.releaseVersion} | Environment: {data.environment}");

            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                Debug.LogError("[Addressables Build] AddressableAssetSettings not found!");
                return;
            }

            try
            {
                AddressableAssetSettings.CleanPlayerContent();
                AddressableAssetSettings.BuildPlayerContent();
                Debug.Log($"[Addressables Build] Addressable Asset Update complete. Release: {data.releaseVersion} | Environment: {data.environment}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[Addressables Build] Failed to update addressable assets: {e.Message}");
            }
        }

        /// <summary>
        /// Applies “remote vs local” logic to each group based on current buildTargetDevice.
        /// Looks at groupConfigurations → targetDevices + remoteDevices flags.
        /// </summary>
        public static void RunGroupConfig()
        {
            // Set only the relevant groups to remote based on current buildTargetDevice
            var data = LoadToolData();
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (data != null)
            {
                bool hasGroupConfigs = data.groupConfigurations != null && data.groupConfigurations.Count > 0;
                bool hasMatchingGroups = false;

                // If we have group configurations, set remote only for matching target device
                if (hasGroupConfigs)
                {
                    Debug.Log($"[Addressables Build] Setting selected groups to remote for target device: {data.buildTargetDevice}");

                    if (settings != null)
                    {
                        // Look for group configurations for the current target device
                        foreach (var groupConfig in data.groupConfigurations)
                        {
                            // Only process groups that should be included in the build for this target device
                            var buildTargetFlag = (TargetDevice)data.buildTargetDevice;
                            if ((groupConfig.targetDevices & buildTargetFlag) != 0)
                            {
                                // This group is targeted for the current device and included in build
                                hasMatchingGroups = true;

                                var group = settings.FindGroup(groupConfig.groupName);
                                if (group != null)
                                {
                                    // Create temporary group config with remote set for just this device
                                    var tempConfig = new GroupConfiguration
                                    {
                                        assets = groupConfig.assets,
                                        groupName = groupConfig.groupName,
                                        labelAssignments = groupConfig.labelAssignments,
                                        targetDevices = buildTargetFlag,
                                        remoteDevices = groupConfig.remoteDevices,
                                        bundleMode = groupConfig.bundleMode
                                    };

                                    // Update just this group
                                    UpdateAddressableGroup(tempConfig);
                                }
                            }
                        }
                    }
                }

                // If no specific group configs were found for the target device, default to making all groups remote
                if (!hasGroupConfigs || !hasMatchingGroups)
                {
                    Debug.Log($"[Addressables Build] No specific group configurations found for {data.buildTargetDevice}. Setting all groups to remote.");
                    MakeGroupsRemote();
                }

                // Group Exclusion Logic
                // Remove groups not included for this build device
                if (settings != null)
                {
                    // Gather names of included groups for this build
                    var includedGroupNames = new HashSet<string>();
                    if (hasGroupConfigs)
                    {
                        var buildTargetFlag = (TargetDevice)data.buildTargetDevice;
                        foreach (var groupConfig in data.groupConfigurations)
                        {
                            if ((groupConfig.targetDevices & buildTargetFlag) != 0)
                                includedGroupNames.Add(groupConfig.groupName);
                        }
                    }

                    // Set IncludeInBuild flag for all non-built-in groups
                    foreach (var group in settings.groups)
                    {
                        if (group == null) continue;
                        if (group.Name == "Built In Data") continue;
                        var schema = group.GetSchema<BundledAssetGroupSchema>();
                        if (schema == null) continue;
                        if (includedGroupNames.Contains(group.Name))
                        {
                            if (!schema.IncludeInBuild)
                            {
                                schema.IncludeInBuild = true;
                                Debug.Log($"[Addressables Build]: Marked group as included in build: {group.Name}");
                                EditorUtility.SetDirty(schema);
                            }
                        }
                        else
                        {
                            if (schema.IncludeInBuild)
                            {
                                schema.IncludeInBuild = false;
                                Debug.Log($"[Addressables Build]: Marked group as EXCLUDED from build: {group.Name}");
                                EditorUtility.SetDirty(schema);
                            }
                        }
                    }
                    EditorUtility.SetDirty(settings);
                }
            }
            else
            {
                // Fallback to legacy method if no data is available
                Debug.LogWarning("[Addressables Build] No AddressableToolData found, falling back to setting all groups remote");
                MakeGroupsLocal();
            }
        }

        /// <summary>
        /// Creates or updates addressable groups based on configurations
        /// </summary>
        public static void CreateAddressableGroups(AddressableToolData data)
        {
            Debug.Log("[Addressables Build] Processing group configurations...");

            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                Debug.LogError("[Addressables Build] AddressableAssetSettings not found! Please create Addressable Asset Settings in your project.");
                return;
            }

            if (data.groupConfigurations == null || data.groupConfigurations.Count == 0)
            {
                Debug.Log("[Addressables Build] No group configurations found. Proceeding with default settings.");
                return;
            }

            foreach (var groupConfig in data.groupConfigurations)
            {
                CreateAddressableGroup(groupConfig);
            }

            EditorUtility.SetDirty(settings);
            Debug.Log("[Addressables Build] Addressable groups created/updated successfully.");
        }

        /// <summary>
        /// Creates a new addressable group or updates an existing one
        /// </summary>
        public static void CreateAddressableGroup(GroupConfiguration groupConfig)
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                Debug.LogError("[Addressables Build] AddressableAssetSettings not found! Please create Addressable Asset Settings in your project.");
                return;
            }

            // Skip empty or invalid configurations
            if (string.IsNullOrEmpty(groupConfig.groupName))
            {
                Debug.LogWarning("[Addressables Build] Cannot create/update group with empty name.");
                return;
            }

            // Get or create the group
            var group = settings.FindGroup(groupConfig.groupName);
            if (group == null)
            {
                group = settings.CreateGroup(
                    groupConfig.groupName,
                    false,
                    false,
                    true,
                    null,
                    typeof(BundledAssetGroupSchema),
                    typeof(ContentUpdateGroupSchema)
                );

                Debug.Log($"[Addressables Build] Created new addressable group: {groupConfig.groupName}");
            }

            // Update the group settings
            UpdateAddressableGroup(groupConfig);
        }

        /// <summary>
        /// Updates an existing addressable group with new settings
        /// </summary>
        public static void UpdateAddressableGroup(GroupConfiguration groupConfig)
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                Debug.LogError("[Addressables Build] AddressableAssetSettings not found! Please create Addressable Asset Settings in your project.");
                return;
            }

            // Find the group
            var group = settings.FindGroup(groupConfig.groupName);
            if (group == null)
            {
                Debug.LogError($"[Addressables Build] Addressable group '{groupConfig.groupName}' not found!");
                return;
            }

            // Update the group schema settings
            UpdateGroupSchemaSettings(settings, group, groupConfig);

            // Add or update assets in the group
            if (groupConfig.assets != null)
            {
                foreach (var assetRef in groupConfig.assets)
                {
                    if (assetRef == null || string.IsNullOrEmpty(assetRef.guid) || string.IsNullOrEmpty(assetRef.assetPath))
                        continue;

                    var entry = settings.CreateOrMoveEntry(assetRef.guid, group, false);
                    if (entry == null)
                        continue;

                    // Apply labels
                    if (groupConfig.labelAssignments != null)
                    {
                        foreach (var label in groupConfig.labelAssignments)
                        {
                            if (!string.IsNullOrEmpty(label))
                            {
                                settings.AddLabel(label, true);
                            }
                            entry.SetLabel(label, true, true, true);
                        }
                    }
                    settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryModified, entry, true);
                }
            }
            Debug.Log($"[Addressables Build] Updated addressable group: {groupConfig.groupName}");
        }

        /// <summary>
        /// Updates the profile based on buildTargetDevice and environment
        /// </summary>
        public static void UpdateProfile()
        {
            var data = LoadToolData();
            if (data == null)
                return;

            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
                return;

            var cdnConfig = AssetDatabase.LoadAssetAtPath<CDNConfig>(
                $"Assets/Editor/AddressableTool/Data/{data.buildTargetDevice.ToString()}-CDNConfig.asset"
            );
            if (cdnConfig == null)
                return;

            if (cdnConfig.localHostEnabled)
                return;

            string targetProfileName = $"{data.buildTargetDevice.ToString().ToLower()}-{data.environment.ToString().ToLower()}";

            // Get profile ID by name (or create if it doesn't exist)
            string targetProfileId = settings.profileSettings.GetProfileId(targetProfileName);

            if (string.IsNullOrEmpty(targetProfileId))
            {
                // Profile doesn't exist, create it
                targetProfileId = settings.profileSettings.AddProfile(targetProfileName, settings.activeProfileId);
                if (string.IsNullOrEmpty(targetProfileId))
                {
                    EditorUtility.DisplayDialog("Error",
                        $"Failed to create profile: {targetProfileName}", "OK");
                    return;
                }
            }

            // Set the found/created profile as active
            settings.activeProfileId = targetProfileId;

            string remotePath = string.Empty;
            if (data.environment == EnvironmentType.Development)
            {
                remotePath = cdnConfig.developmentCdnUrl;
            }
            else if (data.environment == EnvironmentType.Staging)
            {
                remotePath = cdnConfig.stagingCdnUrl;
            }
            else if (data.environment == EnvironmentType.Production)
            {
                remotePath = cdnConfig.productionCdnUrl;
            }

            settings.profileSettings.SetValue(
                targetProfileId,
                "Remote.BuildPath",
                cdnConfig.GetFormattedRemotePath(data.releaseVersion, data.environment)
            );

            settings.profileSettings.SetValue(
                targetProfileId,
                "Remote.LoadPath",
                $"{remotePath}/{data.releaseVersion}"
            );

            // Dirty the settings
            EditorUtility.SetDirty(settings);
        }

        /// <summary>
        /// Removes an addressable group
        /// </summary>
        public static void RemoveAddressableGroup(string groupName)
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                Debug.LogError("[Addressables Build] AddressableAssetSettings not found!");
                return;
            }

            // Find the group
            var group = settings.FindGroup(groupName);
            if (group == null)
            {
                Debug.LogWarning($"Addressable group '{groupName}' not found!");
                return;
            }

            // Remove the group
            settings.RemoveGroup(group);

            EditorUtility.SetDirty(settings);
            Debug.Log($"Removed addressable group: {groupName}");
        }

        /// <summary>
        /// Removes an asset from a group
        /// </summary>
        public static void RemoveAssetFromGroup(string groupName, string assetGuid)
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                Debug.LogError("[Addressables Build] AddressableAssetSettings not found!");
                return;
            }

            // Find the entry for this asset
            var entry = settings.FindAssetEntry(assetGuid);
            if (entry == null)
            {
                Debug.LogWarning($"Asset with GUID '{assetGuid}' not found in addressables!");
                return;
            }

            // Check if the entry is in the specified group
            if (entry.parentGroup.Name != groupName)
            {
                Debug.LogWarning($"Asset is not in group '{groupName}'!");
                return;
            }

            // Remove the entry
            settings.RemoveAssetEntry(assetGuid);

            EditorUtility.SetDirty(settings);
            Debug.Log($"Removed asset from group '{groupName}'");
        }

        /// <summary>
        /// Renames an addressable group
        /// </summary>
        public static void RenameAddressableGroup(string oldName, string newName)
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                Debug.LogError("[Addressables Build] AddressableAssetSettings not found!");
                return;
            }

            // Find the group
            var group = settings.FindGroup(oldName);
            if (group == null)
            {
                Debug.LogWarning($"Addressable group '{oldName}' not found!");
                return;
            }

            // Rename the group
            group.Name = newName;

            EditorUtility.SetDirty(settings);
            Debug.Log($"Renamed addressable group from '{oldName}' to '{newName}'");
        }

        #endregion

        #region Addressable Settings Management

        /// <summary>
        /// Override addressable settings based on configuration data
        /// </summary>
        public static void OverrideAddressableSettings(bool enableRemoteBundles = true)
        {
            AddressableToolData data = LoadToolData();
            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;

            if (settings == null)
            {
                Debug.LogError("AddressableAssetSettings not found! Cannotoverride settings.");
                return;
            }

            Debug.Log("Overriding Addressable settings...");

            // Set build remote catalog based on whether any remote devices are configured
            if (enableRemoteBundles)
            {
                Debug.Log("Enabling RemoteCatalog in Addressable settings...");
                settings.BuildRemoteCatalog = true;
                settings.RemoteCatalogBuildPath.SetVariableByName(settings, "Remote.BuildPath");
                settings.RemoteCatalogLoadPath.SetVariableByName(settings, "Remote.LoadPath");
            }

            // Environment-specific settings
            switch (data.environment)
            {
                case EnvironmentType.Development:
                    // Development-specific settings
                    break;
                case EnvironmentType.Staging:
                    // Staging-specific settings
                    break;
                case EnvironmentType.Production:
                    // Production-specific settings
                    break;
            }

            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssetIfDirty(settings);
        }

        /// <summary>
        /// Makes all groups use remote paths
        /// </summary>
        public static void MakeGroupsRemote()
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                Debug.LogError("[Addressables Build] Addressable Asset Settings not found. Make sure Addressables is set up in the project.");
                return;
            }

            settings.BuildRemoteCatalog = true;

            // Set all groups to remote except built-in ones
            foreach (var group in settings.groups)
            {
                if (group == null || group.Name == "Built In Data")
                {
                    Debug.Log($"[Addressables Build] Skipping built-in group: {group?.Name}");
                    continue;
                }

                var schema = group.GetSchema<BundledAssetGroupSchema>();
                if (schema != null)
                {
                    schema.BuildPath.SetVariableByName(settings, "Remote.BuildPath");
                    schema.LoadPath.SetVariableByName(settings, "Remote.LoadPath");
                    EditorUtility.SetDirty(schema);
                }
                else
                {
                    Debug.LogWarning($"[Addressables Build] Group '{group.Name}' doesn't have a BundledAssetGroupSchema. Skipping.");
                }

                EditorUtility.SetDirty(group);
            }

            EditorUtility.SetDirty(settings);
        }

        /// <summary>
        /// Makes all groups use local paths
        /// </summary>
        public static void MakeGroupsLocal()
        {
            var data = LoadToolData();
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                Debug.LogError("[Addressables Build] Addressable Asset Settings not found. Make sure Addressables is set up in the project.");
                return;
            }

            settings.BuildRemoteCatalog = false;

            // Set all groups to local
            foreach (var group in settings.groups)
            {
                var schema = group.GetSchema<BundledAssetGroupSchema>();
                if (schema != null)
                {
                    schema.BuildPath.SetVariableByName(settings, "Local.BuildPath");
                    schema.LoadPath.SetVariableByName(settings, "Local.LoadPath");
                    EditorUtility.SetDirty(schema);
                    settings.SetDirty(AddressableAssetSettings.ModificationEvent.GroupSchemaModified, schema, true);
                }
                else
                {
                    Debug.LogWarning($"[Addressables Build] Group '{group.Name}' doesn't have a BundledAssetGroupSchema. Skipping.");
                }
            }

            // Update configuration data to reflect local status
            var dataObj = LoadToolData();
            if (dataObj != null && dataObj.groupConfigurations != null)
            {
                foreach (var groupConfig in dataObj.groupConfigurations)
                {
                    groupConfig.remoteDevices = 0;
                }
                EditorUtility.SetDirty(dataObj);
            }

            EditorUtility.SetDirty(settings);
        }

        /// <summary>
        /// Updates the schema settings for a group
        /// </summary>
        private static void UpdateGroupSchemaSettings(
            AddressableAssetSettings settings,
            AddressableAssetGroup group,
            GroupConfiguration config)
        {
            // Get or add the BundledAssetGroupSchema
            var bundledSchema = group.GetSchema<BundledAssetGroupSchema>();
            if (bundledSchema == null)
            {
                bundledSchema = group.AddSchema<BundledAssetGroupSchema>();
            }

            // Configure build and load paths based on remote devices settings
            bool hasRemoteDevices = config.remoteDevices != 0;
            if (hasRemoteDevices)
            {
                bundledSchema.BuildPath.SetVariableByName(settings, "Remote.BuildPath");
                bundledSchema.LoadPath.SetVariableByName(settings, "Remote.LoadPath");
                Debug.Log($"[Addressables Build]: Set group '{config.groupName}' to remote.");
            }
            else
            {
                bundledSchema.BuildPath.SetVariableByName(settings, "Local.BuildPath");
                bundledSchema.LoadPath.SetVariableByName(settings, "Local.LoadPath");
                Debug.Log($"[Addressables Build]: Set group '{config.groupName}' to local.");
            }

            // --- Apply new overrides ---
            // 1. Include in Build (migrated to enum flags)
            // Only include in build if the current build target is in includeInBuildTargets
            var buildTarget = (TargetDevice)UnityEditor.EditorUserBuildSettings.activeBuildTarget;
            // Fallback: try to get build target from config if possible

            // 2. Bundle Mode
            switch (config.bundleMode)
            {
                case BundleMode.PackTogether:
                    bundledSchema.BundleMode = BundledAssetGroupSchema.BundlePackingMode.PackTogether;
                    break;
                case BundleMode.PackSeparately:
                    bundledSchema.BundleMode = BundledAssetGroupSchema.BundlePackingMode.PackSeparately;
                    break;
                case BundleMode.PackByLabel:
                    bundledSchema.BundleMode = BundledAssetGroupSchema.BundlePackingMode.PackTogetherByLabel;
                    break;
            }
            // --- End new overrides ---

            // Get or add the ContentUpdateGroupSchema
            var updateSchema = group.GetSchema<ContentUpdateGroupSchema>();
            if (updateSchema == null)
            {
                updateSchema = group.AddSchema<ContentUpdateGroupSchema>();
            }

            // Configure update schema
            updateSchema.StaticContent = !hasRemoteDevices; // Remote content might change more often

            EditorUtility.SetDirty(bundledSchema);
            AssetDatabase.SaveAssetIfDirty(bundledSchema);
            EditorUtility.SetDirty(updateSchema);
            settings.SetDirty(AddressableAssetSettings.ModificationEvent.GroupSchemaModified, group, true);

            // Enhanced logging: log schema-applied values
            Debug.Log($"[Addressables Build]: Applied schema to group '{group.Name}':\n" +
                $"  Bundle Mode: {bundledSchema.BundleMode}\n" +
                $"  Build Path: {bundledSchema.BuildPath.GetValue(settings)}\n" +
                $"  Load Path: {bundledSchema.LoadPath.GetValue(settings)}\n" +
                $"  Static Content: {updateSchema.StaticContent}");
        }

        #endregion

        #region Duplicate Asset Management

        /// <summary>
        /// Isolates duplicate assets by type using the Addressables analyze system
        /// </summary>
        public static void IsolateDuplicatesUsingAnalyze()
        {
            // Get the AddressableAssetSettings
            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                Debug.LogError("No AddressableAssetSettings found in the project.");
                return;
            }

            // Clean up existing shared groups
            List<AddressableAssetGroup> groupsToRemove = new List<AddressableAssetGroup>();
            foreach (AddressableAssetGroup group in settings.groups)
            {
                if (!group.ReadOnly && group.Name.StartsWith("Shared"))
                    groupsToRemove.Add(group);
            }

            foreach (var grp in groupsToRemove)
            {
                settings.RemoveGroup(grp);
                Debug.Log($"Removed existing shared group: {grp.Name}");
            }

            // Run duplicate analysis using the built-in rule
            CheckBundleDupeDependencies dupeRule = new CheckBundleDupeDependencies();
            List<AnalyzeRule.AnalyzeResult> analysisResults = dupeRule.RefreshAnalysis(settings);
            if (analysisResults == null || analysisResults.Count == 0)
            {
                Debug.Log("No duplicate issues found by analysis.");
                return;
            }

            Debug.Log($"Analysis found {analysisResults.Count} duplicate issue(s).");

            // Process each duplicate result and isolate by asset type
            int moveCount = 0;
            Dictionary<string, AddressableAssetGroup> sharedGroups = new Dictionary<string, AddressableAssetGroup>();

            foreach (AnalyzeRule.AnalyzeResult result in analysisResults)
            {
                // Determine the delimiter: use '|' if present; otherwise, use ':' if present
                char delimiter;
                if (result.resultName.Contains("|"))
                    delimiter = '|';
                else if (result.resultName.Contains(":"))
                    delimiter = ':';
                else
                {
                    Debug.LogWarning($"Unexpected result format (no known delimiter): {result.resultName}");
                    continue;
                }

                string[] parts = result.resultName.Split(delimiter);
                if (parts.Length < 3)
                {
                    Debug.LogWarning($"Unexpected result format (less than 3 parts): {result.resultName}");
                    continue;
                }

                // Search all parts for one that starts with "Assets/"
                string assetPath = parts.FirstOrDefault(p => p.Trim().StartsWith("Assets/"));
                if (string.IsNullOrEmpty(assetPath))
                {
                    Debug.LogWarning($"Could not determine asset path from result parts: [{string.Join(", ", parts)}]");
                    continue;
                }

                assetPath = assetPath.Trim();

                // Get the GUID for this asset
                string guid = AssetDatabase.AssetPathToGUID(assetPath);
                if (string.IsNullOrEmpty(guid))
                {
                    Debug.LogWarning($"No GUID found for asset path: {assetPath}");
                    continue;
                }

                // Determine which shared group to use based on asset type
                string sharedGroupName = GetSharedGroupNameForAsset(assetPath);
                if (!sharedGroups.TryGetValue(sharedGroupName, out AddressableAssetGroup sharedGroup))
                {
                    // Create a new shared group for this type
                    sharedGroup = settings.CreateGroup(
                        sharedGroupName,
                        false,
                        false,
                        false,
                        null,
                        typeof(BundledAssetGroupSchema),
                        typeof(ContentUpdateGroupSchema)
                    );

                    var contentSchema = sharedGroup.GetSchema<ContentUpdateGroupSchema>();
                    if (contentSchema != null)
                        contentSchema.StaticContent = true;

                    sharedGroups[sharedGroupName] = sharedGroup;
                    Debug.Log($"Created shared group: {sharedGroupName}");
                }

                // Try to find the asset entry
                AddressableAssetEntry entry = settings.FindAssetEntry(guid);
                if (entry == null)
                {
                    // Asset is not explicitly addressable. Create a new entry in the shared group
                    entry = settings.CreateOrMoveEntry(guid, sharedGroup);
                    Debug.Log($"Created asset entry for duplicate asset (GUID: {guid}, path: {assetPath}) in group '{sharedGroupName}'.");
                    moveCount++;
                    continue;
                }

                // If the asset entry exists but is not in the shared group, move it
                if (entry.parentGroup != sharedGroup)
                {
                    settings.MoveEntry(entry, sharedGroup);
                    Debug.Log($"Moved duplicate asset '{entry.address}' (path: {assetPath}) to group '{sharedGroupName}'.");
                    moveCount++;
                }
            }

            // Save the Addressable settings
            EditorUtility.SetDirty(settings);
            Debug.Log($"Duplicate asset isolation complete. {moveCount} asset(s) moved to shared groups by type.");
        }

        /// <summary>
        /// Determines a shared group name based on the asset's type
        /// </summary>
        private static string GetSharedGroupNameForAsset(string assetPath)
        {
            Type assetType = AssetDatabase.GetMainAssetTypeAtPath(assetPath);
            if (assetType == typeof(Texture2D) || assetType == typeof(Sprite))
                return "Shared_Textures";
            else if (assetType == typeof(AudioClip))
                return "Shared_Audio";
            else if (assetType == typeof(GameObject))
                return "Shared_Models";
            else if (assetType == typeof(Material))
                return "Shared_Materials";
            else if (assetType == typeof(Shader))
                return "Shared_Shaders";
            else if (assetType == typeof(AnimationClip))
                return "Shared_Animations";
            else
                return "Shared_Others";
        }

        #endregion
    }
}
