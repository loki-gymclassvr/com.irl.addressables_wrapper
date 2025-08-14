using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets;
using UnityEngine.UIElements;

namespace Addressables_Wrapper.Editor
{

    public class AddressablesDependencyGraph : EditorWindow
    {
        private ScrollView scrollView;
        private Dictionary<string, Foldout> parentFoldouts = new Dictionary<string, Foldout>();
        private AddressableAssetSettings settings;
        private HashSet<string> dependencyEntries = new HashSet<string>();

        [MenuItem("Window/Addressables Dependency Graph")]
        public static void ShowWindow()
        {
            var window = GetWindow<AddressablesDependencyGraph>();
            window.titleContent = new GUIContent("Addressables Graph");
            window.Show();
        }

        private void CreateGUI()
        {
            settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                Debug.LogError("Addressables settings not found.");
                return;
            }

            scrollView = new ScrollView();
            rootVisualElement.Add(scrollView);

            Button createGroupButton = new Button(CreateFolderStrcture) { text = "Create Folder Structure" };
            rootVisualElement.Add(createGroupButton);

            GenerateGraph();
        }

        private void CreateFolderStrcture()
        {
            string basePath = "Assets/AddressableContent";
            if (!Directory.Exists(basePath))
            {
                Directory.CreateDirectory(basePath);
            }

            AssetDatabase.Refresh(); // Register new folders before moving assets

            foreach (var parent in parentFoldouts.Keys)
            {
                string folderPath = Path.Combine(basePath, Path.GetFileNameWithoutExtension(parent)).Replace("\\", "/");
                if (!Directory.Exists(folderPath))
                {
                    Directory.CreateDirectory(folderPath);
                    AssetDatabase.Refresh(); // Register folder in AssetDatabase
                }

                if (AssetDatabase.IsValidFolder(folderPath))
                {
                    //MoveAssetToFolder(parent, folderPath);
                    // if (dependencyEntries.Contains(parent))
                    // {
                    //     foreach (var dependency in dependencyEntries)
                    //     {
                    //         MoveAssetToFolder(dependency, folderPath);
                    //     }
                    // }
                }
                else
                {
                    Debug.LogError($"Folder {folderPath} is not recognized in AssetDatabase.");
                }
            }

            AssetDatabase.Refresh();
            Debug.Log("Folder structure created and assets moved successfully.");
        }

        private void MoveAssetToFolder(string assetPath, string targetFolder)
        {
            if (string.IsNullOrEmpty(assetPath) || string.IsNullOrEmpty(targetFolder))
            {
                Debug.LogError("Invalid asset path or target folder.");
                return;
            }

            if (!AssetDatabase.IsValidFolder(targetFolder))
            {
                Debug.LogError($"Target folder {targetFolder} is not recognized in the AssetDatabase.");
                return;
            }

            string fileName = Path.GetFileName(assetPath);
            string targetPath = Path.Combine(targetFolder, fileName).Replace("\\", "/");

            string error = AssetDatabase.MoveAsset(assetPath, targetPath);
            if (!string.IsNullOrEmpty(error))
            {
                Debug.LogError($"Failed to move {assetPath} to {targetPath}: {error}");
            }
            else
            {
                Debug.Log($"Successfully moved {assetPath} to {targetPath}");
            }
        }

        private void GenerateGraph()
        {
            HashSet<string> allAddressables = new HashSet<string>(settings.groups
                .Where(group => group.Name != "Built In Data")
                .SelectMany(group => group.entries.Select(entry => entry.AssetPath)));

            Dictionary<string, List<string>> dependencyMap = new Dictionary<string, List<string>>();
            List<string> parentEntries = new List<string>();

            foreach (var entry in settings.groups.SelectMany(group => group.entries))
            {
                if (entry.parentGroup.Name == "Built In Data") continue;
                var dependencies = AssetDatabase.GetDependencies(entry.AssetPath, true)
                    .Where(dep => allAddressables.Contains(dep))
                    .ToList();

                dependencyMap[entry.AssetPath] = dependencies;
                foreach (var dependency in dependencies)
                {
                    dependencyEntries.Add(dependency);
                }
            }

            foreach (var entry in settings.groups.SelectMany(group => group.entries))
            {
                if (entry.parentGroup.Name == "Built In Data") continue;
                parentEntries.Add(entry.AssetPath);
            }

            foreach (var parent in parentEntries)
            {
                CreateFoldout(parent, allAddressables, dependencyMap);
            }
        }

        private void CreateFoldout(string assetPath, HashSet<string> allAddressables,
            Dictionary<string, List<string>> dependencyMap)
        {
            if (parentFoldouts.ContainsKey(assetPath)) return;

            Foldout foldout = new Foldout { text = System.IO.Path.GetFileName(assetPath) };
            Button parentButton = new Button(() => PingAsset(assetPath))
                { text = System.IO.Path.GetFileName(assetPath) + " | " + assetPath };
            foldout.Add(parentButton);

            if (dependencyMap.TryGetValue(assetPath, out var dependencies) && dependencies.Count > 0)
            {
                foreach (var dependency in dependencies)
                {
                    if (parentFoldouts.ContainsKey(dependency))
                    {
                        var oldParent = parentFoldouts[dependency];
                        scrollView.Remove(oldParent);
                        parentFoldouts.Remove(dependency);
                    }

                    string assetName = System.IO.Path.GetFileName(dependency);
                    Button dependencyButton = new Button(() => PingAsset(dependency))
                    {
                        text = assetName + " | " + dependency
                    };
                    foldout.Add(dependencyButton);
                }
            }

            scrollView.Add(foldout);
            parentFoldouts[assetPath] = foldout;
        }

        private void PingAsset(string assetPath)
        {
            var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
            if (obj != null)
            {
                EditorGUIUtility.PingObject(obj);
                Selection.activeObject = obj;
            }
        }
    }
}