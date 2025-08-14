using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System.IO;
using UnityEditor.AddressableAssets;

namespace Addressables_Wrapper.Editor
{
    /// <summary>
    /// Handles drag and drop operations for Addressable assets
    /// </summary>
    public class AddressableDragDropHandler
    {
        private AddressableToolEditorWindow _parentWindow;
        private AddressableToolStyles _styles;
        private bool _isDragging = false;
        private List<Object> _droppedAssets = new List<Object>();
        private Vector2 _assetListScrollPosition;
        private Dictionary<string, string> _assetExistingGroups = new Dictionary<string, string>();
        
        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="parentWindow">Reference to the parent window</param>
        /// <param name="styles">Reference to the styles manager</param>
        public AddressableDragDropHandler(AddressableToolEditorWindow parentWindow, AddressableToolStyles styles)
        {
            _parentWindow = parentWindow;
            _styles = styles;
        }
        
        /// <summary>
        /// Gets the list of dropped assets
        /// </summary>
        public List<Object> DroppedAssets => _droppedAssets;
        
        /// <summary>
        /// Gets the dictionary of asset existing groups
        /// </summary>
        public Dictionary<string, string> AssetExistingGroups => _assetExistingGroups;
        
        /// <summary>
        /// Clears the selection
        /// </summary>
        public void ClearSelection()
        {
            _droppedAssets.Clear();
            _assetExistingGroups.Clear();
        }
        
        /// <summary>
        /// Processes global drag events
        /// </summary>
        public void ProcessGlobalDragEvents()
        {
            Event evt = Event.current;
            switch (evt.type)
            {
                case EventType.DragExited:
                    _isDragging = false;
                    _parentWindow.Repaint();
                    break;
            }
        }
        
        /// <summary>
        /// Draws the drag and drop area
        /// </summary>
        /// <param name="onRefreshGroups">Callback for refreshing groups</param>
        public void DrawDragAndDropArea(System.Action onRefreshGroups)
        {
            // Create the drop area
            Rect dropArea = GUILayoutUtility.GetRect(0.0f, 80.0f, GUILayout.ExpandWidth(true));
            
            // Change color based on if we're dragging
            Color originalColor = GUI.color;
            GUI.color = _isDragging ? new Color(0.8f, 1.0f, 0.8f, 0.5f) : new Color(0.8f, 0.8f, 0.95f, 0.25f);
            GUI.Box(dropArea, "Drop Assets Here to Add to Addressables");
            GUI.color = originalColor;
            
            // Handle drag events
            Event evt = Event.current;
            switch (evt.type)
            {
                case EventType.DragUpdated:
                case EventType.DragPerform:
                    if (!dropArea.Contains(evt.mousePosition))
                        break;
                        
                    DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                    _isDragging = true;
                    
                    if (evt.type == EventType.DragPerform)
                    {
                        DragAndDrop.AcceptDrag();
                        _isDragging = false;
                        
                        // Maintain the existing dropped assets and only add new ones
                        var settings = AddressableAssetSettingsDefaultObject.Settings;
                        
                        foreach (Object draggedObject in DragAndDrop.objectReferences)
                        {
                            // Check if this asset is already in our list
                            bool alreadyInList = false;
                            foreach (Object existingAsset in _droppedAssets)
                            {
                                if (existingAsset == draggedObject)
                                {
                                    alreadyInList = true;
                                    break;
                                }
                            }
                            
                            // Only add if not already in the list
                            if (!alreadyInList)
                            {
                                _droppedAssets.Add(draggedObject);
                                
                                // Check if this asset is already addressable
                                if (settings != null)
                                {
                                    string assetPath = AssetDatabase.GetAssetPath(draggedObject);
                                    string assetGuid = AssetDatabase.AssetPathToGUID(assetPath);
                                    
                                    var entry = settings.FindAssetEntry(assetGuid);
                                    if (entry != null)
                                    {
                                        // Asset is already addressable, store its current group
                                        _assetExistingGroups[assetGuid] = entry.parentGroup.Name;
                                    }
                                }
                            }
                        }
                        
                        // If we have assets, update existing groups list
                        if (_droppedAssets.Count > 0)
                        {
                            onRefreshGroups?.Invoke();
                        }
                    }
                    
                    evt.Use();
                    break;
                    
                case EventType.DragExited:
                    _isDragging = false;
                    break;
            }
            
            DrawDroppedAssets();
        }
        
