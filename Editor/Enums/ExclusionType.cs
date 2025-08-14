using System;

namespace Addressables_Wrapper
{
    /// <summary>
    /// Defines the type of exclusion: individual asset, entire folder, or file extension.
    /// </summary>
    [Flags]
    public enum ExclusionType
    {
        Asset     = 1 << 0,
        Folder    = 1 << 1,
        Extension = 1 << 2
    }
}