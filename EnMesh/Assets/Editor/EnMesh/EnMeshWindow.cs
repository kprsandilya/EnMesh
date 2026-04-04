using System;
using System.IO;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace EnMesh.Editor
{
    public sealed class EnMeshWindow : EditorWindow
    {
        // ── Persisted preferences ──────────────────────────────────────
        private const string PrefServerUrl = "EnMesh_ServerUrl";
        private const string PrefResolution = "EnMesh_Resolution";
        private const string PrefRemoveBg = "EnMesh_RemoveBg";
        private const string PrefEnvironmentRootGoid = "EnMesh_EnvironmentRootGlobalId";

        // ── UI state ───────────────────────────────────────────────────
        private string _serverUrl;
        private Texture2D _projectTexture;
        private string _externalPath = "";
        private int _resolution;
        private bool _removeBg;
        private bool _generating;
        private string _status = "";
        private float _progress;
        private Vector2 _scroll;

        [SerializeField] private GameObject _environmentRoot;

        private CancellationTokenSource _cts;

        [MenuItem("EnMesh/Generate 3D Mesh %#m")]
        public static void Open()
        {
            var w = GetWindow<EnMeshWindow>("EnMesh");
            w.minSize = new Vector2(380, 720);
        }

        // ── Lifecycle ──────────────────────────────────────────────────
        private void OnEnable()
        {
            _serverUrl = EditorPrefs.GetString(PrefServerUrl, "http://localhost:8000");
            _resolution = EditorPrefs.GetInt(PrefResolution, 256);
            _removeBg = EditorPrefs.GetBool(PrefRemoveBg, true);
            RestoreEnvironmentRootFromPrefs();
        }

        private void OnDisable()
        {
            EditorPrefs.SetString(PrefServerUrl, _serverUrl);
            EditorPrefs.SetInt(PrefResolution, _resolution);
            EditorPrefs.SetBool(PrefRemoveBg, _removeBg);
            PersistEnvironmentRootToPrefs();
            CancelGeneration();
        }

        private void RestoreEnvironmentRootFromPrefs()
        {
            string stored = EditorPrefs.GetString(PrefEnvironmentRootGoid, "");
            if (string.IsNullOrEmpty(stored)) return;
            if (!GlobalObjectId.TryParse(stored, out GlobalObjectId gid)) return;
            EntityId entityId = GlobalObjectId.GlobalObjectIdentifierToEntityIdSlow(gid);
            if (!entityId.IsValid()) return;
            UnityEngine.Object obj = EditorUtility.EntityIdToObject(entityId);
            if (obj is GameObject go && go != null)
                _environmentRoot = go;
        }

        private void PersistEnvironmentRootToPrefs()
        {
            if (_environmentRoot == null)
            {
                EditorPrefs.DeleteKey(PrefEnvironmentRootGoid);
                return;
            }

            GlobalObjectId gid = GlobalObjectId.GetGlobalObjectIdSlow(_environmentRoot);
            EditorPrefs.SetString(PrefEnvironmentRootGoid, gid.ToString());
        }

        // ── Drawing ────────────────────────────────────────────────────
        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            DrawHeader();
            DrawServer();
            DrawImageInput();
            DrawSettings();
            DrawEnvironment();
            DrawActions();
            DrawStatus();

            EditorGUILayout.EndScrollView();
        }

        private void DrawHeader()
        {
            EditorGUILayout.Space(10);
            GUILayout.Label("EnMesh",
                new GUIStyle(EditorStyles.boldLabel)
                    { fontSize = 20, alignment = TextAnchor.MiddleCenter });
            GUILayout.Label("Image  →  3D Mesh",
                new GUIStyle(EditorStyles.centeredGreyMiniLabel) { fontSize = 12 });
            EditorGUILayout.Space(4);
            Separator();
        }

        private void DrawServer()
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Server", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            _serverUrl = EditorGUILayout.TextField("URL", _serverUrl);
            if (GUILayout.Button("Test", GUILayout.Width(50)))
                TestConnection();
            EditorGUILayout.EndHorizontal();

            Separator();
        }

        private void DrawImageInput()
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Input Image", EditorStyles.boldLabel);

            _projectTexture = (Texture2D)EditorGUILayout.ObjectField(
                "Project Texture", _projectTexture, typeof(Texture2D), false);

            EditorGUILayout.BeginHorizontal();
            _externalPath = EditorGUILayout.TextField("External File", _externalPath);
            if (GUILayout.Button("…", GUILayout.Width(28)))
            {
                string picked = EditorUtility.OpenFilePanel(
                    "Select Image", "", "png,jpg,jpeg,bmp,tga,tiff");
                if (!string.IsNullOrEmpty(picked))
                    _externalPath = picked;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.HelpBox(
                "Project Texture: an image already in this Unity project (Assets). "
                + "External File: any image on disk (Desktop, Downloads, etc.). "
                + "If both are set, External File is used.",
                MessageType.None);

            if (_projectTexture != null)
            {
                EditorGUILayout.Space(4);
                float w = Mathf.Max(EditorGUIUtility.currentViewWidth - 40, 100);
                float h = Mathf.Min(w, 200);
                Rect r = GUILayoutUtility.GetRect(w, h);
                r.x += 10;
                r.width -= 20;
                GUI.DrawTexture(r, _projectTexture, ScaleMode.ScaleToFit);
            }

            Separator();
        }

        private void DrawSettings()
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Settings", EditorStyles.boldLabel);

            _resolution = EditorGUILayout.IntSlider("Mesh Resolution", _resolution, 64, 512);
            _removeBg = EditorGUILayout.Toggle("Remove Background", _removeBg);

            Separator();
        }

        private void DrawEnvironment()
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Environment", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            _environmentRoot = (GameObject)EditorGUILayout.ObjectField(
                "Environment Root", _environmentRoot, typeof(GameObject), true);
            if (EditorGUI.EndChangeCheck())
                PersistEnvironmentRootToPrefs();

            if (_environmentRoot == null)
            {
                EditorGUILayout.HelpBox(
                    "Assign an Environment Root. Generated meshes are parented under it. "
                    + "Use the button below to create one.",
                    MessageType.Warning);
            }
            else if (_environmentRoot.transform.childCount == 0)
            {
                EditorGUILayout.HelpBox(
                    "Environment Root has no children yet. Generate meshes or place objects "
                    + "under it before using Finalize Environment.",
                    MessageType.Warning);
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Create New Environment Root", GUILayout.Height(24)))
                CreateNewEnvironmentRoot();
            EditorGUILayout.EndHorizontal();

            EditorGUI.BeginDisabledGroup(_environmentRoot == null
                                         || _environmentRoot.transform.childCount == 0);
            if (GUILayout.Button("Finalize Environment", GUILayout.Height(32)))
                FinalizeEnvironment();
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.HelpBox(
                "Finalize duplicates the root in memory only, merges all MeshFilters into one mesh, "
                + $"saves {EnMeshEnvironmentTools.CombinedMeshAssetPath} and "
                + $"{EnMeshEnvironmentTools.FinalPrefabPath}, then discards the duplicate. "
                + "Your original hierarchy is not modified.",
                MessageType.None);

            Separator();
        }

        private void DrawActions()
        {
            EditorGUILayout.Space(8);

            bool hasInput = _projectTexture != null
                            || !string.IsNullOrEmpty(_externalPath);
            bool canGenerate = hasInput && _environmentRoot != null;

            if (_environmentRoot == null && hasInput)
                EditorGUILayout.HelpBox(
                    "Assign Environment Root before generating — the mesh will be parented under it.",
                    MessageType.Warning);

            EditorGUI.BeginDisabledGroup(_generating || !canGenerate);
            if (GUILayout.Button(_generating ? "Generating …" : "Generate 3D Mesh",
                    GUILayout.Height(38)))
                StartGeneration();
            EditorGUI.EndDisabledGroup();

            if (_generating && GUILayout.Button("Cancel"))
                CancelGeneration();
        }

        private void DrawStatus()
        {
            if (string.IsNullOrEmpty(_status)) return;

            EditorGUILayout.Space(8);
            MessageType mt = _status.StartsWith("Error", StringComparison.OrdinalIgnoreCase)
                ? MessageType.Error
                : _status.StartsWith("Success", StringComparison.OrdinalIgnoreCase)
                    ? MessageType.Info
                    : MessageType.None;
            EditorGUILayout.HelpBox(_status, mt);

            if (_generating)
            {
                Rect bar = GUILayoutUtility.GetRect(0, 18);
                EditorGUI.ProgressBar(bar, _progress, $"{_progress * 100:F0} %");
            }
        }

        private static void Separator()
        {
            EditorGUILayout.Space(2);
            Rect r = EditorGUILayout.GetControlRect(false, 1);
            EditorGUI.DrawRect(r, new Color(0.5f, 0.5f, 0.5f, 0.25f));
        }

        // ── Environment ────────────────────────────────────────────────
        private void CreateNewEnvironmentRoot()
        {
            Debug.Log("[EnMesh] Creating new EnvironmentRoot GameObject …");
            var go = new GameObject("EnvironmentRoot");
            Undo.RegisterCreatedObjectUndo(go, "Create Environment Root");
            if (Selection.activeGameObject != null)
                Undo.SetTransformParent(
                    go.transform,
                    Selection.activeGameObject.transform,
                    "Create Environment Root");
            _environmentRoot = go;
            Selection.activeGameObject = go;
            PersistEnvironmentRootToPrefs();
            EditorGUIUtility.PingObject(go);
            Debug.Log("[EnMesh] EnvironmentRoot created and assigned.");
            Repaint();
        }

        private void FinalizeEnvironment()
        {
            if (_environmentRoot == null)
            {
                Debug.LogWarning("[EnMesh] Finalize skipped: Environment Root is null.");
                return;
            }

            if (_environmentRoot.transform.childCount == 0)
            {
                Debug.LogWarning("[EnMesh] Finalize skipped: Environment Root has no children.");
                return;
            }

            Debug.Log($"[EnMesh] Finalize Environment: processing root '{_environmentRoot.name}' …");

            bool ok = EnMeshEnvironmentTools.TryFinalizeEnvironment(_environmentRoot, out string err);
            if (ok)
            {
                _status = $"Success: combined mesh and prefab saved under {EnMeshEnvironmentTools.GeneratedAssetsFolder}.";
                Debug.Log("[EnMesh] Finalize Environment completed successfully.");
            }
            else
            {
                _status = $"Error: {err}";
                Debug.LogError($"[EnMesh] Finalize Environment failed: {err}");
            }

            Repaint();
        }

        // ── Actions ────────────────────────────────────────────────────
        private async void TestConnection()
        {
            _status = "Testing connection …";
            Repaint();

            bool ok = await EnMeshClient.HealthCheckAsync(_serverUrl);
            _status = ok
                ? "Success: server is reachable."
                : "Error: could not reach server — check URL and that the backend is running.";
            Repaint();
        }

        private async void StartGeneration()
        {
            if (_environmentRoot == null)
            {
                _status = "Error: assign Environment Root first.";
                Debug.LogWarning("[EnMesh] Generate aborted: no Environment Root.");
                return;
            }

            _generating = true;
            _cts = new CancellationTokenSource();
            _progress = 0f;

            try
            {
                SetStatus("Reading image …", 0.05f);
                (byte[] data, string filename) = ReadImage();
                if (data == null || data.Length == 0)
                {
                    _status = "Error: could not read the selected image.";
                    return;
                }

                SetStatus("Uploading & generating mesh (this can take a minute) …", 0.2f);
                byte[] meshBytes = await EnMeshClient.GenerateMeshAsync(
                    _serverUrl, data, filename,
                    _resolution, _removeBg, "obj", _cts.Token);

                SetStatus("Saving mesh to project …", 0.85f);
                string assetPath = WriteMeshAsset(meshBytes);
                AssetDatabase.Refresh();

                var imported = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
                if (imported != null)
                {
                    EditorGUIUtility.PingObject(imported);
                    Selection.activeObject = imported;
                }

                SetStatus("Spawning mesh under Environment Root …", 0.92f);
                SpawnGeneratedMeshUnderEnvironmentRoot(assetPath);

                _status = $"Success: mesh saved to {assetPath} and parented under '{_environmentRoot.name}'.";
                _progress = 1f;
            }
            catch (OperationCanceledException)
            {
                _status = "Generation cancelled.";
            }
            catch (Exception ex)
            {
                _status = $"Error: {ex.Message}";
                Debug.LogException(ex);
            }
            finally
            {
                _generating = false;
                _cts?.Dispose();
                _cts = null;
                Repaint();
            }
        }

        private void CancelGeneration()
        {
            if (_cts == null) return;
            _cts.Cancel();
            _generating = false;
            _status = "Cancelled.";
            Repaint();
        }

        private void SetStatus(string msg, float pct)
        {
            _status = msg;
            _progress = pct;
            Repaint();
        }

        // ── Helpers ────────────────────────────────────────────────────
        private void SpawnGeneratedMeshUnderEnvironmentRoot(string assetPath)
        {
            if (_environmentRoot == null)
            {
                Debug.LogWarning("[EnMesh] Spawn skipped: Environment Root is null.");
                return;
            }

            Mesh mesh = LoadFirstMeshAtAssetPath(assetPath);
            if (mesh == null)
            {
                Debug.LogWarning($"[EnMesh] No Mesh sub-asset found at '{assetPath}' — import may still be processing.");
                return;
            }

            Undo.IncrementCurrentGroup();
            string baseName = Path.GetFileNameWithoutExtension(assetPath);
            var go = new GameObject(string.IsNullOrEmpty(baseName) ? "GeneratedMesh" : baseName);
            Undo.RegisterCreatedObjectUndo(go, "EnMesh Spawn Generated Mesh");
            Undo.RecordObject(go.transform, "EnMesh Parent Mesh");
            go.transform.SetParent(_environmentRoot.transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            var mf = Undo.AddComponent<MeshFilter>(go);
            mf.sharedMesh = mesh;
            var mr = Undo.AddComponent<MeshRenderer>(go);
            mr.sharedMaterial = CreateSpawnMaterial();

            Undo.SetCurrentGroupName("EnMesh Spawn Generated Mesh");
            Selection.activeGameObject = go;
            EditorGUIUtility.PingObject(go);

            Debug.Log($"[EnMesh] Spawned '{go.name}' under '{_environmentRoot.name}'.");
        }

        private static Mesh LoadFirstMeshAtAssetPath(string assetPath)
        {
            foreach (UnityEngine.Object o in AssetDatabase.LoadAllAssetsAtPath(assetPath))
            {
                if (o is Mesh m)
                    return m;
            }

            return null;
        }

        private static Material CreateSpawnMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit")
                            ?? Shader.Find("Universal Render Pipeline/Simple Lit")
                            ?? Shader.Find("Standard");
            if (shader == null)
                shader = Shader.Find("Hidden/InternalErrorShader");
            return new Material(shader);
        }

        private (byte[] data, string filename) ReadImage()
        {
            if (!string.IsNullOrEmpty(_externalPath) && File.Exists(_externalPath))
                return (File.ReadAllBytes(_externalPath), Path.GetFileName(_externalPath));

            if (_projectTexture != null)
            {
                string asset = AssetDatabase.GetAssetPath(_projectTexture);
                if (!string.IsNullOrEmpty(asset))
                {
                    string root = Path.GetDirectoryName(Application.dataPath)!;
                    string full = Path.GetFullPath(Path.Combine(root, asset));
                    if (File.Exists(full))
                        return (File.ReadAllBytes(full), Path.GetFileName(full));
                }

                try { return (_projectTexture.EncodeToPNG(), "input.png"); }
                catch
                {
                    _status = "Error: texture is not readable. " +
                              "Enable Read/Write in the texture import settings.";
                    return (null, null);
                }
            }

            return (null, null);
        }

        private static string WriteMeshAsset(byte[] data)
        {
            const string dir = "Assets/EnMesh/Generated";
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            string ts = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string name = $"mesh_{ts}.obj";
            string path = Path.Combine(dir, name);
            File.WriteAllBytes(path, data);
            return path;
        }
    }
}
