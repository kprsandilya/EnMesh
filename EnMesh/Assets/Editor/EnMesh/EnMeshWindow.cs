using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EnMesh;
using EnMesh.Editor.AutoLayout;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Rendering;

namespace EnMesh.Editor
{
    public sealed class EnMeshWindow : EditorWindow
    {
        // ── Persisted preferences ──────────────────────────────────────
        private const string PrefServerUrl = "EnMesh_ServerUrl";
        private const string PrefResolution = "EnMesh_Resolution";
        private const string PrefRemoveBg = "EnMesh_RemoveBg";
        private const string PrefMeshScale = "EnMesh_MeshScale";
        private const string PrefAlignView = "EnMesh_AlignView";
        private const string PrefMaxExtent = "EnMesh_MaxExtentOverride";
        private const string PrefEnvironmentRootGoid = "EnMesh_EnvironmentRootGlobalId";
        private const string PrefLayoutCell = "EnMesh_LayoutCell";
        private const string PrefLayoutWidth = "EnMesh_LayoutWidth";
        private const string PrefLayoutDepth = "EnMesh_LayoutDepth";
        private const string PrefLayoutMinSep = "EnMesh_LayoutMinSep";
        private const string PrefLayoutUsePhysics = "EnMesh_LayoutUsePhysics";
        private const string PrefLayoutMask = "EnMesh_LayoutMask";
        private const string PrefLayoutSeed = "EnMesh_LayoutSeed";
        private const string PrefFinalizeMeshBase = "EnMesh_FinalizeMeshBase";
        private const string PrefFinalizePrefabBase = "EnMesh_FinalizePrefabBase";
        private const float ContentSidePadding = 14f;

        // ── UI state ───────────────────────────────────────────────────
        private string _serverUrl;
        private Texture2D _projectTexture;
        private string _externalPath = "";
        private int _resolution;
        private bool _removeBg;
        private float _meshScale = 1f;
        private bool _alignViewToImage = true;
        /// <summary>Longest AABB edge in meters after other steps; &lt; 0 = do not send (server default).</summary>
        private float _maxExtentMetersOverride = -1f;
        private bool _generating;
        private string _status = "";
        private float _progress;
        private Vector2 _scroll;

        [SerializeField] private GameObject _environmentRoot;

        private float _layoutCellSize = 1f;
        private float _layoutAreaWidth = 10f;
        private float _layoutAreaDepth = 10f;
        private float _layoutMinSeparation = 0.2f;
        private bool _layoutUsePhysics;
        private LayerMask _layoutPhysicsMask = ~0;
        private int _layoutSeed;

        private string _finalizeMeshBaseName = EnMeshEnvironmentTools.DefaultCombinedMeshBaseName;
        private string _finalizePrefabBaseName = EnMeshEnvironmentTools.DefaultPrefabBaseName;

        private CancellationTokenSource _cts;

        private GUIStyle _styleTitleCenter;
        private GUIStyle _styleSubtitleCenter;
        private GUIStyle _styleSectionHeading;
        private GUIStyle _styleCard;
        private GUIStyle _stylePrimaryButton;
        private GUIStyle _styleSecondaryButton;

        [MenuItem("EnMesh/Generate 3D Mesh %#m")]
        public static void Open()
        {
            var w = GetWindow<EnMeshWindow>("EnMesh");
            w.minSize = new Vector2(420, 520);
        }

