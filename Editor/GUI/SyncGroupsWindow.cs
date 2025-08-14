using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEngine;

namespace Addressables_Wrapper.Editor
{
    // Class to manage the sync dialog
    public class SyncGroupsWindow : EditorWindow
    {
        private List<string> _pendingGroups = new List<string>();
        private Dictionary<string, bool> _groupSelections = new Dictionary<string, bool>();
        private Vector2 _scrollPosition;
        private AddressableToolData _toolData;
        
        public static void ShowWindow(AddressableToolData toolData, List<string> pendingGroups)
        {
            // Create and show window
            SyncGroupsWindow window = GetWindow<SyncGroupsWindow>(true, "New Addressable Groups", true);
            window.minSize = new Vector2(350, 300);
            window.maxSize = new Vector2(450, 500);
            
            // Set up window data
            window._toolData = toolData;
            window._pendingGroups = new List<string>(pendingGroups);
            
            // Initialize selections (all selected by default)
            window._groupSelections = new Dictionary<string, bool>();
            foreach (string group in pendingGroups)
            {
                window._groupSelections[group] = true;
            }
            
            // Position window
            window.position = new Rect(
                (Screen.width - window.minSize.x) / 2,
                (Screen.height - window.minSize.y) / 2,
                window.minSize.x,
                window.minSize.y);
                
            window.ShowModal();
        }
        
        private void OnGUI()
        {
            EditorGUILayout.BeginVertical(EditorStyles.inspectorDefaultMargins);
            
            // Title and info
            EditorGUILayout.LabelField("New Addressable Groups Found", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);
            
            EditorGUILayout.HelpBox(
                "The following groups exist in the Addressable Asset System but not in this tool. " +
                "Select the groups you want to add.", 
                MessageType.Info);
            
            EditorGUILayout.Space(5);
            
            // Select All toggle
            bool allSelected = !_groupSelections.ContainsValue(false);
            bool newAllSelected = EditorGUILayout.Toggle("Select All", allSelected);
            if (newAllSelected != allSelected)
            {
                foreach (string group in _pendingGroups)
                {
                    _groupSelections[group] = newAllSelected;
                }
            }
            
            EditorGUILayout.Space(5);
            
            // Group list with scroll view
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
            
            foreach (string groupName in _pendingGroups)
            {
                _groupSelections[groupName] = EditorGUILayout.Toggle(groupName, _groupSelections[groupName]);
            }
            
            EditorGUILayout.EndScrollView();
            
            EditorGUILayout.Space(5);
            
            // Display selection count
            int selectedCount = _groupSelections.Count(kvp => kvp.Value);
            EditorGUILayout.LabelField($"Selected: {selectedCount} of {_pendingGroups.Count} groups");
            
            EditorGUILayout.Space(10);
            
            // Action buttons
            EditorGUILayout.BeginHorizontal();
            
            if (GUILayout.Button("Cancel", GUILayout.Height(30)))
            {
                Close();
            }
            
            if (GUILayout.Button("Sync Selected Groups", GUILayout.Height(30)))
            {
                SyncSelectedGroups();
                Close();
                GUIUtility.ExitGUI();
            }
            
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.EndVertical();
        }
        
        private void SyncSelectedGroups()
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null || _toolData == null)
                return;
                
            Dictionary<string, List<GroupConfiguration.AssetEntry>> groupAssetUpdates = 
                new Dictionary<string, List<GroupConfiguration.AssetEntry>>();
                
            // Collect asset data for all groups
            foreach (var group in settings.groups)
            {
                if (group == null || string.IsNullOrEmpty(group.Name) || group.Name == "Built In Data")
                    continue;
                    
                List<GroupConfiguration.AssetEntry> validAssets = new List<GroupConfiguration.AssetEntry>();
                
                foreach (var entry in group.entries)
                {
                    if (entry == null)
                        continue;
                        
                    string assetPath = AssetDatabase.GUIDToAssetPath(entry.guid);
                    if (!string.IsNullOrEmpty(assetPath))
                    {
                        string assetName = System.IO.Path.GetFileName(assetPath);
                        validAssets.Add(new GroupConfiguration.AssetEntry(entry.guid, assetPath, assetName));
                    }
                }
                
                groupAssetUpdates[group.Name] = validAssets;
            }
            
            // Add selected groups to tool data
            foreach (string groupName in _pendingGroups)
            {
                if (_groupSelections[groupName])
                {
                    // Create new group config
                    GroupConfiguration newConfig = new GroupConfiguration
                    {
                        groupName = groupName,
                        assets = groupAssetUpdates.ContainsKey(groupName) ? 
                            new List<GroupConfiguration.AssetEntry>(groupAssetUpdates[groupName]) : 
                            new List<GroupConfiguration.AssetEntry>(),
                        labelAssignments = new List<string>(),
                        targetDevices = TargetDevice.Quest,  // Default
                        remoteDevices = TargetDevice.Quest   // Default
                    };
                    
                    if (_toolData.groupConfigurations == null)
                    {
                        _toolData.groupConfigurations = new List<GroupConfiguration>();
                    }
                    
                    _toolData.groupConfigurations.Add(newConfig);
                    
                    Debug.Log($"[AddressableTool] Added group: {groupName} with {newConfig.assets.Count} assets");
                }
            }
            
            EditorUtility.SetDirty(_toolData);
            AssetDatabase.SaveAssetIfDirty(_toolData);
        }
    }
}