using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class SpineExtensionConverter
{
    private const string AssetsMenuPath =
        "Assets/Spine/Convert .atlas and .skel Extensions";

    private const string ToolsMenuPath =
        "Tools/Spine/Convert All Extensions Under Assets";

    private const string EnableStraightAlphaMenuPath =
        "Assets/Spine/Enable Straight Alpha Texture On Materials";

    /// <summary>
    /// Converts files in the selected Project folders:
    /// *.atlas -> *.atlas.txt
    /// *.skel  -> *.skel.bytes
    /// </summary>
    [MenuItem(AssetsMenuPath, false, 2000)]
    private static void ConvertSelectedAssets()
    {
        UnityEngine.Object[] selectedObjects =
            Selection.GetFiltered<UnityEngine.Object>(SelectionMode.Assets);

        if (selectedObjects == null || selectedObjects.Length == 0)
        {
            EditorUtility.DisplayDialog(
                "Spine Extension Converter",
                "Please select one or more folders/files in the Project window.",
                "OK"
            );
            return;
        }

        var selectedPaths = new List<string>();

        foreach (UnityEngine.Object selectedObject in selectedObjects)
        {
            string assetPath = AssetDatabase.GetAssetPath(selectedObject);

            if (!string.IsNullOrEmpty(assetPath))
            {
                selectedPaths.Add(assetPath);
            }
        }

        ConvertAssetPaths(selectedPaths);
    }

    [MenuItem(AssetsMenuPath, true)]
    private static bool ValidateConvertSelectedAssets()
    {
        return Selection.GetFiltered<UnityEngine.Object>(
            SelectionMode.Assets
        ).Length > 0;
    }

    /// <summary>
    /// Converts all matching files recursively under the Assets folder.
    /// </summary>
    [MenuItem(ToolsMenuPath)]
    private static void ConvertAllUnderAssets()
    {
        bool confirmed = EditorUtility.DisplayDialog(
            "Convert Spine File Extensions",
            "Recursively convert all matching files under Assets?\n\n" +
            "*.atlas  -> *.atlas.txt\n" +
            "*.skel   -> *.skel.bytes\n\n" +
            "Existing target files will not be overwritten.",
            "Convert",
            "Cancel"
        );

        if (!confirmed)
        {
            return;
        }

        ConvertAssetPaths(new List<string> { "Assets" });
    }

    /// <summary>
    /// Recursively enables "Straight Alpha Texture" on all materials
    /// under the selected Project folders.
    /// </summary>
    [MenuItem(EnableStraightAlphaMenuPath, false, 2001)]
    private static void EnableStraightAlphaOnSelectedMaterials()
    {
        UnityEngine.Object[] selectedObjects =
            Selection.GetFiltered<UnityEngine.Object>(SelectionMode.Assets);

        if (selectedObjects == null || selectedObjects.Length == 0)
        {
            EditorUtility.DisplayDialog(
                "Spine Material Settings",
                "Please select one or more folders/materials in the Project window.",
                "OK"
            );
            return;
        }

        var selectedPaths = new List<string>();

        foreach (UnityEngine.Object selectedObject in selectedObjects)
        {
            string assetPath = AssetDatabase.GetAssetPath(selectedObject);

            if (!string.IsNullOrEmpty(assetPath))
            {
                selectedPaths.Add(assetPath);
            }
        }

        EnableStraightAlphaOnMaterials(selectedPaths);
    }

    [MenuItem(EnableStraightAlphaMenuPath, true)]
    private static bool ValidateEnableStraightAlphaOnSelectedMaterials()
    {
        return Selection.GetFiltered<UnityEngine.Object>(
            SelectionMode.Assets
        ).Length > 0;
    }

    private static void EnableStraightAlphaOnMaterials(
        IList<string> selectedPaths)
    {
        HashSet<string> materialPaths =
            CollectMaterialAssetPaths(selectedPaths);

        if (materialPaths.Count == 0)
        {
            EditorUtility.DisplayDialog(
                "Spine Material Settings",
                "No material files were found in the selected location.",
                "OK"
            );
            return;
        }

        int changedCount = 0;
        int alreadyEnabledCount = 0;
        int unsupportedCount = 0;

        try
        {
            AssetDatabase.StartAssetEditing();

            foreach (string materialPath in materialPaths)
            {
                Material material =
                    AssetDatabase.LoadAssetAtPath<Material>(materialPath);

                if (material == null)
                {
                    unsupportedCount++;
                    continue;
                }

                // Spine 3.8 "Spine/Skeleton" uses _StraightAlphaInput.
                // Keep fallback names for compatibility with other Spine shaders.
                string propertyName = FindStraightAlphaProperty(material);

                if (string.IsNullOrEmpty(propertyName))
                {
                    Debug.LogWarning(
                        "[Spine Material Settings] Material does not expose " +
                        "'Straight Alpha Texture', skipped:\n" +
                        materialPath,
                        material
                    );

                    unsupportedCount++;
                    continue;
                }

                bool valueAlreadyEnabled =
                    material.GetFloat(propertyName) > 0.5f;

                bool keywordAlreadyEnabled =
                    material.IsKeywordEnabled("_STRAIGHT_ALPHA_INPUT");

                if (valueAlreadyEnabled && keywordAlreadyEnabled)
                {
                    alreadyEnabledCount++;
                    continue;
                }

                Undo.RecordObject(
                    material,
                    "Enable Spine Straight Alpha Texture"
                );

                material.SetFloat(propertyName, 1f);
                material.EnableKeyword("_STRAIGHT_ALPHA_INPUT");

                EditorUtility.SetDirty(material);

                Debug.Log(
                    "[Spine Material Settings] Enabled Straight Alpha Texture:\n" +
                    materialPath,
                    material
                );

                changedCount++;
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        EditorUtility.DisplayDialog(
            "Spine Material Settings",
            "Finished processing materials.\n\n" +
            "Enabled: " + changedCount + "\n" +
            "Already enabled: " + alreadyEnabledCount + "\n" +
            "Unsupported/skipped: " + unsupportedCount,
            "OK"
        );
    }

    private static HashSet<string> CollectMaterialAssetPaths(
        IList<string> selectedPaths)
    {
        var results = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase
        );

        foreach (string assetPath in selectedPaths)
        {
            string absolutePath = AssetPathToAbsolutePath(assetPath);

            if (Directory.Exists(absolutePath))
            {
                string[] materialFiles = Directory.GetFiles(
                    absolutePath,
                    "*.mat",
                    SearchOption.AllDirectories
                );

                foreach (string materialFile in materialFiles)
                {
                    string materialAssetPath =
                        AbsolutePathToAssetPath(materialFile);

                    if (!string.IsNullOrEmpty(materialAssetPath))
                    {
                        results.Add(materialAssetPath);
                    }
                }
            }
            else if (File.Exists(absolutePath) &&
                     Path.GetExtension(absolutePath).Equals(
                         ".mat",
                         StringComparison.OrdinalIgnoreCase))
            {
                results.Add(assetPath);
            }
        }

        return results;
    }

    private static string FindStraightAlphaProperty(Material material)
    {
        string[] propertyNames =
        {
            "_StraightAlphaInput",
            "_StraightAlphaTexture",
            "_StraightAlpha"
        };

        foreach (string propertyName in propertyNames)
        {
            if (material.HasProperty(propertyName))
            {
                return propertyName;
            }
        }

        return null;
    }

    private static void ConvertAssetPaths(IList<string> selectedPaths)
    {
        HashSet<string> candidateFiles = CollectCandidateFiles(selectedPaths);

        if (candidateFiles.Count == 0)
        {
            EditorUtility.DisplayDialog(
                "Spine Extension Converter",
                "No .atlas or .skel files were found.",
                "OK"
            );
            return;
        }

        int convertedCount = 0;
        int skippedCount = 0;
        int failedCount = 0;

        AssetDatabase.StartAssetEditing();

        try
        {
            foreach (string sourceAssetPath in candidateFiles)
            {
                string targetAssetPath = GetTargetAssetPath(sourceAssetPath);

                if (string.IsNullOrEmpty(targetAssetPath))
                {
                    skippedCount++;
                    continue;
                }

                string targetAbsolutePath =
                    AssetPathToAbsolutePath(targetAssetPath);

                if (File.Exists(targetAbsolutePath))
                {
                    Debug.LogWarning(
                        "[Spine Extension Converter] Target already exists, skipped:\n" +
                        targetAssetPath
                    );

                    skippedCount++;
                    continue;
                }

                string error = AssetDatabase.MoveAsset(
                    sourceAssetPath,
                    targetAssetPath
                );

                if (string.IsNullOrEmpty(error))
                {
                    Debug.Log(
                        "[Spine Extension Converter] Converted:\n" +
                        sourceAssetPath + "\n->\n" + targetAssetPath
                    );

                    convertedCount++;
                }
                else
                {
                    Debug.LogError(
                        "[Spine Extension Converter] Failed:\n" +
                        sourceAssetPath + "\nReason: " + error
                    );

                    failedCount++;
                }
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            AssetDatabase.Refresh(
                ImportAssetOptions.ForceSynchronousImport
            );
        }

        EditorUtility.DisplayDialog(
            "Spine Extension Converter",
            "Conversion finished.\n\n" +
            "Converted: " + convertedCount + "\n" +
            "Skipped: " + skippedCount + "\n" +
            "Failed: " + failedCount,
            "OK"
        );
    }

    private static HashSet<string> CollectCandidateFiles(
        IList<string> selectedPaths)
    {
        var results = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase
        );

        foreach (string assetPath in selectedPaths)
        {
            string absolutePath = AssetPathToAbsolutePath(assetPath);

            if (Directory.Exists(absolutePath))
            {
                string[] files = Directory.GetFiles(
                    absolutePath,
                    "*",
                    SearchOption.AllDirectories
                );

                foreach (string file in files)
                {
                    TryAddCandidate(file, results);
                }
            }
            else if (File.Exists(absolutePath))
            {
                TryAddCandidate(absolutePath, results);
            }
        }

        return results;
    }

    private static void TryAddCandidate(
        string absolutePath,
        HashSet<string> results)
    {
        string extension = Path.GetExtension(absolutePath);

        bool isAtlas = extension.Equals(
            ".atlas",
            StringComparison.OrdinalIgnoreCase
        );

        bool isSkel = extension.Equals(
            ".skel",
            StringComparison.OrdinalIgnoreCase
        );

        if (!isAtlas && !isSkel)
        {
            return;
        }

        string assetPath = AbsolutePathToAssetPath(absolutePath);

        if (!string.IsNullOrEmpty(assetPath))
        {
            results.Add(assetPath);
        }
    }

    private static string GetTargetAssetPath(string sourceAssetPath)
    {
        string extension = Path.GetExtension(sourceAssetPath);

        if (extension.Equals(
            ".atlas",
            StringComparison.OrdinalIgnoreCase))
        {
            return sourceAssetPath + ".txt";
        }

        if (extension.Equals(
            ".skel",
            StringComparison.OrdinalIgnoreCase))
        {
            return sourceAssetPath + ".bytes";
        }

        return null;
    }

    private static string AssetPathToAbsolutePath(string assetPath)
    {
        string projectRoot = Path.GetFullPath(
            Path.Combine(Application.dataPath, "..")
        );

        return Path.GetFullPath(
            Path.Combine(projectRoot, assetPath)
        );
    }

    private static string AbsolutePathToAssetPath(string absolutePath)
    {
        string projectRoot = Path.GetFullPath(
            Path.Combine(Application.dataPath, "..")
        );

        string fullPath = Path.GetFullPath(absolutePath);

        if (!fullPath.StartsWith(
            projectRoot,
            StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string relativePath = fullPath.Substring(projectRoot.Length)
            .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Replace('\\', '/');

        if (!relativePath.StartsWith(
            "Assets/",
            StringComparison.OrdinalIgnoreCase) &&
            !relativePath.Equals(
                "Assets",
                StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return relativePath;
    }
}
