using System.Collections.Generic;
using UnityEngine;

namespace Addressables_Wrapper.Editor
{
    public static class AddressableToolValidator
    {
        /// <summary>
        /// Checks the list of GroupConfigurations and returns a list of duplicate group names.
        /// </summary>
        public static List<string> GetDuplicateGroupNames(AddressableToolData data)
        {
            Dictionary<string, int> groupCounts = new Dictionary<string, int>();
            foreach (var config in data.groupConfigurations)
            {
                // Skip empty group names.
                if (string.IsNullOrEmpty(config.groupName))
                    continue;
                
                if (groupCounts.ContainsKey(config.groupName))
                    groupCounts[config.groupName]++;
                else
                    groupCounts[config.groupName] = 1;
            }
            List<string> duplicates = new List<string>();
            foreach (var kvp in groupCounts)
            {
                if (kvp.Value > 1)
                    duplicates.Add(kvp.Key);
            }
            return duplicates;
        }
        
        /// <summary>
        /// Checks if an asset is excluded for a specific target device
        /// </summary>
        /// <param name="data">The AddressableToolData to check</param>
        /// <param name="assetGuid">The GUID of the asset to check</param>
        /// <param name="targetDevice">The target device to check exclusion for</param>
        /// <returns>True if the asset is excluded for the specified target device</returns>
        public static bool IsAssetExcludedForTarget(AddressableToolData data, string assetGuid, TargetDevice targetDevice)
        {
            if (data == null || data.platformExclusions == null || string.IsNullOrEmpty(assetGuid))
            {
                return false;
            }
            
            foreach (var exclusion in data.platformExclusions)
            {
                if (exclusion.assetGuid == assetGuid && (exclusion.excludedPlatforms & targetDevice) != 0)
                {
                    return true;
                }
            }
            
            return false;
        }
        
        /// <summary>
        /// Gets all assets excluded for a specific target device
        /// </summary>
        /// <param name="data">The AddressableToolData to check</param>
        /// <param name="targetDevice">The target device to get exclusions for</param>
        /// <returns>A list of asset GUIDs that are excluded for the specified target device</returns>
        public static List<string> GetExcludedAssetsForTarget(AddressableToolData data, TargetDevice targetDevice)
        {
            List<string> excludedAssets = new List<string>();
            
            if (data == null || data.platformExclusions == null)
            {
                return excludedAssets;
            }
            
            foreach (var exclusion in data.platformExclusions)
            {
                if ((exclusion.excludedPlatforms & targetDevice) != 0)
                {
                    excludedAssets.Add(exclusion.assetGuid);
                }
            }
            
            return excludedAssets;
        }
        
        /// <summary>
        /// Gets assets that are in addressable groups but are excluded for a specific target
        /// </summary>
        /// <param name="data">The AddressableToolData to check</param>
        /// <param name="targetDevice">The target device to check</param>
        /// <returns>List of assets that will be excluded during build</returns>
        public static List<GroupConfiguration.AssetEntry> GetExcludedAddressableAssets(AddressableToolData data, TargetDevice targetDevice)
        {
            List<GroupConfiguration.AssetEntry> excludedAddressables = new List<GroupConfiguration.AssetEntry>();
            
            if (data == null || data.platformExclusions == null || data.groupConfigurations == null)
            {
                return excludedAddressables;
            }
            
            // Get all excluded GUIDs for this target
            List<string> excludedGuids = GetExcludedAssetsForTarget(data, targetDevice);
            
            // Find addressable assets that match these GUIDs
            foreach (var group in data.groupConfigurations)
            {
                if (group.assets == null)
                    continue;
                    
                foreach (var asset in group.assets)
                {
                    if (excludedGuids.Contains(asset.guid))
                    {
                        excludedAddressables.Add(asset);
                    }
                }
            }
            
            return excludedAddressables;
        }
        
        /// <summary>
        /// Checks if there are any conflicts where an asset is both included and excluded for a target
        /// </summary>
        /// <param name="data">The AddressableToolData to check</param>
        /// <returns>List of assets with conflicts</returns>
        public static List<string> GetTargetDeviceConflicts(AddressableToolData data)
        {
            List<string> conflicts = new List<string>();
            
            if (data == null || data.platformExclusions == null || data.groupConfigurations == null)
            {
                return conflicts;
            }
            
            // Check each group configuration
            foreach (var group in data.groupConfigurations)
            {
                if (group.assets == null)
                    continue;
                    
                foreach (var asset in group.assets)
                {
                    // Check if this asset has any exclusions
                    foreach (var exclusion in data.platformExclusions)
                    {
                        if (exclusion.assetGuid == asset.guid)
                        {
                            // Check if there's an overlap between target devices and excluded platforms
                            if ((group.targetDevices & exclusion.excludedPlatforms) != 0)
                            {
                                conflicts.Add(asset.assetPath);
                            }
                        }
                    }
                }
            }
            
            return conflicts;
        }
    }    
}