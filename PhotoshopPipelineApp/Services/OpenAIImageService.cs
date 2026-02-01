using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace PhotoshopPipelineApp.Services;

public class OpenAIImageService
{
    private static readonly HttpClient HttpClient = new();
    private const string ApiUrl = "https://api.openai.com/v1/images/generations";

    /// <summary>Generates an image using DALL-E 2 or DALL-E 3. Returns image bytes or null on failure.</summary>
    public async Task<byte[]?> GenerateImageAsync(string apiKey, string model, string prompt, string size, string quality, string style, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return null;
        if (string.IsNullOrWhiteSpace(prompt))
            return null;

        var isGptImage = string.Equals(model, "gpt-image-1.5", StringComparison.OrdinalIgnoreCase);
        var useDallE3 = string.Equals(model, "dall-e-3", StringComparison.OrdinalIgnoreCase);

        object requestBody = isGptImage
            ? new
            {
                Model = "gpt-image-1.5",
                Prompt = prompt.Trim(),
                N = 1,
                Size = size,
                Quality = quality
            }
            : useDallE3
                ? new
                {
                    Model = "dall-e-3",
                    Prompt = prompt.Trim(),
                    N = 1,
                    Size = size,
                    Quality = quality,
                    Style = style,
                    ResponseFormat = "b64_json"
                }
                : new
                {
                    Model = "dall-e-2",
                    Prompt = prompt.Trim(),
                    N = 1,
                    Size = size,
                    ResponseFormat = "b64_json"
                };

        var json = JsonSerializer.Serialize(requestBody, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, WriteIndented = false });
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = content;

        HttpResponseMessage response;
        try
        {
            response = await HttpClient.SendAsync(request, ct).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }

        var responseJson = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            return null;

        try
        {
            using var doc = JsonDocument.Parse(responseJson);
            var root = doc.RootElement;
            if (!root.TryGetProperty("data", out var data) || data.GetArrayLength() == 0)
                return null;

            var first = data[0];
            if (!first.TryGetProperty("b64_json", out var b64El))
                return null;

            var b64 = b64El.GetString();
            if (string.IsNullOrWhiteSpace(b64))
                return null;

            return Convert.FromBase64String(b64);
        }
        catch
        {
            return null;
        }
    }
}
