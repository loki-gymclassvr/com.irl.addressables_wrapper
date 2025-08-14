#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;

namespace Addressables_Wrapper.Editor
{
    public static class AddressableSettingsOverrider
    {
        [MenuItem("Tools/Addressables/Override All Settings")]
        public static void OverrideAllAddressableGroupSettings()
        {
            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                Debug.LogError("No AddressableAssetSettings found. Make sure you have Addressables configured.");
                return;
            }

            settings.OptimizeCatalogSize = true;

            settings.IgnoreUnsupportedFilesInBuild = false;

            settings.ContiguousBundles = true;

            settings.NonRecursiveBuilding = false;

            settings.MaxConcurrentWebRequests = 5;

            //-------------------------------------------------------------------------
            // Override Group-Specific Settings
            // For each group, we can modify schemas like BundledAssetGroupSchema, ContentUpdateGroupSchema, etc.
            //-------------------------------------------------------------------------

            // We'll iterate over all groups in the project. 
            foreach (var group in settings.groups)
            {
                if (group == null || group.Name == "Built In Data") continue;
                // Skip built-in read-only groups (e.g. "Built In Data").
                if (group.ReadOnly)
                    continue;

                var bundledSchema = group.GetSchema<BundledAssetGroupSchema>();
                if (bundledSchema != null)
                {
                    // Use asset bundle cache
                    bundledSchema.UseAssetBundleCache = true;

                    // Use asset bundle CRC
                    bundledSchema.UseAssetBundleCrc = false;

                    // Include GUIDs in catalog
                    bundledSchema.IncludeGUIDInCatalog = true;

                    // Set compression mode (LZ4, LZMA, Uncompressed)
                    bundledSchema.Compression = BundledAssetGroupSchema.BundleCompressionMode.LZ4;

                    bundledSchema.BundleNaming = BundledAssetGroupSchema.BundleNamingStyle.AppendHash;

                    bundledSchema.AssetBundledCacheClearBehavior = BundledAssetGroupSchema.CacheClearBehavior.ClearWhenWhenNewVersionLoaded;

                    // Internal ID naming mode 
                    bundledSchema.InternalIdNamingMode = BundledAssetGroupSchema.AssetNamingMode.Filename;

                    EditorUtility.SetDirty(bundledSchema);
                    AssetDatabase.SaveAssetIfDirty(bundledSchema);
                }

                // --- ContentUpdateGroupSchema ---
                var contentUpdateSchema = group.GetSchema<ContentUpdateGroupSchema>();
                if (contentUpdateSchema != null)
                {
                    // Make content static (cannot be updated separately in content update).
                    contentUpdateSchema.StaticContent = false;
                    EditorUtility.SetDirty(contentUpdateSchema);
                    AssetDatabase.SaveAssetIfDirty(contentUpdateSchema);
                }
            }

            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssetIfDirty(settings);

            Debug.Log("All Addressable settings have been overridden successfully!");
        }
    }
#endif
}