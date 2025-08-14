using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using UnityEditor.AddressableAssets;

namespace Addressables_Wrapper.Editor
{
    /// <summary>
    /// Draws the UI for excluding addressable entries by asset, folder, or extension,
    /// and allows editing the Platforms/Environments on each existing exclusion row.
    /// </summary>
    public class AddressableExclusionDrawer
    {
        private AddressableToolData _toolData;
        private bool _isExcludeDragging = false;
        private List<Object> _excludedDroppedObjects = new List<Object>();
        private bool _showExcludeDeviceDialog = false;
        private TargetDevice _selectedExcludeDevice = TargetDevice.Quest;
        private EnvironmentType _selectedExcludeEnvironment = EnvironmentType.Development;
        private ExclusionType _dialogExclusionType = ExclusionType.Asset;
        private Vector2 _scrollPosition;
        private string _extensionInput = "";

        /// <summary>
        /// Initializes a new instance of the <see cref="AddressableExclusionDrawer"/> class.
        /// </summary>
        /// <param name="toolData">Reference to the AddressableToolData containing exclusion entries.</param>
        public AddressableExclusionDrawer(AddressableToolData toolData)
        {
            _toolData = toolData;
            if (_toolData.platformExclusions == null)
            {
                _toolData.platformExclusions = new List<PlatformExclusion>();
            }
        }

        /// <summary>
        /// Returns true if the exclusion dialog is currently being shown.
        /// </summary>
        public bool IsShowingDialog()
        {
            return _showExcludeDeviceDialog;
        }

        /// <summary>
        /// Hides any open dialogs and clears all transient dialog state.
        /// </summary>
        public void ClearDialogs()
        {
            _showExcludeDeviceDialog = false;
            _excludedDroppedObjects.Clear();
            _extensionInput = "";
        }

        /// <summary>
        /// Renders the entire exclusion section in the editor window.
        /// </summary>
        public void DrawExclusionSection()
        {
            EditorGUILayout.LabelField(
                "Exclude from Addressables for specific Platforms / Environments",
                EditorStyles.miniLabel);

            DrawDropArea();
            EditorGUILayout.Space(5);
            DrawExtensionInput();
            EditorGUILayout.Space(10);

            if (_showExcludeDeviceDialog)
            {
                DrawExcludeDeviceDialog();
            }

            EditorGUILayout.Space(10);
            DrawExistingExclusions();
        }

        /// <summary>
        /// Renders a drag-and-drop area for assets or folders to exclude.
        /// </summary>
        private void DrawDropArea()
        {
            Rect dropArea = GUILayoutUtility.GetRect(0f, 40f, GUILayout.ExpandWidth(true));
            Color originalColor = GUI.color;
            GUI.color = _isExcludeDragging
                ? new Color(1f, 0.8f, 0.8f, 0.5f)
                : new Color(0.95f, 0.8f, 0.8f, 0.25f);
            GUI.Box(dropArea, "Drop Assets or Folders Here to Exclude");
            GUI.color = originalColor;

            Event evt = Event.current;
            switch (evt.type)
            {
                case EventType.DragUpdated:
                case EventType.DragPerform:
                    if (!dropArea.Contains(evt.mousePosition))
                    {
                        break;
                    }

                    DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                    _isExcludeDragging = true;

                    if (evt.type == EventType.DragPerform)
                    {
                        DragAndDrop.AcceptDrag();
                        _isExcludeDragging = false;
                        _excludedDroppedObjects.Clear();

                        foreach (var dragged in DragAndDrop.objectReferences)
                        {
                            string path = AssetDatabase.GetAssetPath(dragged);
                            if (!string.IsNullOrEmpty(path))
                            {
                                _excludedDroppedObjects.Add(dragged);
                            }
                        }

                        if (_excludedDroppedObjects.Count > 0)
                        {
                            _showExcludeDeviceDialog = true;
                        }
                    }

                    evt.Use();
                    break;

                case EventType.DragExited:
                    _isExcludeDragging = false;
                    break;
            }
        }

