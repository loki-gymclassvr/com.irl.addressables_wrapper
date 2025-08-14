using UnityEngine;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using System;
using System.Collections.Generic;
using System.IO;
using Editor_Utility;
using UnityEditor.AddressableAssets.Build;

namespace Addressables_Wrapper.Editor.CDN
{
    public class CloudflareCDNUploaderWindow : EditorWindowBase
    {
        private CloudflareConfig cloudflareConfig;
        private CDNConfig cdnConfig;
        private AddressableToolData addressableToolData;
        private List<CDNConfig> availableConfigs = new List<CDNConfig>();
        private int selectedConfigIndex = 0;

        // UI Foldout states
        private bool showUploadOptions = true;
        private bool showConfigOptions = false;
        private bool showCloudflareConfig = false;
        private bool showAdvancedOptions = false;
        private bool showDangerZone = false;
        private bool showLocalHostingOptions = true;

        // Scroll position
        private Vector2 scrollPosition;

        // Status indicators
        private bool isUploading = false;
        private bool isDeleting = false;
        private bool isCleaning = false;
        private float uploadProgress = 0f;
        private string statusMessage = "";

        // Delete confirmation
        private string deleteConfirmText = "";
        private string deletePrefix = "";
        private readonly string deleteConfirmRequiredText = "DELETE";

        // Local hosting
        private AddressablesLocalHostingService localHostingService;
        private int localHostingPort = 8081;

        // Cloudflare R2 Service
        private CloudflareR2Service r2Service;

        // Add menu item
        [MenuItem("Tools/Addressables/CDN Hosting")]
        public static void ShowWindow()
        {
            GetWindow<CloudflareCDNUploaderWindow>("CDN Uploader");
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            HeaderTitle = "CDN Uploader Tool";
            
            // Find AddressableToolData
            if (addressableToolData == null)
            {
                addressableToolData =
                    AssetDatabase.LoadAssetAtPath<AddressableToolData>(
                        "Assets/Editor/AddressableTool/Data/AddressableToolData.asset");
            }

            // Find all CDN configs
            LoadAllConfigs();

            // Try to find existing Cloudflare config
            if (cloudflareConfig == null)
            {
                cloudflareConfig =
                    AssetDatabase.LoadAssetAtPath<CloudflareConfig>(
                        "Assets/Editor/AddressableTool/Data/CloudflareConfig.asset");

                // Load secret key from EditorPrefs if it exists
                if (cloudflareConfig != null && string.IsNullOrEmpty(cloudflareConfig.cloudflareSecretAccessKey))
                {
                    cloudflareConfig.cloudflareSecretAccessKey =
                        EditorPrefs.GetString("CloudflareSecretKey_" + cloudflareConfig.cloudflareAccessKey, "");
                }

                // Initialize R2 service if we have valid credentials
                InitializeR2Service();
            }

            // Initialize Local Hosting Service
            if (localHostingService == null)
            {
                localHostingService = new AddressablesLocalHostingService();

                // If the service is already running, update our state
                if (localHostingService.IsHostingServiceRunning)
                {
                    cdnConfig.localHostEnabled = true;
                    EditorUtility.SetDirty(cdnConfig);
                }
            }
        }

        private void OnDisable()
        {
            // Dispose the R2 service when the window is closed
            r2Service?.Dispose();
            r2Service = null;

            // Don't stop the hosting service when window is closed - let it keep running if it was started
        }

        // Initialize the R2 service with current credentials
        private void InitializeR2Service()
        {
            // Only initialize if we have valid credentials
            if (cloudflareConfig != null &&
                !string.IsNullOrEmpty(cloudflareConfig.cloudflareAccountId) &&
                !string.IsNullOrEmpty(cloudflareConfig.cloudflareAccessKey) &&
                !string.IsNullOrEmpty(cloudflareConfig.cloudflareSecretAccessKey))
            {
                // Dispose any existing service
                if (r2Service != null)
                {
                    r2Service.Dispose();
                }

                // Create a new service
                r2Service = new CloudflareR2Service(
                    cloudflareConfig.cloudflareAccountId,
                    cloudflareConfig.cloudflareAccessKey,
                    cloudflareConfig.cloudflareSecretAccessKey);
            }
        }

