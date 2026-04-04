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
        private const string PrefRemoveBg   = "EnMesh_RemoveBg";

        // ── UI state ───────────────────────────────────────────────────
        private string     _serverUrl;
        private Texture2D  _projectTexture;
        private string     _externalPath = "";
        private int        _resolution;
        private bool       _removeBg;
        private bool       _generating;
        private string     _status = "";
        private float      _progress;
        private Vector2    _scroll;

        private CancellationTokenSource _cts;

        [MenuItem("EnMesh/Generate 3D Mesh %#m")]
        public static void Open()
        {
            var w = GetWindow<EnMeshWindow>("EnMesh");
            w.minSize = new Vector2(380, 560);
        }

        // ── Lifecycle ──────────────────────────────────────────────────
        private void OnEnable()
        {
            _serverUrl  = EditorPrefs.GetString(PrefServerUrl, "http://localhost:8000");
            _resolution = EditorPrefs.GetInt(PrefResolution, 256);
            _removeBg   = EditorPrefs.GetBool(PrefRemoveBg, true);
        }

        private void OnDisable()
        {
            EditorPrefs.SetString(PrefServerUrl, _serverUrl);
            EditorPrefs.SetInt(PrefResolution, _resolution);
            EditorPrefs.SetBool(PrefRemoveBg, _removeBg);
            CancelGeneration();
        }

        // ── Drawing ────────────────────────────────────────────────────
        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            DrawHeader();
            DrawServer();
            DrawImageInput();
            DrawSettings();
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

            if (_projectTexture != null)
            {
                EditorGUILayout.Space(4);
                float w = Mathf.Max(EditorGUIUtility.currentViewWidth - 40, 100);
                float h = Mathf.Min(w, 200);
                Rect r  = GUILayoutUtility.GetRect(w, h);
                r.x     += 10;
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
            _removeBg   = EditorGUILayout.Toggle("Remove Background", _removeBg);

            Separator();
        }

        private void DrawActions()
        {
            EditorGUILayout.Space(8);

            bool hasInput = _projectTexture != null
                            || !string.IsNullOrEmpty(_externalPath);

            EditorGUI.BeginDisabledGroup(_generating || !hasInput);
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

                _status = $"Success: mesh saved to {assetPath}";
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
            _status   = msg;
            _progress = pct;
            Repaint();
        }

        // ── Helpers ────────────────────────────────────────────────────
        private (byte[] data, string filename) ReadImage()
        {
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

            if (!string.IsNullOrEmpty(_externalPath) && File.Exists(_externalPath))
                return (File.ReadAllBytes(_externalPath),
                        Path.GetFileName(_externalPath));

            return (null, null);
        }

        private static string WriteMeshAsset(byte[] data)
        {
            const string dir = "Assets/EnMesh/Generated";
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            string ts   = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string name = $"mesh_{ts}.obj";
            string path = Path.Combine(dir, name);
            File.WriteAllBytes(path, data);
            return path;
        }
    }
}
