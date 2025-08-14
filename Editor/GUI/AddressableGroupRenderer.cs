using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using Addressables_Wrapper.Editor;
using System.IO;
using System.Linq;

namespace Addressables_Wrapper.Editor
{
    /// <summary>
    /// Handles rendering of both the Group Configurations UI and the Build Options UI
    /// inside the Addressable Tool window.
    /// </summary>
    public class AddressableGroupRenderer
    {
        private AddressableToolEditorWindow _parentWindow;
        private AddressableToolStyles _styles;
        private AddressableToolData _toolData;

        // Scroll position for the “Group Configurations” list
        private Vector2 _groupConfigScrollPosition;

        // Foldout states for each group’s details, assets list, and labels list
        private Dictionary<string, bool> _groupFoldouts  = new Dictionary<string, bool>();
        private Dictionary<string, bool> _assetsFoldouts = new Dictionary<string, bool>();
        private Dictionary<string, bool> _labelsFoldouts = new Dictionary<string, bool>();

        // Which checkboxes are checked (for “Apply to Checked Groups”)
        private List<bool> _groupSelections = new List<bool>();
        private bool _showGlobalOverrides = false;

        // Fields for the “Apply to Checked Groups” section
        private string _globalLabelInput    = "";
        private TargetDevice _globalTargetDevices = (TargetDevice)(-1);
        private TargetDevice _globalRemoteDevices = (TargetDevice)(-1);
        // New global override fields
        private BundleMode? _globalBundleMode = null;

        /// <summary>
        /// Constructor: caches references and initializes checkbox states.
        /// </summary>
        public AddressableGroupRenderer(
            AddressableToolEditorWindow parentWindow,
            AddressableToolStyles styles,
            AddressableToolData toolData)
        {
            _parentWindow = parentWindow;
            _styles       = styles;
            _toolData     = toolData;

            if (_toolData != null && _toolData.groupConfigurations != null)
            {
                _groupSelections = Enumerable.Repeat(false, _toolData.groupConfigurations.Count).ToList();
            }
        }

        /// <summary>
        /// Draws the “Group Configurations” section, including:
        /// Refresh / Select All / Deselect All buttons
        /// Global Overrides (Labels, Target Devices, Remote Devices) applied to all checked groups
        /// A scrollable list of each group with checkbox, foldout, Rename/Remove, and per-group Apply Changes
        /// </summary>
        /// <param name="onRefreshGroups">Callback invoked when “Refresh Groups” is clicked</param>
        /// <param name="onRenameGroup">Callback invoked when a specific group’s “Rename” button is clicked</param>
        public void DrawGroupConfigurations(
            System.Action onRefreshGroups,
            System.Action<GroupConfiguration> onRenameGroup)
        {
            // “Refresh Groups” Button
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Refresh Groups", GUILayout.Width(120)))
            {
                onRefreshGroups?.Invoke();

                // After refresh, clear all checkboxes
                if (_toolData.groupConfigurations != null)
                {
                    _groupSelections = Enumerable.Repeat(false, _toolData.groupConfigurations.Count).ToList();
                }
            }
            EditorGUILayout.EndHorizontal();

            // If there’s no toolData or no group configurations, show a message and return.
            if (_toolData == null)
            {
                EditorGUILayout.HelpBox(
                    "No data loaded. Create or select a settings asset first.",
                    MessageType.Warning
                );
                return;
            }
            if (_toolData.groupConfigurations == null || _toolData.groupConfigurations.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "No groups available. Drag assets or folders to create a group.",
                    MessageType.Info
                );
                return;
            }

            // Global Overrides Block
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Global Overrides for Checked Groups", EditorStyles.boldLabel);

            _showGlobalOverrides = EditorGUILayout.Foldout(
                _showGlobalOverrides, 
                "Global Overrides for Checked Groups",
                true
            );

            if (_showGlobalOverrides)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                // 1) Labels input
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Labels:", GUILayout.Width(60));
                _globalLabelInput = EditorGUILayout.TextField(_globalLabelInput);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.HelpBox(
                    "Type labels like \"UI;Common;Level1\" to assign to checked groups.",
                    MessageType.None
                );