        /// <summary>
        /// Draws the list of dropped assets
        /// </summary>
        private void DrawDroppedAssets()
        {
            // Show dropped assets if any
            if (_droppedAssets.Count > 0)
            {
                EditorGUILayout.LabelField("Selected Assets:", EditorStyles.boldLabel);
                
                // Use scroll view for asset list to avoid excessive window growth
                _assetListScrollPosition = EditorGUILayout.BeginScrollView(
                    _assetListScrollPosition, 
                    EditorStyles.helpBox,
                    GUILayout.MinHeight(150), // Increase minimum height
                    GUILayout.MaxHeight(250)); // Increase maximum height
                
                // Display each asset with more space
                foreach (Object asset in _droppedAssets)
                {
                    EditorGUILayout.BeginHorizontal(GUILayout.Height(24)); // Increase row height
                    
                    // Add a small indent and use object field for better display
                    GUILayout.Space(5);
                    EditorGUILayout.ObjectField(asset, typeof(Object), false, GUILayout.ExpandWidth(true));
                    
                    string assetPath = AssetDatabase.GetAssetPath(asset);
                    string assetGuid = AssetDatabase.AssetPathToGUID(assetPath);
                    
                    // If asset is already addressable, show which group it belongs to
                    if (_assetExistingGroups.ContainsKey(assetGuid))
                    {
                        EditorGUILayout.LabelField($"In group: {_assetExistingGroups[assetGuid]}", 
                            EditorStyles.miniLabel, GUILayout.Width(200));
                    }
                    
                    if (GUILayout.Button("Ping", GUILayout.Width(50)))
                    {
                        Selection.activeObject = asset;
                        EditorGUIUtility.PingObject(asset);
                    }
                    EditorGUILayout.EndHorizontal();
                    
                    // Add a small space between items
                    GUILayout.Space(2);
                }
                
                EditorGUILayout.EndScrollView();
                
                // If any assets are already addressable, show a notification
                if (_assetExistingGroups.Count > 0)
                {
                    EditorGUILayout.HelpBox(
                        "Some selected assets are already in addressable groups. " +
                        "Moving them to a new group will remove them from their current groups.", 
                        MessageType.Info);
                }
            }
        }
        
        /// <summary>
        /// Draws action buttons for the dropped assets
        /// </summary>
        /// <param name="existingGroupNames">List of existing group names</param>
        /// <param name="onCreateNewGroup">Callback for creating a new group</param>
        /// <param name="onAddToExistingGroup">Callback for adding to an existing group</param>
        /// <param name="onClearSelection">Callback for clearing the selection</param>
        public void DrawActionButtons(
            List<string> existingGroupNames,
            System.Action onCreateNewGroup,
            System.Action onAddToExistingGroup,
            System.Action onClearSelection)
        {
            if (_droppedAssets.Count > 0)
            {
                EditorGUILayout.Space(5);
                
                // Show action buttons
                EditorGUILayout.BeginHorizontal();
                
                if (GUILayout.Button("Create New Group", GUILayout.Height(30)))
                {
                    onCreateNewGroup?.Invoke();
                }
                
                if (existingGroupNames.Count > 0 && GUILayout.Button("Add to Existing Group", GUILayout.Height(30)))
                {
                    onAddToExistingGroup?.Invoke();
                }
                
                if (GUILayout.Button("Clear Selection", GUILayout.Height(30)))
                {
                    onClearSelection?.Invoke();
                }
                
                EditorGUILayout.EndHorizontal();
            }
        }
    }
}