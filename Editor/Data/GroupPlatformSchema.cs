using Addressables_Wrapper.Editor;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace Addressables_Wrapper
{
    /// <summary>
    /// Custom Addressables group schema that holds:
    /// • allowedDevices: which platforms this group is included on
    /// • remoteDevices: which platforms this group should be remote‐enabled (CDN)
    /// </summary>
    [CreateAssetMenu(menuName = "Addressables/Group Platform Schema")]
    public class GroupPlatformSchema : AddressableAssetGroupSchema
    {
        /// <summary>
        /// Platforms on which this group is included. Use EnumFlags to combine multiple.
        /// </summary>
        public TargetDevice allowedDevices;

        /// <summary>
        /// Platforms on which this group is “remote‐enabled.”  
        /// In other words, when building for any of these platforms, we treat the group as hosted on a CDN.
        /// </summary>
        public TargetDevice remoteDevices;
    }
}