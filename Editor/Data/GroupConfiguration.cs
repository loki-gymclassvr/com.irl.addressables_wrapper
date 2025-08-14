using System;
using System.Collections.Generic;
using UnityEngine.Serialization;

namespace Addressables_Wrapper.Editor
{
    /// <summary>
    /// Holds the configuration for a group of addressable assets.
    /// </summary>
    [Serializable]
    public class GroupConfiguration
    {
        public string groupName;
        /// <summary>
        /// List of asset entries (GUID, path, name) in this group.
        /// </summary>
        [Serializable]
        public class AssetEntry
        {
            public string guid;
            public string assetPath;
            public string assetName;

            public AssetEntry(string guid, string assetPath, string assetName)
            {
                this.guid = guid;
                this.assetPath = assetPath;
                this.assetName = assetName;
            }
        }

        public List<AssetEntry> assets = new List<AssetEntry>();
        public List<string> labelAssignments = new List<string>();
        public TargetDevice targetDevices;
        public TargetDevice remoteDevices;


        /// <summary>
        /// How assets in this group are bundled at build time.
        /// </summary>
        public BundleMode bundleMode = BundleMode.PackSeparately;
    }
}