using UnityEngine;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using System;
using System.Net;
using System.Collections.Generic;
using System.Reflection;

namespace Addressables_Wrapper.Editor.CDN
{
    /// <summary>
    /// Service to manage Unity Addressables local hosting functionality
    /// Integrates with the existing AddressablesEditorManager
    /// </summary>
    public class AddressablesLocalHostingService
    {
        private object _hostingServiceManager;
        private object _hostingService;
        private AddressableAssetSettings _settings;
        private bool _isHostingServiceRunning = false;
        
        public bool IsHostingServiceRunning => _isHostingServiceRunning;
        
        public string HostingServiceUrl 
        { 
            get 
            {
                if (_hostingService == null)
                    return string.Empty;
                
                var addressableToolData = LoadToolData();
                if (addressableToolData == null)
                    return string.Empty;
                
                // Try to get URL via reflection
                try {
                    
                    
                    var prop = _hostingService.GetType().GetProperty("BaseURL");
                    if (prop != null)
                    {
                        return prop.GetValue(_hostingService) as string;
                    }
                    else
                    {
                        // Fallback URL using local IP and port
                        int port = 8080;
                        var portProp = _hostingService.GetType().GetProperty("HostingServicePort");
                        if (portProp != null)
                        {
                            port = (int)portProp.GetValue(_hostingService);
                        }
                        return $"http://{GetLocalIPAddress()}:{port}/";
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Failed to get hosting service URL: {ex.Message}");
                    return $"http://{GetLocalIPAddress()}:8080/";
                }
            }
        }
        
        public AddressablesLocalHostingService()
        {
            InitializeService();
        }
        
        private void InitializeService()
        {
            try
            {
                // Get the Addressables settings
                _settings = AddressableAssetSettingsDefaultObject.Settings;
                if (_settings == null)
                {
                    Debug.LogError("Could not find Addressable Asset Settings.");
                    return;
                }
                
                // Access the hosting service manager
                _hostingServiceManager = GetHostingServiceManager(_settings);
                if (_hostingServiceManager == null)
                {
                    Debug.LogError("Could not access hosting service manager.");
                    return;
                }
                
                // Get existing hosting services
                var existingServices = GetHostingServices();
                
                // Check if we already have an HTTP hosting service
                foreach (var service in existingServices)
                {
                    if (service.GetType().Name == "HttpHostingService")
                    {
                        _hostingService = service;
                        
                        // Check if it's running
                        var runningMethod = service.GetType().GetMethod("IsHostingServiceRunning");
                        if (runningMethod != null)
                        {
                            _isHostingServiceRunning = (bool)runningMethod.Invoke(service, null);
                        }
                        
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error initializing hosting service: {ex.Message}");
            }
        }
        
        private object GetHostingServiceManager(AddressableAssetSettings settings)
        {
            try
            {
                // First try direct property
                var prop = settings.GetType().GetProperty("HostingServices");
                if (prop != null)
                {
                    return prop.GetValue(settings);
                }
                
                // Then try field access
                var field = settings.GetType().GetField("m_HostingServicesManager", 
                    BindingFlags.NonPublic | BindingFlags.Instance);
                
                if (field != null)
                {
                    return field.GetValue(settings);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to get hosting service manager: {ex.Message}");
            }
            
            return null;
        }
        
        private IList<object> GetHostingServices()
        {
            var result = new List<object>();
            
            try
            {
                if (_hostingServiceManager == null)
                    return result;
                
                // Try to get the services collection
                var servicesProperty = _hostingServiceManager.GetType().GetProperty("HostingServices");
                if (servicesProperty != null)
                {
                    var services = servicesProperty.GetValue(_hostingServiceManager);
                    
                    // If it's an ICollection, convert to List
                    if (services is System.Collections.IEnumerable enumerable)
                    {
                        foreach (var service in enumerable)
                        {
                            result.Add(service);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to get hosting services: {ex.Message}");
            }
            
            return result;
        }
        
        /// <summary>
        /// Starts the local hosting service
        /// </summary>
        /// <param name="port">Port to use for hosting (default: 8080)</param>
        /// <returns>The URL of the hosting service</returns>
        public string StartHostingService(int port = 8080)
        {
            try
            {
                if (_settings == null)
                {
                    _settings = AddressableAssetSettingsDefaultObject.Settings;
                    if (_settings == null)
                    {
                        throw new Exception("Addressable Asset Settings not found. Have you set up Addressables in your project?");
                    }
                }
                
                if (_hostingServiceManager == null)
                {
                    _hostingServiceManager = GetHostingServiceManager(_settings);
                    if (_hostingServiceManager == null)
                    {
                        throw new Exception("Could not access hosting service manager.");
                    }
                }
                
                // Create a new hosting service if one doesn't exist
                if (_hostingService == null)
                {
                    // Find the HttpHostingService type
                    Type httpServiceType = null;
                    foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        var types = assembly.GetTypes();
                        foreach (var type in types)
                        {
                            if (type.Name == "HttpHostingService")
                            {
                                httpServiceType = type;
                                break;
                            }
                        }
                        
                        if (httpServiceType != null)
                            break;
                    }
                    
                    if (httpServiceType == null)
                    {
                        throw new Exception("Could not find HttpHostingService type.");
                    }
                    
                    // Add the hosting service
                    var addServiceMethod = _hostingServiceManager.GetType().GetMethod("AddHostingService", 
                        new[] { typeof(Type), typeof(string) });
                    
                    if (addServiceMethod == null)
                    {
                        throw new Exception("Could not find AddHostingService method.");
                    }
                    
                    _hostingService = addServiceMethod.Invoke(_hostingServiceManager, 
                        new object[] { httpServiceType, "Addressables Local Hosting" });
                    
                    if (_hostingService == null)
                    {
                        throw new Exception("Failed to create hosting service.");
                    }
                }
                
                // Get the build path from the addressables settings
                string profileId = _settings.activeProfileId;
                string buildPath = _settings.profileSettings.GetValueByName(profileId, "Remote.BuildPath");
                if (string.IsNullOrEmpty(buildPath))
                {
                    throw new Exception("Could not find Remote.BuildPath in Addressables profile.");
                }
                
                // Expand environment variables (if any)
                buildPath = Environment.ExpandEnvironmentVariables(buildPath);
                
                // Add the build path to the content roots
                var rootsProperty = _hostingService.GetType().GetProperty("HostingServiceContentRoots");
                if (rootsProperty != null)
                {
                    var roots = rootsProperty.GetValue(_hostingService) as IList<string>;
                    if (roots != null)
                    {
                        roots.Clear();
                        roots.Add(buildPath);
                    }
                }
                
                // Set the port
                var portProperty = _hostingService.GetType().GetProperty("HostingServicePort");
                if (portProperty != null)
                {
                    portProperty.SetValue(_hostingService, port);
                }
                
                // Start the service
                if (!_isHostingServiceRunning)
                {
                    var startMethod = _hostingService.GetType().GetMethod("StartHostingService");
                    if (startMethod != null)
                    {
                        startMethod.Invoke(_hostingService, null);
                        _isHostingServiceRunning = true;
                    }
                }
                
                // Get the hosting service URL
                string serviceUrl = HostingServiceUrl;
                
                // Update the load path in the active profile to point to the local hosting service
                _settings.profileSettings.SetValue(profileId, "Remote.LoadPath", serviceUrl);
                
                // Save the settings
                EditorUtility.SetDirty(_settings);
                AssetDatabase.SaveAssetIfDirty(_settings);
                
                return serviceUrl;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to start hosting service: {ex.Message}");
                throw;
            }
        }
        
        /// <summary>
        /// Checks if groups are set to use remote paths
        /// </summary>
        private bool AreGroupsSetToRemote()
        {
            Debug.Log("Checking if groups are set to remote paths...");
            
            try
            {
                if (_settings == null)
                {
                    _settings = AddressableAssetSettingsDefaultObject.Settings;
                    if (_settings == null)
                    {
                        Debug.LogError("Addressable Asset Settings not found.");
                        return false;
                    }
                }
                
                // Get the current profile's remote paths for comparison
                string profileId = _settings.activeProfileId;
                string expectedBuildPath = _settings.profileSettings.GetValueByName(profileId, "Remote.BuildPath");
                string expectedLoadPath = _settings.profileSettings.GetValueByName(profileId, "Remote.LoadPath");
                
                Debug.Log($"Expected Remote.BuildPath: {expectedBuildPath}");
                Debug.Log($"Expected Remote.LoadPath: {expectedLoadPath}");
                
                if (string.IsNullOrEmpty(expectedBuildPath) || string.IsNullOrEmpty(expectedLoadPath))
                {
                    Debug.LogWarning("Remote paths are not properly defined in the profile.");
                    return false;
                }
                
                // Skip this check entirely and just use MakeGroupsRemote directly since it's safer
                return false;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error checking if groups are set to remote: {ex.Message}");
                Debug.LogException(ex);
                return false;
            }
        }
        
        /// <summary>
        /// Stops the local hosting service
        /// </summary>
        public void StopHostingService()
        {
            try
            {
                if (_hostingService != null && _isHostingServiceRunning)
                {
                    var stopMethod = _hostingService.GetType().GetMethod("StopHostingService");
                    if (stopMethod != null)
                    {
                        stopMethod.Invoke(_hostingService, null);
                        _isHostingServiceRunning = false;
                        Debug.Log("Addressables local hosting service stopped.");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to stop hosting service: {ex.Message}");
                throw;
            }
        }
        
        /// <summary>
        /// Gets the local IP address of the machine
        /// </summary>
        private string GetLocalIPAddress()
        {
            string hostName = Dns.GetHostName();
            IPHostEntry hostEntry = Dns.GetHostEntry(hostName);
            
            foreach (IPAddress ip in hostEntry.AddressList)
            {
                // Only get IPv4 addresses
                if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                {
                    return ip.ToString();
                }
            }
            
            // If no IPv4 address found, return localhost
            return "127.0.0.1";
        }
        
        /// <summary>
        /// Updates the Addressables profile for local hosting
        /// </summary>
        /// <param name="deviceType">Target device type</param>
        /// <returns>True if the profile was updated successfully</returns>
        public bool UpdateAddressablesProfileForLocalHosting(BuildTargetDevice deviceType, string releaseVersion, int port)
        {
            if (_settings == null)
            {
                _settings = AddressableAssetSettingsDefaultObject.Settings;
                if (_settings == null)
                {
                    Debug.LogError("Addressable Asset Settings not found.");
                    return false;
                }
            }
            
            try
            {
                string profileId = _settings.activeProfileId;
                string activeProfileName = _settings.profileSettings.GetProfileName(profileId);
                string deviceTypeName = deviceType.ToString().ToLower();
                
                // Check if we need to create a local profile
                if (!activeProfileName.Contains("local"))
                {
                    // Check if we need to create or find a local profile
                    string localProfileName = $"{deviceTypeName}-local";
                    string localProfileId = null;

                    localProfileId = _settings.profileSettings.GetProfileId(localProfileName);

                    // If the profile doesn't exist, create it
                    if (string.IsNullOrEmpty(localProfileId))
                    {
                        localProfileId = _settings.profileSettings.AddProfile(localProfileName, profileId);
                        if (string.IsNullOrEmpty(localProfileId))
                        {
                            Debug.LogError($"Failed to create local profile: {localProfileName}");
                            return false;
                        }
                        Debug.Log($"Created new localhost profile: {localProfileName}");
                    }

                    // Set it as the active profile
                    _settings.activeProfileId = localProfileId;
                    profileId = localProfileId;
                    Debug.Log($"Setting localhost profile active: {localProfileName}");
                }
                
                // Get local IP address
                string ipAddress = GetLocalIPAddress();
                
                if (_hostingService != null)
                {
                    var portProperty = _hostingService.GetType().GetProperty("HostingServicePort");
                    if (portProperty != null)
                    {
                        port = (int)portProperty.GetValue(_hostingService);
                    }
                }
                
                // Set the remote load path to the local hosting service URL
                string loadPath = $"http://{ipAddress}:{port}/";
                _settings.profileSettings.SetValue(profileId, "Remote.LoadPath", loadPath);
                
                // Set the remote build path to a local server data path
                string buildPath = "{UnityEngine.Application.dataPath}/../ServerData/"+$"{deviceTypeName}/{releaseVersion}";
                _settings.profileSettings.SetValue(profileId, "Remote.BuildPath", buildPath);
                
                // Save the settings
                EditorUtility.SetDirty(_settings);
                AssetDatabase.SaveAssetIfDirty(_settings);
                
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to update profile for local hosting: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Builds Addressables for local hosting using AddressablesEditorManager
        /// </summary>
        public bool BuildAddressablesForLocalHosting()
        {
            try
            {
                // Create a builder and build addressables
                var addressableToolData = LoadToolData();
                if (addressableToolData != null)
                {
                    AddressablesEditorManager.BuildAddressables();
                    return true;
                }
                else
                {
                    throw new Exception("Could not load AddressableToolData for building.");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to build Addressables: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Loads the AddressableToolData from the project
        /// </summary>
        private AddressableToolData LoadToolData()
        {
            // Find all AddressableToolData assets in the project
            string[] guids = AssetDatabase.FindAssets("t:AddressableToolData");
            if (guids.Length == 0)
            {
                Debug.LogError("No AddressableToolData found in project. Please create one first.");
                return null;
            }
            
            // Load the first found instance
            string path = AssetDatabase.GUIDToAssetPath(guids[0]);
            var data = AssetDatabase.LoadAssetAtPath<AddressableToolData>(path);
            
            if (data == null)
            {
                Debug.LogError($"Failed to load AddressableToolData at path: {path}");
                return null;
            }
            
            return data;
        }
    }
}