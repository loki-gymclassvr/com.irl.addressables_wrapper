using System;

namespace Addressables_Wrapper.Editor
{
    /// <summary>
    /// How assets in a group are bundled at build time.
    /// </summary>
    [Serializable]
    public enum BundleMode
    {
        PackTogether = 0,
        PackSeparately = 1,
        PackByLabel = 2
    }
}
