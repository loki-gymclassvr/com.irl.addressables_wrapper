using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ShovelTools;
using UnityEngine.AddressableAssets;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement.ResourceLocations;

namespace AddressableSystem
{
    /// <summary>
    /// Standalone helper for transforming Addressables InternalIds to the correct OBB paths,
    /// and for pre-warming the transform cache so that the first real load never misses.
    /// </summary>
    public static class InternalIdTransformer
    {
        // markers & constants
        private const string OBB_JAR_MARKER             = ".obb!/assets";
        private const string JAR_SEPARATOR              = "!/";
        private const string ASSETS_PATH                = "/assets/";
        private const string BUILTIN_MARKER             = "builtindata_";
        private const string PATCH_MARKER               = "patch.";
        private const string MAIN_MARKER                = "main.";
        private const string MLB_MARKER                 = "mlb_";
        private const string DEFAULT_LOCAL_GROUP_MARKER = "defaultlocalgroup_";
        private const string JAR_FILE_PREFIX            = "jar:file://";
        private const string ASSETS_AA_PATH             = "!/assets/aa/";

        // cache and path‐builder
        private static readonly Dictionary<string, string> _cache           = new Dictionary<string, string>(1000);
        private static readonly StringBuilder               _pathBuilder     = new StringBuilder(256);
        private static          Dictionary<string, string> _obbPathByMarker = new Dictionary<string,string>(4);

        /// <summary>
        /// Call once at startup, as soon as you know your three OBB file paths.
        /// </summary>
        public static void InitializeObbPaths(string patchObbPath, string mainObbPath, string mlbObbPath)
        {
            _obbPathByMarker.Clear();
            if (!string.IsNullOrEmpty(patchObbPath))
            {
                _obbPathByMarker[PATCH_MARKER]               = patchObbPath;
                _obbPathByMarker[DEFAULT_LOCAL_GROUP_MARKER] = patchObbPath;
                _obbPathByMarker[BUILTIN_MARKER]             = patchObbPath;
            }
            if (!string.IsNullOrEmpty(mlbObbPath))
                _obbPathByMarker[MLB_MARKER] = mlbObbPath;
            if (!string.IsNullOrEmpty(mainObbPath))
                _obbPathByMarker[MAIN_MARKER] = mainObbPath;
        }

        /// <summary>
        /// Assign this once, after InitializeObbPaths:
        ///     Addressables.InternalIdTransformFunc = loc => InternalIdTransformer.Transform(loc);
        /// </summary>
        public static string Transform(IResourceLocation location)
            => Transform(location.InternalId);

