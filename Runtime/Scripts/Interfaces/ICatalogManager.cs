using System.Collections.Generic;
using System.Threading.Tasks;

namespace AddressableSystem
{
    /// <summary>
    /// Interface for catalog management
    /// </summary>
    public interface ICatalogManager
    {
        /// <summary>
        /// Load a content catalog from the given path
        /// </summary>
        Task<bool> LoadCatalogAsync(string catalogPath);
        
        /// <summary>
        /// Get the current CDN URL
        /// </summary>
        string GetCatalogUrl();

        /// <summary>
        /// Is catalog signed urls still valid
        /// </summary>
        /// <param name="catalogKey"></param>
        /// <returns></returns>
        bool IsCatalogStillValid(string catalogKey);

        /// <summary>
        /// unload passed catalog
        /// </summary>
        /// <param name="catalogPath"></param>
        void UnloadCatalog(string catalogPath);

        /// <summary>
        /// Unloads all catalogs keeping the one passed in keepPath
        /// </summary>
        /// <param name="keepPath"></param>
        void UnloadAllExceptRemote();
    }
}