using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace EnMesh.Editor
{
    /// <summary>JSON body from POST /generate (snake_case keys for Python FastAPI).</summary>
    [Serializable]
    public sealed class GenerateMeshResponseDto
    {
        public string mesh_path;
        public string category;
        public string download_path;
    }

    /// <summary>Mesh bytes plus metadata returned from the EnMesh backend.</summary>
    public sealed class GeneratedMeshResult
    {
        public byte[] MeshBytes;
        public string Category;
        public string ServerMeshPath;
    }

    /// <summary>
    /// Thin HTTP wrapper that talks to the EnMesh FastAPI backend.
    /// All public methods are async and safe to call from the main thread.
    /// </summary>
    public static class EnMeshClient
    {
        private static readonly HttpClient Http = new()
        {
            Timeout = TimeSpan.FromMinutes(10)
        };

        /// <summary>
        /// POST /generate (JSON), then GET the mesh from <c>download_path</c> under the same server.
        /// </summary>
        public static async Task<GeneratedMeshResult> GenerateMeshAsync(
            string serverUrl,
            byte[] imageData,
            string filename,
            int resolution = 256,
            bool removeBackground = true,
            string format = "obj",
            CancellationToken ct = default)
        {
            string baseUrl = serverUrl.TrimEnd('/');

            using var form = new MultipartFormDataContent();
            var imageContent = new ByteArrayContent(imageData);
            imageContent.Headers.ContentType = new MediaTypeHeaderValue(
                GuessMediaType(filename));
            form.Add(imageContent, "image", filename);

            string postUrl =
                $"{baseUrl}/generate" +
                $"?format={Uri.EscapeDataString(format)}" +
                $"&resolution={resolution}" +
                $"&remove_bg={removeBackground.ToString().ToLower()}";

            using var postResponse = await Http.PostAsync(postUrl, form, ct);

            if (!postResponse.IsSuccessStatusCode)
            {
                string body = await postResponse.Content.ReadAsStringAsync();
                throw new HttpRequestException(
                    $"Server returned {(int)postResponse.StatusCode}: {body}");
            }

            string json = await postResponse.Content.ReadAsStringAsync();
            var dto = JsonUtility.FromJson<GenerateMeshResponseDto>(json);
            if (dto == null || string.IsNullOrEmpty(dto.download_path))
            {
                throw new HttpRequestException(
                    "Invalid /generate JSON: missing download_path. Body: " + json);
            }

            string fileUrl = baseUrl + dto.download_path;
            using var fileResponse = await Http.GetAsync(fileUrl, ct);
            if (!fileResponse.IsSuccessStatusCode)
            {
                string err = await fileResponse.Content.ReadAsStringAsync();
                throw new HttpRequestException(
                    $"GET mesh failed {(int)fileResponse.StatusCode}: {err}");
            }

            byte[] meshBytes = await fileResponse.Content.ReadAsByteArrayAsync();
            return new GeneratedMeshResult
            {
                MeshBytes = meshBytes,
                Category = dto.category ?? "",
                ServerMeshPath = dto.mesh_path ?? ""
            };
        }

        public static async Task<bool> HealthCheckAsync(
            string serverUrl, CancellationToken ct = default)
        {
            try
            {
                using var response = await Http.GetAsync(
                    $"{serverUrl.TrimEnd('/')}/health", ct);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[EnMesh] Health-check failed: {ex.Message}");
                return false;
            }
        }

        private static string GuessMediaType(string filename)
        {
            string ext = Path.GetExtension(filename)?.ToLowerInvariant();
            return ext switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".bmp" => "image/bmp",
                ".tga" => "image/tga",
                ".tiff" or ".tif" => "image/tiff",
                _ => "image/png",
            };
        }
    }
}
