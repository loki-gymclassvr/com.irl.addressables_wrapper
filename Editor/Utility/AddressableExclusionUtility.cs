using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace Addressables_Wrapper.Editor
{
    /// <summary>
    /// Utility for applying and restoring platform- and environment-specific exclusions on Addressables.
    /// </summary>
    public static class AddressableExclusionUtility
    {
        private const string RemovedEntriesKey = "AddressableExclusionUtility_RemovedEntries";

        /// <summary>
        /// Applies exclusions for the given target device and environment by removing matching addressable entries.
        /// </summary>
        /// <param name="targetDevice">Platform for which to apply exclusions.</param>
        /// <param name="buildEnvironment">Environment for which to apply exclusions.</param>
        /// <returns>List of excluded asset paths for logging.</returns>
        public static List<string> ApplyPlatformExclusions(TargetDevice targetDevice, EnvironmentType buildEnvironment)
        {
            var excludedAssetPaths = new List<string>();
            string dataPath = "Assets/Editor/AddressableTool/Data/AddressableToolData.asset";
            var toolData = AssetDatabase.LoadAssetAtPath<AddressableToolData>(dataPath);

            if (toolData == null || toolData.platformExclusions == null || toolData.platformExclusions.Count == 0)
            {
                Debug.Log("[AddressableExclusionUtility] No platform exclusions found.");
                return excludedAssetPaths;
            }

            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                Debug.LogError("[AddressableExclusionUtility] AddressableAssetSettings not found!");
                return excludedAssetPaths;
            }

            var applicableExclusions = new List<PlatformExclusion>();
            for (int i = 0; i < toolData.platformExclusions.Count; i++)
            {
                var ex = toolData.platformExclusions[i];
                bool platformMatch = ex.excludedPlatforms.HasFlag(targetDevice);
                bool envMatch = ex.excludedEnvironments.HasFlag(buildEnvironment);
                Debug.Log($"[AddressableExclusionUtility][DEBUG] Exclusion {i}: {ex.folderPath ?? ex.assetGuid ?? ex.fileExtension} | Platforms: {ex.excludedPlatforms} ({(int)ex.excludedPlatforms}) & {targetDevice} ({(int)targetDevice}) = {platformMatch} | Envs: {ex.excludedEnvironments} ({(int)ex.excludedEnvironments}) & {buildEnvironment} ({(int)buildEnvironment}) = {envMatch}");
                if (platformMatch && envMatch)
                    applicableExclusions.Add(ex);
            }

            if (applicableExclusions.Count == 0)
            {
                Debug.Log($"[AddressableExclusionUtility] No exclusions for {targetDevice} / {buildEnvironment}");
                return excludedAssetPaths;
            }

            var removedEntries = new List<RemovedAddressableEntry>();

            foreach (var ex in applicableExclusions.Where(e => e.exclusionType == ExclusionType.Asset))
            {
                var entry = settings.FindAssetEntry(ex.assetGuid);
                if (entry != null)
                {
                    removedEntries.Add(new RemovedAddressableEntry
                    {
                        guid = ex.assetGuid,
                        groupName = entry.parentGroup.Name,
                        address = entry.address,
                        labels = entry.labels.ToArray()
                    });

                    var group = entry.parentGroup;
                    group.RemoveAssetEntry(entry);
                    settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryRemoved, entry, true, false);
                    
                    excludedAssetPaths.Add(ex.assetPath);
                    Debug.Log($"[AddressableExclusionUtility] Excluded asset: {ex.assetPath}");
                }
            }

            foreach (var ex in applicableExclusions.Where(e => e.exclusionType == ExclusionType.Folder))
            {
                string folder = ex.folderPath;
                var allEntries = new List<AddressableAssetEntry>();
                foreach (var group in settings.groups)
                {
                    if (group == null || group.Name == "Built In Data") continue;
                    foreach (var entry in group.entries)
                    {
                        if (entry != null)
                            allEntries.Add(entry);
                    }
                }

                foreach (var entry in allEntries)
                {
                    string assetPath = AssetDatabase.GUIDToAssetPath(entry.guid);
                    if (!string.IsNullOrEmpty(assetPath) && assetPath.StartsWith(folder))
                    {
                        removedEntries.Add(new RemovedAddressableEntry
                        {
                            guid = entry.guid,
                            groupName = entry.parentGroup.Name,
                            address = entry.address,
                            labels = entry.labels.ToArray()
                        });
                        
                        var group = entry.parentGroup;
                        group.RemoveAssetEntry(entry, false);
                        settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryRemoved, entry, true, false);
                        
                        excludedAssetPaths.Add(assetPath);
                        Debug.Log($"[AddressableExclusionUtility] Excluded folder-asset: {assetPath}");
                    }
                }
            }

            foreach (var ex in applicableExclusions.Where(e => e.exclusionType == ExclusionType.Extension))
            {
                string ext = ex.fileExtension.ToLowerInvariant();
                var allEntries = settings.groups
                    .SelectMany(g => g.entries)
                    .Where(entry => entry != null)
                    .ToList();

                foreach (var entry in allEntries)
                {
                    string assetPath = AssetDatabase.GUIDToAssetPath(entry.guid);
                    if (!string.IsNullOrEmpty(assetPath) && assetPath.ToLowerInvariant().EndsWith(ext))
                    {
                        removedEntries.Add(new RemovedAddressableEntry
                        {
                            guid = entry.guid,
                            groupName = entry.parentGroup.Name,
                            address = entry.address,
                            labels = entry.labels.ToArray()
                        });
                        
                        var group = entry.parentGroup;
                        group.RemoveAssetEntry(entry, false);
                        settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryRemoved, entry, true, false);
                        
                        excludedAssetPaths.Add(assetPath);
                        Debug.Log($"[AddressableExclusionUtility] Excluded by extension: {assetPath}");
                    }
                }
            }

            if (removedEntries.Count > 0)
            {
                settings.SetDirty(AddressableAssetSettings.ModificationEvent.BatchModification, null, true, true);
                StoreRemovedEntries(removedEntries);
            }
            
            AssetDatabase.SaveAssetIfDirty(settings);

            // Remove excluded assets from groupConfigurations as well
            if (toolData != null && toolData.groupConfigurations != null && toolData.groupConfigurations.Count > 0)
            {
                // Collect all excluded GUIDs
                var excludedGuids = new HashSet<string>();
                foreach (var removed in removedEntries)
                {
                    excludedGuids.Add(removed.guid);
                }
                int totalRemoved = 0;
                foreach (var groupConfig in toolData.groupConfigurations)
                {
                    int before = groupConfig.assets.Count;
                    groupConfig.assets.RemoveAll(asset => excludedGuids.Contains(asset.guid));
                    int after = groupConfig.assets.Count;
                    int diff = before - after;
                    if (diff > 0)
                    {
                        Debug.Log($"[AddressableExclusionUtility] Removed {diff} excluded assets from groupConfigurations for group '{groupConfig.groupName}'");
                        totalRemoved += diff;
                    }
                }
                if (totalRemoved > 0)
                {
                    EditorUtility.SetDirty(toolData);
                    AssetDatabase.SaveAssetIfDirty(toolData);
                    Debug.Log($"[AddressableExclusionUtility] Saved AddressableToolData after removing {totalRemoved} excluded assets from groupConfigurations.");
                }
            }

            return excludedAssetPaths;
        }

        /// <summary>
        /// Restores previously excluded addressable entries to their original groups.
        /// </summary>
        public static void RestoreExcludedAssets()
        {
            var removedEntries = LoadRemovedEntries();
            if (removedEntries == null || removedEntries.Count == 0)
            {
                Debug.Log("[AddressableExclusionUtility] No assets to restore.");
                return;
            }

            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                Debug.LogError("[AddressableExclusionUtility] AddressableAssetSettings not found!");
                return;
            }

            int restoredCount = 0;
            foreach (var entry in removedEntries)
            {
                var group = settings.FindGroup(entry.groupName);
                if (group == null)
                {
                    group = settings.CreateGroup(entry.groupName, false, false, true, null);
                }

                string assetPath = AssetDatabase.GUIDToAssetPath(entry.guid);
                if (!string.IsNullOrEmpty(assetPath))
                {
                    var newEntry = settings.CreateOrMoveEntry(entry.guid, group);
                    newEntry.address = entry.address;
                    foreach (var label in entry.labels)
                    {
                        if (!settings.GetLabels().Contains(label))
                        {
                            settings.AddLabel(label);
                        }
                        newEntry.SetLabel(label, true);
                    }
                    restoredCount++;
                    Debug.Log($"[AddressableExclusionUtility] Restored asset: {assetPath}");
                }
            }

            if (restoredCount > 0)
            {
                settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryAdded, null, true);
            }

            ClearRemovedEntries();
        }

        /// <summary>
        /// Stores the list of removed addressable entries in EditorPrefs as JSON.
        /// </summary>
        /// <param name="removedEntries">List of removed entries to store.</param>
        private static void StoreRemovedEntries(List<RemovedAddressableEntry> removedEntries)
        {
            var json = JsonUtility.ToJson(new RemovedEntryContainer { entries = removedEntries });
            EditorPrefs.SetString(RemovedEntriesKey, json);
        }

        /// <summary>
        /// Loads the list of removed addressable entries from EditorPrefs.
        /// </summary>
        /// <returns>List of removed entries loaded from storage.</returns>
        private static List<RemovedAddressableEntry> LoadRemovedEntries()
        {
            if (!EditorPrefs.HasKey(RemovedEntriesKey))
            {
                return new List<RemovedAddressableEntry>();
            }

            var json = EditorPrefs.GetString(RemovedEntriesKey);
            if (string.IsNullOrEmpty(json))
            {
                return new List<RemovedAddressableEntry>();
            }

            var container = JsonUtility.FromJson<RemovedEntryContainer>(json);
            return container.entries ?? new List<RemovedAddressableEntry>();
        }

        /// <summary>
        /// Clears stored removed entries from EditorPrefs.
        /// </summary>
        private static void ClearRemovedEntries()
        {
            if (EditorPrefs.HasKey(RemovedEntriesKey))
            {
                EditorPrefs.DeleteKey(RemovedEntriesKey);
            }
        }

        [System.Serializable]
        private class RemovedEntryContainer
        {
            /// <summary>
            /// Container for JSON serialization of removed entries.
            /// </summary>
            public List<RemovedAddressableEntry> entries;
        }
    }

    /// <summary>
    /// Represents a single removed addressable entry for later restoration.
    /// </summary>
    [System.Serializable]
    public class RemovedAddressableEntry
    {
        /// <summary>
        /// GUID of the removed asset.
        /// </summary>
        public string guid;

        /// <summary>
        /// Name of the group from which the asset was removed.
        /// </summary>
        public string groupName;

        /// <summary>
        /// Address of the asset entry.
        /// </summary>
        public string address;

        /// <summary>
        /// Labels that were assigned to the asset entry.
        /// </summary>
        public string[] labels;
    }
}
