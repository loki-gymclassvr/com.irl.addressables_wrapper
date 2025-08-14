using System;

namespace Addressables_Wrapper.Editor
{
    /// <summary>
    /// Reference to an addressable asset.
    /// </summary>
    [Serializable]
    public class AddressableAssetReference
    {
        public string assetPath;
        public string assetGuid;
    }
}