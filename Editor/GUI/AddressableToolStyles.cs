using UnityEditor;
using UnityEngine;

namespace Addressables_Wrapper.Editor
{
    /// <summary>
    /// Manages UI styles for the Addressable Tool
    /// </summary>
    public class AddressableToolStyles
    {
        // Style variables
        public GUIStyle DropAreaStyle { get; private set; }
        public GUIStyle HeaderStyle { get; private set; }
        public GUIStyle AssetListStyle { get; private set; }
        public GUIStyle SectionHeaderStyle { get; private set; }
        
        private bool _stylesInitialized = false;
        
        /// <summary>
        /// Ensures styles are initialized
        /// </summary>
        public void EnsureStylesInitialized()
        {
            if (_stylesInitialized)
                return;
    
            // Create drop area style
            DropAreaStyle = new GUIStyle(GUI.skin.box);
            DropAreaStyle.normal.background = EditorGUIUtility.whiteTexture;
            DropAreaStyle.alignment = TextAnchor.MiddleCenter;
            DropAreaStyle.fontSize = 14;
            DropAreaStyle.fontStyle = FontStyle.Bold;
    
            // Create header style
            HeaderStyle = new GUIStyle(EditorStyles.boldLabel);
            HeaderStyle.fontSize = 13;
            HeaderStyle.alignment = TextAnchor.MiddleLeft;
            HeaderStyle.normal.textColor = EditorGUIUtility.isProSkin ? new Color(0.9f, 0.9f, 0.9f) : new Color(0.1f, 0.1f, 0.1f);
    
            // Create asset list style
            AssetListStyle = new GUIStyle(EditorStyles.helpBox);
            AssetListStyle.padding = new RectOffset(5, 5, 5, 5);
            AssetListStyle.margin = new RectOffset(5, 5, 5, 5);
            
            // Create section header style
            SectionHeaderStyle = new GUIStyle(EditorStyles.boldLabel);
            SectionHeaderStyle.fontSize = 14;
            SectionHeaderStyle.margin = new RectOffset(0, 0, 10, 5);
            
            _stylesInitialized = true;
        }
    }
}