        // ── Lifecycle ──────────────────────────────────────────────────
        private void OnEnable()
        {
            _serverUrl = EditorPrefs.GetString(PrefServerUrl, "http://localhost:8000");
            _resolution = EditorPrefs.GetInt(PrefResolution, 256);
            _removeBg = EditorPrefs.GetBool(PrefRemoveBg, true);
            _meshScale = EditorPrefs.GetFloat(PrefMeshScale, 1f);
            _alignViewToImage = EditorPrefs.GetBool(PrefAlignView, true);
            _maxExtentMetersOverride = EditorPrefs.GetFloat(PrefMaxExtent, -1f);
            _layoutCellSize = EditorPrefs.GetFloat(PrefLayoutCell, 1f);
            _layoutAreaWidth = EditorPrefs.GetFloat(PrefLayoutWidth, 10f);
            _layoutAreaDepth = EditorPrefs.GetFloat(PrefLayoutDepth, 10f);
            _layoutMinSeparation = EditorPrefs.GetFloat(PrefLayoutMinSep, 0.2f);
            _layoutUsePhysics = EditorPrefs.GetBool(PrefLayoutUsePhysics, false);
            _layoutPhysicsMask = EditorPrefs.GetInt(PrefLayoutMask, ~0);
            _layoutSeed = EditorPrefs.GetInt(PrefLayoutSeed, 0);
            _finalizeMeshBaseName = EditorPrefs.GetString(
                PrefFinalizeMeshBase,
                EnMeshEnvironmentTools.DefaultCombinedMeshBaseName);
            _finalizePrefabBaseName = EditorPrefs.GetString(
                PrefFinalizePrefabBase,
                EnMeshEnvironmentTools.DefaultPrefabBaseName);
            RestoreEnvironmentRootFromPrefs();
        }

        private void OnDisable()
        {
            EditorPrefs.SetString(PrefServerUrl, _serverUrl);
            EditorPrefs.SetInt(PrefResolution, _resolution);
            EditorPrefs.SetBool(PrefRemoveBg, _removeBg);
            EditorPrefs.SetFloat(PrefMeshScale, _meshScale);
            EditorPrefs.SetBool(PrefAlignView, _alignViewToImage);
            EditorPrefs.SetFloat(PrefMaxExtent, _maxExtentMetersOverride);
            EditorPrefs.SetFloat(PrefLayoutCell, _layoutCellSize);
            EditorPrefs.SetFloat(PrefLayoutWidth, _layoutAreaWidth);
            EditorPrefs.SetFloat(PrefLayoutDepth, _layoutAreaDepth);
            EditorPrefs.SetFloat(PrefLayoutMinSep, _layoutMinSeparation);
            EditorPrefs.SetBool(PrefLayoutUsePhysics, _layoutUsePhysics);
            EditorPrefs.SetInt(PrefLayoutMask, _layoutPhysicsMask);
            EditorPrefs.SetInt(PrefLayoutSeed, _layoutSeed);
            EditorPrefs.SetString(PrefFinalizeMeshBase, _finalizeMeshBaseName ?? "");
            EditorPrefs.SetString(PrefFinalizePrefabBase, _finalizePrefabBaseName ?? "");
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
            EnsureGuiStyles();

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(ContentSidePadding);
            EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));

            DrawHeader();
            DrawServer();
            DrawImageInput();
            DrawSettings();
            DrawEnvironment();
            DrawActions();
            DrawStatus();

