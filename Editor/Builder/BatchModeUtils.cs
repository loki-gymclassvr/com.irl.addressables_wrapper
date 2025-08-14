using UnityEngine;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using System;
using System.Collections.Generic;
using Addressables_Wrapper.Editor.CDN;

namespace Addressables_Wrapper.Editor
{
    /// <summary>
    /// Utility class for batch mode operations that provides shared functionality
    /// across different batch mode classes.
    /// </summary>
    public static class BatchModeUtils
    {
        /// <summary>
        /// Gets a command line argument value by name.
        /// </summary>
        /// <param name="argName">The argument name, including the hyphen (e.g., "-targetDevice")</param>
        /// <returns>The value of the argument, or null if not found</returns>
        public static string GetCommandLineArgValue(string argName)
        {
            string[] args = Environment.GetCommandLineArgs();
            
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i].Equals(argName, StringComparison.OrdinalIgnoreCase))
                {
                    return args[i + 1];
                }
            }
            
            return null;
        }
        
        /// <summary>
        /// Tries to get an enum value from a command line argument.
        /// </summary>
        /// <typeparam name="T">The enum type</typeparam>
        /// <param name="argName">The argument name, including the hyphen</param>
        /// <param name="defaultValue">The default value if argument is missing or invalid</param>
        /// <returns>The parsed enum value, or the default value</returns>
        public static T GetEnumArgValue<T>(string argName, T defaultValue) where T : struct, Enum
        {
            string argValue = GetCommandLineArgValue(argName);
            
            if (string.IsNullOrEmpty(argValue))
            {
                return defaultValue;
            }
            
            if (Enum.TryParse<T>(argValue, true, out var result))
            {
                Debug.Log($"Argument {argName} set to {result}");
                return result;
            }
            else
            {
                Debug.LogWarning($"Invalid value for {argName}: {argValue}, using default: {defaultValue}");
                return defaultValue;
            }
        }
        
        /// <summary>
        /// Tries to get a boolean value from a command line argument.
        /// </summary>
        /// <param name="argName">The argument name, including the hyphen</param>
        /// <param name="defaultValue">The default value if argument is missing or invalid</param>
        /// <returns>The parsed boolean value, or the default value</returns>
        public static bool GetBoolArgValue(string argName, bool defaultValue = false)
        {
            string argValue = GetCommandLineArgValue(argName);
            
            if (string.IsNullOrEmpty(argValue))
            {
                return defaultValue;
            }
            
            if (bool.TryParse(argValue, out bool result))
            {
                Debug.Log($"Argument {argName} set to {result}");
                return result;
            }
            else
            {
                Debug.LogWarning($"Invalid value for {argName}: {argValue}, using default: {defaultValue}");
                return defaultValue;
            }
        }
        
        /// <summary>
        /// Loads and configures AddressableToolData settings.
        /// </summary>
        /// <param name="targetDevice">The target device</param>
        /// <param name="environment">The environment</param>
        /// <param name="releaseVersion">The release version</param>
        /// <returns>True if successful, false otherwise</returns>
        public static bool LoadAndConfigureSettings(BuildTargetDevice targetDevice, EnvironmentType environment, string releaseVersion)
        {
            try 
            {
                // Find AddressableToolData
                string[] guids = AssetDatabase.FindAssets("t:AddressableToolData");
                if (guids.Length == 0)
                {
                    Debug.LogWarning("No AddressableToolData found in project");
                    return false;
                }
                
                // Load the first found instance
                string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                var data = AssetDatabase.LoadAssetAtPath<AddressableToolData>(path);
                
                if (data == null)
                {
                    Debug.LogWarning($"Failed to load AddressableToolData at path: {path}");
                    return false;
                }
                
                // Update settings
                data.buildTargetDevice = targetDevice;
                data.environment = environment;
                
                if (!string.IsNullOrEmpty(releaseVersion))
                {
                    data.releaseVersion = releaseVersion;
                }
                
                // Save
                EditorUtility.SetDirty(data);
                AssetDatabase.SaveAssets();
                
                Debug.Log($"Updated AddressableToolData: Device={targetDevice}, Environment={environment}, Version={releaseVersion}");
                return true;
            }
            catch (Exception ex) 
            {
                Debug.LogError($"Error configuring settings: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Finds the CDN config for a specific target device.
        /// </summary>
        /// <param name="targetDevice">The target device to find the config for</param>
        /// <returns>The CDN config, or null if not found</returns>
        public static CDNConfig FindCDNConfig(BuildTargetDevice targetDevice)
        {
            var config = AssetDatabase.LoadAssetAtPath<CDNConfig>($"Assets/Editor/AddressableTool/Data/{targetDevice.ToString()}-CDNConfig.asset");
            if(config != null)
                return config;
            
            Debug.LogWarning($"No CDN configuration found for target device: {targetDevice}");
            return null;
        }
        
        /// <summary>
        /// Loads the Cloudflare configuration if available.
        /// </summary>
        /// <returns>The Cloudflare config, or null if not found</returns>
        public static CloudflareConfig LoadCloudflareConfig()
        {
            // Try to find the config at the expected path first
            var cloudflareConfig = AssetDatabase.LoadAssetAtPath<CloudflareConfig>("Assets/Editor/AddressableTool/ToolData/CloudflareConfig.asset");
            
            // If not found at the specific path, search for it
            if (cloudflareConfig == null)
            {
                string[] guids = AssetDatabase.FindAssets("t:CloudflareConfig");
                if (guids.Length > 0)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                    cloudflareConfig = AssetDatabase.LoadAssetAtPath<CloudflareConfig>(path);
                }
            }
            
            if (cloudflareConfig == null)
            {
                Debug.LogWarning("Cloudflare configuration not found");
                return null;
            }
            
            // Load the secret key from EditorPrefs if needed
            if (string.IsNullOrEmpty(cloudflareConfig.cloudflareSecretAccessKey))
            {
                cloudflareConfig.cloudflareSecretAccessKey = EditorPrefs.GetString("CloudflareSecretKey_" + cloudflareConfig.cloudflareAccessKey, "");
                
                if (string.IsNullOrEmpty(cloudflareConfig.cloudflareSecretAccessKey))
                {
                    Debug.LogWarning("Cloudflare Secret Access Key not found");
                }
            }
            
            return cloudflareConfig;
        }
        
        /// <summary>
        /// Sets the active Addressables profile.
        /// </summary>
        /// <param name="settings">The Addressable Asset Settings</param>
        /// <param name="profileName">The profile name to set as active</param>
        /// <param name="targetDevice">Fallback device for profile name construction</param>
        /// <param name="environment">Fallback environment for profile name construction</param>
        /// <returns>True if successful, false otherwise</returns>
        public static bool SetAddressablesProfile(
            AddressableAssetSettings settings, 
            string profileName, 
            BuildTargetDevice targetDevice, 
            EnvironmentType environment)
        {
            if (settings == null)
            {
                Debug.LogError("AddressableAssetSettings is null");
                return false;
            }
            
            // If profileName is provided, try to set it
            if (!string.IsNullOrEmpty(profileName))
            {
                string profileId = settings.profileSettings.GetProfileId(profileName);
                if (!string.IsNullOrEmpty(profileId))
                {
                    Debug.Log($"Setting active profile to: {profileName}");
                    settings.activeProfileId = profileId;
                    EditorUtility.SetDirty(settings);
                    AssetDatabase.SaveAssets();
                    return true;
                }
            }
            
            // Try to construct profile name from target device and environment
            string constructedProfileName = $"{targetDevice.ToString().ToLower()}-{environment.ToString().ToLower()}";
            string constructedProfileId = settings.profileSettings.GetProfileId(constructedProfileName);
            
            if (!string.IsNullOrEmpty(constructedProfileId))
            {
                Debug.Log($"Setting active profile to: {constructedProfileName}");
                settings.activeProfileId = constructedProfileId;
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssets();
                return true;
            }
            
            // Try alternate naming format
            string alternateName = $"{targetDevice}_{environment}";
            string alternateId = settings.profileSettings.GetProfileId(alternateName);
            
            if (!string.IsNullOrEmpty(alternateId))
            {
                Debug.Log($"Setting active profile to: {alternateName}");
                settings.activeProfileId = alternateId;
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssets();
                return true;
            }
            
            Debug.LogWarning($"Could not find profile '{profileName ?? "none"}' or '{constructedProfileName}' or '{alternateName}'. Using current active profile.");
            return false;
        }
        
        /// <summary>
        /// Exits the Unity editor with the appropriate exit code.
        /// </summary>
        /// <param name="success">Whether the operation was successful</param>
        public static void ExitWithCode(bool success)
        {
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(success ? 0 : 1);
            }
        }
    }
}