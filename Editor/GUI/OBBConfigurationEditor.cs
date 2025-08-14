using Addressables_Wrapper.Editor;
using UnityEditor;
using UnityEngine;

namespace AddressableSystem.Editor
{
    /// <summary>
    /// Editor window for configuring OBB settings.
    /// </summary>
    public class OBBConfigurationEditor : EditorWindow
    {
        private OBBPackingConfig config;
        private Vector2 scrollPosition;
        private bool[] foldoutStates; // Array to track foldout states for each OBB definition

        [MenuItem("Tools/Addressables/OBB Configuration Editor")]
        public static void ShowWindow()
        {
            GetWindow<OBBConfigurationEditor>("OBB Configuration");
        }

        private void OnEnable()
        {
            // Try to load existing configuration
            string configPath = "Assets/Editor/AddressableTool/Data/OBBPackingConfig.asset";
            config = AssetDatabase.LoadAssetAtPath<OBBPackingConfig>(configPath);

            if (config == null)
            {
                // Create a new configuration if none exists
                config = ScriptableObject.CreateInstance<OBBPackingConfig>();
                
                // Set default catalog prefix
                config.catalogPrefix = "mainobb";

                // Add default OBB definition
                var defaultObb = new OBBPackingConfig.OBBDefinition
                {
                    obbPrefix = "patch",
                    includeCatalog = true,
                    includeWhenInherit = false
                };
                defaultObb.assetBundlePrefixes.Add("default_group_");
                config.obbDefinitions.Add(defaultObb);

                // Ensure directory exists
                string directory = System.IO.Path.GetDirectoryName(configPath);
                if (!System.IO.Directory.Exists(directory))
                {
                    System.IO.Directory.CreateDirectory(directory);
                }

                // Create the asset
                AssetDatabase.CreateAsset(config, configPath);
                AssetDatabase.SaveAssets();
            }
            
            // Initialize foldout states
            InitializeFoldoutStates();
        }
        
        private void InitializeFoldoutStates()
        {
            if (config != null)
            {
                foldoutStates = new bool[config.obbDefinitions.Count];
                for (int i = 0; i < foldoutStates.Length; i++)
                {
                    foldoutStates[i] = false;
                }
            }
        }

        private void OnGUI()
        {
            if (config == null)
            {
                EditorGUILayout.HelpBox("Configuration asset not found or could not be created.", MessageType.Error);
                return;
            }

            EditorGUI.BeginChangeCheck();

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            EditorGUILayout.LabelField("OBB Packing Configuration", EditorStyles.boldLabel);
            EditorGUILayout.Space();
            
            // Add the global catalog prefix field
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Global Settings", EditorStyles.boldLabel);
            
            EditorGUILayout.HelpBox("The catalog prefix when making incremental (inherit) build with catalog in main OBB.", MessageType.Info);
            
            config.catalogPrefix = EditorGUILayout.TextField(
                new GUIContent("MainOBB Catalog Prefix", 
                "Prefix used for catalog files in main OBB during incremental builds"), 
                config.catalogPrefix);
                
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space();

            // Display each OBB definition
            for (int i = 0; i < config.obbDefinitions.Count; i++)
            {
                // Make sure we have enough foldout states
                if (foldoutStates == null || foldoutStates.Length <= i)
                {
                    InitializeFoldoutStates();
                }
                
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                EditorGUILayout.LabelField($"OBB Definition {i + 1}", EditorStyles.boldLabel);

                var obbDef = config.obbDefinitions[i];

                obbDef.obbPrefix = EditorGUILayout.TextField("OBB Prefix", obbDef.obbPrefix);
                obbDef.includeCatalog = EditorGUILayout.Toggle("Include Catalog", obbDef.includeCatalog);
                obbDef.includeWhenInherit = EditorGUILayout.Toggle(
                    new GUIContent("Include When Inherit", 
                        "If checked, this OBB will be created even during incremental builds"),
                    obbDef.includeWhenInherit);

                // Foldout for asset bundle prefixes
                foldoutStates[i] = EditorGUILayout.Foldout(foldoutStates[i], "Asset Bundle Prefixes", true, EditorStyles.foldoutHeader);
                
                if (foldoutStates[i])
                {
                    EditorGUI.indentLevel++;
                    
                    // Display and edit asset bundle prefixes
                    for (int j = 0; j < obbDef.assetBundlePrefixes.Count; j++)
                    {
                        EditorGUILayout.BeginHorizontal();
                        obbDef.assetBundlePrefixes[j] =
                            EditorGUILayout.TextField($"Prefix {j + 1}", obbDef.assetBundlePrefixes[j]);

                        if (GUILayout.Button("Remove", GUILayout.Width(80)))
                        {
                            obbDef.assetBundlePrefixes.RemoveAt(j);
                            break;
                        }

                        EditorGUILayout.EndHorizontal();
                    }
                    
                    if (GUILayout.Button("Add Bundle Prefix", GUILayout.Width(150)))
                    {
                        obbDef.assetBundlePrefixes.Add("");
                    }
                    
                    EditorGUI.indentLevel--;
                }

                EditorGUILayout.Space();

                if (GUILayout.Button("Remove This OBB Definition"))
                {
                    config.obbDefinitions.RemoveAt(i);
                    // Remove foldout state for this definition
                    var newFoldoutStates = new bool[foldoutStates.Length - 1];
                    for (int j = 0; j < i; j++)
                        newFoldoutStates[j] = foldoutStates[j];
                    for (int j = i; j < newFoldoutStates.Length; j++)
                        newFoldoutStates[j] = foldoutStates[j + 1];
                    foldoutStates = newFoldoutStates;
                    break;
                }

                EditorGUILayout.EndVertical();
                EditorGUILayout.Space();
            }

            if (GUILayout.Button("Add New OBB Definition"))
            {
                var newObb = new OBBPackingConfig.OBBDefinition
                {
                    obbPrefix = "obb_" + (config.obbDefinitions.Count + 1),
                    includeCatalog = false,
                    includeWhenInherit = true
                };
                newObb.assetBundlePrefixes.Add("");
                config.obbDefinitions.Add(newObb);
                
                // Add a new foldout state
                if (foldoutStates != null)
                {
                    var newFoldoutStates = new bool[foldoutStates.Length + 1];
                    for (int i = 0; i < foldoutStates.Length; i++)
                        newFoldoutStates[i] = foldoutStates[i];
                    newFoldoutStates[foldoutStates.Length] = true; // Open the new foldout by default
                    foldoutStates = newFoldoutStates;
                }
                else
                {
                    foldoutStates = new[] { true };
                }
            }

            EditorGUILayout.EndScrollView();

            if (EditorGUI.EndChangeCheck())
            {
                EditorUtility.SetDirty(config);
                AssetDatabase.SaveAssetIfDirty(config);
            }
        }
    }
}