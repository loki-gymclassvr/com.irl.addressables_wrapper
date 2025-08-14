using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AddressableSystem.Editor
{
    /// <summary>
    /// Extracts and caches technical details about assets
    /// </summary>
    public static class AssetDetailsExtractor
    {
        private static Dictionary<string, AssetTechDetails> s_TechDetailsCache = new Dictionary<string, AssetTechDetails>();
        
        /// <summary>
        /// Gets technical details for an asset, extracting them if not already cached
        /// </summary>
        public static AssetTechDetails GetAssetDetails(string guid, Object obj, string path)
        {
            if (string.IsNullOrEmpty(guid) || obj == null)
                return null;
                
            if (s_TechDetailsCache.TryGetValue(guid, out AssetTechDetails details))
                return details;
                
            details = ExtractAssetTechDetails(guid, obj, path);
            return details;
        }
        
        /// <summary>
        /// Extracts technical details for an asset based on its type
        /// </summary>
        private static AssetTechDetails ExtractAssetTechDetails(string guid, Object obj, string path)
        {
            var details = new AssetTechDetails();
            details.assetType = obj.GetType().Name;
            
            try
            {
                // Get file size
                System.IO.FileInfo fileInfo = new System.IO.FileInfo(path);
                if (fileInfo.Exists)
                {
                    details.fileSizeKB = fileInfo.Length / 1024f;
                }
                
                // Extract type-specific details
                if (obj is Mesh mesh)
                {
                    ExtractMeshDetails(mesh, details);
                }
                else if (obj is GameObject go)
                {
                    ExtractGameObjectDetails(go, details);
                }
                else if (obj is Material material)
                {
                    ExtractMaterialDetails(material, details);
                }
                else if (obj is Texture2D texture)
                {
                    ExtractTextureDetails(texture, path, details);
                }
                
                // Store in cache
                s_TechDetailsCache[guid] = details;
                return details;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Error extracting asset details: {ex.Message}");
                return details;
            }
        }
        
        /// <summary>
        /// Clears cached details for a specific asset
        /// </summary>
        public static void ClearAssetDetails(string guid)
        {
            if (!string.IsNullOrEmpty(guid))
            {
                s_TechDetailsCache.Remove(guid);
            }
        }
        
        /// <summary>
        /// Extracts technical details from a mesh
        /// </summary>
        private static void ExtractMeshDetails(Mesh mesh, AssetTechDetails details)
        {
            details.triangleCount = mesh.triangles.Length / 3;
            details.vertexCount = mesh.vertexCount;
        }
        
        /// <summary>
        /// Extracts technical details from a GameObject (prefab)
        /// </summary>
        private static void ExtractGameObjectDetails(GameObject go, AssetTechDetails details)
        {
            // For prefabs, try to get mesh data
            MeshFilter[] meshFilters = go.GetComponentsInChildren<MeshFilter>();
            int totalTris = 0;
            int totalVerts = 0;
            
            foreach (var meshFilter in meshFilters)
            {
                if (meshFilter.sharedMesh != null)
                {
                    totalTris += meshFilter.sharedMesh.triangles.Length / 3;
                    totalVerts += meshFilter.sharedMesh.vertexCount;
                }
            }
            
            // Also check for skinned mesh renderers
            SkinnedMeshRenderer[] skinnedMeshes = go.GetComponentsInChildren<SkinnedMeshRenderer>();
            foreach (var smr in skinnedMeshes)
            {
                if (smr.sharedMesh != null)
                {
                    totalTris += smr.sharedMesh.triangles.Length / 3;
                    totalVerts += smr.sharedMesh.vertexCount;
                }
            }
            
            details.triangleCount = totalTris;
            details.vertexCount = totalVerts;
        }
        
        /// <summary>
        /// Extracts technical details from a material
        /// </summary>
        private static void ExtractMaterialDetails(Material material, AssetTechDetails details)
        {
            // Get shader name
            details.shaderName = material.shader != null ? material.shader.name : "Unknown";
            
            // Try to get texture names
            Shader shader = material.shader;
            if (shader != null)
            {
                int propertyCount = ShaderUtil.GetPropertyCount(shader);
                for (int i = 0; i < propertyCount; i++)
                {
                    if (ShaderUtil.GetPropertyType(shader, i) == ShaderUtil.ShaderPropertyType.TexEnv)
                    {
                        string propName = ShaderUtil.GetPropertyName(shader, i);
                        Texture tex = material.GetTexture(propName);
                        if (tex != null)
                        {
                            details.textureNames.Add($"{propName}: {tex.name}");
                        }
                    }
                }
            }
        }
        
        /// <summary>
        /// Extracts technical details from a texture
        /// </summary>
        private static void ExtractTextureDetails(Texture2D texture, string path, AssetTechDetails details)
        {
            details.dimensions = new Vector2Int(texture.width, texture.height);
            details.format = texture.format.ToString();
    
            // Estimate memory size
            int bytesPerPixel = 4; // Default for most formats
            switch (texture.format)
            {
                case TextureFormat.RGB24:
                    bytesPerPixel = 3;
                    break;
                case TextureFormat.RGBA32:
                    bytesPerPixel = 4;
                    break;
                case TextureFormat.RGB565:
                case TextureFormat.RGBA4444:
                    bytesPerPixel = 2;
                    break;
                case TextureFormat.DXT1:
                    bytesPerPixel = 1;
                    break;
                case TextureFormat.DXT5:
                    bytesPerPixel = 2;
                    break;
            }
    
            details.memorySizeKB = (texture.width * texture.height * bytesPerPixel) / 1024f;
    
            // Try to get texture import settings
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null)
            {
                details.compressionType = importer.textureCompression.ToString();
            }
        }
    }
}