        private void OnGUI()
        {
            // Basic styling
            GUILayout.BeginVertical(EditorStyles.helpBox);
            // Draw the branded header using the base class method
            DrawHeader();

            // Check if we have AddressableToolData
            if (addressableToolData == null)
            {
                EditorGUILayout.HelpBox("AddressableToolData not found. Please create it first.", MessageType.Error);
                if (GUILayout.Button("Create AddressableToolData"))
                {
                    CreateAddressableToolData();
                }

                GUILayout.EndVertical();
                return;
            }

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            // Local Hosting Section
            DrawLocalHostingSection();

            // Check for Cloudflare config
            if (cloudflareConfig == null)
            {
                EditorGUILayout.HelpBox("No Cloudflare configuration found. Create a new configuration asset?",
                    MessageType.Warning);
                if (GUILayout.Button("Create Cloudflare Configuration"))
                {
                    CreateCloudflareConfig();
                }
            }

            // Check for CDN configs
            if (availableConfigs.Count == 0)
            {
                EditorGUILayout.HelpBox("No CDN configurations found. Create a new configuration asset?",
                    MessageType.Warning);
                if (GUILayout.Button("Create CDN Configuration"))
                {
                    CreateCDNConfig();
                    LoadAllConfigs();
                }

                EditorGUILayout.EndScrollView();
                GUILayout.EndVertical();
                return;
            }

            // Config selector
            string[] configNames = new string[availableConfigs.Count];
            for (int i = 0; i < availableConfigs.Count; i++)
            {
                configNames[i] = availableConfigs[i].targetDevice.ToString();
            }

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Target Device Config", GUILayout.Width(150));
            int newConfigIndex = EditorGUILayout.Popup(selectedConfigIndex, configNames);
            if (newConfigIndex != selectedConfigIndex)
            {
                selectedConfigIndex = newConfigIndex;
                cdnConfig = availableConfigs[selectedConfigIndex];

                // Update the AddressableToolData to match this config
                addressableToolData.buildTargetDevice = (BuildTargetDevice)cdnConfig.targetDevice;
                EditorUtility.SetDirty(addressableToolData);
            }

            if (GUILayout.Button("New", GUILayout.Width(60)))
            {
                CreateCDNConfig();
                LoadAllConfigs();
            }

            EditorGUILayout.EndHorizontal();

            // Sync the environment from AddressableToolData
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Environment", GUILayout.Width(150));

            // Disable environment selection if local hosting is enabled
            EditorGUI.BeginDisabledGroup(cdnConfig.localHostEnabled);

            EnvironmentType newEnvironment =
                (EnvironmentType)EditorGUILayout.EnumPopup(addressableToolData.environment);
            if (newEnvironment != addressableToolData.environment && !cdnConfig.localHostEnabled)
            {
                addressableToolData.environment = newEnvironment;
                EditorUtility.SetDirty(addressableToolData);
            }

            EditorGUI.EndDisabledGroup();

            if (cdnConfig.localHostEnabled)
            {
                GUILayout.Label("(Locked to local when hosting is enabled)", EditorStyles.miniLabel);
            }

            EditorGUILayout.EndHorizontal();

            // Sync the version from AddressableToolData
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Version", GUILayout.Width(150));
            string newVersion = EditorGUILayout.TextField(addressableToolData.releaseVersion);
            if (newVersion != addressableToolData.releaseVersion)
            {
                addressableToolData.releaseVersion = newVersion;
                EditorUtility.SetDirty(addressableToolData);
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();

            // Upload section
            showUploadOptions = EditorGUILayout.Foldout(showUploadOptions, "Upload Options", true);
            if (showUploadOptions)
            {
                GUILayout.BeginVertical(EditorStyles.helpBox);

                // Ensure version starts with "v"
                if (!string.IsNullOrEmpty(addressableToolData.releaseVersion) &&
                    !addressableToolData.releaseVersion.StartsWith("v"))
                {
                    EditorGUILayout.HelpBox("Version should start with 'v' (e.g., v1, v2.1)", MessageType.Info);
                }

                // Display remote path for clarity
                EditorGUILayout.LabelField("Remote Build Path",
                    cdnConfig.GetFormattedRemotePath(addressableToolData.releaseVersion,
                        addressableToolData.environment));
                EditorGUILayout.LabelField("Target Bucket", cdnConfig.GetBucketName(addressableToolData.environment));
                EditorGUILayout.LabelField("CDN URL", cdnConfig.GetCdnUrl(addressableToolData.environment));

                // Build and upload button
                EditorGUI.BeginDisabledGroup(
                    isUploading ||
                    cloudflareConfig == null ||
                    string.IsNullOrEmpty(addressableToolData.releaseVersion) ||
                    cdnConfig.localHostEnabled);

                if (GUILayout.Button(isUploading ? "Uploading..." : "Build and Upload Addressables"))
                {
                    // Ensure version starts with "v"
                    if (!string.IsNullOrEmpty(addressableToolData.releaseVersion) &&
                        !addressableToolData.releaseVersion.StartsWith("v"))
                    {
                        addressableToolData.releaseVersion = "v" + addressableToolData.releaseVersion;
                        EditorUtility.SetDirty(addressableToolData);
                        AssetDatabase.SaveAssetIfDirty(addressableToolData);
                    }

                    BuildAndUploadAddressables();
                    GUIUtility.ExitGUI();
                }

                EditorGUI.EndDisabledGroup();

                if (cdnConfig.localHostEnabled)
                {
                    EditorGUILayout.HelpBox("Upload is disabled when local hosting is enabled.", MessageType.Info);
                }

                // Progress bar when uploading
                if (isUploading)
                {
                    EditorGUI.ProgressBar(EditorGUILayout.GetControlRect(false, 20), uploadProgress, "Uploading...");
                    EditorGUILayout.LabelField(statusMessage);
                }

                GUILayout.EndVertical();
            }

            EditorGUILayout.Space();

            // CDN Configuration section
            showConfigOptions = EditorGUILayout.Foldout(showConfigOptions, "CDN Configuration", true);
            if (showConfigOptions && cdnConfig != null)
            {
                GUILayout.BeginVertical(EditorStyles.helpBox);

                // Target device
                cdnConfig.targetDevice = (BuildTargetDevice)EditorGUILayout.EnumFlagsField("Target Device", cdnConfig.targetDevice);

                // Platform name (if your CDNConfig has this property)
                if (typeof(CDNConfig).GetField("platformName") != null)
                {
                    cdnConfig.platformName = EditorGUILayout.TextField("Platform Name", cdnConfig.platformName);
                }

                // Product name (if your CDNConfig has this property)
                if (typeof(CDNConfig).GetField("productName") != null)
                {
                    cdnConfig.productName = EditorGUILayout.TextField("Product Name", cdnConfig.productName);
                }

                // Path configuration
                EditorGUILayout.LabelField("Path Configuration", EditorStyles.boldLabel);
                cdnConfig.remotePathFormat = EditorGUILayout.TextField("Path Format", cdnConfig.remotePathFormat);
                EditorGUILayout.HelpBox("Use {0} for device type and {1} for version in the path format.",
                    MessageType.Info);

                // Bucket configuration
                EditorGUILayout.LabelField("Bucket Configuration", EditorStyles.boldLabel);
                cdnConfig.bucketNameFormat =
                    EditorGUILayout.TextField("Bucket Name Format", cdnConfig.bucketNameFormat);

                // This help box changes depending on whether platformName and productName exist
                if (typeof(CDNConfig).GetField("platformName") != null &&
                    typeof(CDNConfig).GetField("productName") != null)
                {
                    EditorGUILayout.HelpBox(
                        "Use {0} for device, {1} for platform, {2} for product, and {3} for environment.",
                        MessageType.Info);
                }
                else
                {
                    EditorGUILayout.HelpBox(
                        "Use {0} for device type and {1} for environment in the bucket name format.", MessageType.Info);
                }

                // CDN URLs
                EditorGUILayout.LabelField("CDN URLs", EditorStyles.boldLabel);
                cdnConfig.developmentCdnUrl =
                    EditorGUILayout.TextField("Development CDN URL", cdnConfig.developmentCdnUrl);
                cdnConfig.stagingCdnUrl = EditorGUILayout.TextField("Staging CDN URL", cdnConfig.stagingCdnUrl);
                cdnConfig.productionCdnUrl =
                    EditorGUILayout.TextField("Production CDN URL", cdnConfig.productionCdnUrl);

                // Save config button
                if (GUILayout.Button("Save CDN Configuration"))
                {
                    EditorUtility.SetDirty(cdnConfig);
                    AssetDatabase.SaveAssetIfDirty(cdnConfig);
                    EditorUtility.DisplayDialog("Configuration Saved", "CDN configuration has been saved successfully.",
                        "OK");
                }

                GUILayout.EndVertical();
            }

            EditorGUILayout.Space();

            // Cloudflare configuration section
            showCloudflareConfig = EditorGUILayout.Foldout(showCloudflareConfig, "Cloudflare Configuration", true);
            if (showCloudflareConfig && cloudflareConfig != null)
            {
                GUILayout.BeginVertical(EditorStyles.helpBox);

                // Cloudflare configuration
                EditorGUILayout.LabelField("Cloudflare R2 Credentials", EditorStyles.boldLabel);
                cloudflareConfig.cloudflareAccountId =
                    EditorGUILayout.TextField("Account ID", cloudflareConfig.cloudflareAccountId);
                cloudflareConfig.cloudflareAccessKey =
                    EditorGUILayout.TextField("Access Key", cloudflareConfig.cloudflareAccessKey);

                // Secret key field with masking
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PrefixLabel("Secret Access Key");

                // If we have a value but it's masked, use the cached version
                string displayKey = !string.IsNullOrEmpty(cloudflareConfig.cloudflareSecretAccessKey)
                    ? "••••••••••••••••••"
                    : "";
                string newKey = EditorGUILayout.PasswordField(displayKey);

                // Only update if user changed the value
                if (newKey != displayKey)
                {
                    cloudflareConfig.cloudflareSecretAccessKey = newKey;
                    // Save to EditorPrefs
                    if (!string.IsNullOrEmpty(cloudflareConfig.cloudflareAccessKey) && !string.IsNullOrEmpty(newKey))
                    {
                        EditorPrefs.SetString("CloudflareSecretKey_" + cloudflareConfig.cloudflareAccessKey, newKey);
                    }
                }

                EditorGUILayout.EndHorizontal();

                // Test connection button
                if (GUILayout.Button("Test Cloudflare Connection"))
                {
                    TestCloudflareConnection();
                }

                // Save config button
                if (GUILayout.Button("Save Cloudflare Configuration"))
                {
                    EditorUtility.SetDirty(cloudflareConfig);
                    AssetDatabase.SaveAssetIfDirty(cloudflareConfig);
                    EditorUtility.DisplayDialog("Configuration Saved",
                        "Cloudflare configuration has been saved successfully.", "OK");
                }

                GUILayout.EndVertical();
            }

            EditorGUILayout.Space();

            // Advanced options
            showAdvancedOptions = EditorGUILayout.Foldout(showAdvancedOptions, "Advanced Options", true);
            if (showAdvancedOptions)
            {
                GUILayout.BeginVertical(EditorStyles.helpBox);

                // Update addressables profile
                if (GUILayout.Button("Update Addressables Profile"))
                {
                    UpdateAddressablesProfile();
                }

                // Clean local builds
                if (GUILayout.Button("Clean Build Cache"))
                {
                    CleanBuildCache();
                }

                GUILayout.EndVertical();
            }

            EditorGUILayout.Space();

            // Danger Zone
            showDangerZone = EditorGUILayout.Foldout(showDangerZone, "Danger Zone", true);
            if (showDangerZone)
            {
                GUILayout.BeginVertical(EditorStyles.helpBox);

                // Warning message
                EditorGUILayout.HelpBox("WARNING: These actions are destructive and cannot be undone!",
                    MessageType.Warning);

                // Delete bucket contents section
                EditorGUILayout.LabelField("Delete Target Bucket Contents", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(
                    $"This will delete ALL files in bucket: {cdnConfig?.GetBucketName(addressableToolData.environment)}",
                    MessageType.Error);

                // Optional prefix field
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PrefixLabel("Path Prefix (Optional)");
                deletePrefix = EditorGUILayout.TextField(deletePrefix);
                EditorGUILayout.EndHorizontal();

                // Confirmation field
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PrefixLabel($"Type '{deleteConfirmRequiredText}' to confirm");
                deleteConfirmText = EditorGUILayout.TextField(deleteConfirmText);
                EditorGUILayout.EndHorizontal();

                // Delete button
                EditorGUI.BeginDisabledGroup(isDeleting || deleteConfirmText != deleteConfirmRequiredText ||
                                             cloudflareConfig == null);
                if (GUILayout.Button(isDeleting ? "Deleting..." : "Delete All Bucket Contents"))
                {
                    DeleteBucketContents(deletePrefix);
                }

                EditorGUI.EndDisabledGroup();

                // Progress bar when deleting
                if (isDeleting)
                {
                    EditorGUI.ProgressBar(EditorGUILayout.GetControlRect(false, 20), uploadProgress, "Deleting...");
                    EditorGUILayout.LabelField(statusMessage);
                }

                GUILayout.EndVertical();
            }

            // Multipart cleanup section
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Clean up incomplete multipart uploads", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "This will clean up any abandoned or incomplete multipart uploads that might be stuck in the bucket.",
                MessageType.Info);

            // Optional prefix field
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("Path Prefix (Optional)");
            string cleanupPrefix = EditorGUILayout.TextField("");
            EditorGUILayout.EndHorizontal();

            // Cleanup button
            EditorGUI.BeginDisabledGroup(isCleaning || cloudflareConfig == null);
            if (GUILayout.Button(isCleaning ? "Cleaning..." : "Clean up Multipart Uploads"))
            {
                CleanupMultipartUploads(cleanupPrefix);
            }

            EditorGUI.EndDisabledGroup();

            // Progress bar when cleaning
            if (isCleaning)
            {
                EditorGUI.ProgressBar(EditorGUILayout.GetControlRect(false, 20), uploadProgress, "Cleaning...");
                EditorGUILayout.LabelField(statusMessage);
            }

            EditorGUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        #region Local Hosting Methods

        private void DrawLocalHostingSection()
        {
            showLocalHostingOptions = EditorGUILayout.Foldout(showLocalHostingOptions, "Local Hosting Options", true);

            if (showLocalHostingOptions)
            {
                GUILayout.BeginVertical(EditorStyles.helpBox);

                EditorGUI.BeginChangeCheck();
                // Local hosting toggle
                cdnConfig.localHostEnabled = EditorGUILayout.Toggle("Enable Local Hosting", cdnConfig.localHostEnabled);
                if (EditorGUI.EndChangeCheck())
                {
                    EditorUtility.SetDirty(cdnConfig);
                }
                
                if (cdnConfig.localHostEnabled)
                {
                    // If disabling, stop the hosting service
                    if (!cdnConfig.localHostEnabled && localHostingService.IsHostingServiceRunning)
                    {
                        StopLocalHosting();
                    }
                }

                if (cdnConfig.localHostEnabled)
                {
                    // Port selection
                    localHostingPort = EditorGUILayout.IntField("Local Hosting Port", localHostingPort);

                    // Show current local hosting URL if service is running
                    if (localHostingService.IsHostingServiceRunning)
                    {
                        EditorGUILayout.HelpBox(
                            $"Hosting service is running at:\n{localHostingService.HostingServiceUrl}",
                            MessageType.Info);

                        // Stop hosting button
                        if (GUILayout.Button("Stop Local Hosting"))
                        {
                            StopLocalHosting();
                        }
                    }
                    else
                    {
                        // Start hosting button
                        if (GUILayout.Button("Start Local Hosting"))
                        {
                            StartLocalHosting(localHostingPort);
                        }
                    }

                    // Build for local hosting button
                    if (GUILayout.Button("Build Addressables for Local Hosting"))
                    {
                        BuildForLocalHosting();
                        GUIUtility.ExitGUI();
                    }

                    EditorGUILayout.HelpBox(
                        "Local hosting allows you to test your Addressables content without uploading to a CDN. It creates a local HTTP server to serve your content.",
                        MessageType.Info);
                }

                GUILayout.EndVertical();
            }
        }

        private void StartLocalHosting(int port)
        {
            try
            {
                // First update the profile to use local hosting
                localHostingService.UpdateAddressablesProfileForLocalHosting(cdnConfig.targetDevice, addressableToolData.releaseVersion, port);

                // Start the hosting service
                string hostingUrl = localHostingService.StartHostingService(port);

                // Update UI state
                cdnConfig.localHostEnabled = true;
                EditorUtility.SetDirty(cdnConfig);

                EditorUtility.DisplayDialog("Local Hosting Started",
                    $"Local hosting service started at:\n{hostingUrl}\n\nRemember to build your Addressables to test with local hosting.",
                    "OK");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to start local hosting: {ex.Message}");
                EditorUtility.DisplayDialog("Error", $"Failed to start local hosting: {ex.Message}", "OK");
            }
        }

        private void StopLocalHosting()
        {
            try
            {
                localHostingService.StopHostingService();
                cdnConfig.localHostEnabled = false;
                EditorUtility.SetDirty(cdnConfig);
                
                AddressablesEditorManager.UpdateProfile();

                EditorUtility.DisplayDialog("Local Hosting Stopped",
                    "Local hosting service has been stopped.", "OK");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to stop local hosting: {ex.Message}");
                EditorUtility.DisplayDialog("Error", $"Failed to stop local hosting: {ex.Message}", "OK");
            }
        }

        private void BuildForLocalHosting()
        {
            try
            {
                // First update the profile to use local hosting
                if (!localHostingService.UpdateAddressablesProfileForLocalHosting(cdnConfig.targetDevice, addressableToolData.releaseVersion, localHostingPort))
                {
                    EditorUtility.DisplayDialog("Error", "Failed to update Addressables profile for local hosting.",
                        "OK");
                    return;
                }

                // Build the Addressables
                if (!localHostingService.BuildAddressablesForLocalHosting())
                {
                    EditorUtility.DisplayDialog("Error", "Failed to build Addressables for local hosting.", "OK");
                    return;
                }

                // If hosting isn't running yet, start it now
                if (!localHostingService.IsHostingServiceRunning)
                {
                    string hostingUrl = localHostingService.StartHostingService(localHostingPort);
                    cdnConfig.localHostEnabled = true;
                    EditorUtility.SetDirty(cdnConfig);

                    EditorUtility.DisplayDialog("Build Complete",
                        $"Addressables built successfully and local hosting started at:\n{hostingUrl}", "OK");
                }
                else
                {
                    EditorUtility.DisplayDialog("Build Complete",
                        "Addressables built successfully for local hosting.", "OK");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to build for local hosting: {ex.Message}");
                EditorUtility.DisplayDialog("Error", $"Failed to build for local hosting: {ex.Message}", "OK");
            }
        }

        #endregion

        #region CloudflareR2 Methods

        private async void CleanupMultipartUploads(string prefix = null)
        {
            if (cloudflareConfig == null ||
                string.IsNullOrEmpty(cloudflareConfig.cloudflareAccountId) ||
                string.IsNullOrEmpty(cloudflareConfig.cloudflareAccessKey) ||
                string.IsNullOrEmpty(cloudflareConfig.cloudflareSecretAccessKey))
            {
                EditorUtility.DisplayDialog("Error", "Please fill in all Cloudflare credentials first.", "OK");
                return;
            }

            string bucketName = cdnConfig.GetBucketName(addressableToolData.environment);
            bool confirmCleanup = EditorUtility.DisplayDialog(
                "Confirm Cleanup",
                $"You are about to clean up all incomplete multipart uploads in bucket '{bucketName}'" +
                (string.IsNullOrEmpty(prefix) ? "" : $" with prefix '{prefix}'") +
                ".\n\nThis will abort any uploads that might be in progress. Continue?",
                "Yes, Clean Up", "Cancel");

            if (!confirmCleanup)
            {
                return;
            }

            try
            {
                isCleaning = true;
                uploadProgress = 0f;
                statusMessage = "Preparing to clean up multipart uploads...";

                // Initialize R2 service if needed
                if (r2Service == null)
                {
                    InitializeR2Service();
                }

                // Clean up multipart uploads
                await r2Service.CleanupMultipartUploads(
                    bucketName,
                    prefix,
                    (progress, message) =>
                    {
                        uploadProgress = progress;
                        statusMessage = message;
                        Repaint();
                    }
                );

                // Reset UI state
                isCleaning = false;
                uploadProgress = 0f;

                EditorUtility.DisplayDialog("Cleanup Complete",
                    $"Successfully cleaned up incomplete multipart uploads from bucket '{bucketName}'.", "OK");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Cleanup failed: {ex.Message}");
                isCleaning = false;
                uploadProgress = 0f;
                EditorUtility.DisplayDialog("Error", $"Cleanup failed: {ex.Message}", "OK");
            }
        }

        private async void DeleteBucketContents(string prefix = null)
        {
            if (cloudflareConfig == null ||
                string.IsNullOrEmpty(cloudflareConfig.cloudflareAccountId) ||
                string.IsNullOrEmpty(cloudflareConfig.cloudflareAccessKey) ||
                string.IsNullOrEmpty(cloudflareConfig.cloudflareSecretAccessKey))
            {
                EditorUtility.DisplayDialog("Error", "Please fill in all Cloudflare credentials first.", "OK");
                return;
            }

            // Additional confirmation dialog
            string bucketName = cdnConfig.GetBucketName(addressableToolData.environment);
            bool confirmDelete = EditorUtility.DisplayDialog(
                "Confirm Deletion",
                $"You are about to delete ALL content from bucket '{bucketName}'" +
                (string.IsNullOrEmpty(prefix) ? "" : $" with prefix '{prefix}'") +
                ".\n\nThis action CANNOT be undone. Are you absolutely sure?",
                "Yes, Delete Everything", "Cancel");

            if (!confirmDelete)
            {
                return;
            }

            try
            {
                isDeleting = true;
                uploadProgress = 0f;
                statusMessage = "Preparing to delete bucket contents...";

                // Initialize R2 service if needed
                if (r2Service == null)
                {
                    InitializeR2Service();
                }

                // Delete all bucket contents
                await r2Service.DeleteAllBucketContents(
                    bucketName,
                    prefix,
                    (progress, message) =>
                    {
                        uploadProgress = progress;
                        statusMessage = message;
                        Repaint();
                    }
                );

                // Reset UI state
                isDeleting = false;
                uploadProgress = 0f;
                deleteConfirmText = "";

                EditorUtility.DisplayDialog("Deletion Complete",
                    $"Successfully deleted all contents from bucket '{bucketName}'.", "OK");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Deletion failed: {ex.Message}");
                isDeleting = false;
                uploadProgress = 0f;
                EditorUtility.DisplayDialog("Error", $"Deletion failed: {ex.Message}", "OK");
            }
        }

        private async void TestCloudflareConnection()
        {
            if (cloudflareConfig == null ||
                string.IsNullOrEmpty(cloudflareConfig.cloudflareAccountId) ||
                string.IsNullOrEmpty(cloudflareConfig.cloudflareAccessKey) ||
                string.IsNullOrEmpty(cloudflareConfig.cloudflareSecretAccessKey))
            {
                EditorUtility.DisplayDialog("Error", "Please fill in all Cloudflare credentials first.", "OK");
                return;
            }

            try
            {
                statusMessage = "Testing connection to Cloudflare R2...";

                // Initialize R2 service if needed
                if (r2Service == null)
                {
                    InitializeR2Service();
                }

                // Use the service to test connection
                if (r2Service != null)
                {
                    List<string> buckets = await r2Service.TestConnection();

                    string bucketInfo = string.Join(", ", buckets);
                    EditorUtility.DisplayDialog("Connection Successful",
                        $"Successfully connected to Cloudflare R2.\nBuckets found: {bucketInfo}", "OK");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Cloudflare connection test failed: {ex.Message}");
                EditorUtility.DisplayDialog("Connection Failed",
                    $"Failed to connect to Cloudflare R2. Error: {ex.Message}", "OK");
            }

            statusMessage = "";
        }

        private async void BuildAndUploadAddressables()
        {
            if (cloudflareConfig == null ||
                string.IsNullOrEmpty(cloudflareConfig.cloudflareAccountId) ||
                string.IsNullOrEmpty(cloudflareConfig.cloudflareAccessKey) ||
                string.IsNullOrEmpty(cloudflareConfig.cloudflareSecretAccessKey))
            {
                EditorUtility.DisplayDialog("Error", "Please fill in all Cloudflare credentials first.", "OK");
                return;
            }

            try
            {
                isUploading = true;
                uploadProgress = 0f;
                statusMessage = "Updating Addressables profile...";

                // Update the Addressables profile first
                UpdateAddressablesProfile();

                // Get the build path from the addressables settings
                AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
                string profileId = settings.activeProfileId;

                // Correctly get the local build path using ProfileValueReference
                string buildPath = settings.profileSettings.GetValueByName(profileId, "Remote.BuildPath");
                if (string.IsNullOrEmpty(buildPath))
                {
                    throw new Exception($"Could not find Remote.BuildPath {buildPath} in Addressables profile.");
                }

                buildPath = Environment.ExpandEnvironmentVariables(buildPath);
                
                string bucketName = cdnConfig.GetBucketName(addressableToolData.environment);
                string remotePathPrefix = $"{addressableToolData.releaseVersion}";

                // Initialize R2 service if needed
                if (r2Service == null)
                {
                    InitializeR2Service();
                }
                
                Debug.Log("Overriding Addressable settings...");
                AddressablesEditorManager.OverrideAddressableSettings();
                
                Debug.Log("Cleaning remote addressables...");
                AddressablesEditorManager.CleanRemoteAddressables();
                
                Debug.Log("Running duplicate isolation using analyze...");
                AddressablesEditorManager.IsolateDuplicatesUsingAnalyze();
                
                //Set groups remote based on configuration in GroupConfig
                AddressablesEditorManager.RunGroupConfig();
                
                Debug.Log("Overriding all addressable group settings...");
                AddressableSettingsOverrider.OverrideAllAddressableGroupSettings();

                // Build the addressables
                statusMessage = "Building Addressables...";
                AddressableAssetSettings.BuildPlayerContent(out AddressablesPlayerBuildResult result);

                // Check for build errors
                if (!string.IsNullOrEmpty(result.Error))
                {
                    throw new Exception($"Failed to build Addressables: {result.Error}");
                }

                // Upload to R2
                await r2Service.UploadDirectory(
                    buildPath,
                    bucketName,
                    remotePathPrefix,
                    (progress, file) =>
                    {
                        uploadProgress = progress;
                        statusMessage = $"Uploading: {file}";
                        Repaint();
                    }
                );

                isUploading = false;
                uploadProgress = 0f;
                EditorUtility.DisplayDialog("Upload Complete",
                    $"Successfully uploaded Addressables to {bucketName}.", "OK");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Build and upload failed: {ex.Message}");
                isUploading = false;
                uploadProgress = 0f;
                EditorUtility.DisplayDialog("Error", $"Build and upload failed: {ex.Message}", "OK");
            }
        }

        #endregion

        #region Helper Methods

        // Create the AddressableToolData asset
        private void CreateAddressableToolData()
        {
            // Create directory if it doesn't exist
            string dirPath = "Assets/Editor/AddressableTool/Data";
            if (!AssetDatabase.IsValidFolder(dirPath))
            {
                // Create the full directory path if it doesn't exist
                string[] folders = dirPath.Split('/');
                string currentPath = folders[0]; // "Assets"

                for (int i = 1; i < folders.Length; i++)
                {
                    string folderToCheck = currentPath + "/" + folders[i];
                    if (!AssetDatabase.IsValidFolder(folderToCheck))
                    {
                        AssetDatabase.CreateFolder(currentPath, folders[i]);
                    }

                    currentPath = folderToCheck;
                }
            }

            // Create AddressableToolData
            addressableToolData = CreateInstance<AddressableToolData>();
            AssetDatabase.CreateAsset(addressableToolData,
                "Assets/Editor/AddressableTool/Data/AddressableToolData.asset");
            AssetDatabase.SaveAssetIfDirty(addressableToolData);
            AssetDatabase.Refresh();
        }

        // Create the CloudflareConfig asset
        private void CreateCloudflareConfig()
        {
            // Create directory if it doesn't exist
            string dirPath = "Assets/Editor/AddressableTool/Data";
            if (!AssetDatabase.IsValidFolder(dirPath))
            {
                // Create the full directory path if it doesn't exist
                string[] folders = dirPath.Split('/');
                string currentPath = folders[0]; // "Assets"

                for (int i = 1; i < folders.Length; i++)
                {
                    string folderToCheck = currentPath + "/" + folders[i];
                    if (!AssetDatabase.IsValidFolder(folderToCheck))
                    {
                        AssetDatabase.CreateFolder(currentPath, folders[i]);
                    }

                    currentPath = folderToCheck;
                }
            }

            // Create config asset
            cloudflareConfig = CreateInstance<CloudflareConfig>();
            AssetDatabase.CreateAsset(cloudflareConfig,
                "Assets/Editor/AddressableTool/Data/CloudflareConfig.asset");
            AssetDatabase.SaveAssetIfDirty(cloudflareConfig);
            AssetDatabase.Refresh();

            EditorUtility.FocusProjectWindow();
            Selection.activeObject = cloudflareConfig;
        }

        // Create a CDN config asset
        private void CreateCDNConfig()
        {
            // Create directory if it doesn't exist
            string dirPath = "Assets/Editor/AddressableTool/Data";
            if (!AssetDatabase.IsValidFolder(dirPath))
            {
                // Create the full directory path if it doesn't exist
                string[] folders = dirPath.Split('/');
                string currentPath = folders[0]; // "Assets"

                for (int i = 1; i < folders.Length; i++)
                {
                    string folderToCheck = currentPath + "/" + folders[i];
                    if (!AssetDatabase.IsValidFolder(folderToCheck))
                    {
                        AssetDatabase.CreateFolder(currentPath, folders[i]);
                    }

                    currentPath = folderToCheck;
                }
            }

            // Create config asset
            CDNConfig newConfig = CreateInstance<CDNConfig>();

            // If we have AddressableToolData, use its target device for this config
            if (addressableToolData != null)
            {
                newConfig.targetDevice = addressableToolData.buildTargetDevice;
            }

            AssetDatabase.CreateAsset(newConfig, "Assets/Editor/AddressableTool/Data/CDNConfig.asset");
            AssetDatabase.SaveAssetIfDirty(newConfig);
            AssetDatabase.Refresh();

            availableConfigs.Add(newConfig);
            selectedConfigIndex = availableConfigs.Count - 1;
            cdnConfig = newConfig;

            EditorUtility.FocusProjectWindow();
            Selection.activeObject = newConfig;
        }

        // Load all available CDN configs
        private void LoadAllConfigs()
        {
            availableConfigs.Clear();

            // Try direct loading first
            CDNConfig directConfig =
                AssetDatabase.LoadAssetAtPath<CDNConfig>("Assets/Editor/AddressableTool/Data/CDNConfig.asset");
            if (directConfig != null)
            {
                availableConfigs.Add(directConfig);
            }

            // Also search for any other CDN configs in the project
            string[] guids = AssetDatabase.FindAssets("t:CDNConfig");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                CDNConfig config = AssetDatabase.LoadAssetAtPath<CDNConfig>(path);

                // Skip if it's the one we already added
                if (config != null && !availableConfigs.Contains(config))
                {
                    availableConfigs.Add(config);
                }
            }

            // Set default config if available
            if (availableConfigs.Count > 0)
            {
                // Try to find a config matching the current build target device
                if (addressableToolData != null)
                {
                    for (int i = 0; i < availableConfigs.Count; i++)
                    {
                        if (availableConfigs[i].targetDevice == addressableToolData.buildTargetDevice)
                        {
                            selectedConfigIndex = i;
                            break;
                        }
                    }
                }

                cdnConfig = availableConfigs[selectedConfigIndex];
            }
        }

        // Update addressables profile
       private void UpdateAddressablesProfile()
        {
            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                EditorUtility.DisplayDialog("Error",
                    "Addressable Asset Settings not found. Have you set up Addressables in your project?", "OK");
                return;
            }

            // If local hosting is enabled, use the local hosting profile update instead
            if (cdnConfig.localHostEnabled)
            {
                localHostingService.UpdateAddressablesProfileForLocalHosting(cdnConfig.targetDevice, addressableToolData.releaseVersion, localHostingPort);
                return;
            }

            // Construct the target profile name
            string targetProfileName = $"{cdnConfig.targetDevice.ToString().ToLower()}-{addressableToolData.environment.ToString().ToLower()}";
            
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

            // Update the remote paths for this profile
            string remotePath = $"{cdnConfig.GetCdnUrl(addressableToolData.environment)}/{addressableToolData.releaseVersion}";
            settings.profileSettings.SetValue(targetProfileId, "Remote.BuildPath",
                cdnConfig.GetFormattedRemotePath(addressableToolData.releaseVersion, addressableToolData.environment));
            settings.profileSettings.SetValue(targetProfileId, "Remote.LoadPath", remotePath);

            // Save the settings
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssetIfDirty(settings);

            EditorUtility.DisplayDialog("Profile Updated",
                $"Set active profile to '{targetProfileName}' and updated remote load path to:\n{remotePath}", "OK");
        }

        // Clean build cache
        private void CleanBuildCache()
        {
            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                EditorUtility.DisplayDialog("Error", "Addressable Asset Settings not found.", "OK");
                return;
            }

            try
            {
                AddressableAssetSettings.CleanPlayerContent();
                string remoteBuildPath = Application.dataPath.Replace("/Assets", $"/ServerData");
                if (Directory.Exists(remoteBuildPath))
                {
                    Directory.Delete(remoteBuildPath, true);
                }

                EditorUtility.DisplayDialog("Cleanup Complete", "Successfully cleaned local Addressables build cache.",
                    "OK");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error cleaning Addressables build: {ex.Message}");
                EditorUtility.DisplayDialog("Cleanup Failed", $"Failed to clean local builds. Error: {ex.Message}",
                    "OK");
            }
        }

        #endregion
    }
}