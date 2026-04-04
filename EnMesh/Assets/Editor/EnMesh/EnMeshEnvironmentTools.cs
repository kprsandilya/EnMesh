using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace EnMesh.Editor
{
    /// <summary>
    /// Combines meshes under an environment root (working on a duplicate only) and writes
    /// <see cref="CombinedMeshAssetPath"/> + <see cref="FinalPrefabPath"/>.
    /// </summary>
    public static class EnMeshEnvironmentTools
    {
        public const string GeneratedAssetsFolder = "Assets/Generated";
        public const string CombinedMeshAssetPath = "Assets/Generated/combined.asset";
        public const string FinalPrefabPath = "Assets/Generated/final.prefab";

        /// <summary>
        /// Ensures <see cref="GeneratedAssetsFolder"/> exists and is registered in the AssetDatabase.
        /// Prefer <see cref="AssetDatabase.CreateFolder"/> so Unity reliably accepts CreateAsset/SaveAsPrefab paths.
        /// </summary>
        public static bool EnsureGeneratedFolderExists(out string errorMessage)
        {
            errorMessage = null;

            if (AssetDatabase.IsValidFolder(GeneratedAssetsFolder))
                return true;

            string guid = AssetDatabase.CreateFolder("Assets", "Generated");
            if (!string.IsNullOrEmpty(guid))
            {
                Debug.Log($"[EnMesh] Created folder {GeneratedAssetsFolder} (AssetDatabase).");
                AssetDatabase.Refresh();
                return true;
            }

            // Fallback: folder on disk + import (e.g. unusual project layout)
            string full = Path.Combine(
                Path.GetDirectoryName(Application.dataPath)!,
                "Generated");
            try
            {
                if (!Directory.Exists(full))
                    Directory.CreateDirectory(full);
                AssetDatabase.Refresh();
                AssetDatabase.ImportAsset(GeneratedAssetsFolder, ImportAssetOptions.ForceUpdate);
            }
            catch (Exception ex)
            {
                errorMessage =
                    $"Could not create or import '{GeneratedAssetsFolder}': {ex.Message}";
                return false;
            }

            if (!AssetDatabase.IsValidFolder(GeneratedAssetsFolder))
            {
                errorMessage =
                    $"Unity does not recognize '{GeneratedAssetsFolder}' as a folder. "
                    + "Create Assets/Generated in the Project window, then try again.";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Duplicates <paramref name="environmentRoot"/>, combines all <see cref="MeshFilter"/>
        /// meshes in the duplicate, saves assets, then destroys the duplicate. Does not modify
        /// the original hierarchy.
        /// </summary>
        public static bool TryFinalizeEnvironment(GameObject environmentRoot, out string errorMessage)
        {
            errorMessage = null;

            if (environmentRoot == null)
            {
                errorMessage = "Environment Root is not assigned.";
                return false;
            }

            if (environmentRoot.transform.childCount == 0)
            {
                errorMessage = "Environment Root has no children — add meshes under it before finalizing.";
                return false;
            }

            if (!EnsureGeneratedFolderExists(out string folderError))
            {
                errorMessage = folderError;
                return false;
            }

            GameObject duplicate = null;
            Mesh combinedMesh = null;
            Material sharedMat = null;

            try
            {
                Debug.Log($"[EnMesh] Finalize: duplicating '{environmentRoot.name}' for safe processing …");
                duplicate = UnityEngine.Object.Instantiate(
                    environmentRoot.gameObject,
                    environmentRoot.transform.parent);
                duplicate.name = environmentRoot.name + "_TempCombine";
                duplicate.transform.SetLocalPositionAndRotation(
                    environmentRoot.transform.localPosition,
                    environmentRoot.transform.localRotation);
                duplicate.transform.localScale = environmentRoot.transform.localScale;
                duplicate.hideFlags = HideFlags.HideAndDontSave;
                duplicate.SetActive(false);

                if (!TryBuildCombinedMesh(duplicate, out combinedMesh, out sharedMat, out string buildError))
                {
                    errorMessage = buildError;
                    return false;
                }

                if (combinedMesh.vertexCount == 0)
                {
                    errorMessage = "Combined mesh has no vertices — nothing to save.";
                    return false;
                }

                Debug.Log("[EnMesh] Finalize: writing combined mesh asset …");
                if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(CombinedMeshAssetPath) != null)
                {
                    AssetDatabase.DeleteAsset(CombinedMeshAssetPath);
                    AssetDatabase.SaveAssets();
                    AssetDatabase.Refresh();
                }

                combinedMesh.name = "CombinedEnvironment";

                try
                {
                    AssetDatabase.CreateAsset(combinedMesh, CombinedMeshAssetPath);
                }
                catch (Exception ex)
                {
                    errorMessage =
                        $"CreateAsset failed for '{CombinedMeshAssetPath}': {ex.Message}. "
                        + "Ensure Assets/Generated exists as a project folder and check the Console.";
                    UnityEngine.Object.DestroyImmediate(combinedMesh);
                    combinedMesh = null;
                    return false;
                }

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                Mesh persistedMesh =
                    AssetDatabase.LoadAssetAtPath<Mesh>(CombinedMeshAssetPath);
                if (persistedMesh == null)
                {
                    errorMessage =
                        $"Failed to load mesh at '{CombinedMeshAssetPath}' after CreateAsset. "
                        + "See earlier Unity Console messages for the exact import/create error.";
                    return false;
                }

                if (sharedMat == null)
                    sharedMat = CreateFallbackLitMaterial();

                Debug.Log("[EnMesh] Finalize: creating FinalEnvironment and saving prefab …");
                GameObject finalGo = new GameObject("FinalEnvironment");

                if (!TryMoveToActiveScene(finalGo, out string sceneError))
                {
                    errorMessage = sceneError;
                    UnityEngine.Object.DestroyImmediate(finalGo);
                    return false;
                }

                var mf = finalGo.AddComponent<MeshFilter>();
                mf.sharedMesh = persistedMesh;
                var mr = finalGo.AddComponent<MeshRenderer>();
                mr.sharedMaterial = ResolveMaterialForPrefab(sharedMat);

                if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(FinalPrefabPath) != null)
                {
                    AssetDatabase.DeleteAsset(FinalPrefabPath);
                    AssetDatabase.SaveAssets();
                    AssetDatabase.Refresh();
                }

                GameObject prefabRoot = PrefabUtility.SaveAsPrefabAsset(finalGo, FinalPrefabPath);
                UnityEngine.Object.DestroyImmediate(finalGo);

                if (prefabRoot == null)
                {
                    errorMessage =
                        "SaveAsPrefabAsset returned null. Check the Console. "
                        + "Common causes: no valid active scene, or invalid renderer/material setup.";
                    return false;
                }

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                Debug.Log($"[EnMesh] Finalize: saved mesh → {CombinedMeshAssetPath}");
                Debug.Log($"[EnMesh] Finalize: saved prefab → {FinalPrefabPath}");

                Selection.activeObject = prefabRoot;
                EditorGUIUtility.PingObject(prefabRoot);
                return true;
            }
            finally
            {
                if (duplicate != null)
                {
                    Debug.Log("[EnMesh] Finalize: destroying temporary duplicate …");
                    UnityEngine.Object.DestroyImmediate(duplicate);
                }
            }
        }

        private static bool TryBuildCombinedMesh(
            GameObject root,
            out Mesh combinedMesh,
            out Material firstSharedMaterial,
            out string error)
        {
            combinedMesh = null;
            firstSharedMaterial = null;
            error = null;

            var filters = root.GetComponentsInChildren<MeshFilter>(true);
            var combines = new List<CombineInstance>();
            foreach (MeshFilter mf in filters)
            {
                if (mf.sharedMesh == null)
                    continue;

                combines.Add(new CombineInstance
                {
                    mesh = mf.sharedMesh,
                    transform = mf.transform.localToWorldMatrix
                });

                if (firstSharedMaterial == null)
                {
                    var mr = mf.GetComponent<MeshRenderer>();
                    if (mr != null && mr.sharedMaterial != null)
                        firstSharedMaterial = mr.sharedMaterial;
                }
            }

            if (combines.Count == 0)
            {
                error = "No MeshFilter with a non-null sharedMesh found under the environment.";
                return false;
            }

            Debug.Log($"[EnMesh] Finalize: combining {combines.Count} mesh instance(s) …");

            combinedMesh = new Mesh();
            combinedMesh.indexFormat = IndexFormat.UInt32;
            try
            {
                combinedMesh.CombineMeshes(combines.ToArray(), mergeSubMeshes: true, useMatrices: true);
            }
            catch (Exception ex)
            {
                UnityEngine.Object.DestroyImmediate(combinedMesh);
                combinedMesh = null;
                error =
                    "Mesh.CombineMeshes failed (meshes may not be readable). "
                    + "Enable Read/Write on model import settings where needed. "
                    + $"Details: {ex.Message}";
                return false;
            }

            combinedMesh.RecalculateBounds();
            combinedMesh.RecalculateNormals();

            Debug.Log(
                $"[EnMesh] Finalize: combined mesh has {combinedMesh.vertexCount} vertices, " +
                $"{combinedMesh.triangles.Length / 3} triangles.");
            return true;
        }

        private static Material CreateFallbackLitMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit")
                            ?? Shader.Find("Universal Render Pipeline/Simple Lit")
                            ?? Shader.Find("Standard")
                            ?? Shader.Find("Diffuse");
            if (shader == null)
                shader = Shader.Find("Hidden/InternalErrorShader");
            return new Material(shader);
        }

        /// <summary>
        /// SaveAsPrefabAsset requires the instance to live in a loaded scene; editor-created GOs are
        /// often scene-less until moved.
        /// </summary>
        private static bool TryMoveToActiveScene(GameObject go, out string errorMessage)
        {
            errorMessage = null;
            Scene active = EditorSceneManager.GetActiveScene();
            if (!active.IsValid() || !active.isLoaded)
            {
                errorMessage =
                    "No loaded active scene. Open or create a scene in the Editor, then run Finalize again.";
                return false;
            }

            EditorSceneManager.MoveGameObjectToScene(go, active);
            return true;
        }

        /// <summary>
        /// Runtime materials (no asset path) can make SaveAsPrefabAsset fail; prefer project/builtin assets.
        /// </summary>
        private static Material ResolveMaterialForPrefab(Material preferred)
        {
            if (preferred != null && !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(preferred)))
                return preferred;

            Material builtin = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Material.mat");
            if (builtin != null)
                return builtin;

            return preferred;
        }
    }
}
