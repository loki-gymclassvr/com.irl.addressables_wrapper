using System;
using System.Collections.Generic;
using UnityEngine;

namespace Addressables_Wrapper.Editor
{
    //[CreateAssetMenu(fileName = "OBBPackingConfig", menuName = "Addressable_Wrapper/OBB Packing Configuration")]
    public class OBBPackingConfig : ScriptableObject
    {
        [Serializable]
        public class OBBDefinition
        {
            [Tooltip("The prefix for the OBB file (e.g., 'patch', 'main')")]
            public string obbPrefix = "patch";

            [Tooltip("List of asset bundle prefixes to include in this OBB")]
            public List<string> assetBundlePrefixes = new List<string>();

            [Tooltip("Whether to include the catalog and settings files in this OBB")]
            public bool includeCatalog = true;

            [Tooltip("Do we create patch obb when we inherit")]
            public bool includeWhenInherit = false;
        }

        [Tooltip("Inherit OBB Configurations")]
        public string catalogPrefix = "mainobb";
        
        [Tooltip("List of OBB configurations to create during build")]
        public List<OBBDefinition> obbDefinitions = new List<OBBDefinition>();
    }
}