        /// <summary>
        /// Renders a text field for entering file extensions to exclude.
        /// </summary>
        private void DrawExtensionInput()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Or exclude by file-extension:", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            _extensionInput = EditorGUILayout.TextField("Extensions (semicolon-separated):", _extensionInput);
            if (GUILayout.Button("Add", GUILayout.Width(60)))
            {
                if (!string.IsNullOrWhiteSpace(_extensionInput))
                {
                    _dialogExclusionType = ExclusionType.Extension;
                    _showExcludeDeviceDialog = true;
                }
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.HelpBox("Example: \".png; .fbx; .wav\"", MessageType.Info);
            EditorGUILayout.EndVertical();
        }

        /// <summary>
        /// Renders the dialog allowing selection of platforms and environments for the exclusion.
        /// </summary>
        private void DrawExcludeDeviceDialog()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Exclude From Platforms / Environments", EditorStyles.boldLabel);

            if (_restrictedToDraggedAssets())
            {
                bool anyFolder = false;
                foreach (var o in _excludedDroppedObjects)
                {
                    var path = AssetDatabase.GetAssetPath(o);
                    if (AssetDatabase.IsValidFolder(path))
                    {
                        anyFolder = true;
                        break;
                    }
                }

                _dialogExclusionType = anyFolder ? ExclusionType.Folder : ExclusionType.Asset;
                EditorGUILayout.LabelField($"Detected as: {_dialogExclusionType}", EditorStyles.helpBox);
            }
            else
            {
                _dialogExclusionType = ExclusionType.Extension;
                EditorGUILayout.LabelField("Detected as: Extension", EditorStyles.helpBox);
            }

            EditorGUILayout.Space();
            _selectedExcludeDevice = (TargetDevice)EditorGUILayout.EnumFlagsField(
                "Exclude From Platforms:", _selectedExcludeDevice);
            _selectedExcludeEnvironment = (EnvironmentType)EditorGUILayout.EnumFlagsField(
                "Exclude From Environments:", _selectedExcludeEnvironment);
            EditorGUILayout.Space();

