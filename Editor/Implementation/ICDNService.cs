using UnityEngine;
using System;
using System.Threading.Tasks;

namespace Addressables_Wrapper.Editor.CDN
{
    /// <summary>
    /// Interface for CDN services
    /// </summary>
    public interface ICDNService
    {
        /// <summary>
        /// Name of the CDN service
        /// </summary>
        string ServiceName { get; }
        
        /// <summary>
        /// Whether the service is active
        /// </summary>
        bool IsActive { get; }
        
        /// <summary>
        /// Start the service
        /// </summary>
        /// <returns>Success flag</returns>
        Task<bool> StartService();
        
        /// <summary>
        /// Stop the service
        /// </summary>
        /// <returns>Success flag</returns>
        Task<bool> StopService();
        
        /// <summary>
        /// Upload content to the CDN
        /// </summary>
        /// <param name="sourcePath">Source path</param>
        /// <param name="targetPath">Target path on CDN</param>
        /// <param name="progressCallback">Progress callback</param>
        /// <returns>Success flag</returns>
        Task<bool> UploadContent(string sourcePath, string targetPath, Action<float, string> progressCallback);
        
        /// <summary>
        /// Test the connection to the CDN
        /// </summary>
        /// <returns>Success flag</returns>
        Task<bool> TestConnection();
        
        /// <summary>
        /// Update the Addressables profile for this CDN
        /// </summary>
        /// <param name="deviceType">Target device type</param>
        /// <param name="environmentType">Environment type</param>
        /// <param name="version">Version string</param>
        /// <returns>Success flag</returns>
        bool UpdateAddressablesProfile(BuildTargetDevice deviceType, EnvironmentType environmentType, string version);
        
        /// <summary>
        /// Dispose the service
        /// </summary>
        void Dispose();
    }
    
    /// <summary>
    /// CDN service type enumeration
    /// </summary>
    public enum CDNServiceType
    {
        Cloudflare,
        LocalHosting,
        // Future CDN types can be added here
    }
}