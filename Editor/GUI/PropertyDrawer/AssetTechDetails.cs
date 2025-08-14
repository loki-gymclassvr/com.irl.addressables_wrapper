using System.Collections.Generic;
using UnityEngine;

namespace AddressableSystem.Editor
{
    /// <summary>
    /// Stores technical details about various asset types
    /// </summary>
    public class AssetTechDetails
    {
        // Mesh details
        public int triangleCount;
        public int vertexCount;
        
        // Material details
        public string shaderName;
        public List<string> textureNames = new List<string>();
        
        // Texture details
        public Vector2Int dimensions;
        public string format;
        public float memorySizeKB;
        public string compressionType;
        
        // General info
        public string assetType;
        public float fileSizeKB;
    }
}