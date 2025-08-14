using Addressables_Wrapper.Editor;
using UnityEngine;

namespace Addressables_Wrapper
{
    /// <summary>
    /// Represents a rule for excluding addressable entries based on asset, folder, or extension.
    /// </summary>
    [System.Serializable]
    public class PlatformExclusion
    {
        /// <summary>
        /// Type of exclusion (Asset, Folder, or Extension).
        /// </summary>
        public ExclusionType exclusionType;

        /// <summary>
        /// GUID of the individual asset to exclude when exclusionType is Asset.
        /// </summary>
        public string assetGuid;

        /// <summary>
        /// Path of the asset to exclude when exclusionType is Asset.
        /// </summary>
        public string assetPath;

        /// <summary>
        /// Path of the folder to exclude when exclusionType is Folder (ensure trailing slash).
        /// </summary>
        public string folderPath;

        /// <summary>
        /// File extension to exclude when exclusionType is Extension (include leading dot).
        /// </summary>
        public string fileExtension;

        /// <summary>
        /// Platforms from which this exclusion applies.
        /// </summary>
        public TargetDevice excludedPlatforms;

        /// <summary>
        /// Environments from which this exclusion applies.
        /// </summary>
        public EnvironmentType excludedEnvironments;
    }
}