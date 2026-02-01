using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PhotoshopPipelineApp.Models;

namespace PhotoshopPipelineApp.Services;

public class OpenAIPreStep : IPreStep
{
    private static readonly HttpClient HttpClient = new();
    private const string DefaultModel = "gpt-4o";
    private const string ApiUrl = "https://api.openai.com/v1/chat/completions";

    private static string GetApiKey(IReadOnlyDictionary<string, string> settings)
    {
        var env = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (!string.IsNullOrWhiteSpace(env)) return env.Trim();
        if (settings.TryGetValue("ApiKey", out var key) && !string.IsNullOrWhiteSpace(key)) return key.Trim();
        return string.Empty;
    }

    public async Task ExecuteAsync(PipelineJobContext context, IReadOnlyDictionary<string, string> settings, CancellationToken ct = default)
    {
        var apiKey = GetApiKey(settings);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            context.OpenAIMetadata = null;
            return;
        }

        var inputPath = context.InputFilePath;
        if (string.IsNullOrWhiteSpace(inputPath) || !File.Exists(inputPath))
        {
            context.OpenAIMetadata = null;
            return;
        }

        byte[] imageBytes;
        try
        {
            imageBytes = await File.ReadAllBytesAsync(inputPath, ct).ConfigureAwait(false);
        }
        catch
        {
            context.OpenAIMetadata = null;
            return;
        }

        var base64 = Convert.ToBase64String(imageBytes);
        var ext = Path.GetExtension(inputPath).ToLowerInvariant();
        var mime = ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            _ => "image/jpeg"
        };
        var dataUrl = $"data:{mime};base64,{base64}";

        var model = settings.TryGetValue("Model", out var m) && !string.IsNullOrWhiteSpace(m) ? m.Trim() : DefaultModel;
        var prompt = settings.TryGetValue("Prompt", out var p) && !string.IsNullOrWhiteSpace(p)
            ? p
            : "Analyze this image and return a JSON object with exactly these keys: \"title\" (short product title, one line), \"description\" (product description for a web store, 1-3 sentences), \"tags\" (array of strings, 3-8 relevant tags). Return only valid JSON, no markdown.";

        var userText = prompt.Trim();
        if (!userText.Contains("json", StringComparison.OrdinalIgnoreCase))
            userText += " Respond with valid JSON only.";
        var requestBody = new
        {
            model,
            max_tokens = 500,
            response_format = new { type = "json_object" },
            messages = new object[]
            {
                new { role = "system", content = "You must respond with a single valid JSON object. Do not use markdown code fences." },
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "text", text = userText },
                        new { type = "image_url", image_url = new { url = dataUrl } }
                    }
                }
            }
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
        catch (Exception ex)
        {
            context.OpenAIMetadata = null;
            throw new InvalidOperationException($"OpenAI request failed: {ex.Message}", ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            context.OpenAIMetadata = null;
            throw new InvalidOperationException($"OpenAI API error ({response.StatusCode}): {body}");
        }

        var responseJson = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(responseJson);
        var root = doc.RootElement;
        if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
        {
            context.OpenAIMetadata = null;
            return;
        }

        var message = choices[0].GetProperty("message");
        var text = message.TryGetProperty("content", out var c) ? c.GetString() : null;
        if (string.IsNullOrWhiteSpace(text))
        {
            context.OpenAIMetadata = null;
            return;
        }

        text = text.Trim();
        var jsonMatch = Regex.Match(text, @"\{[\s\S]*\}");
        if (jsonMatch.Success)
            text = jsonMatch.Value;

        try
        {
            using var metaDoc = JsonDocument.Parse(text);
            var metaRoot = metaDoc.RootElement;
            var title = metaRoot.TryGetProperty("title", out var t) ? t.GetString()?.Trim() ?? "" : "";
            var description = metaRoot.TryGetProperty("description", out var d) ? d.GetString()?.Trim() ?? "" : "";
            var tagsList = new List<string>();
            if (metaRoot.TryGetProperty("tags", out var tagsEl) && tagsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in tagsEl.EnumerateArray())
                {
                    var tag = item.GetString()?.Trim();
                    if (!string.IsNullOrEmpty(tag)) tagsList.Add(tag);
                }
            }

            context.OpenAIMetadata = new OpenAIMetadata
            {
                Title = title,
                Description = description,
                Tags = tagsList
            };
        }
        catch
        {
            context.OpenAIMetadata = null;
        }
    }
}
