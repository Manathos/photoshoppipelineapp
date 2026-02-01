using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace PhotoshopPipelineApp.Services;

/// <summary>
/// Uses a cheap GPT model (gpt-4o-mini) to rewrite a user's image prompt for better results with DALL·E / GPT Image models.
/// </summary>
public class PromptRewriteService
{
    private static readonly HttpClient HttpClient = new();
    private const string ApiUrl = "https://api.openai.com/v1/chat/completions";
    private const string Model = "gpt-4o-mini";

    private const string SystemPrompt = @"You are an expert at writing image generation prompts for DALL·E and similar models.
Given a short or vague user prompt, rewrite it into a single, detailed prompt that will produce the best possible image.
Rules:
- Output ONLY the improved prompt text, no quotes, no preamble, no explanation.
- Keep it one paragraph, suitable for an image API (typically 1–3 sentences).
- Add specific details: style (e.g. digital art, photorealistic, watercolor), lighting, composition, mood.
- Preserve the user's core idea and intent; only expand and clarify.
- Do not add markdown, bullets, or labels.";

    /// <summary>
    /// Rewrites the given prompt for better image generation. Returns null on failure.
    /// </summary>
    public async Task<string?> RewritePromptAsync(string apiKey, string userPrompt, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(userPrompt))
            return null;

        var requestBody = new
        {
            model = Model,
            max_tokens = 300,
            messages = new object[]
            {
                new { role = "system", content = SystemPrompt },
                new { role = "user", content = userPrompt.Trim() }
            }
        };

        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, WriteIndented = false };
        var json = JsonSerializer.Serialize(requestBody, options);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = content;

        try
        {
            var response = await HttpClient.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return null;

            var responseJson = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(responseJson);
            var root = doc.RootElement;
            if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                return null;

            var message = choices[0].GetProperty("message");
            var text = message.TryGetProperty("content", out var c) ? c.GetString() : null;
            return text?.Trim();
        }
        catch
        {
            return null;
        }
    }
}