            if (_dialogExclusionType == ExclusionType.Asset)
            {
                EditorGUILayout.LabelField("Selected Assets to Exclude:", EditorStyles.boldLabel);
                _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition, GUILayout.Height(100));
                foreach (var o in _excludedDroppedObjects)
                {
                    var p = AssetDatabase.GetAssetPath(o);
                    if (!AssetDatabase.IsValidFolder(p))
                    {
                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.ObjectField(o, typeof(Object), false);
                        if (GUILayout.Button("Ping", GUILayout.Width(50)))
                        {
                            Selection.activeObject = o;
                            EditorGUIUtility.PingObject(o);
                        }
                        EditorGUILayout.EndHorizontal();
                    }
                }
                EditorGUILayout.EndScrollView();
            }
            else if (_dialogExclusionType == ExclusionType.Folder)
            {
                EditorGUILayout.LabelField("Selected Folders to Exclude:", EditorStyles.boldLabel);
                _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition, GUILayout.Height(100));
                foreach (var o in _excludedDroppedObjects)
                {
                    var p = AssetDatabase.GetAssetPath(o);
                    if (AssetDatabase.IsValidFolder(p))
                    {
                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.LabelField(p);
                        if (GUILayout.Button("Ping", GUILayout.Width(50)))
                        {
                            var folderObj = AssetDatabase.LoadAssetAtPath<Object>(p);
                            Selection.activeObject = folderObj;
                            EditorGUIUtility.PingObject(folderObj);
                        }
                        EditorGUILayout.EndHorizontal();
                    }
                }
                EditorGUILayout.EndScrollView();
            }
            else if (_dialogExclusionType == ExclusionType.Extension)
            {
                EditorGUILayout.LabelField("Extensions to Exclude:", EditorStyles.boldLabel);
                EditorGUILayout.LabelField(_extensionInput, EditorStyles.wordWrappedLabel);
                EditorGUILayout.HelpBox(
                    "All assets matching these extensions will be excluded for the chosen platforms/environments.",
                    MessageType.Info);
            }

            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Cancel", GUILayout.Height(28)))
            {
                _showExcludeDeviceDialog = false;
                _excludedDroppedObjects.Clear();
                _extensionInput = "";
            }
            if (GUILayout.Button("Add Exclusions", GUILayout.Height(28)))
            {
                switch (_dialogExclusionType)
                {
                    case ExclusionType.Asset:
                        AddAssetExclusions(_excludedDroppedObjects, _selectedExcludeDevice, _selectedExcludeEnvironment);
                        break;
                    case ExclusionType.Folder:
                        AddFolderExclusions(_excludedDroppedObjects, _selectedExcludeDevice, _selectedExcludeEnvironment);
                        break;
                    case ExclusionType.Extension:
                        AddExtensionExclusions(_extensionInput, _selectedExcludeDevice, _selectedExcludeEnvironment);
                        break;
                }
                _showExcludeDeviceDialog = false;
                _excludedDroppedObjects.Clear();
                _extensionInput = "";
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        /// <summary>
        /// Checks if the current exclusion type is Asset or Folder.
        /// </summary>
        /// <returns>True if exclusion type is Asset or Folder; otherwise, false.</returns>
        private bool _restrictedToDraggedAssets()
        {
            return _dialogExclusionType == ExclusionType.Asset || _dialogExclusionType == ExclusionType.Folder;
        }

        /// <summary>
        /// Adds individual asset exclusions to the tool data.
        /// </summary>
        /// <param name="assets">List of objects representing assets.</param>
        /// <param name="platforms">Platforms to exclude from.</param>
        /// <param name="environments">Environments to exclude from.</param>
        private void AddAssetExclusions(
            List<Object> assets,
            TargetDevice platforms,
            EnvironmentType environments)
        {
            foreach (var o in assets)
            {
                string path = AssetDatabase.GetAssetPath(o);
                if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path))
                {
                    continue;
                }

                string guid = AssetDatabase.AssetPathToGUID(path);
                if (string.IsNullOrEmpty(guid))
                {
                    continue;
                }

                bool alreadyExists = _toolData.platformExclusions.Exists(e =>
                    e.exclusionType == ExclusionType.Asset &&
                    e.assetGuid == guid &&
                    e.excludedPlatforms == platforms &&
                    e.excludedEnvironments == environments);

                if (!alreadyExists)
                {
                    var entry = new PlatformExclusion
                    {
                        exclusionType = ExclusionType.Asset,
                        assetGuid = guid,
                        assetPath = path,
                        excludedPlatforms = platforms,
                        excludedEnvironments = environments
                    };
                    _toolData.platformExclusions.Add(entry);
                }
            }

            EditorUtility.SetDirty(_toolData);
        }

        /// <summary>
        /// Adds folder exclusions to the tool data.
        /// </summary>
        /// <param name="folders">List of objects representing folders.</param>
        /// <param name="platforms">Platforms to exclude from.</param>
        /// <param name="environments">Environments to exclude from.</param>
        private void AddFolderExclusions(
            List<Object> folders,
            TargetDevice platforms,
            EnvironmentType environments)
        {
            foreach (var o in folders)
            {
                string path = AssetDatabase.GetAssetPath(o);
                if (!AssetDatabase.IsValidFolder(path))
                {
                    continue;
                }

                if (!path.EndsWith("/"))
                {
                    path += "/";
                }

                bool alreadyExists = _toolData.platformExclusions.Exists(e =>
                    e.exclusionType == ExclusionType.Folder &&
                    e.folderPath == path &&
                    e.excludedPlatforms == platforms &&
                    e.excludedEnvironments == environments);

                if (!alreadyExists)
                {
                    var entry = new PlatformExclusion
                    {
                        exclusionType = ExclusionType.Folder,
                        folderPath = path,
                        excludedPlatforms = platforms,
                        excludedEnvironments = environments
                    };
                    _toolData.platformExclusions.Add(entry);
                }
            }

            EditorUtility.SetDirty(_toolData);
        }

        /// <summary>
        /// Adds extension-based exclusions to the tool data.
        /// </summary>
        /// <param name="extensionList">Semicolon-separated list of file extensions.</param>
        /// <param name="platforms">Platforms to exclude from.</param>
        /// <param name="environments">Environments to exclude from.</param>
        private void AddExtensionExclusions(
            string extensionList,
            TargetDevice platforms,
            EnvironmentType environments)
        {
            var tokens = extensionList
                .Split(new[] { ';', ',', ' ' }, System.StringSplitOptions.RemoveEmptyEntries);

            foreach (var raw in tokens)
            {
                string ext = raw.Trim();
                if (!ext.StartsWith("."))
                {
                    ext = "." + ext;
                }

                bool alreadyExists = _toolData.platformExclusions.Exists(e =>
                    e.exclusionType == ExclusionType.Extension &&
                    e.fileExtension.Equals(ext, System.StringComparison.OrdinalIgnoreCase) &&
                    e.excludedPlatforms == platforms &&
                    e.excludedEnvironments == environments);

                if (!alreadyExists)
                {
                    var entry = new PlatformExclusion
                    {
                        exclusionType = ExclusionType.Extension,
                        fileExtension = ext,
                        excludedPlatforms = platforms,
                        excludedEnvironments = environments
                    };
                    _toolData.platformExclusions.Add(entry);
                }
            }

            EditorUtility.SetDirty(_toolData);
        }

        /// <summary>
        /// Renders and allows editing of the list of existing exclusions in the tool data.
        /// </summary>
        private void DrawExistingExclusions()
        {
            if (_toolData.platformExclusions == null || _toolData.platformExclusions.Count == 0)
            {
                EditorGUILayout.HelpBox("No exclusions defined yet.", MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField("Current Exclusions:", EditorStyles.boldLabel);
            float listHeight = Mathf.Min(200, _toolData.platformExclusions.Count * 50 + 10);
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition, GUILayout.Height(listHeight));

            for (int i = 0; i < _toolData.platformExclusions.Count; i++)
            {
                var ex = _toolData.platformExclusions[i];
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                // Display the path/extension (read-only)
                string labelPrefix = ex.exclusionType switch
                {
                    ExclusionType.Asset     => "[Asset] ",
                    ExclusionType.Folder    => "[Folder] ",
                    ExclusionType.Extension => "[Ext] ",
                    _                       => ""
                };

                string pathOrExt = ex.exclusionType switch
                {
                    ExclusionType.Asset     => ex.assetPath,
                    ExclusionType.Folder    => ex.folderPath,
                    ExclusionType.Extension => ex.fileExtension,
                    _                       => ""
                };

                EditorGUILayout.LabelField(labelPrefix + pathOrExt);

                // Editable Platforms dropdown (EnumFlagsField)
                TargetDevice newPlatforms = (TargetDevice)EditorGUILayout.EnumFlagsField(
                    "Platforms:", ex.excludedPlatforms);
                if (newPlatforms != ex.excludedPlatforms)
                {
                    ex.excludedPlatforms = newPlatforms;
                    EditorUtility.SetDirty(_toolData);
                }

                // Editable Environments dropdown (EnumFlagsField)
                EnvironmentType newEnvs = (EnvironmentType)EditorGUILayout.EnumFlagsField(
                    "Environments:", ex.excludedEnvironments);
                if (newEnvs != ex.excludedEnvironments)
                {
                    ex.excludedEnvironments = newEnvs;
                    EditorUtility.SetDirty(_toolData);
                }

                // Remove button
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Remove", GUILayout.Width(60)))
                {
                    _toolData.platformExclusions.RemoveAt(i);
                    EditorUtility.SetDirty(_toolData);
                    i--;
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                    continue;
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.EndScrollView();
        }
    }
}
