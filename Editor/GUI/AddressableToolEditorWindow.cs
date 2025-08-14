using UnityEditor;
using UnityEngine;
using System.IO;
using System.Collections.Generic;
using Editor_Utility;
using System.Linq;
using UnityEditor.AddressableAssets;

namespace Addressables_Wrapper.Editor
{
    public class AddressableToolEditorWindow : EditorWindowBase
    {
        private AddressableToolData _toolData;
        private const string AddressableRootPath = "Assets/AddressableContent/";
        
        // Main scroll view position
        private Vector2 _mainScrollPosition;
        
        // Component instances
        private AddressableToolStyles _styles;
        private AddressableToolDialogs _dialogs;
        private AddressableDragDropHandler _dragDropHandler;
        private AddressableGroupRenderer _groupRenderer;
        private AddressableExclusionDrawer _exclusionDrawer;
        
        // Supporting data
        private List<string> _existingGroupNames = new List<string>();
        private bool _allowSyncDialog = false;
        
        [MenuItem("Tools/Addressables/Grouping Tool")]
        public static void ShowWindow()
        {
            GetWindow<AddressableToolEditorWindow>("Addressable Tool");
        }

        protected override void OnEnable()
        {
            base.OnEnable(); // Call base method to load image

            HeaderTitle = "Addressable Tool"; // Set custom title text

            // Ensure directories exist
            string dataDir = "Assets/Editor/AddressableTool/Data";
            if (!Directory.Exists(dataDir))
            {
                Directory.CreateDirectory(dataDir);
            }
                
            string dataPath = Path.Combine(dataDir, "AddressableToolData.asset");
            _toolData = AssetDatabase.LoadAssetAtPath<AddressableToolData>(dataPath);
            if (_toolData == null)
            {
                _toolData = CreateInstance<AddressableToolData>();
                AssetDatabase.CreateAsset(_toolData, dataPath);
                AssetDatabase.SaveAssetIfDirty(_toolData);
            }
            
            // Initialize components
            _styles = new AddressableToolStyles();
            _dialogs = new AddressableToolDialogs(this);
            _dragDropHandler = new AddressableDragDropHandler(this, _styles);
            _groupRenderer = new AddressableGroupRenderer(this, _styles, _toolData);
            _exclusionDrawer = new AddressableExclusionDrawer(_toolData);
            
            // Get existing group names
            RefreshExistingGroups(); 
        }

        private void OnGUI()
        {
            _styles.EnsureStylesInitialized();
            
            // Process drag events at window level to prevent blank window
            _dragDropHandler.ProcessGlobalDragEvents();
            
            // Draw header outside scroll view
            DrawHeader();

            // Begin main scroll view for the entire window content
            _mainScrollPosition = EditorGUILayout.BeginScrollView(_mainScrollPosition);
            
            EditorGUILayout.Space(10);
            
            // Draw all sections regardless of dialog state, but control interaction
            bool hasActiveDialog = _dialogs.IsAnyDialogShown() || 
                                  (_exclusionDrawer != null && _exclusionDrawer.IsShowingDialog());
            
            // Addressables drag and drop section
            DrawSection("Drag and Drop Assets Here", () => {
                _dragDropHandler.DrawDragAndDropArea(() => RefreshExistingGroups());
                
                // Draw action buttons for dropped assets
                _dragDropHandler.DrawActionButtons(
                    _existingGroupNames,
                    () => {
                        _dialogs.ShowNewGroupDialog();
                        if (_exclusionDrawer != null) _exclusionDrawer.ClearDialogs();
                    },
                    () => {
                        _dialogs.ShowAddToExistingGroupDialog();
                        if (_exclusionDrawer != null) _exclusionDrawer.ClearDialogs();
                    },
                    () => {
                        _dragDropHandler.ClearSelection();
                        _dialogs.CloseAllDialogs();
                    }
                );
                
                // Dialog rendering
                _dialogs.DrawNewGroupDialog((groupName) => {
                    CreateNewGroup(groupName, _dragDropHandler.DroppedAssets);
                    _dragDropHandler.ClearSelection();
                });
                
                _dialogs.DrawAddToExistingGroupDialog(
                    _existingGroupNames,
                    _dragDropHandler.AssetExistingGroups,
                    (groupName) => {
                        AddToExistingGroup(groupName, _dragDropHandler.DroppedAssets);
                        _dragDropHandler.ClearSelection();
                    }
                );
            });
            
            EditorGUILayout.Space(10);
            
            // Exclusion section - always visible but may show dialog
            DrawSection("Platform-Specific Asset Exclusions", () => {
                if (_exclusionDrawer != null)
                {
                    _exclusionDrawer.DrawExclusionSection();
                }
            });
            
            EditorGUILayout.Space(10);
            
            // Group configurations section
            DrawSection("Group Configurations", () => {
                // Only enable interactions if no dialog is active
                using (new EditorGUI.DisabledGroupScope(hasActiveDialog))
                {
                    _groupRenderer.DrawGroupConfigurations(
                        () => {
                            // Defer the refresh to avoid GUI layout issues
                            EditorApplication.delayCall += () => {
                                _allowSyncDialog = true;
                                RefreshExistingGroups();
                                _allowSyncDialog = false;
                            };
                        },
                        (config) => _dialogs.ShowRenameDialog(config)
                    );
                }
            });
            
            EditorGUILayout.Space(10);
            
            // Build options section
            DrawSection("Build Options", () => {
                // Only enable interactions if no dialog is active
                using (new EditorGUI.DisabledGroupScope(hasActiveDialog))
                {
                    _groupRenderer.DrawBuildOptions();
                }
            });
            
            EditorGUILayout.Space(20); // Add bottom padding
            
            EditorGUILayout.EndScrollView();
            
            // Draw rename dialog (outside of scroll view to stay visible)
            _dialogs.DrawRenameDialog();

            // Mark data as dirty if GUI has changed
            if (GUI.changed)
            {
                EditorUtility.SetDirty(_toolData);
            }
        }
        
