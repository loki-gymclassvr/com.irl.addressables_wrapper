using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;

namespace AddressableSystem.Editor
{
    /// <summary>
    /// Utilities for working with the Addressable Asset System
    /// </summary>
    public static class AddressableAssetUtils
    {
        /// <summary>
        /// Makes an asset addressable with the default settings
        /// </summary>
        public static void MakeAssetAddressable(string guid, string name)
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings != null)
            {
                var defaultGroup = settings.DefaultGroup;
                if (defaultGroup != null)
                {
                    // Check if entry already exists
                    var existingEntry = settings.FindAssetEntry(guid);
                    if (existingEntry == null)
                    {
                        var entry = settings.CreateOrMoveEntry(guid, defaultGroup);
                        entry.address = name;
                        settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryAdded, entry, true);
                    }
                }
            }
        }
        
        /// <summary>
        /// Gets the address for an addressable asset by GUID
        /// </summary>
        public static string GetAddressForAsset(string guid)
        {
            if (string.IsNullOrEmpty(guid))
                return string.Empty;
                
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings != null)
            {
                var entry = settings.FindAssetEntry(guid);
                if (entry != null)
                {
                    return entry.address;
                }
            }
            
            return string.Empty;
        }
        
        /// <summary>
        /// Checks if an asset is already addressable
        /// </summary>
        public static bool IsAssetAddressable(string guid)
        {
            if (string.IsNullOrEmpty(guid))
                return false;
                
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings != null)
            {
                var entry = settings.FindAssetEntry(guid);
                return entry != null;
            }
            
            return false;
        }
    }
}