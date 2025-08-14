using UnityEngine;
using UnityEditor;
using System;
using System.IO;
using System.Threading.Tasks;
using Addressables_Wrapper.Editor.CDN;

namespace Addressables_Wrapper.Editor
{
    /// <summary>
    /// Provides static methods for Cloudflare R2 operations that can be called from batch mode
    /// </summary>
    public static class CloudflareBatchOperations
    {
        /// <summary>
        /// Uploads addressable content to the CDN.
        /// Called from command line with -executeMethod Addressables_Wrapper.Editor.CloudflareBatchOperations.UploadToCDN
        /// </summary>
        public static void UploadToCDN()
        {
            try
            {
                Debug.Log("Starting CDN upload in batch mode...");
                
                // Parse command line arguments
                string releaseVersion = BatchModeUtils.GetCommandLineArgValue("-releaseVersion");
                bool cleanupMultipartUploads = BatchModeUtils.GetBoolArgValue("-cleanupMultipartUploads", true);
                
                // Get target device and environment
                BuildTargetDevice targetDevice = BatchModeUtils.GetEnumArgValue("-targetDevice", BuildTargetDevice.Quest);
                EnvironmentType environment = BatchModeUtils.GetEnumArgValue("-environment", EnvironmentType.Development);
                
                Debug.Log($"Target device: {targetDevice}");
                Debug.Log($"Environment: {environment}");
                Debug.Log($"Release version: {releaseVersion}");
                Debug.Log($"Cleanup multipart uploads: {cleanupMultipartUploads}");
                
                // Load the necessary configuration
                BatchModeUtils.LoadAndConfigureSettings(targetDevice, environment, releaseVersion);
                
                // Upload to CDN
                UploadAddressablesToCDN(targetDevice, environment, releaseVersion);
                
                // Cleanup multipart uploads if requested
                if (cleanupMultipartUploads)
                {
                    CleanupMultipartUploadsAfterUpload(targetDevice, environment);
                }
                
                Debug.Log("CDN upload (and cleanup) completed successfully.");
                BatchModeUtils.ExitWithCode(true);
            }
            catch (Exception ex)
            {
                Debug.LogError($"CDN upload failed: {ex.Message}");
                Debug.LogException(ex);
                BatchModeUtils.ExitWithCode(false);
            }
        }
        
        /// <summary>
        /// Deletes all contents from a target bucket.
        /// Called from command line with -executeMethod Addressables_Wrapper.Editor.CloudflareBatchOperations.DeleteBucketContents
        /// </summary>
        public static void DeleteBucketContents()
        {
            try
            {
                Debug.Log("Starting bucket cleanup in batch mode...");
                
                // Parse command line arguments
                string prefix = BatchModeUtils.GetCommandLineArgValue("-prefix"); // Optional
                
                // Get target device and environment
                BuildTargetDevice targetDevice = BatchModeUtils.GetEnumArgValue("-targetDevice", BuildTargetDevice.Quest);
                EnvironmentType environment = BatchModeUtils.GetEnumArgValue("-environment", EnvironmentType.Development);
                
                Debug.Log($"Target device: {targetDevice}");
                Debug.Log($"Environment: {environment}");
                if (!string.IsNullOrEmpty(prefix))
                    Debug.Log($"Prefix: {prefix}");
                
                // Load necessary configurations
                var (cdnConfig, cloudflareConfig, r2Service) = LoadConfigurations(targetDevice);
                
                // Get bucket name
                string bucketName = cdnConfig.GetBucketName(environment);
                Debug.Log($"Deleting contents from bucket: {bucketName}");
                
                // Execute the actual delete operation (implemented as async)
                ExecuteDeleteOperation(r2Service, bucketName, prefix).Wait();
                
                Debug.Log($"Successfully deleted contents from bucket: {bucketName}");
                BatchModeUtils.ExitWithCode(true);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Bucket cleanup failed: {ex.Message}");
                Debug.LogException(ex);
                BatchModeUtils.ExitWithCode(false);
            }
        }
        
