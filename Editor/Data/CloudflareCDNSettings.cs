using UnityEngine;

namespace Addressables_Wrapper.Editor.CDN
{
    /// <summary>
    /// Settings for the Cloudflare CDN uploader.
    /// </summary>
    public class CloudflareCDNSettings : ScriptableObject
    {
        // Singleton instance
        private static CloudflareCDNSettings instance;
        
        // The path where settings will be stored
        private static readonly string SettingsPath = "Assets/Editor/AddressableTool/ToolData/CloudflareCDNSettings.asset";
        
        public static CloudflareCDNSettings Instance
        {
            get
            {
                if (instance == null)
                {
                    // First try to load from the specified path
                    instance = UnityEditor.AssetDatabase.LoadAssetAtPath<CloudflareCDNSettings>(SettingsPath);
                    
                    if (instance == null)
                    {
                        instance = CreateInstance<CloudflareCDNSettings>();
                        
                        // Create directory structure if it doesn't exist
                        string dirPath = "Assets/Editor/AddressableTool/ToolData";
                        if (!UnityEditor.AssetDatabase.IsValidFolder(dirPath))
                        {
                            // Create the full directory path if it doesn't exist
                            string[] folders = dirPath.Split('/');
                            string currentPath = folders[0]; // "Assets"
                            
                            for (int i = 1; i < folders.Length; i++)
                            {
                                string folderToCheck = currentPath + "/" + folders[i];
                                if (!UnityEditor.AssetDatabase.IsValidFolder(folderToCheck))
                                {
                                    UnityEditor.AssetDatabase.CreateFolder(currentPath, folders[i]);
                                }
                                currentPath = folderToCheck;
                            }
                        }
                        
                        UnityEditor.AssetDatabase.CreateAsset(instance, SettingsPath);
                        UnityEditor.AssetDatabase.SaveAssets();
                    }
                }
                
                return instance;
            }
        }
        
        // UI state
        public bool showUploadOptions = true;
        public bool showConfigOptions = false;
        public bool showCloudflareConfig = false;
        public bool showAdvancedOptions = false;
        
        // Last used settings
        public int lastSelectedConfigIndex = 0;
    }
}