        // Helper to draw a consistent section with header
        private void DrawSection(string title, System.Action drawContent)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            
            EditorGUILayout.LabelField(title, _styles.SectionHeaderStyle);
            
            if (drawContent != null)
            {
                drawContent();
            }
            
            EditorGUILayout.EndVertical();
        }

        private void CreateNewGroup(string groupName, List<Object> assets)
        {
            if (string.IsNullOrEmpty(groupName))
            {
                EditorUtility.DisplayDialog("Error", "Group name cannot be empty.", "OK");
                return;
            }
            
            // Check if we already have this group in our data
            if (_toolData.groupConfigurations != null &&
                _toolData.groupConfigurations.Any(g => g.groupName == groupName))
            {
                EditorUtility.DisplayDialog("Error", $"Group '{groupName}' already exists.", "OK");
                return;
            }
            
            // Create a new group configuration
            GroupConfiguration newConfig = new GroupConfiguration
            {
                groupName = groupName,
                assets = new List<GroupConfiguration.AssetEntry>(),
                labelAssignments = new List<string>(),
                targetDevices = TargetDevice.Quest,
                remoteDevices = TargetDevice.Quest
            };
            
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                Debug.LogError("AddressableAssetSettings not found!");
                return;
            }
            
            // Store old group references for tracking
            Dictionary<string, bool> oldGroups = new Dictionary<string, bool>();
            
            // Add all assets to the configuration
            foreach (Object asset in assets)
            {
                string assetPath = AssetDatabase.GetAssetPath(asset);
                string assetGuid = AssetDatabase.AssetPathToGUID(assetPath);
                
                if (!string.IsNullOrEmpty(assetPath))
                {
                    // Check if asset is already in another group
                    if (_dragDropHandler.AssetExistingGroups.ContainsKey(assetGuid))
                    {
                        // Record the old group for later update
                        string oldGroupName = _dragDropHandler.AssetExistingGroups[assetGuid];
                        if (!oldGroups.ContainsKey(oldGroupName))
                        {
                            oldGroups[oldGroupName] = true;
                        }
                        
                        // No need to manually remove asset from old config - it'll be handled by the sync
                    }
                    
                    newConfig.assets.Add(new GroupConfiguration.AssetEntry(assetGuid, assetPath, System.IO.Path.GetFileName(assetPath)));
                }
            }
            
            // Add the new configuration to the tool data
            if (_toolData.groupConfigurations == null)
            {
                _toolData.groupConfigurations = new List<GroupConfiguration>();
            }
            
            _toolData.groupConfigurations.Add(newConfig);
            
            // Create the addressable group through the manager
            AddressablesEditorManager.CreateAddressableGroup(newConfig);
            
            // Wait until next frame to update affected groups to avoid collection modification
            EditorApplication.delayCall += () => 
            {
                // Now sync everything to get accurate data
                SyncWithAddressableSystem();
                
                EditorUtility.SetDirty(_toolData);
                AssetDatabase.SaveAssetIfDirty(_toolData);
            };
            