        /// <summary>
        /// Cleans up incomplete multipart uploads from a target bucket.
        /// Called from command line with -executeMethod Addressables_Wrapper.Editor.CloudflareBatchOperations.CleanupMultipartUploads
        /// </summary>
        public static void CleanupMultipartUploads()
        {
            try
            {
                Debug.Log("Starting multipart uploads cleanup in batch mode...");
                
                // Parse command line arguments
                string prefix = BatchModeUtils.GetCommandLineArgValue("-prefix"); // Optional
                
                // Get target device and environment
                BuildTargetDevice targetDevice = BatchModeUtils.GetEnumArgValue("-targetDevice", BuildTargetDevice.Quest);
                EnvironmentType environment = BatchModeUtils.GetEnumArgValue("-environment", EnvironmentType.Development);
                
                Debug.Log($"Target device: {targetDevice}");
                Debug.Log($"Environment: {environment}");
                if (!string.IsNullOrEmpty(prefix))
                    Debug.Log($"Prefix: {prefix}");
                
                // Load necessary configurations
                var (cdnConfig, cloudflareConfig, r2Service) = LoadConfigurations(targetDevice);
                
                // Get bucket name
                string bucketName = cdnConfig.GetBucketName(environment);
                Debug.Log($"Cleaning up multipart uploads from bucket: {bucketName}");
                
                // Execute the actual cleanup operation
                ExecuteMultipartCleanupOperation(r2Service, bucketName, prefix).Wait();
                
                Debug.Log($"Successfully cleaned up multipart uploads from bucket: {bucketName}");
                BatchModeUtils.ExitWithCode(true);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Multipart uploads cleanup failed: {ex.Message}");
                Debug.LogException(ex);
                BatchModeUtils.ExitWithCode(false);
            }
        }
        
        // Helper method for cleaning up multipart uploads as part of the upload process
        private static void CleanupMultipartUploadsAfterUpload(BuildTargetDevice targetDevice, EnvironmentType environment)
        {
            try
            {
                Debug.Log("Starting post-upload multipart cleanup...");
                
                // Load necessary configurations
                var (cdnConfig, cloudflareConfig, r2Service) = LoadConfigurations(targetDevice);
                
                // Get bucket name
                string bucketName = cdnConfig.GetBucketName(environment);
                Debug.Log($"Cleaning up multipart uploads from bucket: {bucketName}");
                
                // Execute the multipart cleanup
                ExecuteMultipartCleanupOperation(r2Service, bucketName, null).Wait();
                
                Debug.Log("Post-upload multipart cleanup completed successfully.");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Post-upload multipart cleanup failed: {ex.Message}. Continuing anyway.");
                // We don't want to fail the entire upload process if just the cleanup fails
            }
        }
        