            EditorGUILayout.EndVertical();
            GUILayout.Space(ContentSidePadding);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndScrollView();
        }

        private void EnsureGuiStyles()
        {
            if (_styleTitleCenter != null)
                return;

            _styleTitleCenter = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 20,
                alignment = TextAnchor.MiddleCenter,
                margin = new RectOffset(0, 0, 8, 2)
            };

            Color subtitle = EditorGUIUtility.isProSkin
                ? new Color(0.62f, 0.62f, 0.65f)
                : new Color(0.38f, 0.38f, 0.4f);
            _styleSubtitleCenter = new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = subtitle },
                margin = new RectOffset(0, 0, 0, 10)
            };

            _styleSectionHeading = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 12,
                margin = new RectOffset(0, 0, 0, 8)
            };

            _styleCard = new GUIStyle(EditorStyles.helpBox)
            {
                padding = new RectOffset(12, 12, 12, 12),
                margin = new RectOffset(0, 0, 0, 10)
            };

            _stylePrimaryButton = new GUIStyle(GUI.skin.button)
            {
                fontStyle = FontStyle.Bold,
                fontSize = 12,
                fixedHeight = 36,
                margin = new RectOffset(0, 0, 4, 4)
            };

            _styleSecondaryButton = new GUIStyle(GUI.skin.button)
            {
                fixedHeight = 22,
                fontSize = 11
            };
        }

        private void DrawHeader()
        {
            GUILayout.Label("EnMesh", _styleTitleCenter);
            GUILayout.Label("Image to 3D mesh", _styleSubtitleCenter);
            Separator();
        }

        private void DrawServer()
        {
            EditorGUILayout.BeginVertical(_styleCard);
            GUILayout.Label("Server", _styleSectionHeading);

            EditorGUILayout.BeginHorizontal();
            _serverUrl = EditorGUILayout.TextField("URL", _serverUrl);
            if (GUILayout.Button("Test", _styleSecondaryButton, GUILayout.Width(64)))
                TestConnection();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private void DrawImageInput()
        {
            EditorGUILayout.BeginVertical(_styleCard);
            GUILayout.Label("Input image", _styleSectionHeading);

            _projectTexture = (Texture2D)EditorGUILayout.ObjectField(
                "Project texture", _projectTexture, typeof(Texture2D), false);

            EditorGUILayout.BeginHorizontal();
            _externalPath = EditorGUILayout.TextField("External file", _externalPath);
            if (GUILayout.Button("Browse…", _styleSecondaryButton, GUILayout.Width(72)))
            {
                string picked = EditorUtility.OpenFilePanel(
                    "Select Image", "", "png,jpg,jpeg,bmp,tga,tiff");
                if (!string.IsNullOrEmpty(picked))
                    _externalPath = picked;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.HelpBox(
                "Project texture: an asset already in this project. "
                + "External file: any image on disk. If both are set, the external file is used.",
                MessageType.None);

            if (_projectTexture != null)
            {
                EditorGUILayout.Space(6);
                float inner = Mathf.Max(
                    EditorGUIUtility.currentViewWidth - ContentSidePadding * 2f - 48f,
                    80f);
                float w = inner;
                float h = Mathf.Min(w * 0.55f, 200f);
                Rect r = GUILayoutUtility.GetRect(w, h);
                EditorGUI.DrawRect(r, EditorGUIUtility.isProSkin
                    ? new Color(0.12f, 0.12f, 0.12f, 1f)
                    : new Color(0.94f, 0.94f, 0.94f, 1f));
                r = new Rect(r.x + 2, r.y + 2, r.width - 4, r.height - 4);
                GUI.DrawTexture(r, _projectTexture, ScaleMode.ScaleToFit);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawSettings()
        {
            EditorGUILayout.BeginVertical(_styleCard);
            GUILayout.Label("Generation settings", _styleSectionHeading);

            _resolution = EditorGUILayout.IntSlider("Mesh resolution", _resolution, 64, 512);
            _removeBg = EditorGUILayout.Toggle("Remove background", _removeBg);
            _meshScale = EditorGUILayout.FloatField(
                new GUIContent("Mesh scale", "Uniform multiplier after server centering / max-extent step."),
                _meshScale);
            if (_meshScale <= 0f)
                _meshScale = 1f;
            _alignViewToImage = EditorGUILayout.Toggle(
                new GUIContent(
                    "Align mesh to image",
                    "TripoSR Gradio-style rotation so the model faces the camera like the input image."),
                _alignViewToImage);
            _maxExtentMetersOverride = EditorGUILayout.FloatField(
                new GUIContent(
                    "Max AABB edge (m)",
                    "Longest bounding-box edge in meters. Negative = use server env default; 0 = no uniform fit."),
                _maxExtentMetersOverride);

            EditorGUILayout.EndVertical();
        }

        private void DrawEnvironment()
        {
            EditorGUILayout.BeginVertical(_styleCard);
            GUILayout.Label("Environment", _styleSectionHeading);

            EditorGUI.BeginChangeCheck();
            _environmentRoot = (GameObject)EditorGUILayout.ObjectField(
                "Environment root", _environmentRoot, typeof(GameObject), true);
            if (EditorGUI.EndChangeCheck())
                PersistEnvironmentRootToPrefs();

            if (_environmentRoot == null)
            {
                EditorGUILayout.HelpBox(
                    "Assign an environment root. Generated meshes are parented under it, "
                    + "or use Create below.",
                    MessageType.Warning);
            }
            else if (_environmentRoot.transform.childCount == 0)
            {
                EditorGUILayout.HelpBox(
                    "This root has no children yet. Generate meshes or place objects under it "
                    + "before using Finalize Environment.",
                    MessageType.Warning);
            }

            if (GUILayout.Button("Create new environment root", _styleSecondaryButton))
                CreateNewEnvironmentRoot();

            DrawAutoLayout();

            EditorGUILayout.Space(4);
            GUILayout.Label("Finalize output", _styleSectionHeading);
            _finalizeMeshBaseName = EditorGUILayout.TextField(
                new GUIContent(
                    "Combined mesh name",
                    "File base name only (no .asset). Saved under Assets/Generated."),
                _finalizeMeshBaseName ?? "");
            _finalizePrefabBaseName = EditorGUILayout.TextField(
                new GUIContent(
                    "Prefab name",
                    "File base name only (no .prefab). Saved under Assets/Generated."),
                _finalizePrefabBaseName ?? "");

            EditorGUI.BeginDisabledGroup(_environmentRoot == null
                                         || _environmentRoot.transform.childCount == 0);
            if (GUILayout.Button("Finalize environment", GUILayout.Height(30)))
                FinalizeEnvironment();
            EditorGUI.EndDisabledGroup();

            string meshOut = $"{EnMeshEnvironmentTools.GeneratedAssetsFolder}/" +
                             $"{EnMeshEnvironmentTools.SanitizeAssetBaseName(_finalizeMeshBaseName, EnMeshEnvironmentTools.DefaultCombinedMeshBaseName)}.asset";
            string prefabOut = $"{EnMeshEnvironmentTools.GeneratedAssetsFolder}/" +
                               $"{EnMeshEnvironmentTools.SanitizeAssetBaseName(_finalizePrefabBaseName, EnMeshEnvironmentTools.DefaultPrefabBaseName)}.prefab";
            EditorGUILayout.HelpBox(
                "Finalize duplicates the root in memory, merges MeshFilters into one mesh, "
                + $"then saves:\n• {meshOut}\n• {prefabOut}\n"
                + "Your scene hierarchy is not modified.",
                MessageType.None);

            EditorGUILayout.EndVertical();
        }

        private void DrawActions()
        {
            EditorGUILayout.BeginVertical(_styleCard);
            GUILayout.Label("Generate", _styleSectionHeading);

            bool hasInput = _projectTexture != null
                            || !string.IsNullOrEmpty(_externalPath);
            bool canGenerate = hasInput && _environmentRoot != null;

            if (_environmentRoot == null && hasInput)
                EditorGUILayout.HelpBox(
                    "Assign an environment root before generating — the mesh will be parented under it.",
                    MessageType.Warning);

            EditorGUI.BeginDisabledGroup(_generating || !canGenerate);
            if (GUILayout.Button(_generating ? "Generating…" : "Generate 3D mesh", _stylePrimaryButton))
                StartGeneration();
            EditorGUI.EndDisabledGroup();

            if (_generating)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Cancel", _styleSecondaryButton, GUILayout.Width(88)))
                    CancelGeneration();
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawStatus()
        {
            if (string.IsNullOrEmpty(_status)) return;

            EditorGUILayout.Space(4);
            MessageType mt = _status.StartsWith("Error", StringComparison.OrdinalIgnoreCase)
                ? MessageType.Error
                : _status.StartsWith("Success", StringComparison.OrdinalIgnoreCase)
                    ? MessageType.Info
                    : MessageType.None;
            EditorGUILayout.HelpBox(_status, mt);

            if (_generating)
            {
                EditorGUILayout.Space(4);
                Rect bar = GUILayoutUtility.GetRect(0, 20);
                EditorGUI.ProgressBar(bar, _progress, $"{_progress * 100:F0} %");
            }
        }

        private static void Separator()
        {
            EditorGUILayout.Space(4);
            Rect r = EditorGUILayout.GetControlRect(false, 1);
            EditorGUI.DrawRect(r, EditorGUIUtility.isProSkin
                ? new Color(1f, 1f, 1f, 0.08f)
                : new Color(0f, 0f, 0f, 0.12f));
            EditorGUILayout.Space(10);
        }

        // ── Environment ────────────────────────────────────────────────
        private void DrawAutoLayout()
        {
            EditorGUILayout.Space(8);
            GUILayout.Label("Auto layout", _styleSectionHeading);

            _layoutCellSize = EditorGUILayout.FloatField(
                "Grid cell size",
                Mathf.Max(0.05f, _layoutCellSize));
            _layoutAreaWidth = EditorGUILayout.FloatField(
                "Area width (local X)",
                Mathf.Max(_layoutCellSize, _layoutAreaWidth));
            _layoutAreaDepth = EditorGUILayout.FloatField(
                "Area depth (local Z)",
                Mathf.Max(_layoutCellSize, _layoutAreaDepth));
            _layoutMinSeparation = EditorGUILayout.FloatField(
                "Min separation",
                Mathf.Max(0f, _layoutMinSeparation));
            _layoutUsePhysics = EditorGUILayout.Toggle(
                new GUIContent(
                    "Physics overlap check",
                    "Uses Physics.CheckSphere for scene colliders (static props, ground, etc.)."),
                _layoutUsePhysics);

            using (new EditorGUI.DisabledScope(!_layoutUsePhysics))
                DrawPhysicsLayersMaskField();

            _layoutSeed = EditorGUILayout.IntField(
                new GUIContent("Random seed", "0 = pick a new seed each run (layouts vary)."),
                _layoutSeed);

            int placeableCount = _environmentRoot != null
                ? _environmentRoot.GetComponentsInChildren<PlaceableItem>(true).Length
                : 0;
            int meshWithoutPlaceable = _environmentRoot != null
                ? CountMeshObjectsMissingPlaceableItem(_environmentRoot.transform)
                : 0;

            if (_environmentRoot != null && placeableCount == 0)
            {
                string extra = meshWithoutPlaceable > 0
                    ? $" {meshWithoutPlaceable} child object(s) have a mesh but no PlaceableItem — use the button below."
                    : " Files in EnMesh/Generated are only assets until you drag instances into the scene under this root, "
                      + "or use Generate 3D Mesh (which adds PlaceableItem automatically).";
                EditorGUILayout.HelpBox(
                    "Auto layout only considers scene objects under the environment root that have a "
                    + "PlaceableItem (role: Anchor, Support, or Fill). Optional category is for custom phases."
                    + extra,
                    MessageType.Warning);
            }

            EditorGUI.BeginDisabledGroup(_environmentRoot == null || meshWithoutPlaceable == 0);
            if (GUILayout.Button(
                    meshWithoutPlaceable > 0
                        ? $"Add PlaceableItem to {meshWithoutPlaceable} mesh object(s)"
                        : "Add PlaceableItem to mesh objects",
                    _styleSecondaryButton))
                AddPlaceableItemToMeshObjectsUnderRoot();
            EditorGUI.EndDisabledGroup();

            EditorGUI.BeginDisabledGroup(_environmentRoot == null || placeableCount == 0);
            if (GUILayout.Button("Run auto layout", GUILayout.Height(30)))
                RunAutoLayout();
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.HelpBox(
                "Calls the server (/auto-layout) to assign Anchor / Support / Fill from mesh names "
                + "(layout source from Generate, otherwise the GameObject name), then runs anchors → supports → fill.",
                MessageType.None);
        }

        /// <summary>
        /// LayerMaskField is not available on all Unity versions; MaskField + layer names is the usual fallback.
        /// </summary>
        private void DrawPhysicsLayersMaskField()
        {
            _layoutPhysicsMask = EditorGUILayout.MaskField(
                new GUIContent("Physics layers"),
                (int)_layoutPhysicsMask,
                InternalEditorUtility.layers);
        }

        private void RunAutoLayout()
        {
            RunAutoLayoutAsync();
        }

        private async void RunAutoLayoutAsync()
        {
            if (_environmentRoot == null)
            {
                _status = "Error: assign Environment Root first.";
                Repaint();
                return;
            }

            PlaceableItem[] items = _environmentRoot.GetComponentsInChildren<PlaceableItem>(true);
            if (items.Length == 0)
            {
                _status = "Error: no PlaceableItem components under the environment root.";
                Repaint();
                return;
            }

            int n = items.Length;
            var undoTargets = new UnityEngine.Object[n * 2];
            for (int i = 0; i < n; i++)
            {
                undoTargets[i] = items[i];
                undoTargets[i + n] = items[i].transform;
            }

            Undo.RecordObjects(undoTargets, "EnMesh Auto Layout");

            var names = new string[n];
            for (int i = 0; i < n; i++)
            {
                string src = items[i].LayoutSourceMeshName;
                names[i] = string.IsNullOrEmpty(src) ? items[i].gameObject.name : src;
            }

            CancellationToken ct = _cts != null ? _cts.Token : CancellationToken.None;
            try
            {
                _status = "Auto Layout: classifying meshes on server …";
                Repaint();
                var layoutRes = await EnMeshClient.AutoLayoutAsync(_serverUrl, names, ct);
                for (int i = 0; i < n; i++)
                    items[i].Role = ParsePlacementRole(layoutRes.results[i].category);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[EnMesh] Auto-layout classification skipped: {ex.Message}");
                _status = $"Warning: classification failed ({ex.Message}) — using existing roles.";
                Repaint();
            }

            var settings = new LayoutSettings(
                _layoutCellSize,
                _layoutAreaWidth,
                _layoutAreaDepth,
                _layoutMinSeparation,
                _layoutUsePhysics,
                _layoutPhysicsMask,
                0.05f,
                _layoutSeed == 0 ? (int?)null : _layoutSeed);

            LayoutResult result = EnvironmentAutoLayout.Run(_environmentRoot.transform, settings);
            if (result.Ok)
            {
                _status = $"Success: {result.Message}";
                Debug.Log($"[EnMesh] {result.Message}");
            }
            else
            {
                _status = $"Error: {result.Message}";
                Debug.LogWarning($"[EnMesh] {result.Message}");
            }

            Repaint();
        }

        private static PlacementRole ParsePlacementRole(string category)
        {
            if (string.Equals(category, "Anchor", StringComparison.OrdinalIgnoreCase))
                return PlacementRole.Anchor;
            if (string.Equals(category, "Support", StringComparison.OrdinalIgnoreCase))
                return PlacementRole.Support;
            return PlacementRole.Fill;
        }

        private static int CountMeshObjectsMissingPlaceableItem(Transform environmentRoot)
        {
            int n = 0;
            foreach (MeshFilter mf in environmentRoot.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                if (mf.GetComponent<MeshRenderer>() == null) continue;
                if (mf.GetComponent<PlaceableItem>() != null) continue;
                n++;
            }

            return n;
        }

        private void AddPlaceableItemToMeshObjectsUnderRoot()
        {
            if (_environmentRoot == null) return;

            int added = 0;
            Undo.IncrementCurrentGroup();
            foreach (MeshFilter mf in _environmentRoot.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                if (mf.GetComponent<MeshRenderer>() == null) continue;
                if (mf.GetComponent<PlaceableItem>() != null) continue;
                Undo.AddComponent<PlaceableItem>(mf.gameObject);
                added++;
            }

            Undo.SetCurrentGroupName("EnMesh Add PlaceableItem");

            if (added > 0)
                Debug.Log($"[EnMesh] Added PlaceableItem to {added} object(s) under '{_environmentRoot.name}'.");

            _status = added > 0
                ? $"Added PlaceableItem to {added} mesh object(s). Set Role (Anchor / Support / Fill) in the Inspector."
                : "No mesh objects needed PlaceableItem (already have it, or missing MeshRenderer / mesh).";
            Repaint();
        }

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

            bool ok = EnMeshEnvironmentTools.TryFinalizeEnvironment(
                _environmentRoot,
                _finalizeMeshBaseName,
                _finalizePrefabBaseName,
                out string meshPath,
                out string prefabPath,
                out string err);
            if (ok)
            {
                _status = $"Success: saved\n{meshPath}\n{prefabPath}";
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
                float? maxExtent = _maxExtentMetersOverride < 0f
                    ? (float?)null
                    : _maxExtentMetersOverride;
                (byte[] meshBytes, string meshName, string assetStem) = await EnMeshClient.GenerateMeshAsync(
                    _serverUrl, data, filename,
                    _resolution, _removeBg, "obj", _cts.Token,
                    maxExtent, _meshScale, _alignViewToImage);

                SetStatus("Saving mesh to project …", 0.85f);
                string assetPath = WriteMeshAsset(meshBytes, assetStem, meshName);
                assetPath = assetPath.Replace("\\", "/");
                // Refresh alone is async; OBJ sub-meshes may not exist until import finishes.
                AssetDatabase.ImportAsset(
                    assetPath,
                    ImportAssetOptions.ForceSynchronousImport);

                var imported = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
                if (imported != null)
                {
                    EditorGUIUtility.PingObject(imported);
                    Selection.activeObject = imported;
                }

                SetStatus("Spawning mesh under Environment Root …", 0.92f);
                SpawnGeneratedMeshUnderEnvironmentRoot(assetPath, meshName);

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
        private void SpawnGeneratedMeshUnderEnvironmentRoot(
            string assetPath,
            string layoutSourceMeshName,
            bool allowDelayedRetry = true)
        {
            if (_environmentRoot == null)
            {
                Debug.LogWarning("[EnMesh] Spawn skipped: Environment Root is null.");
                return;
            }

            assetPath = assetPath.Replace("\\", "/");
            AssetDatabase.ImportAsset(
                assetPath,
                ImportAssetOptions.ForceSynchronousImport);

            Mesh mesh = LoadFirstMeshAtAssetPath(assetPath);
            if (mesh == null)
            {
                if (allowDelayedRetry)
                {
                    Debug.Log(
                        "[EnMesh] Mesh not imported yet — retrying spawn on the next editor tick.");
                    EditorApplication.delayCall += () =>
                        SpawnGeneratedMeshUnderEnvironmentRoot(
                            assetPath,
                            layoutSourceMeshName,
                            allowDelayedRetry: false);
                    return;
                }

                Debug.LogWarning(
                    $"[EnMesh] No Mesh sub-asset at '{assetPath}'. " +
                    "Check the .obj in the Project window and Model Import settings.");
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
            var placeable = Undo.AddComponent<PlaceableItem>(go);
            placeable.LayoutSourceMeshName = layoutSourceMeshName ?? "";

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
            // URP/HDRP shaders do not render correctly under the Built-in pipeline (invisible / pink).
            Shader shader;
            if (GraphicsSettings.defaultRenderPipeline != null)
            {
                shader = Shader.Find("Universal Render Pipeline/Lit")
                         ?? Shader.Find("Universal Render Pipeline/Simple Lit")
                         ?? Shader.Find("HDRP/Lit");
            }
            else
            {
                shader = Shader.Find("Standard")
                         ?? Shader.Find("Legacy Shaders/Diffuse");
            }

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

        /// <summary>
        /// Writes under Assets/EnMesh/Generated using <paramref name="assetStem"/> (from upload name),
        /// not the server's unique mesh id. Adds _2, _3, … on collision. Falls back to server id if needed.
        /// </summary>
        private static string WriteMeshAsset(byte[] data, string assetStem, string serverMeshName)
        {
            const string dir = "Assets/EnMesh/Generated";
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            string preferred = EnMeshEnvironmentTools.SanitizeAssetBaseName(
                assetStem ?? "",
                "");
            if (string.IsNullOrWhiteSpace(preferred))
            {
                preferred = EnMeshEnvironmentTools.SanitizeAssetBaseName(
                    serverMeshName,
                    "generated_mesh");
            }

            string baseName = EnsureUniqueGeneratedObjBaseName(dir, preferred, serverMeshName);
            string path = Path.Combine(dir, baseName + ".obj");
            File.WriteAllBytes(path, data);
            return path.Replace("\\", "/");
        }

        private static string EnsureUniqueGeneratedObjBaseName(
            string assetDirUnity,
            string preferredBase,
            string serverMeshFallback)
        {
            string sub = assetDirUnity.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)
                ? assetDirUnity.Substring("Assets/".Length)
                : assetDirUnity;
            string folderFull = Path.Combine(
                Application.dataPath,
                sub.Replace('/', Path.DirectorySeparatorChar));

            for (int n = 0; n < 1000; n++)
            {
                string candidate = n == 0
                    ? preferredBase
                    : $"{preferredBase}_{n}";
                string full = Path.Combine(folderFull, candidate + ".obj");
                if (!File.Exists(full))
                    return candidate;
            }

            return EnMeshEnvironmentTools.SanitizeAssetBaseName(serverMeshFallback, "generated_mesh");
        }
    }
}