            Debug.Log($"[AddressableTool] Created new group '{groupName}' with {assets.Count} assets.");
            
            // Refresh UI
            RefreshExistingGroups();
        }

        private void AddToExistingGroup(string groupName, List<Object> assets)
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                Debug.LogError("AddressableAssetSettings not found!");
                return;
            }
            
            // Find the existing group configuration
            GroupConfiguration existingConfig = _toolData.groupConfigurations?.FirstOrDefault(g => g.groupName == groupName);
            
            // If we don't have it in our data yet, create a new entry
            if (existingConfig == null)
            {
                existingConfig = new GroupConfiguration
                {
                    groupName = groupName,
                    assets = new List<GroupConfiguration.AssetEntry>(),
                    labelAssignments = new List<string>(),
                    targetDevices = TargetDevice.Quest,
                    remoteDevices = TargetDevice.Quest
                };
                
                if (_toolData.groupConfigurations == null)
                {
                    _toolData.groupConfigurations = new List<GroupConfiguration>();
                }
                
                _toolData.groupConfigurations.Add(existingConfig);
            }
            
            // Store old group references for tracking
            Dictionary<string, bool> oldGroups = new Dictionary<string, bool>();
            oldGroups[groupName] = true;
            
            // Add all assets to the configuration
            foreach (Object asset in assets)
            {
                string assetPath = AssetDatabase.GetAssetPath(asset);
                string assetGuid = AssetDatabase.AssetPathToGUID(assetPath);
                
                if (!string.IsNullOrEmpty(assetPath))
                {
                    // Check if asset is already in this group - if so, skip it
                    if (_dragDropHandler.AssetExistingGroups.ContainsKey(assetGuid) && 
                        _dragDropHandler.AssetExistingGroups[assetGuid] == groupName)
                    {
                        continue;
                    }
                    
                    // Check if asset is already in another group
                    if (_dragDropHandler.AssetExistingGroups.ContainsKey(assetGuid))
                    {
                        // Record the old group for later
                        string oldGroupName = _dragDropHandler.AssetExistingGroups[assetGuid];
                        if (!oldGroups.ContainsKey(oldGroupName))
                        {
                            oldGroups[oldGroupName] = true;
                        }
                        
                        // No need to manually remove asset from old config - it'll be handled by the sync
                    }
                    
                    // Check if asset is already in the group config
                    if (!existingConfig.assets.Any(a => a.guid == assetGuid))
                    {
                        existingConfig.assets.Add(new GroupConfiguration.AssetEntry(assetGuid, assetPath, System.IO.Path.GetFileName(assetPath)));
                    }
                }
            }
            
            // Update the target group first
            AddressablesEditorManager.UpdateAddressableGroup(existingConfig);
            
            // Wait until next frame to update affected groups to avoid collection modification
            EditorApplication.delayCall += () => 
            {
                // Now sync everything to get accurate data
                SyncWithAddressableSystem();
                
                EditorUtility.SetDirty(_toolData);
                AssetDatabase.SaveAssetIfDirty(_toolData);
            };
            
            Debug.Log($"[AddressableTool] Added assets to group '{groupName}'.");
        }
        
        // Sync our tool data with the current addressable system state
        private void SyncWithAddressableSystem()
        {
            if (_toolData == null)
                return;
                
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
                return;
            
            // Update existing group assets first
            foreach (var groupConfig in _toolData.groupConfigurations.ToList())
            {
                if (groupConfig == null || string.IsNullOrEmpty(groupConfig.groupName))
                    continue;
                    
                // Find corresponding addressable group
                var addressableGroup = settings.groups.FirstOrDefault(g => g.Name == groupConfig.groupName);
                if (addressableGroup != null)
                {
                    // Update assets
                    List<GroupConfiguration.AssetEntry> validAssets = new List<GroupConfiguration.AssetEntry>();
                    
                    foreach (var entry in addressableGroup.entries)
                    {
                        if (entry == null)
                            continue;
                            
                        string assetPath = AssetDatabase.GUIDToAssetPath(entry.guid);
                        if (!string.IsNullOrEmpty(assetPath))
                        {
                            validAssets.Add(new GroupConfiguration.AssetEntry(entry.guid, assetPath, System.IO.Path.GetFileName(assetPath)));
                        }
                    }
                    
                    groupConfig.assets = validAssets;
                }
                else
                {
                    // Group exists in our data but not in addressables - it may have been removed
                    groupConfig.assets = new List<GroupConfiguration.AssetEntry>();
                }
            }
            
            // Get existing group names
            HashSet<string> existingGroupNames = new HashSet<string>();
            if (_toolData.groupConfigurations != null)
            {
                foreach (var config in _toolData.groupConfigurations)
                {
                    if (config != null && !string.IsNullOrEmpty(config.groupName))
                    {
                        existingGroupNames.Add(config.groupName);
                    }
                }
            }
            
            // Find new groups
            List<string> newGroups = new List<string>();
            foreach (var group in settings.groups)
            {
                if (group != null && !string.IsNullOrEmpty(group.Name) && !existingGroupNames.Contains(group.Name))
                {
                    newGroups.Add(group.Name);
                }
            }
            
            // If new groups found, show the sync window only if allowed
            if (newGroups.Count > 0 && _allowSyncDialog)
            {
                SyncGroupsWindow.ShowWindow(_toolData, newGroups); 
            }
            
            EditorUtility.SetDirty(_toolData);
        }

        private void RefreshExistingGroups()
        {
            if (_existingGroupNames == null)
            {
                _existingGroupNames = new List<string>();
            }
            else
            {
                _existingGroupNames.Clear();
            }
            
            // Get addressable settings
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings != null)
            {
                foreach (var group in settings.groups)
                {
                    if (group != null)
                    {
                        _existingGroupNames.Add(group.Name);
                    }
                }
            }
            
            // Also add groups from our tool data
            if (_toolData != null && _toolData.groupConfigurations != null)
            {
                foreach (var config in _toolData.groupConfigurations)
                {
                    if (config != null && !string.IsNullOrEmpty(config.groupName) && !_existingGroupNames.Contains(config.groupName))
                    {
                        _existingGroupNames.Add(config.groupName);
                    }
                }
            }
            
            // Sync our data with the addressable system to ensure assets are up to date
            SyncWithAddressableSystem();
        }
        
        [MenuItem("Assets/Addressable Config/Remote Quest", false, 10)]
        private static void SetRemoteQuest()
        {
            SetRemoteDevice(TargetDevice.Quest);
        }

        [MenuItem("Assets/Addressable Config/Remote Mobile_Android", false, 10)]
        private static void SetRemoteMobileAndroid()
        {
            SetRemoteDevice(TargetDevice.Mobile_Android);
        }

        [MenuItem("Assets/Addressable Config/Remote Mobile_iOS", false, 10)]
        private static void SetRemoteMobileiOS()
        {
            SetRemoteDevice(TargetDevice.Mobile_iOS);
        }
        
        [MenuItem("Assets/Addressable Config/Remote PC", false, 10)]
        private static void SetRemotePC()
        {
            SetRemoteDevice(TargetDevice.PC);
        }

        private static void SetRemoteDevice(TargetDevice target)
        {
            // Get the selected object
            Object selectedObject = Selection.activeObject;
            if (selectedObject == null)
                return;
                
            string assetPath = AssetDatabase.GetAssetPath(selectedObject);
            string assetGuid = AssetDatabase.AssetPathToGUID(assetPath);
            
            if (string.IsNullOrEmpty(assetGuid))
                return;
            
            // Load the tool data
            string dataPath = "Assets/Editor/AddressableTool/Data/AddressableToolData.asset";
            AddressableToolData toolData = AssetDatabase.LoadAssetAtPath<AddressableToolData>(dataPath);
            if (toolData == null)
            {
                Debug.LogError("[AddressableTool] AddressableToolData asset not found.");
                return;
            }
            
            // Find which group contains this asset
            GroupConfiguration containingGroup = null;
            if (toolData.groupConfigurations != null)
            {
                foreach (var group in toolData.groupConfigurations)
                {
                    if (group != null && group.assets != null && group.assets.Any(asset => asset.guid == assetGuid))
                    {
                        containingGroup = group;
                        break;
                    }
                }
            }
            
            if (containingGroup != null)
            {
                // Update the remote devices flag
                containingGroup.remoteDevices |= target;
                Debug.Log($"[AddressableTool] Updated '{containingGroup.groupName}' to be remote for {target}.");
                
                // Apply the changes
                AddressablesEditorManager.UpdateAddressableGroup(containingGroup);
                
                EditorUtility.SetDirty(toolData);
                AssetDatabase.SaveAssetIfDirty(toolData);
            }
            else
            {
                Debug.LogWarning("[AddressableTool] Selected asset is not in any addressable group.");
            }
        }
    }
}