        // Upload Addressables to CDN
        // Upload Addressables to CDN
        private static void UploadAddressablesToCDN(BuildTargetDevice targetDevice, EnvironmentType environment, string releaseVersion)
        {
            // Load configurations
            var (cdnConfig, cloudflareConfig, r2Service) = LoadConfigurations(targetDevice);
            
            // Get addressable build path from Unity settings
            var settings = UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject.Settings;
            string profileId = settings.activeProfileId;
            string buildPath = settings.profileSettings.GetValueByName(profileId, "Remote.BuildPath");
            
            if (string.IsNullOrEmpty(buildPath))
            {
                throw new Exception("Could not find Remote.BuildPath in Addressables profile");
            }
            
            buildPath = Environment.ExpandEnvironmentVariables(buildPath);
            
            // Get bucket name and remote path
            string bucketName = cdnConfig.GetBucketName(environment);
            string remotePathPrefix =  releaseVersion;
            
            Debug.Log($"Uploading from: {buildPath}");
            Debug.Log($"Target bucket: {bucketName}");
            Debug.Log($"Remote path prefix: {remotePathPrefix}");
            
            try
            {
                // Count files to upload first to validate we're ready
                string[] files = Directory.GetFiles(buildPath, "*.*", SearchOption.AllDirectories);
                Debug.Log($"Found {files.Length} files to upload from {buildPath}");
                
                if (files.Length == 0)
                {
                    Debug.LogWarning($"No files found in build path: {buildPath}. Check if Addressables were built correctly.");
                }
                
                // Log more details about the S3 client configuration
                var s3Client = r2Service.GetS3Client();
                if (s3Client != null)
                {
                    Debug.Log($"S3 Client info - Max Error Retry: {s3Client.Config.MaxErrorRetry}, " +
                              $"Timeout: {s3Client.Config.Timeout}, " +
                              $"ReadWriteTimeout: {s3Client.Config.ReadWriteTimeout}, " +
                              $"ThrottleRetries: {s3Client.Config.ThrottleRetries}");
                }
                
                // Execute the upload with more verbose logging
                Task uploadTask = r2Service.UploadDirectory(
                    buildPath,
                    bucketName,
                    remotePathPrefix,
                    (progress, file) => {
                        Debug.Log($"Upload progress: {progress:P2} - {file}");
                        
                        // Force log writing in Jenkins environment
                        if (Application.isBatchMode)
                        {
                            // Explicitly flush the log to ensure it's written immediately
                            Debug.unityLogger.logHandler.LogFormat(LogType.Log, null, "Upload progress: {0:P2} - {1}", progress, file);
                        }
                    }
                );
                
                // Wait for the upload to complete with a timeout
                bool completed = uploadTask.Wait(TimeSpan.FromMinutes(30));
                
                if (!completed)
                {
                    throw new TimeoutException("Upload operation timed out after 30 minutes");
                }
                
                Debug.Log("Upload completed successfully");
            }
            catch (AggregateException ae)
            {
                foreach (var ex in ae.InnerExceptions)
                {
                    Debug.LogError($"Upload error: {ex.GetType().Name}: {ex.Message}");
                    
                    if (ex is Amazon.S3.AmazonS3Exception s3Ex)
                    {
                        Debug.LogError($"S3 Error Code: {s3Ex.ErrorCode}, Status: {s3Ex.StatusCode}");
                        Debug.LogError($"Request ID: {s3Ex.RequestId}");
                    }
                }
                throw;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Upload error: {ex.GetType().Name}: {ex.Message}");
                Debug.LogException(ex);
                throw;
            }
        }
        
        // Load all necessary configurations
        // Load all necessary configurations with extended timeout for CI/CD
        private static (CDNConfig cdnConfig, CloudflareConfig cloudflareConfig, CDN.CloudflareR2Service r2Service) LoadConfigurations(BuildTargetDevice targetDevice)
        {
            // Find the CDN config for this target device
            CDNConfig cdnConfig = BatchModeUtils.FindCDNConfig(targetDevice);
            if (cdnConfig == null)
            {
                throw new Exception($"CDN configuration for target device {targetDevice} not found");
            }
    
            // Load the Cloudflare config
            var cloudflareConfig = BatchModeUtils.LoadCloudflareConfig();
            if (cloudflareConfig == null)
            {
                throw new Exception("Cloudflare configuration not found or incomplete");
            }
    
            // For Jenkins/batch mode, use a longer timeout
            int requestTimeout = Application.isBatchMode ? 30 : 10; // 30 minutes for batch mode, 10 minutes for editor
    
            // Create the R2 service with extended timeout
            var r2Service = new CDN.CloudflareR2Service(
                cloudflareConfig.cloudflareAccountId,
                cloudflareConfig.cloudflareAccessKey,
                cloudflareConfig.cloudflareSecretAccessKey,
                requestTimeout
            );
    
            return (cdnConfig, cloudflareConfig, r2Service);
        }
        
        // Execute the delete operation
        private static async Task ExecuteDeleteOperation(CDN.CloudflareR2Service r2Service, string bucketName, string prefix)
        {
            await r2Service.DeleteAllBucketContents(
                bucketName,
                prefix,
                (progress, message) => {
                    Debug.Log($"Delete progress: {progress:P2} - {message}");
                }
            );
        }
        
        // Execute the multipart cleanup operation
        private static async Task ExecuteMultipartCleanupOperation(CDN.CloudflareR2Service r2Service, string bucketName, string prefix)
        {
            await r2Service.CleanupMultipartUploads(
                bucketName,
                prefix,
                (progress, message) => {
                    Debug.Log($"Cleanup progress: {progress:P2} - {message}");
                }
            );
        }
    }
}