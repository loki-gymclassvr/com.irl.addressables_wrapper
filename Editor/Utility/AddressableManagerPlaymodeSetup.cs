#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using AddressableSystem;
using UnityEditor.AddressableAssets;

[InitializeOnLoad]
static class AddressableManagerPlaymodeSetup
{
    static AddressableManagerPlaymodeSetup() 
    {
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state) {
        if (state == PlayModeStateChange.ExitingEditMode) {
            ConfigureForLocalTesting();
        }
    }

    private static void ConfigureForLocalTesting() {
        const string assetPath = "Assets/Resources/AddressableManager.asset";
        var mgr = AssetDatabase.LoadAssetAtPath<AddressableManager>(assetPath);
        if (mgr == null || !mgr.EnableRemoteCDN)
            return;

        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null) {
            Debug.LogWarning("[AddressableManagerPlaymodeSetup] No AddressableAssetSettings found.");
            return;
        }

        // Build the profile name from your enum, e.g. "quest-local"
        string deviceName  = mgr.SelectedTargetDevice.ToString().ToLower().Replace('_','-');
        string profileName = $"{deviceName}-local";
        var ps = settings.profileSettings;
        
        // Find the profile - don't create a new one
        string profileId = ps.GetProfileId(profileName);
        if (string.IsNullOrEmpty(profileId)) {
            Debug.LogError($"[PlaymodeSetup] Profile '{profileName}' not found. Please create it manually.");
            return;
        }

        // Activate it
        settings.activeProfileId = profileId;
        EditorUtility.SetDirty(settings);
        Debug.Log($"[PlaymodeSetup] Activated profile '{profileName}'");

        string remoteBuildKey = string.Empty;
        string remoteLoadKey = string.Empty;
        
        string remoteBuildPath = string.Empty;
        string remoteLoadPath = string.Empty;
        
        // Read its Remote.BuildPath & Remote.LoadPath
        var keys = settings.profileSettings.GetVariableNames();

        foreach (var key in keys)
        {
            if (key.Contains("Remote") && key.Contains("Build"))
            {
                remoteBuildKey = key;
            }
            if (key.Contains("Remote") && key.Contains("Load"))
            {
                remoteLoadKey = key;
            }
        }
        
        remoteBuildPath = settings.profileSettings.GetValueByName(settings.activeProfileId, remoteBuildKey);
        remoteLoadPath = settings.profileSettings.GetValueByName(settings.activeProfileId, remoteLoadKey);
        
        if (string.IsNullOrEmpty(remoteBuildPath) || string.IsNullOrEmpty(remoteLoadPath)) {
            Debug.LogError($"[PlaymodeSetup] Missing BuildPath/LoadPath in profile '{profileName}'");
            return;
        }

        // Convert file:// URIs to local filesystem paths
        string localBuildFolder = remoteBuildPath.StartsWith("file://")
            ? new Uri(remoteBuildPath).LocalPath
            : remoteBuildPath;
        if (!Directory.Exists(localBuildFolder)) {
            Debug.LogError($"[PlaymodeSetup] Build folder not found: {localBuildFolder}");
            return;
        }

        // Pick the newest catalog*.json in that folder
        var files = Directory.GetFiles(localBuildFolder, "catalog*.json");
        if (files.Length == 0) {
            Debug.LogError($"[PlaymodeSetup] No catalog*.json files in {localBuildFolder}");
            return;
        }
        string catalogName = files
            .OrderByDescending(f => File.GetLastWriteTimeUtc(f))
            .First()
            .Split(Path.DirectorySeparatorChar)
            .Last();

        // Update your AddressableManager SO
        var so      = new SerializedObject(mgr);
        var urlProp = so.FindProperty("_cdnUrl");
        if (urlProp != null)
            urlProp.stringValue = remoteLoadPath.TrimEnd('/');
        var listProp = so.FindProperty("_defaultCatalogPaths");
        if (listProp != null && listProp.isArray) {
            listProp.ClearArray();
            listProp.InsertArrayElementAtIndex(0);
            listProp.GetArrayElementAtIndex(0).stringValue =
                $"{remoteLoadPath.TrimEnd('/')}/{catalogName}";
        }

        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(mgr);
        AssetDatabase.SaveAssets();

        Debug.Log($"[PlaymodeSetup] AddressableManager configured:\n" +
                  $"  • CDN URL = {remoteLoadPath}\n" +
                  $"  • Catalog = {catalogName}");
    }
}
#endif