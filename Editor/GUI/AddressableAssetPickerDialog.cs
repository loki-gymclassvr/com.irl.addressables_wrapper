using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace AddressableSystem.Editor
{
    /// <summary>
    /// Custom dialog window for selecting addressable assets with filtering and preview capabilities.
    /// </summary>
    public class AddressableAssetPickerDialog : EditorWindow
    {
        // Static field to store the callback for when an asset is selected
        private static Action<AddressableAssetEntry> OnAssetSelectedCallback;
        
        // Search and filtering
        private SearchField m_SearchField;
        private string m_SearchString = string.Empty;
        private Type m_AssetType;
        private string m_TypeFilter;
        
        // UI Layout
        private Vector2 m_ScrollPosition;
        private float m_PreviewSize = 64f;
        private bool m_ShowPreviews = true;
        
        // Implement pagination
        private int m_CurrentPage = 0;
        private int m_ItemsPerPage = 20;
        private int m_TotalPages = 1;
        
        // Asset data
        private List<AddressableAssetEntry> m_FilteredEntries = new List<AddressableAssetEntry>();
        private Dictionary<string, Texture2D> m_PreviewCache = new Dictionary<string, Texture2D>();
        private Dictionary<string, UnityEngine.Object> m_ObjectCache = new Dictionary<string, UnityEngine.Object>();
        
        /// <summary>
        /// Shows the addressable asset picker dialog.
        /// </summary>
        /// <param name="assetType">The type of asset to filter by.</param>
        /// <param name="typeFilter">Optional additional filter string.</param>
        /// <param name="onAssetSelected">Callback when an asset is selected.</param>
        public static void ShowWindow(Type assetType, string typeFilter, Action<AddressableAssetEntry> onAssetSelected)
        {
            OnAssetSelectedCallback = onAssetSelected;
            
            var window = GetWindow<AddressableAssetPickerDialog>(true, "Select Addressable Asset", true);
            window.minSize = new Vector2(500, 400);
            
            window.m_AssetType = assetType;
            window.m_TypeFilter = typeFilter;
            window.m_SearchField = new SearchField();
            
            window.LoadAddressableEntries();
            window.Show();
        }
        
        private void OnEnable()
        {
            // Initialize search field if needed
            if (m_SearchField == null)
            {
                m_SearchField = new SearchField();
            }
        }
        
        private void OnGUI()
        {
            DrawToolbar();
            EditorGUILayout.Space();
            DrawAssetGrid();
        }
        
        /// <summary>
        /// Draws the toolbar with search and filter options.
        /// </summary>
        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            
            // Search field
            string newSearch = m_SearchField.OnToolbarGUI(m_SearchString, GUILayout.ExpandWidth(true));
            if (newSearch != m_SearchString)
            {
                m_SearchString = newSearch;
                FilterEntries();
            }
            
            // Preview toggle
            bool newShowPreviews = EditorGUILayout.ToggleLeft("Show Previews", m_ShowPreviews, GUILayout.Width(100));
            if (newShowPreviews != m_ShowPreviews)
            {
                m_ShowPreviews = newShowPreviews;
                Repaint();
            }
            
            // Preview size slider
            if (m_ShowPreviews)
            {
                float newPreviewSize = EditorGUILayout.Slider(m_PreviewSize, 32f, 128f, GUILayout.Width(150));
                if (Math.Abs(newPreviewSize - m_PreviewSize) > 0.01f)
                {
                    m_PreviewSize = newPreviewSize;
                    Repaint();
                }
            }
            
            EditorGUILayout.EndHorizontal();
        }
        
        

        // Modify DrawAssetGrid to support pagination
        private void DrawAssetGrid()
        {
            if (m_FilteredEntries.Count == 0)
            {
                EditorGUILayout.HelpBox("No addressable assets match the current filter.", MessageType.Info);
                return;
            }
            
            // Draw pagination controls
            EditorGUILayout.BeginHorizontal();
            
            if (GUILayout.Button("◀ Previous", GUILayout.Width(100)))
            {
                m_CurrentPage = Mathf.Max(0, m_CurrentPage - 1);
            }
            
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField($"Page {m_CurrentPage + 1} of {m_TotalPages}", 
                                      EditorStyles.boldLabel, GUILayout.Width(100));
            GUILayout.FlexibleSpace();
            
            if (GUILayout.Button("Next ▶", GUILayout.Width(100)))
            {
                m_CurrentPage = Mathf.Min(m_TotalPages - 1, m_CurrentPage + 1);
            }
            
            EditorGUILayout.EndHorizontal();
            
            // Calculate page-specific entries
            int startIndex = m_CurrentPage * m_ItemsPerPage;
            int endIndex = Mathf.Min(startIndex + m_ItemsPerPage, m_FilteredEntries.Count);
            
            m_ScrollPosition = EditorGUILayout.BeginScrollView(m_ScrollPosition);
            
            // Improved grid layout
            float cellWidth = 100;
            float cellHeight = 120;
            int columns = Mathf.Max(1, Mathf.FloorToInt((position.width - 20) / cellWidth));
            
            // Prepare layout
            EditorGUILayout.BeginVertical();
            
            for (int i = startIndex; i < endIndex; i += columns)
            {
                EditorGUILayout.BeginHorizontal();
                
                for (int col = 0; col < columns && i + col < endIndex; col++)
                {
                    var entry = m_FilteredEntries[i + col];
                    DrawAssetCell(GUILayoutUtility.GetRect(cellWidth, cellHeight), entry);
                }
                
                EditorGUILayout.EndHorizontal();
            }
            
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndScrollView();
        }

        // Modify DrawAssetCell for better visuals
        private void DrawAssetCell(Rect cellRect, AddressableAssetEntry entry)
        {
            // Draw a clear background
            EditorGUI.DrawRect(cellRect, new Color(0.3f, 0.3f, 0.3f, 0.2f));
            
            // Better preview area
            Rect previewRect = new Rect(
                cellRect.x + 10,
                cellRect.y + 5,
                cellRect.width - 20,
                cellRect.height - 30);
            
            // Better label area
            Rect labelRect = new Rect(
                cellRect.x,
                previewRect.yMax + 5,
                cellRect.width,
                20);
            
            // Draw selection highlight
            bool isHovering = cellRect.Contains(Event.current.mousePosition);
            if (isHovering)
            {
                EditorGUI.DrawRect(cellRect, new Color(0.5f, 0.5f, 0.9f, 0.3f));
            }
            
            // Get the asset and its preview
            string assetPath = AssetDatabase.GUIDToAssetPath(entry.guid);
            UnityEngine.Object asset = GetOrLoadAsset(entry.guid, assetPath);
            
            // Draw preview
            if (m_ShowPreviews && asset != null)
            {
                Texture2D preview = GetOrCreatePreview(entry.guid, asset);
                if (preview != null)
                {
                    GUI.DrawTexture(previewRect, preview, ScaleMode.ScaleToFit);
                }
            }
            
            // Improved label with background to ensure visibility
            Rect labelBgRect = labelRect;
            labelBgRect.height += 4;
            labelBgRect.y -= 2;
            EditorGUI.DrawRect(labelBgRect, new Color(0.2f, 0.2f, 0.2f, 0.7f));
            
            // Draw label
            string displayName = string.IsNullOrEmpty(entry.address) ? 
                                  System.IO.Path.GetFileNameWithoutExtension(assetPath) : 
                                  entry.address;
            
            GUI.Label(labelRect, displayName, EditorStyles.whiteBoldLabel);
            
            // Handle clicks
            if (Event.current.type == EventType.MouseDown && cellRect.Contains(Event.current.mousePosition))
            {
                OnAssetSelectedCallback?.Invoke(entry);
                Close();
                Event.current.Use();
            }
        }
        
        /// <summary>
        /// Gets or loads an asset by GUID and path.
        /// </summary>
        private UnityEngine.Object GetOrLoadAsset(string guid, string path)
        {
            if (string.IsNullOrEmpty(guid) || string.IsNullOrEmpty(path))
                return null;
                
            if (m_ObjectCache.TryGetValue(guid, out UnityEngine.Object asset) && asset != null)
                return asset;
            
            asset = AssetDatabase.LoadMainAssetAtPath(path);
            if (asset != null)
            {
                m_ObjectCache[guid] = asset;
            }
            
            return asset;
        }
        
        /// <summary>
        /// Gets or creates a preview for an asset.
        /// </summary>
        private Texture2D GetOrCreatePreview(string guid, UnityEngine.Object asset)
        {
            if (m_PreviewCache.TryGetValue(guid, out Texture2D preview) && preview != null)
                return preview;
            
            if (asset == null)
                return null;
            
            // Try to get a preview (this might be null if the preview is still being generated)
            preview = AssetPreview.GetAssetPreview(asset);
            if (preview == null)
            {
                // Fall back to mini thumbnail
                preview = AssetPreview.GetMiniThumbnail(asset);
            }
            
            if (preview != null)
            {
                m_PreviewCache[guid] = preview;
            }
            else
            {
                // If we still don't have a preview, ask for a repaint to try again
                EditorApplication.delayCall += Repaint;
            }
            
            return preview;
        }
        
        /// <summary>
        /// Loads and filters addressable entries based on the current settings.
        /// </summary>
        private void LoadAddressableEntries()
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                m_FilteredEntries.Clear();
                return;
            }
            
            // Collect all entries from all groups
            List<AddressableAssetEntry> allEntries = new List<AddressableAssetEntry>();
            foreach (var group in settings.groups)
            {
                if (group == null)
                    continue;
                    
                allEntries.AddRange(group.entries);
            }
            
            // Apply initial filtering
            m_FilteredEntries = FilterEntriesByType(allEntries);
            
            // Apply search filter if needed
            if (!string.IsNullOrEmpty(m_SearchString))
            {
                FilterEntries();
            }
        }
        
        /// <summary>
        /// Applies the current search filter to the entries.
        /// </summary>
        private void FilterEntries()
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                m_FilteredEntries.Clear();
                return;
            }
            
            // Start with all entries filtered by type
            List<AddressableAssetEntry> allEntries = new List<AddressableAssetEntry>();
            foreach (var group in settings.groups)
            {
                if (group == null)
                    continue;
                    
                allEntries.AddRange(group.entries);
            }
            
            List<AddressableAssetEntry> typeFiltered = FilterEntriesByType(allEntries);
            
            // If no search, use the type filtered list
            if (string.IsNullOrEmpty(m_SearchString))
            {
                m_FilteredEntries = typeFiltered;
                return;
            }
            
            // Apply search filter
            string search = m_SearchString.ToLowerInvariant();
            m_FilteredEntries = typeFiltered.Where(entry => 
                entry.address.ToLowerInvariant().Contains(search) ||
                System.IO.Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(entry.guid))
                    .ToLowerInvariant().Contains(search)
            ).ToList();
            
            // Calculate total pages
            m_TotalPages = Mathf.CeilToInt((float)m_FilteredEntries.Count / m_ItemsPerPage);
            m_CurrentPage = Mathf.Min(m_CurrentPage, m_TotalPages - 1);
        }
        
        /// <summary>
        /// Filters entries by the selected asset type.
        /// </summary>
        private List<AddressableAssetEntry> FilterEntriesByType(List<AddressableAssetEntry> entries)
        {
            if (m_AssetType == null)
                return entries;
                
            List<AddressableAssetEntry> filtered = new List<AddressableAssetEntry>();
            
            foreach (var entry in entries)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(entry.guid);
                if (string.IsNullOrEmpty(assetPath))
                    continue;
                    
                // Check against type filter if provided
                if (!string.IsNullOrEmpty(m_TypeFilter))
                {
                    if (!assetPath.EndsWith(".asset") && !m_TypeFilter.StartsWith("t:"))
                    {
                        // Simple extension check for common types
                        if (m_TypeFilter == "t:Texture2D" && !assetPath.EndsWith(".png") && !assetPath.EndsWith(".jpg") && !assetPath.EndsWith(".jpeg"))
                            continue;
                        if (m_TypeFilter == "t:Model" && !assetPath.EndsWith(".fbx") && !assetPath.EndsWith(".obj"))
                            continue;
                        if (m_TypeFilter == "t:Prefab" && !assetPath.EndsWith(".prefab"))
                            continue;
                        if (m_TypeFilter == "t:Material" && !assetPath.EndsWith(".mat"))
                            continue;
                        if (m_TypeFilter == "t:Scene" && !assetPath.EndsWith(".unity"))
                            continue;
                        if (m_TypeFilter == "t:AudioClip" && !assetPath.EndsWith(".mp3") && !assetPath.EndsWith(".wav") && !assetPath.EndsWith(".ogg"))
                            continue;
                    }
                }
                
                // Only load the asset if we need to check its type
                Type assetType = null;
                try
                {
                    // This is potentially slow but necessary for accurate filtering
                    UnityEngine.Object asset = AssetDatabase.LoadMainAssetAtPath(assetPath);
                    if (asset != null)
                    {
                        assetType = asset.GetType();
                        
                        // Cache the loaded asset
                        m_ObjectCache[entry.guid] = asset;
                        
                        // Check type compatibility
                        if (m_AssetType.IsAssignableFrom(assetType) || assetType.IsSubclassOf(m_AssetType))
                        {
                            filtered.Add(entry);
                        }
                    }
                }
                catch (System.Exception)
                {
                    // Skip entries that cause errors
                }
            }
            
            return filtered;
        }
    }
}