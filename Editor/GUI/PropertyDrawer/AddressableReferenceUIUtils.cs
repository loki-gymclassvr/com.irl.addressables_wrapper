using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AddressableSystem.Editor
{
    /// <summary>
    /// UI utilities for addressable reference property drawers
    /// </summary>
    public static class AddressableReferenceUIUtils
    {
        // Cache for dependency counts to avoid recalculating every frame
        private static Dictionary<string, int> s_DependencyCountCache = new Dictionary<string, int>();
        private static HashSet<string> s_PendingDependencyCalculations = new HashSet<string>();

        /// <summary>
        /// Draws detailed information about an addressable asset with optimized performance
        /// </summary>
        public static void DrawAssetInfo(Rect position, Object obj, string guid, string address, AssetTechDetails details)
        {
            // Draw background
            GUI.Box(position, GUIContent.none, EditorStyles.helpBox);
            
            // Split into lines
            float lineHeight = EditorGUIUtility.singleLineHeight;
            Rect addressRect = new Rect(position.x + 5, position.y + 2, position.width - 10, lineHeight);
            Rect guidRect = new Rect(position.x + 5, addressRect.yMax + 2, position.width - 10, lineHeight);
            Rect dependenciesRect = new Rect(position.x + 5, guidRect.yMax + 2, position.width - 10, lineHeight);
            
            Color dimColor = new Color(0.7f, 0.7f, 0.7f);
            GUI.color = Color.white;
            EditorGUI.LabelField(addressRect, "Address:", address);
            
            GUI.color = dimColor;
            EditorGUI.LabelField(guidRect, "GUID:", guid);
            
            // Draw dependency count
            GUI.color = Color.white;
            int dependencyCount = 0;
            
            // Check if we have this in the cache
            if (s_DependencyCountCache.TryGetValue(guid, out dependencyCount))
            {
                // Use cached value
                EditorGUI.LabelField(dependenciesRect, "Dependencies:", dependencyCount.ToString());
            }
            else
            {
                // If not calculated yet, show as calculating and queue the calculation
                EditorGUI.LabelField(dependenciesRect, "Dependencies:", "Calculating...");
                
                // Queue dependency calculation if not already queued
                if (!string.IsNullOrEmpty(guid) && !s_PendingDependencyCalculations.Contains(guid))
                {
                    s_PendingDependencyCalculations.Add(guid);
                    
                    // Use EditorApplication.delayCall to run the calculation off the GUI thread
                    EditorApplication.delayCall += () => 
                    {
                        CalculateDependencyCount(guid);
                        s_PendingDependencyCalculations.Remove(guid);
                        // Force repaint so the UI updates with the new value
                        EditorApplication.RepaintHierarchyWindow();
                        EditorApplication.RepaintProjectWindow();
                    };
                }
            }
            
            GUI.color = Color.white;
        }

        /// <summary>
        /// Calculates and caches dependency count for an asset
        /// </summary>
        private static void CalculateDependencyCount(string guid)
        {
            if (string.IsNullOrEmpty(guid))
            {
                s_DependencyCountCache[guid] = 0;
                return;
            }
            
            try
            {
                // Convert GUID to asset path
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(assetPath))
                {
                    // Get all dependencies using the standard AssetDatabase
                    string[] dependencies = AssetDatabase.GetDependencies(assetPath, true);
                    
                    // Subtract 1 to exclude the asset itself from the count
                    int count = System.Math.Max(0, dependencies.Length - 1);
                    
                    // Store in cache
                    s_DependencyCountCache[guid] = count;
                }
                else
                {
                    // Invalid path, cache as 0
                    s_DependencyCountCache[guid] = 0;
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"Error getting dependencies: {ex.Message}");
                s_DependencyCountCache[guid] = 0;
            }
        }

        /// <summary>
        /// Clears the dependency count cache
        /// </summary>
        public static void ClearDependencyCache()
        {
            s_DependencyCountCache.Clear();
        }
    }
}