using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using System.Linq;
using PsdTools.UIToolKit;

namespace PsdToUIToolKit.Editor
{
    /// <summary>
    /// Right-click context menu items for the Unity Project window.
    /// "PsdToUIToolKit/Add to Common Dir" — adds the selected folder to the common directories list
    /// used by the PsdToUIToolKit Editor for dedup / save-to-common-dir.
    /// </summary>
    public static class PsdUiToolkitContextMenus
    {
        private const string MenuPath = "Assets/PsdToUIToolKit/Add to Common Dir";
        private const string PrefSuppressAddCommonDirDialog = "PsdUiToolkit_SuppressAddCommonDirDialog";

        [MenuItem(MenuPath, false, 1000)]
        private static void AddToCommonDir()
        {
            string assetPath = AssetDatabase.GetAssetPath(Selection.activeObject);
            if (string.IsNullOrEmpty(assetPath)) return;

            // Normalise separators and strip trailing slash
            string normalizedPath = assetPath.Replace('\\', '/').TrimEnd('/');

            // Load existing list
            var configData = PsdUiToolkitImageExportConfig.LoadCommonDirectories(true);
            var list = configData.paths != null ? configData.paths.ToList() : new List<string>();

            // Reject duplicates (case-insensitive)
            foreach (string existing in list)
            {
                if (string.Equals(existing.Replace('\\', '/').TrimEnd('/'), normalizedPath,
                        System.StringComparison.OrdinalIgnoreCase))
                {
                    EditorUtility.DisplayDialog(
                        "Already in Common Directories",
                        $"\"{normalizedPath}\" is already in the common directories list.",
                        "OK");
                    return;
                }
            }

            // Add, save, and notify
            list.Add(normalizedPath);
            configData.paths = list.ToArray();
            PsdUiToolkitImageExportConfig.SaveCommonDirectories(configData);

            bool suppressed = EditorPrefs.GetBool(PrefSuppressAddCommonDirDialog, false);
            if (!suppressed)
            {
                PsdUiToolkitAddCommonDirSuccessPopup.Show(
                    $"\"{normalizedPath}\" has been added to the common directories list.\n\n" +
                    "Please reopen PsdToUIToolKit Window for the change to take effect if it is open.");
            }
        }

        [MenuItem(MenuPath, true)]
        private static bool ValidateAddToCommonDir()
        {
            if (Selection.activeObject == null) return false;
            string assetPath = AssetDatabase.GetAssetPath(Selection.activeObject);
            return !string.IsNullOrEmpty(assetPath) && AssetDatabase.IsValidFolder(assetPath);
        }
    }

    /// <summary>
    /// Small modal popup shown after a directory is successfully added to the common directories list.
    /// Contains a "Don't show again" toggle that persists via EditorPrefs.
    /// </summary>
    internal sealed class PsdUiToolkitAddCommonDirSuccessPopup : EditorWindow
    {
        private const string PrefSuppressAddCommonDirDialog = "PsdUiToolkit_SuppressAddCommonDirDialog";

        private string _message;
        private bool _dontShowAgain;

        /// <summary>Show the popup modally. Blocks until the user dismisses it.</summary>
        public static void Show(string message)
        {
            var win = CreateInstance<PsdUiToolkitAddCommonDirSuccessPopup>();
            win._message = message;
            win._dontShowAgain = false;
            win.titleContent = new GUIContent("PsdToUIToolKit — Common Dir Updated");
            win.minSize = new Vector2(420, 130);
            win.maxSize = new Vector2(560, 160);
            win.ShowModalUtility();
        }

        private void OnGUI()
        {
            GUILayout.Space(14);
            EditorGUILayout.LabelField(_message, EditorStyles.wordWrappedLabel);
            GUILayout.Space(10);
            _dontShowAgain = EditorGUILayout.ToggleLeft("Don't show this again", _dontShowAgain);
            GUILayout.FlexibleSpace();

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("OK", GUILayout.Width(80)))
            {
                if (_dontShowAgain)
                    EditorPrefs.SetBool(PrefSuppressAddCommonDirDialog, true);
                Close();
            }
            GUILayout.Space(10);
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(10);
        }
    }
}
