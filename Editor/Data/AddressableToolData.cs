using System.Collections.Generic;
using UnityEngine;

namespace Addressables_Wrapper.Editor
{
    /// <summary>
    /// A ScriptableObject that stores all addressable tool settings.
    /// </summary>
    [CreateAssetMenu(fileName = "AddressableToolData", menuName = "Addressables_Wrapper/AddressableToolData")]
    public class AddressableToolData : ScriptableObject
    {
        // New field for group-based approach
        public List<GroupConfiguration> groupConfigurations = new List<GroupConfiguration>();
        
        // Platform-specific asset exclusions
        public List<PlatformExclusion> platformExclusions = new List<PlatformExclusion>();
        
        // Build settings
        public string releaseVersion;
        public BuildTargetDevice buildTargetDevice;
        public EnvironmentType environment;
    }

}