                // Target Devices dropdown
                _globalTargetDevices = (TargetDevice)EditorGUILayout.EnumFlagsField(
                    "Target Devices:", 
                    _globalTargetDevices
                );

                EditorGUILayout.Space();

                // Remote Devices dropdown
                _globalRemoteDevices = (TargetDevice)EditorGUILayout.EnumFlagsField(
                    "Remote Devices:", 
                    _globalRemoteDevices
                );

                EditorGUILayout.Space();

                // Bundle Mode override
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Bundle Mode:", GUILayout.Width(120));
                BundleMode bundleModeValue = _globalBundleMode ?? BundleMode.PackSeparately;
                BundleMode newBundleMode = (BundleMode)EditorGUILayout.EnumPopup(bundleModeValue);
                if (_globalBundleMode != newBundleMode)
                    _globalBundleMode = newBundleMode;
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.Space();

                // “Apply to Checked Groups” button
                GUI.enabled = _groupSelections.Any(x => x);
                if (GUILayout.Button("Apply to Checked Groups", GUILayout.Height(30)))
                {
                    ApplyToCheckedGroups(
                        _globalLabelInput, 
                        _globalTargetDevices, 
                        _globalRemoteDevices,
                        _globalBundleMode
                    );
                }
                GUI.enabled = true;

                EditorGUILayout.EndVertical();
                EditorGUILayout.Space();
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space();

            // Select All / Deselect All Row
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            if (GUILayout.Button("Select All", GUILayout.Width(100)))
            {
                for (int i = 0; i < _toolData.groupConfigurations.Count; i++)
                {
                    _groupSelections[i] = true;
                }
            }
            if (GUILayout.Button("Deselect All", GUILayout.Width(100)))
            {
                for (int i = 0; i < _toolData.groupConfigurations.Count; i++)
                {
                    _groupSelections[i] = false;
                }
            }
            EditorGUILayout.LabelField(
                "(Use checkboxes to pick multiple groups)", EditorStyles.miniLabel
            );
            EditorGUILayout.EndHorizontal();

            // Scrollable List of Groups
            float maxHeight = Mathf.Min(400, _parentWindow.position.height * 0.4f);
            _groupConfigScrollPosition = EditorGUILayout.BeginScrollView(
                _groupConfigScrollPosition,
                GUILayout.Height(maxHeight)
            );

            // Ensure _groupSelections matches the current group count
            if (_groupSelections.Count != _toolData.groupConfigurations.Count)
            {
                _groupSelections = Enumerable.Repeat(false, _toolData.groupConfigurations.Count).ToList();
            }

            // Precompute any duplicate group names so we can show an error under them
            var duplicateNames = AddressableToolValidator.GetDuplicateGroupNames(_toolData);

            for (int i = 0; i < _toolData.groupConfigurations.Count; i++)
            {
                var config = _toolData.groupConfigurations[i];
                if (config == null) 
                    continue;

                string foldoutKey = "group_" + i;
                if (!_groupFoldouts.ContainsKey(foldoutKey))
                {
                    _groupFoldouts[foldoutKey] = false;
                }

                EditorGUILayout.BeginVertical("box");

                // Top Row: Checkbox, Foldout, Rename, Remove — 
                EditorGUILayout.BeginHorizontal();

                // Checkbox on far left
                _groupSelections[i] = EditorGUILayout.Toggle(
                    _groupSelections[i], GUILayout.Width(18)
                );

                // Foldout toggle for showing/hiding this group’s details
                bool prevState = _groupFoldouts[foldoutKey];
                _groupFoldouts[foldoutKey] = EditorGUILayout.Foldout(
                    _groupFoldouts[foldoutKey], config.groupName, true, EditorStyles.foldout
                );
                if (_groupFoldouts[foldoutKey] != prevState)
                {
                    // Remove focus to avoid Unity GUI quirks
                    GUI.FocusControl(null);
                }

                // Rename button
                if (GUILayout.Button("Rename", GUILayout.Width(60)))
                {
                    onRenameGroup?.Invoke(config);
                }

                // Remove button (with confirmation)
                if (GUILayout.Button("Remove", GUILayout.Width(60)))
                {
                    bool confirmed = EditorUtility.DisplayDialog(
                        "Remove Group",
                        $"Are you sure you want to remove '{config.groupName}'?",
                        "Yes", "No"
                    );
                    if (confirmed)
                    {
                        AddressablesEditorManager.RemoveAddressableGroup(config.groupName);
                        _toolData.groupConfigurations.RemoveAt(i);
                        EditorGUILayout.EndHorizontal();
                        EditorGUILayout.EndVertical();
                        onRefreshGroups?.Invoke();
                        break;
                    }
                }

                EditorGUILayout.EndHorizontal();

                // If this group name is duplicated, show an error help box
                if (!string.IsNullOrEmpty(config.groupName) &&
                    duplicateNames.Contains(config.groupName))
                {
                    EditorGUILayout.HelpBox("Duplicate group name detected.", MessageType.Error);
                }

                // Only draw the group’s contents if the foldout is open
                if (_groupFoldouts[foldoutKey])
                {
                    DrawGroupContent(i, config);
                }

                EditorGUILayout.EndVertical();
                EditorGUILayout.Space();
            }

