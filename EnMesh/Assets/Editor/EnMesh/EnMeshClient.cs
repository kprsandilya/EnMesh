using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace EnMesh.Editor
{
    /// <summary>HTTP client for the EnMesh FastAPI backend.</summary>
    public static class EnMeshClient
    {
        private static readonly HttpClient Http = new()
        {
            Timeout = TimeSpan.FromMinutes(10)
        };

        [Serializable]
        private class GenerateMeshResponseDto
        {
            public string mesh_path;
            public string mesh_name;
            public string source_stem;
            public string download_url;
        }

        [Serializable]
        private class MeshNameEntryDto
        {
            public string name;
        }

        [Serializable]
        private class AutoLayoutRequestDto
        {
            public MeshNameEntryDto[] meshes;
        }

        /// <summary>One entry from POST /auto-layout (public for API accessibility rules).</summary>
        [Serializable]
        public class AutoLayoutMeshResultDto
        {
            public string mesh_name;
            public string absolute_path;
            public string category;
        }

        [Serializable]
        public class AutoLayoutResponseDto
        {
            public AutoLayoutMeshResultDto[] results;
        }

        public static async Task<(byte[] meshBytes, string meshName, string assetStem)> GenerateMeshAsync(
            string serverUrl,
            byte[] imageData,
            string filename,
            int resolution = 256,
            bool removeBackground = true,
            string format = "obj",
            CancellationToken ct = default)
        {
            using var form = new MultipartFormDataContent();
            var imageContent = new ByteArrayContent(imageData);
            imageContent.Headers.ContentType = new MediaTypeHeaderValue(
                GuessMediaType(filename));
            form.Add(imageContent, "image", filename);

            string url =
                $"{serverUrl.TrimEnd('/')}/generate" +
                $"?format={Uri.EscapeDataString(format)}" +
                $"&resolution={resolution}" +
                $"&remove_bg={removeBackground.ToString().ToLower()}";

            using var post = await Http.PostAsync(url, form, ct);
            string body = await post.Content.ReadAsStringAsync();
            if (!post.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"Server returned {(int)post.StatusCode}: {body}");
            }

            var meta = JsonUtility.FromJson<GenerateMeshResponseDto>(body);
            if (meta == null || string.IsNullOrEmpty(meta.download_url))
            {
                throw new InvalidOperationException(
                    "Invalid /generate JSON (missing download_url).");
            }

            string meshName = string.IsNullOrEmpty(meta.mesh_name)
                ? "mesh"
                : meta.mesh_name;

            string assetStem = string.IsNullOrWhiteSpace(meta.source_stem)
                ? Path.GetFileNameWithoutExtension(filename)
                : meta.source_stem;

            string getUrl = meta.download_url.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? meta.download_url
                : $"{serverUrl.TrimEnd('/')}{meta.download_url}";

            using var get = await Http.GetAsync(getUrl, ct);
            get.EnsureSuccessStatusCode();
            byte[] meshBytes = await get.Content.ReadAsByteArrayAsync();
            return (meshBytes, meshName, assetStem);
        }

        public static async Task<AutoLayoutResponseDto> AutoLayoutAsync(
            string serverUrl,
            string[] meshNames,
            CancellationToken ct = default)
        {
            var req = new AutoLayoutRequestDto
            {
                meshes = new MeshNameEntryDto[meshNames.Length]
            };
            for (int i = 0; i < meshNames.Length; i++)
                req.meshes[i] = new MeshNameEntryDto { name = meshNames[i] };

            string json = JsonUtility.ToJson(req);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            string url = $"{serverUrl.TrimEnd('/')}/auto-layout";
            using var post = await Http.PostAsync(url, content, ct);
            string respBody = await post.Content.ReadAsStringAsync();
            if (!post.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"Server returned {(int)post.StatusCode}: {respBody}");
            }

            var parsed = JsonUtility.FromJson<AutoLayoutResponseDto>(respBody);
            if (parsed?.results == null || parsed.results.Length != meshNames.Length)
            {
                throw new InvalidOperationException(
                    "Invalid /auto-layout JSON or result count mismatch.");
            }

            return parsed;
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