        /// <summary>
        /// The meat of the transform: normalize, pick the right OBB based on markers,
        /// rebuild a jar:file://... URL if needed, un-escape, and cache.
        /// </summary>
        public static string Transform(string originalId)
        {
            // Log entry
            //DLM.Log(DLM.FeatureFlags.Addressables, $"[AddressableInitializer] Transform requested for: {originalId}");
            
            if (_cache.TryGetValue(originalId, out var cached))
            {
                //DLM.Log(DLM.FeatureFlags.Addressables, $"[AddressableInitializer] → cache hit: {cached}");
                return cached;
            }

            // normalize separators
            var normalized = originalId.Replace('\\', '/');
            var result     = normalized;

            if(AddressableManager.Instance.SelectedTargetDevice == TargetDevice.Quest)
            {
                #if UNITY_ANDROID && !UNITY_EDITOR
                if (normalized.IndexOf(OBB_JAR_MARKER, StringComparison.Ordinal) >= 0)
                {
                    var internalPath = ExtractInternalPath(normalized);
                    if (internalPath != null)
                    {
                        // choose which OBB to use
                        string targetObb = null;

                        if (normalized.IndexOf(MAIN_MARKER, StringComparison.Ordinal) >= 0)
                        {
                            // catalogs/settings always come from patch
                            if (normalized.IndexOf("catalog.json", StringComparison.Ordinal) >= 0 ||
                                normalized.IndexOf("settings.json", StringComparison.Ordinal) >= 0)
                            {
                                _obbPathByMarker.TryGetValue(PATCH_MARKER, out targetObb);
                            }
                            // mlb assets go to mlb.obb
                            else if (normalized.IndexOf(MLB_MARKER, StringComparison.Ordinal) >= 0)
                            {
                                _obbPathByMarker.TryGetValue(MLB_MARKER, out targetObb);
                            }
                            else if(normalized.IndexOf(DEFAULT_LOCAL_GROUP_MARKER, StringComparison.Ordinal) >= 0)
                            {
                                // default groups => patch
                                _obbPathByMarker.TryGetValue(DEFAULT_LOCAL_GROUP_MARKER, out targetObb);
                            }
                            else if(normalized.IndexOf(BUILTIN_MARKER, StringComparison.Ordinal) >= 0)
                            {
                                // builtindata groups => patch
                                _obbPathByMarker.TryGetValue(BUILTIN_MARKER, out targetObb);
                            }
                            else
                            {
                                // all other groups mostly shared_ bundles => main
                                _obbPathByMarker.TryGetValue(MAIN_MARKER, out targetObb);
                            }
                        }
                        else if (normalized.IndexOf(PATCH_MARKER, StringComparison.Ordinal) >= 0)
                        {
                            _obbPathByMarker.TryGetValue(PATCH_MARKER, out targetObb);
                        }

                        if (!string.IsNullOrEmpty(targetObb))
                        {
                            _pathBuilder.Length = 0;
                            _pathBuilder
                                .Append(JAR_FILE_PREFIX)
                                .Append(targetObb)
                                .Append(internalPath);
                            result = _pathBuilder.ToString();
                            
                            //DLM.Log(DLM.FeatureFlags.Addressables, $"[AddressableInitializer]  → remapped to: {result}");
                        }
                    }
                }
                #endif
            }

            // undo Unity’s double‐encoding
            var unescaped = UnityEngine.Networking.UnityWebRequest.UnEscapeURL(result);

            // cache & return
            if (_cache.Count >= 1000)
                _cache.Clear();
            _cache[originalId] = unescaped;
            
            //DLM.Log(DLM.FeatureFlags.Addressables, $"[AddressableInitializer] → final transform: {unescaped}");
            
            return unescaped;
        }

        /// <summary>
        /// Extracts the internal “!/…” portion from a jar:file://… URL.
        /// </summary>
        private static string ExtractInternalPath(string path)
        {
            int idx = path.IndexOf(JAR_SEPARATOR, StringComparison.Ordinal);
            if (idx >= 0)
                return path.Substring(idx);

            idx = path.IndexOf(ASSETS_PATH, StringComparison.Ordinal);
            if (idx >= 0)
            {
                _pathBuilder.Length = 0;
                _pathBuilder.Append('!')
                            .Append(path, idx, path.Length - idx);
                return _pathBuilder.ToString();
            }

            return null;
        }

        /// <summary>
        /// Builds up the cache of transformed IDs ahead of time so your first loads never hit a miss.
        /// Call this once after all catalogs are in.
        /// </summary>
        public static Task PrewarmCacheAsync()
        {
            // Take a snapshot of your locators on the main thread
            var locators = Addressables.ResourceLocators.ToList();

            // Kick off the background work
            return Task.Run(() =>
                {
                    int cachedCount = 0;

                    foreach (var locator in locators)
                    {
                        foreach (var key in locator.Keys)
                        {
                            locator.Locate(key, null, out var locs);
                            foreach (var loc in locs)
                            {
                                // only warm those that hit your OBB transformer
                                var id = loc.InternalId.Replace('\\', '/');
                                if (id.IndexOf(OBB_JAR_MARKER, StringComparison.Ordinal) >= 0)
                                {
                                    Transform(id);
                                    cachedCount++;
                                }
                            }
                        }
                    }

                    return cachedCount;
                })
                // back on main thread to log
                .ContinueWith(t =>
                {
                    DLM.Log(DLM.FeatureFlags.Addressables,
                        $"[AddressableInitializer] Prewarm complete: {_cache.Count} OBB keys cached.");
                }, TaskScheduler.FromCurrentSynchronizationContext());
        }

    }
}