            EditorGUILayout.EndScrollView();
        }

        /// <summary>
        /// Parses the user's global inputs (labels, targetDevices, remoteDevices) and
        /// applies them to every group whose checkbox is checked. Then immediately calls
        /// UpdateAddressableGroup() for each so the changes take effect.
        /// </summary>
        private void ApplyToCheckedGroups(
            string labelInput,
            TargetDevice targetDevices,
            TargetDevice remoteDevices,
            BundleMode? bundleModeOverride)
        {
            if (_toolData == null || _toolData.groupConfigurations == null)
                return;

            // Parse semicolon/comma/space-separated labels into a List<string>
            var labelsToAssign = new List<string>();
            if (!string.IsNullOrWhiteSpace(labelInput))
            {
                var tokens = labelInput
                    .Split(new[] { ';', ',', ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
                foreach (var raw in tokens)
                {
                    var trimmed = raw.Trim();
                    if (!string.IsNullOrEmpty(trimmed) &&
                        !labelsToAssign.Contains(trimmed))
                    {
                        labelsToAssign.Add(trimmed);
                    }
                }
            }

            // Loop through each group; if its checkbox is true, overwrite those fields
            for (int i = 0; i < _toolData.groupConfigurations.Count; i++)
            {
                if (!_groupSelections[i]) 
                    continue;

                var config = _toolData.groupConfigurations[i];
                if (config == null) 
                    continue;

                config.labelAssignments = new List<string>(labelsToAssign);
                config.targetDevices    = targetDevices;
                config.remoteDevices    = remoteDevices;
                
                if (bundleModeOverride.HasValue)
                    config.bundleMode = bundleModeOverride.Value;

                // Immediately push changes into Addressables settings
                AddressablesEditorManager.UpdateAddressableGroup(config);
            }

            // 3) Persist the ScriptableObject
            EditorUtility.SetDirty(_toolData);
            AssetDatabase.SaveAssetIfDirty(_toolData);
        }

        /// <summary>
        /// Draws the inner details for one group:
        /// Assets foldout (list of assets, Ping/Remove)
        /// Labels foldout (list of labels, Add/Remove)
        /// Device Settings (TargetDevices & RemoteDevices flags)
        /// Per-group “Apply Changes” button
        /// </summary>
        private void DrawGroupContent(int groupIndex, GroupConfiguration config)
        {
            // Draw the “Assets” foldout
            string assetsKey = "assets_" + groupIndex;
            if (!_assetsFoldouts.ContainsKey(assetsKey))
            {
                _assetsFoldouts[assetsKey] = false;
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            _assetsFoldouts[assetsKey] = EditorGUILayout.Foldout(
                _assetsFoldouts[assetsKey], "Assets", true
            );

            if (_assetsFoldouts[assetsKey])
            {
                DrawGroupAssets(config);
            }
            EditorGUILayout.EndVertical();

            // Draw the “Label Assignments” foldout
            string labelsKey = "labels_" + groupIndex;
            if (!_labelsFoldouts.ContainsKey(labelsKey))
            {
                _labelsFoldouts[labelsKey] = false;
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            _labelsFoldouts[labelsKey] = EditorGUILayout.Foldout(
                _labelsFoldouts[labelsKey], "Label Assignments", true
            );

            if (_labelsFoldouts[labelsKey])
            {
                DrawGroupLabels(config);
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(5);

            // Draw the “Device Settings” area
            EditorGUILayout.LabelField("Device Settings", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            config.targetDevices = (TargetDevice)EditorGUILayout.EnumFlagsField(
                "Target Devices", config.targetDevices
            );
            config.remoteDevices = (TargetDevice)EditorGUILayout.EnumFlagsField(
                "Remote Devices", config.remoteDevices
            );
            if (EditorGUI.EndChangeCheck())
            {
                SaveToolData();
            }

            EditorGUILayout.Space(5);

            // Include In Build Targets (enum flags)
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Include In Build Targets:", GUILayout.Width(120));
            
            
            EditorGUILayout.EndHorizontal();

            // Bundle Mode (dropdown)
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Bundle Mode:", GUILayout.Width(120));
            BundleMode newBundleMode = (BundleMode)EditorGUILayout.EnumPopup(config.bundleMode);
            if (config.bundleMode != newBundleMode)
            {
                config.bundleMode = newBundleMode;
                SaveToolData();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(5);

            // Per-group “Apply Changes” button
            if (GUILayout.Button("Apply Changes"))
            {
                AddressablesEditorManager.UpdateAddressableGroup(config);
                SaveToolData();
            }
        }

        /// <summary>
        /// Helper to draw the list of assets inside one group. Each asset row has:
        /// Filename label
        /// “Ping” button (pings in Project window)
        /// “Remove” button (removes from this group)
        /// </summary>
        private void DrawGroupAssets(GroupConfiguration config)
        {
            EditorGUILayout.Space(5);

            if (config.assets == null)
                config.assets = new List<GroupConfiguration.AssetEntry>();

            // Draw current assets
            if (config.assets.Count == 0)
            {
                EditorGUILayout.HelpBox("No assets in this group.", MessageType.Info);
            }
            else
            {
                EditorGUILayout.BeginVertical();
                for (int j = 0; j < config.assets.Count; j++)
                {
                    var entry = config.assets[j];
                    if (entry == null || string.IsNullOrEmpty(entry.guid))
                        continue;

                    string assetPath = entry.assetPath;
                    string assetName = entry.assetName;
                    // Fallback if missing
                    if (string.IsNullOrEmpty(assetPath) || string.IsNullOrEmpty(assetName))
                    {
                        assetPath = AssetDatabase.GUIDToAssetPath(entry.guid);
                        assetName = !string.IsNullOrEmpty(assetPath) ? Path.GetFileName(assetPath) : "(Missing Asset)";
                    }
                    Object assetObj = !string.IsNullOrEmpty(assetPath) ? AssetDatabase.LoadAssetAtPath<Object>(assetPath) : null;

                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(assetName, GUILayout.Width(150));
                    EditorGUILayout.LabelField(assetPath, EditorStyles.miniLabel);

                    if (GUILayout.Button("Ping", GUILayout.Width(50)) && assetObj != null)
                    {
                        Selection.activeObject = assetObj;
                        EditorGUIUtility.PingObject(assetObj);
                    }

                    if (GUILayout.Button("Remove", GUILayout.Width(60)))
                    {
                        AddressablesEditorManager.RemoveAssetFromGroup(config.groupName, entry.guid);
                        config.assets.RemoveAt(j);
                        SaveToolData();
                        EditorGUILayout.EndHorizontal();
                        break;
                    }

                    EditorGUILayout.EndHorizontal();
                }
                EditorGUILayout.EndVertical();
            }

            // Add drag-and-drop area
            Rect dropArea = GUILayoutUtility.GetRect(0, 40, GUILayout.ExpandWidth(true));
            GUI.Box(dropArea, "Drag assets here to add", EditorStyles.helpBox);
            Event evt = Event.current;
            if (evt.type == EventType.DragUpdated || evt.type == EventType.DragPerform)
            {
                if (dropArea.Contains(evt.mousePosition))
                {
                    DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                    if (evt.type == EventType.DragPerform)
                    {
                        DragAndDrop.AcceptDrag();
                        foreach (Object dragged in DragAndDrop.objectReferences)
                        {
                            string path = AssetDatabase.GetAssetPath(dragged);
                            string guid = AssetDatabase.AssetPathToGUID(path);
                            string name = !string.IsNullOrEmpty(path) ? Path.GetFileName(path) : "";
                            if (!string.IsNullOrEmpty(guid) && !config.assets.Exists(a => a.guid == guid))
                            {
                                config.assets.Add(new GroupConfiguration.AssetEntry(guid, path, name));
                            }
                        }
                        SaveToolData();
                    }
                    Event.current.Use();
                }
            }

            // Add asset picker button
            if (GUILayout.Button("Add Asset", GUILayout.Width(120)))
            {
                string assetPath = EditorUtility.OpenFilePanel("Select Asset", Application.dataPath, "");
                if (!string.IsNullOrEmpty(assetPath))
                {
                    if (assetPath.StartsWith(Application.dataPath))
                    {
                        string relPath = "Assets" + assetPath.Substring(Application.dataPath.Length);
                        string guid = AssetDatabase.AssetPathToGUID(relPath);
                        string name = !string.IsNullOrEmpty(relPath) ? Path.GetFileName(relPath) : "";
                        if (!string.IsNullOrEmpty(guid) && !config.assets.Exists(a => a.guid == guid))
                        {
                            config.assets.Add(new GroupConfiguration.AssetEntry(guid, relPath, name));
                            SaveToolData();
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Helper to draw the list of labels inside one group. Each label row has:
        /// Editable text field
        /// “Remove” button
        /// And at the bottom, an “Add Label” button to append a new blank entry.
        /// </summary>
        private void DrawGroupLabels(GroupConfiguration config)
        {
            EditorGUILayout.Space(5);
            if (config.labelAssignments == null)
                config.labelAssignments = new List<string>();

            for (int j = 0; j < config.labelAssignments.Count; j++)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUI.BeginChangeCheck();
                config.labelAssignments[j] = EditorGUILayout.TextField(config.labelAssignments[j]);
                if (EditorGUI.EndChangeCheck())
                {
                    SaveToolData();
                }

                if (GUILayout.Button("Remove", GUILayout.Width(60)))
                {
                    config.labelAssignments.RemoveAt(j);
                    SaveToolData();
                    EditorGUILayout.EndHorizontal();
                    break;
                }
                EditorGUILayout.EndHorizontal();
            }

            if (GUILayout.Button("Add Label"))
            {
                config.labelAssignments.Add("");
                SaveToolData();
            }
        }

        /// <summary>
        /// Saves the AddressableToolData asset if it’s dirty.
        /// </summary>
        private void SaveToolData()
        {
            if (_toolData != null)
            {
                EditorUtility.SetDirty(_toolData);
                AssetDatabase.SaveAssetIfDirty(_toolData);
            }
        }

        /// <summary>
        /// Draws the “Build Options” section. This must match your original implementation:
        /// A vertical help-box that contains:
        /// Release Version (text field)
        /// Build Target Device (enum flags field)
        /// Environment (enum popup)
        /// Another row of buttons: Build Addressables / Update Addressables
        /// </summary>
        public void DrawBuildOptions()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // Release Version
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Release Version", GUILayout.Width(120));
            _toolData.releaseVersion = EditorGUILayout.TextField(_toolData.releaseVersion);
            EditorGUILayout.EndHorizontal();

            // Build Target Device
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Build Target Device", GUILayout.Width(120));
            _toolData.buildTargetDevice = (BuildTargetDevice)EditorGUILayout.EnumFlagsField(
                _toolData.buildTargetDevice
            );
            EditorGUILayout.EndHorizontal();

            // Environment
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Environment", GUILayout.Width(120));
            _toolData.environment = (EnvironmentType)EditorGUILayout.EnumPopup(_toolData.environment);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(10);

            // Build / Update buttons
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Build Addressables", GUILayout.Height(30), GUILayout.Width(150)))
            {
                AddressablesEditorManager.BuildAddressables();
            }

            if (GUILayout.Button("Update Addressables", GUILayout.Height(30), GUILayout.Width(150)))
            {
                AddressablesEditorManager.UpdateAddressables(_toolData);
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }
    }
}
