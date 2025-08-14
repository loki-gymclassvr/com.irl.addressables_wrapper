
namespace AddressableSystem
{
    /// <summary>
    /// Interface for unloading addressable assets.
    /// </summary>
    public interface IAddressableUnloader
    {
        void UnloadHandle(string key);
        void UnloadAllHandles();
        void UnloadAutoUnloadHandles();
    }
}