using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;

namespace Addressables_Wrapper.Editor
{
    /// <summary>
    /// Handles dialogs for the Addressable Tool
    /// </summary>
    public class AddressableToolDialogs
    {
        private AddressableToolEditorWindow _parentWindow;
        
        // Rename dialog variables
        private bool _showRenameDialog = false;
        private GroupConfiguration _groupToRename = null;
        private string _newGroupNameInput = "";
        
        // New group dialog variables
        private bool _showNewGroupDialog = false;
        private string _newGroupName = "New Group";
        
        // Add to existing group dialog variables
        private bool _showAddToExistingGroupDialog = false;
        private int _selectedGroupIndex = -1;
        
        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="parentWindow">Reference to the parent window</param>
        public AddressableToolDialogs(AddressableToolEditorWindow parentWindow)
        {
            _parentWindow = parentWindow;
        }
        
        /// <summary>
        /// Checks if any dialog is currently shown
        /// </summary>
        public bool IsAnyDialogShown()
        {
            return _showRenameDialog || _showNewGroupDialog || _showAddToExistingGroupDialog;
        }
        
        /// <summary>
        /// Shows the rename dialog
        /// </summary>
        /// <param name="groupConfig">The group to rename</param>
        public void ShowRenameDialog(GroupConfiguration groupConfig)
        {
            _groupToRename = groupConfig;
            _newGroupNameInput = groupConfig?.groupName ?? "";
            _showRenameDialog = true;
        }
        
        /// <summary>
        /// Shows the new group dialog
        /// </summary>
        public void ShowNewGroupDialog()
        {
            _showNewGroupDialog = true;
            _showAddToExistingGroupDialog = false;
        }
        
        /// <summary>
        /// Shows the add to existing group dialog
        /// </summary>
        /// <param name="defaultIndex">Default selected group index</param>
        public void ShowAddToExistingGroupDialog(int defaultIndex = 0)
        {
            _showAddToExistingGroupDialog = true;
            _showNewGroupDialog = false;
            _selectedGroupIndex = defaultIndex;
        }
        
        /// <summary>
        /// Closes all dialogs
        /// </summary>
        public void CloseAllDialogs()
        {
            _showRenameDialog = false;
            _showNewGroupDialog = false;
            _showAddToExistingGroupDialog = false;
            _groupToRename = null;
        }
        
        /// <summary>
        /// Draws the rename dialog
        /// </summary>
        public void DrawRenameDialog()
        {
            if (!_showRenameDialog)
                return;
                
            // Create a centered dialog box
            int dialogWidth = 300;
            int dialogHeight = 100;
            Rect dialogRect = new Rect(
                (_parentWindow.position.width - dialogWidth) / 2,
                (_parentWindow.position.height - dialogHeight) / 2,
                dialogWidth, 
                dialogHeight);
                
            // Draw dialog background
            GUI.Box(dialogRect, "");
            
            GUILayout.BeginArea(dialogRect);
            
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            
            EditorGUILayout.LabelField("Rename Group", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);
            
            _newGroupNameInput = EditorGUILayout.TextField("New Name:", _newGroupNameInput);
            
            EditorGUILayout.Space(10);
            
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            
            if (GUILayout.Button("Cancel", GUILayout.Width(80)))
            {
                _showRenameDialog = false;
                _groupToRename = null;
            }
            
            if (GUILayout.Button("OK", GUILayout.Width(80)))
            {
                if (_groupToRename != null && !string.IsNullOrEmpty(_newGroupNameInput))
                {
                    string previousName = _groupToRename.groupName;
                    _groupToRename.groupName = _newGroupNameInput;
                    
                    if (previousName != _newGroupNameInput)
                    {
                        AddressablesEditorManager.RenameAddressableGroup(previousName, _newGroupNameInput);
                    }
                }
                
                _showRenameDialog = false;
                _groupToRename = null;
            }
            
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.EndVertical();
            
            GUILayout.EndArea();
        }
        
        /// <summary>
        /// Draws the new group dialog
        /// </summary>
        /// <param name="onCreateGroup">Callback for when a group is created</param>
        public void DrawNewGroupDialog(System.Action<string> onCreateGroup)
        {
            if (!_showNewGroupDialog)
                return;
                
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Create New Addressable Group", EditorStyles.boldLabel);
            
            _newGroupName = EditorGUILayout.TextField("Group Name:", _newGroupName);
            
            EditorGUILayout.Space();
            
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Create Group"))
            {
                onCreateGroup?.Invoke(_newGroupName);
                _showNewGroupDialog = false;
                _newGroupName = "New Group";
            }
            
            if (GUILayout.Button("Cancel"))
            {
                _showNewGroupDialog = false;
            }
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.EndVertical();
        }
        
        /// <summary>
        /// Draws the add to existing group dialog
        /// </summary>
        /// <param name="existingGroupNames">List of existing group names</param>
        /// <param name="assetExistingGroups">Dictionary mapping asset GUIDs to group names</param>
        /// <param name="onAddToGroup">Callback for when assets are added to a group</param>
        public void DrawAddToExistingGroupDialog(
            List<string> existingGroupNames, 
            Dictionary<string, string> assetExistingGroups,
            System.Action<string> onAddToGroup)
        {
            if (!_showAddToExistingGroupDialog)
                return;
                
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Add to Existing Addressable Group", EditorStyles.boldLabel);
            
            if (existingGroupNames.Count > 0)
            {
                _selectedGroupIndex = EditorGUILayout.Popup("Select Group:", _selectedGroupIndex, existingGroupNames.ToArray());
                
                // Check if any assets are already in the selected group
                string selectedGroupName = existingGroupNames[_selectedGroupIndex];
                bool anyAssetsAlreadyInGroup = false;
                
                foreach (var kvp in assetExistingGroups)
                {
                    if (kvp.Value == selectedGroupName)
                    {
                        anyAssetsAlreadyInGroup = true;
                        break;
                    }
                }
                
                if (anyAssetsAlreadyInGroup)
                {
                    EditorGUILayout.HelpBox(
                        "Some assets are already in this group and will be skipped.", 
                        MessageType.Info);
                }
                
                EditorGUILayout.Space();
                
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Add Assets"))
                {
                    onAddToGroup?.Invoke(existingGroupNames[_selectedGroupIndex]);
                    _showAddToExistingGroupDialog = false;
                }
                
                if (GUILayout.Button("Cancel"))
                {
                    _showAddToExistingGroupDialog = false;
                }
                EditorGUILayout.EndHorizontal();
            }
            else
            {
                EditorGUILayout.HelpBox("No existing groups found. Please create a new group first.", MessageType.Warning);
                
                if (GUILayout.Button("Close"))
                {
                    _showAddToExistingGroupDialog = false;
                }
            }
            
            EditorGUILayout.EndVertical();
        }
    }
}