using UnityEngine;
using Addressables_Wrapper.Editor;

namespace Addressables_Wrapper.Editor.CDN
{
    /// <summary>
    /// A ScriptableObject that stores CDN configuration for a specific target device.
    /// </summary>
    [CreateAssetMenu(fileName = "CDNConfig", menuName = "Addressables_Wrapper/CDNConfig")]
    public class CDNConfig : ScriptableObject
    {
        [Header("Target Device")]
        [Tooltip("Which target device this configuration is for")]
        public BuildTargetDevice targetDevice = BuildTargetDevice.Quest;
        
        [Header("Platform Configuration")]
        [Tooltip("Platform name to use in bucket naming")]
        public string platformName = "android";
        
        [Header("Product Configuration")]
        [Tooltip("Product name to use in bucket naming")]
        public string productName = "gymclassvr";
        
        [Header("Path Configuration")]
        [Tooltip("Path format: ServerData/{DeviceType}/{Environment}/{Version}")]
        public string remotePathFormat = "ServerData/{0}/{1}/{2}";
        
        [Header("Bucket Configuration")]
        [Tooltip("Bucket name format: {device}-{platform}-{product}-{environment}")]
        public string bucketNameFormat = "{0}-{1}-{2}-{3}";
        
        [Header("CDN URLs")]
        [Tooltip("Development environment CDN URL")]
        public string developmentCdnUrl = "https://pub-xxxx.r2.dev";
        
        [Tooltip("Staging environment CDN URL")]
        public string stagingCdnUrl = "https://pub-xxxx.r2.dev";
        
        [Tooltip("Production environment CDN URL")]
        public string productionCdnUrl = "https://cdn.yourgame.com";
        
        [Tooltip("Enable/Disable local host")]
        public bool localHostEnabled = false;
        
        /// <summary>
        /// Get the target device name as a string.
        /// </summary>
        public string GetDeviceTypeString()
        {
            if (targetDevice == BuildTargetDevice.Quest || targetDevice == BuildTargetDevice.PC)
            {
                return targetDevice.ToString().ToLower().Replace('_', '-');
            }
            return targetDevice.ToString().ToLower();
        }
        
        /// <summary>
        /// Get the formatted remote path.
        /// </summary>
        public string GetFormattedRemotePath(string version, EnvironmentType environment)
        {
            return string.Format(remotePathFormat, GetDeviceTypeString(), environment, version);
        }
        
        /// <summary>
        /// Get the environment type as a string.
        /// </summary>
        public string GetEnvironmentTypeString(EnvironmentType environment)
        {
            return environment.ToString().ToLower();
        }
        
        /// <summary>
        /// Get the bucket name for the specified environment.
        /// </summary>
        public string GetBucketName(EnvironmentType environment)
        {
            // Special handling for Mobile_Android and Mobile_iOS
            if (targetDevice == BuildTargetDevice.Mobile_Android)
            {
                // Format: mobile-android-{product}-{environment}
                return $"mobile-android-{productName.ToLower()}-{GetEnvironmentTypeString(environment)}";
            }
            else if (targetDevice == BuildTargetDevice.Mobile_iOS)
            {
                // Format: mobile-ios-{product}-{environment}
                return $"mobile-ios-{productName.ToLower()}-{GetEnvironmentTypeString(environment)}";
            }
            // Default (existing) format
            return string.Format(bucketNameFormat, 
                GetDeviceTypeString(),
                platformName.ToLower(),
                productName.ToLower(), 
                GetEnvironmentTypeString(environment));
        }
        
        /// <summary>
        /// Get the CDN URL for the specified environment.
        /// </summary>
        public string GetCdnUrl(EnvironmentType environment)
        {
            switch (environment)
            {
                case EnvironmentType.Development:
                    return developmentCdnUrl;
                case EnvironmentType.Staging:
                    return stagingCdnUrl;
                case EnvironmentType.Production:
                    return productionCdnUrl;
                default:
                    return developmentCdnUrl;
            }
        }
    }
}