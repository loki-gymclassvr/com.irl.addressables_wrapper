using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AddressableSystem.Editor
{
    [CustomPropertyDrawer(typeof(CustomAddressableReference), true)]
    public class CustomAddressableReferenceDrawer : PropertyDrawer
    {
        // Constants
        private const string k_AssetGUIDField = "_assetGUID";
        private const string k_AssetAddressField = "_assetAddress";
        private const string k_AssetNameField = "_assetName";
        private const string k_AssetTypeField = "_assetType";
        private const string k_EditorAssetPathField = "_editorAssetPath";
        private const string k_SubObjectNameField = "_subObjectName";
        private const string k_HasSubObjectsField = "_hasSubObjects";
        
        // Layout constants
        private const float k_IconSize = 20f;
        private const float k_ButtonWidth = 55f;
        private const float k_PreviewSize = 120f;
        private const float k_InfoHeight = 80f;
        
        // Object cache
        private static Dictionary<string, Object> s_ObjectCache = new Dictionary<string, Object>();
        
        // Preview state - using instanceID to track per instance
        private Dictionary<int, bool> m_ShowingPreviewStates = new Dictionary<int, bool>();
        
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            // Get instance ID for this property
            int instanceID = property.serializedObject.targetObject.GetInstanceID() + property.propertyPath.GetHashCode();
            
            // Check if this property is showing preview
            bool isShowingPreview = false;
            m_ShowingPreviewStates.TryGetValue(instanceID, out isShowingPreview);
            
            // Standard height for the field
            float height = EditorGUIUtility.singleLineHeight;
            
            // If showing preview, add extra height
            if (isShowingPreview)
            {
                // Preview + address/GUID info height + tech details
                height += k_PreviewSize + k_InfoHeight + 10;
            }
            
            return height;
        }
        
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            // Create a clean label without icon
            GUIContent cleanLabel = new GUIContent(label.text, label.tooltip);
            
            // Get instance ID for this property
            int instanceID = property.serializedObject.targetObject.GetInstanceID() + property.propertyPath.GetHashCode();
            
            // Get showing preview state
            bool isShowingPreview = false;
            m_ShowingPreviewStates.TryGetValue(instanceID, out isShowingPreview);
            
            EditorGUI.BeginProperty(position, cleanLabel, property);
            
            try
            {
                var guidProp = property.FindPropertyRelative(k_AssetGUIDField);
                var nameProp = property.FindPropertyRelative(k_AssetNameField);
                var pathProp = property.FindPropertyRelative(k_EditorAssetPathField);
                var addressProp = property.FindPropertyRelative(k_AssetAddressField);
                
                if (guidProp == null || pathProp == null)
                {
                    EditorGUI.LabelField(position, cleanLabel.text, "Error: Missing properties");
                    EditorGUI.EndProperty();
                    return;
                }
                
                // Get the object and its preview
                Object obj = GetObjectFromProperty(guidProp.stringValue, pathProp.stringValue, property);
                Texture2D thumbnailPreview = AssetPreviewManager.GetBestThumbnail(guidProp.stringValue, obj);
                string address = addressProp != null ? addressProp.stringValue : "";
                
                // If address is empty but we have a valid GUID, try to get it
                if (string.IsNullOrEmpty(address) && !string.IsNullOrEmpty(guidProp.stringValue))
                {
                    address = AddressableAssetUtils.GetAddressForAsset(guidProp.stringValue);
                    if (addressProp != null && !string.IsNullOrEmpty(address))
                    {
                        addressProp.stringValue = address;
                    }
                }
                
                // Draw the main object field with thumbnail icon
                Rect iconRect = new Rect(position.x, position.y, k_IconSize, EditorGUIUtility.singleLineHeight);
                Rect objectFieldRect = new Rect(position.x + k_IconSize, position.y, 
                    position.width - k_IconSize - k_ButtonWidth - 5, EditorGUIUtility.singleLineHeight);
                Rect previewToggleRect = new Rect(objectFieldRect.xMax + 5, position.y, k_ButtonWidth, EditorGUIUtility.singleLineHeight);
                
                // Draw icon
                if (thumbnailPreview != null)
                {
                    GUI.DrawTexture(iconRect, thumbnailPreview, ScaleMode.ScaleToFit);
                }
                
                // Handle object field changes
                EditorGUI.BeginChangeCheck();
                var newObj = EditorGUI.ObjectField(objectFieldRect, cleanLabel, obj, GetTypeFromProperty(property), false);
                
                // Preview button
                if (GUI.Button(previewToggleRect, isShowingPreview ? "Hide" : "Preview"))
                {
                    isShowingPreview = !isShowingPreview;
                    m_ShowingPreviewStates[instanceID] = isShowingPreview;
                }
                
                if (EditorGUI.EndChangeCheck() && newObj != obj)
                {
                    // Update the values
                    UpdateReferenceValues(property, newObj);
                }
                
                // Draw preview with additional info
                if (isShowingPreview && obj != null)
                {
                    string guid = guidProp.stringValue;
                    
                    // Draw the preview area
                    Rect previewRect = new Rect(
                        position.x, 
                        position.y + EditorGUIUtility.singleLineHeight + 5,
                        position.width,
                        k_PreviewSize);
                    
                    // Draw the info area
                    Rect infoRect = new Rect(
                        position.x,
                        previewRect.yMax + 5,
                        position.width,
                        k_InfoHeight);
                    
                    AssetPreviewManager.DrawObjectPreview(previewRect, obj, guid);
                    
                    // Get tech details for the asset
                    AssetTechDetails details = AssetDetailsExtractor.GetAssetDetails(guid, obj, pathProp.stringValue);
                    AddressableReferenceUIUtils.DrawAssetInfo(infoRect, obj, guid, address, details);
                }
            }
            catch (Exception ex)
            {
                if (!(ex is ExitGUIException))
                {
                    Debug.LogError($"Error in AddressableReferenceDrawer: {ex.Message}");
                    EditorGUI.LabelField(position, cleanLabel.text, "Error drawing field");
                }
                else
                {
                    throw;
                }
            }
            
            EditorGUI.EndProperty();
        }
        
        /// <summary>
        /// Gets an Object reference from property data
        /// </summary>
        private Object GetObjectFromProperty(string guid, string path, SerializedProperty property)
        {
            if (string.IsNullOrEmpty(guid) || string.IsNullOrEmpty(path))
                return null;
                
            if (!s_ObjectCache.TryGetValue(guid, out Object obj))
            {
                obj = AssetDatabase.LoadAssetAtPath(path, GetTypeFromProperty(property));
                if (obj != null)
                {
                    s_ObjectCache[guid] = obj;
                }
            }
            
            return obj;
        }
        
        /// <summary>
        /// Updates property values when a new asset is assigned
        /// </summary>
        private void UpdateReferenceValues(SerializedProperty property, Object obj)
        {
            var guidProp = property.FindPropertyRelative(k_AssetGUIDField);
            var nameProp = property.FindPropertyRelative(k_AssetNameField);
            var typeProp = property.FindPropertyRelative(k_AssetTypeField);
            var pathProp = property.FindPropertyRelative(k_EditorAssetPathField);
            var subObjectNameProp = property.FindPropertyRelative(k_SubObjectNameField);
            var hasSubObjectsProp = property.FindPropertyRelative(k_HasSubObjectsField);
            var addressProp = property.FindPropertyRelative(k_AssetAddressField);

            string oldGuid = guidProp.stringValue;

            if (obj == null)
            {
                // Clear values
                guidProp.stringValue = string.Empty;
                nameProp.stringValue = string.Empty;
                typeProp.stringValue = string.Empty;
                pathProp.stringValue = string.Empty;
                subObjectNameProp.stringValue = string.Empty;
                hasSubObjectsProp.boolValue = false;
                if (addressProp != null) addressProp.stringValue = string.Empty;

                // Clear cached data
                if (!string.IsNullOrEmpty(oldGuid))
                {
                    s_ObjectCache.Remove(oldGuid);
                    AssetPreviewManager.ClearPreviewsForAsset(oldGuid);
                    AssetDetailsExtractor.ClearAssetDetails(oldGuid);
                }

                property.serializedObject.ApplyModifiedProperties();
                return;
            }

            string path = AssetDatabase.GetAssetPath(obj);

            // Check for sub-asset
            bool isSubAsset = false;
            string subObjectName = string.Empty;

            if (AssetDatabase.IsSubAsset(obj))
            {
                isSubAsset = true;
                subObjectName = obj.name;
                Object mainAsset = AssetDatabase.LoadMainAssetAtPath(path);
                if (mainAsset != null)
                {
                    path = AssetDatabase.GetAssetPath(mainAsset);
                }
            }

            string guid = AssetDatabase.AssetPathToGUID(path);

            // Get address from Addressable settings
            string address = AddressableAssetUtils.GetAddressForAsset(guid);

            // Set all serialized fields including address
            guidProp.stringValue = guid;
            nameProp.stringValue = obj.name;
            typeProp.stringValue = obj.GetType().FullName;
            pathProp.stringValue = path;
            subObjectNameProp.stringValue = subObjectName;
            hasSubObjectsProp.boolValue = isSubAsset;
            if (addressProp != null) addressProp.stringValue = address;

            // Update cache
            s_ObjectCache[guid] = AssetDatabase.LoadMainAssetAtPath(path);

            // Clean up old cached data if GUID changed
            if (!string.IsNullOrEmpty(oldGuid) && oldGuid != guid)
            {
                AssetPreviewManager.ClearPreviewsForAsset(oldGuid);
                AssetDetailsExtractor.ClearAssetDetails(oldGuid);
                s_ObjectCache.Remove(oldGuid);
            }

            // Always make it addressable - no option to skip
            AddressableAssetUtils.MakeAssetAddressable(guid, obj.name);

            property.serializedObject.ApplyModifiedProperties();
        }
        
        /// <summary>
        /// Gets the target type for the asset reference
        /// </summary>
        private Type GetTypeFromProperty(SerializedProperty property)
        {
            try
            {
                Type baseType = fieldInfo.FieldType;
                
                // Handle collections
                if (baseType.IsArray)
                    baseType = baseType.GetElementType();
                else if (baseType.IsGenericType)
                    baseType = baseType.GetGenericArguments()[0];
                
                // Handle specific types
                string typeName = baseType.Name;
                
                if (typeName == "PrefabReference")
                    return typeof(GameObject);
                if (typeName == "SceneReference")
                    return typeof(SceneAsset);
                if (typeName == "MaterialReference")
                    return typeof(Material);
                if (typeName == "TextureReference")
                    return typeof(Texture2D);
                if (typeName == "AudioReference")
                    return typeof(AudioClip);
                if (typeName == "ModelReference")
                    return typeof(UnityEngine.Mesh);
                
                // Default to generic Object type
                return typeof(Object);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error getting type from property: {ex.Message}");
                return typeof(Object);
            }
        }
    }
}