using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace EnMesh.Editor
{
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

        public static async Task<byte[]> GenerateMeshAsync(
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

            using var response = await Http.PostAsync(url, form, ct);

            if (!response.IsSuccessStatusCode)
            {
                string body = await response.Content.ReadAsStringAsync();
                throw new HttpRequestException(
                    $"Server returned {(int)response.StatusCode}: {body}");
            }

            return await response.Content.ReadAsByteArrayAsync();
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
