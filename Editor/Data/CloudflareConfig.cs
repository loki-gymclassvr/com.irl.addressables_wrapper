using UnityEngine;
using UnityEngine.Serialization;

namespace Addressables_Wrapper.Editor.CDN
{
    /// <summary>
    /// A ScriptableObject that stores Cloudflare R2 credentials.
    /// </summary>
    [CreateAssetMenu(fileName = "CloudflareConfig", menuName = "Addressables_Wrapper/CloudflareConfig")]
    public class CloudflareConfig : ScriptableObject
    {
        [Header("Cloudflare Configuration")]
        [Tooltip("Cloudflare Account ID")]
        public string cloudflareAccountId = "";
        
        [Tooltip("Cloudflare Access Key ID")]
        public string cloudflareAccessKey = "";
        
        [Tooltip("Cloudflare Secret Access Key (will be stored in ProjectSettings)")]
        [HideInInspector] public string cloudflareSecretAccessKey = "";
    }
}