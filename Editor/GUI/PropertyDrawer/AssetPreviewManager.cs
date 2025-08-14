using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AddressableSystem.Editor
{
    /// <summary>
    /// Manages asset previews and thumbnails
    /// </summary>
    public static class AssetPreviewManager
    {
        // Cache for previews and thumbnails
        private static Dictionary<string, Texture2D> s_PreviewCache = new Dictionary<string, Texture2D>();
        private static Dictionary<string, Texture2D> s_ModelThumbnailCache = new Dictionary<string, Texture2D>();
        private static Dictionary<string, UnityEditor.Editor> s_PreviewEditors = new Dictionary<string, UnityEditor.Editor>();
        
        /// <summary>
        /// Gets the best available thumbnail for an asset
        /// </summary>
        public static Texture2D GetBestThumbnail(string guid, Object obj)
        {
            if (obj == null)
                return null;
            
            // For 3D models/prefabs, try to use our captured thumbnail first
            bool isModel = obj is GameObject || obj is Mesh;
            if (isModel && s_ModelThumbnailCache.TryGetValue(guid, out Texture2D modelThumbnail) && modelThumbnail != null)
            {
                return modelThumbnail;
            }
            
            // Fall back to standard preview
            Texture2D standardThumbnail = AssetPreview.GetMiniThumbnail(obj);
            
            // For models without a cached preview, we'll trigger the capture when they're first previewed
            if (isModel && !s_ModelThumbnailCache.ContainsKey(guid))
            {
                // Ensure we have a preview editor for this asset
                if (!s_PreviewEditors.TryGetValue(guid, out UnityEditor.Editor previewEditor) || previewEditor == null)
                {
                    previewEditor = UnityEditor.Editor.CreateEditor(obj);
                    s_PreviewEditors[guid] = previewEditor;
                    
                    // Queue a repaint to generate the thumbnail
                    EditorApplication.delayCall += () => EditorWindow.focusedWindow?.Repaint();
                }
            }
            
            return standardThumbnail;
        }
        
        /// <summary>
        /// Gets or creates a full-size preview texture for an asset
        /// </summary>
        public static Texture2D GetPreviewTexture(string guid, Object obj)
        {
            if (s_PreviewCache.TryGetValue(guid, out Texture2D preview) && preview != null)
                return preview;
                
            if (obj == null)
                return null;
                
            preview = AssetPreview.GetAssetPreview(obj);
            if (preview == null)
                preview = AssetPreview.GetMiniThumbnail(obj);
                
            if (preview != null)
                s_PreviewCache[guid] = preview;
                
            return preview;
        }
        
        /// <summary>
        /// Gets or creates a preview editor for an asset
        /// </summary>
        public static UnityEditor.Editor GetPreviewEditor(string guid, Object obj)
        {
            if (obj == null)
                return null;
                
            if (!s_PreviewEditors.TryGetValue(guid, out UnityEditor.Editor editor) || editor == null)
            {
                editor = UnityEditor.Editor.CreateEditor(obj);
                s_PreviewEditors[guid] = editor;
            }
            
            return editor;
        }
        
        /// <summary>
        /// Draws an interactive preview for an asset
        /// </summary>
        public static void DrawObjectPreview(Rect position, Object obj, string guid)
        {
            // Draw a background
            GUI.Box(position, GUIContent.none, EditorStyles.helpBox);
            
            bool isModel = obj is GameObject || obj is Mesh;
            bool isTexture = obj is Texture;
            bool isMaterial = obj is Material;
            
            if (isModel || isMaterial)
            {
                // Use editor preview if we have it
                UnityEditor.Editor previewEditor = GetPreviewEditor(guid, obj);
                if (previewEditor != null)
                {
                    previewEditor.OnInteractivePreviewGUI(position, EditorStyles.helpBox);
                    
                    // If it's a 3D model, capture the preview for thumbnail
                    if (isModel && !s_ModelThumbnailCache.ContainsKey(guid) && Event.current.type == EventType.Repaint)
                    {
                        CaptureModelThumbnail(guid, position, previewEditor);
                    }
                }
                else
                {
                    // Fall back to static preview
                    Texture2D preview = GetPreviewTexture(guid, obj);
                    if (preview != null)
                    {
                        GUI.DrawTexture(position, preview, ScaleMode.ScaleToFit);
                    }
                }
            }
            else if (isTexture)
            {
                // Draw texture directly
                GUI.DrawTexture(position, (Texture)obj, ScaleMode.ScaleToFit);
            }
            else
            {
                // Standard preview
                Texture2D preview = GetPreviewTexture(guid, obj);
                if (preview != null)
                {
                    GUI.DrawTexture(position, preview, ScaleMode.ScaleToFit);
                }
            }
        }
        
        /// <summary>
        /// Captures a 3D thumbnail from a model's preview
        /// </summary>
        private static void CaptureModelThumbnail(string guid, Rect position, UnityEditor.Editor previewEditor)
        {
            try
            {
                // We need to use reflection to access the preview utility's camera
                var previewRenderUtilityType = Type.GetType("UnityEditor.PreviewRenderUtility, UnityEditor");
                if (previewRenderUtilityType != null)
                {
                    var previewUtilityField = previewEditor.GetType()
                        .GetField("m_PreviewUtility", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        
                    if (previewUtilityField != null)
                    {
                        var previewUtility = previewUtilityField.GetValue(previewEditor);
                        if (previewUtility != null)
                        {
                            // Position the camera for a nice thumbnail view
                            var cameraProperty = previewRenderUtilityType.GetProperty("camera");
                            if (cameraProperty != null)
                            {
                                var camera = cameraProperty.GetValue(previewUtility) as Camera;
                                if (camera != null)
                                {
                                    // Set up a good default angle
                                    camera.transform.rotation = Quaternion.Euler(20f, 225f, 0);
                                    
                                    // Create a render texture and render to it
                                    RenderTexture renderTexture = RenderTexture.GetTemporary(128, 128, 16, RenderTextureFormat.ARGB32);
                                    RenderTexture oldRT = RenderTexture.active;
                                    camera.targetTexture = renderTexture;
                                    RenderTexture.active = renderTexture;
                                    
                                    // Create a texture to hold the thumbnail
                                    Texture2D thumbnail = new Texture2D(128, 128, TextureFormat.RGBA32, false);
                                    
                                    // Render and read pixels
                                    camera.Render();
                                    thumbnail.ReadPixels(new Rect(0, 0, 128, 128), 0, 0);
                                    thumbnail.Apply();
                                    
                                    // Reset render texture
                                    camera.targetTexture = null;
                                    RenderTexture.active = oldRT;
                                    RenderTexture.ReleaseTemporary(renderTexture);
                                    
                                    // Cache the thumbnail
                                    s_ModelThumbnailCache[guid] = thumbnail;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // If anything goes wrong, just use the standard preview
                Debug.LogWarning($"Failed to create 3D thumbnail: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Cleans up all preview resources for an asset
        /// </summary>
        public static void ClearPreviewsForAsset(string guid)
        {
            if (string.IsNullOrEmpty(guid))
                return;
                
            s_PreviewCache.Remove(guid);
            s_ModelThumbnailCache.Remove(guid);
            
            if (s_PreviewEditors.TryGetValue(guid, out UnityEditor.Editor editor) && editor != null)
            {
                UnityEngine.Object.DestroyImmediate(editor);
                s_PreviewEditors.Remove(guid);
            }
        }
    }
}