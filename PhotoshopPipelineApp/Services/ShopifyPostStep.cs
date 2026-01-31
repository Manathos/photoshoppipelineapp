using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using PhotoshopPipelineApp.Models;

namespace PhotoshopPipelineApp.Services;

public class ShopifyPostStep : IPostStep
{
    private static readonly HttpClient HttpClient = new();
    private const string ApiVersion = "2024-01";

    private static string GetAccessToken(IReadOnlyDictionary<string, string> settings)
    {
        var env = Environment.GetEnvironmentVariable("SHOPIFY_ACCESS_TOKEN");
        if (!string.IsNullOrWhiteSpace(env)) return env.Trim();
        if (settings.TryGetValue("AccessToken", out var tok) && !string.IsNullOrWhiteSpace(tok)) return tok.Trim();
        if (settings.TryGetValue("ApiKey", out var key) && !string.IsNullOrWhiteSpace(key)) return key.Trim();
        return string.Empty;
    }

    public async Task ExecuteAsync(PipelineJobContext context, IReadOnlyDictionary<string, string> settings, CancellationToken ct = default)
    {
        var storeUrl = settings.TryGetValue("StoreUrl", out var u) ? u?.Trim() ?? "" : "";
        if (string.IsNullOrWhiteSpace(storeUrl))
            throw new InvalidOperationException("Shopify StoreUrl is not set.");
        if (!storeUrl.Contains("."))
            storeUrl = storeUrl + ".myshopify.com";
        if (!storeUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            storeUrl = "https://" + storeUrl;

        var accessToken = GetAccessToken(settings);
        if (string.IsNullOrWhiteSpace(accessToken))
            throw new InvalidOperationException("Shopify Access token is not set. Set it in Settings or SHOPIFY_ACCESS_TOKEN env.");

        var createDraft = settings.TryGetValue("CreateAsDraft", out var d) && string.Equals(d, "true", StringComparison.OrdinalIgnoreCase);
        var defaultPrice = settings.TryGetValue("DefaultPrice", out var p) ? p?.Trim() ?? "0.00" : "0.00";
        var skuPattern = settings.TryGetValue("SkuPattern", out var skuPat) ? skuPat?.Trim() ?? "IMG-{filename}" : "IMG-{filename}";
        var optionName = settings.TryGetValue("VariantOptionName", out var on) ? on?.Trim() : "";
        var optionValuesStr = settings.TryGetValue("VariantOptionValues", out var ov) ? ov?.Trim() : "";

        var title = context.OpenAIMetadata?.Title?.Trim();
        if (string.IsNullOrWhiteSpace(title))
            title = Path.GetFileNameWithoutExtension(context.InputFilePath) ?? "Product";
        var bodyHtml = context.OpenAIMetadata?.Description?.Trim() ?? "";
        var tags = context.OpenAIMetadata?.Tags != null && context.OpenAIMetadata.Tags.Count > 0
            ? string.Join(", ", context.OpenAIMetadata.Tags)
            : "";

        var baseFilename = Path.GetFileNameWithoutExtension(context.InputFilePath) ?? "img";
        var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        var sku = skuPattern
            .Replace("{filename}", baseFilename, StringComparison.OrdinalIgnoreCase)
            .Replace("{timestamp}", timestamp, StringComparison.OrdinalIgnoreCase);

        var optionValues = string.IsNullOrWhiteSpace(optionValuesStr)
            ? Array.Empty<string>()
            : optionValuesStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var variants = new List<object>();
        if (!string.IsNullOrWhiteSpace(optionName) && optionValues.Length > 0)
        {
            for (var i = 0; i < optionValues.Length; i++)
            {
                variants.Add(new
                {
                    price = defaultPrice,
                    sku = optionValues.Length > 1 ? $"{sku}-{optionValues[i]}" : sku,
                    option1 = optionValues[i]
                });
            }
        }
        else
        {
            variants.Add(new { price = defaultPrice, sku });
        }

        var productPayload = new
        {
                product = new
                {
                    title,
                    body_html = bodyHtml,
                    tags,
                    status = createDraft ? "draft" : "active",
                    variants,
                    options = (!string.IsNullOrWhiteSpace(optionName) && optionValues.Length > 0)
                        ? new[] { new { name = optionName, values = optionValues } }
                        : (object?)null
                }
        };

        var opts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = false };
        var json = JsonSerializer.Serialize(productPayload, opts);
        var baseUrl = storeUrl.TrimEnd('/') + "/admin/api/" + ApiVersion + "/";
        using var createReq = new HttpRequestMessage(HttpMethod.Post, baseUrl + "products.json");
        createReq.Headers.Add("X-Shopify-Access-Token", accessToken);
        createReq.Content = new StringContent(json, Encoding.UTF8, "application/json");

        HttpResponseMessage createRes;
        try
        {
            createRes = await HttpClient.SendAsync(createReq, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Shopify request failed: {ex.Message}", ex);
        }

        if (!createRes.IsSuccessStatusCode)
        {
            var errBody = await createRes.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new InvalidOperationException($"Shopify API error ({(int)createRes.StatusCode}): {errBody}");
        }

        var createBody = await createRes.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var createDoc = JsonDocument.Parse(createBody);
        var productEl = createDoc.RootElement.GetProperty("product");
        var productId = productEl.GetProperty("id").GetInt64();

        if (context.VerifiedOutputFiles.Count > 0)
        {
            foreach (var filePath in context.VerifiedOutputFiles)
            {
                if (!File.Exists(filePath)) continue;
                byte[] bytes;
                try
                {
                    bytes = await File.ReadAllBytesAsync(filePath, ct).ConfigureAwait(false);
                }
                catch
                {
                    continue;
                }
                var base64 = Convert.ToBase64String(bytes);
                var imagePayload = new { image = new { attachment = base64 } };
                var imageJson = JsonSerializer.Serialize(imagePayload, opts);
                using var imgReq = new HttpRequestMessage(HttpMethod.Post, baseUrl + $"products/{productId}/images.json");
                imgReq.Headers.Add("X-Shopify-Access-Token", accessToken);
                imgReq.Content = new StringContent(imageJson, Encoding.UTF8, "application/json");
                try
                {
                    var imgRes = await HttpClient.SendAsync(imgReq, ct).ConfigureAwait(false);
                    if (!imgRes.IsSuccessStatusCode)
                    {
                        var errImg = await imgRes.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                        throw new InvalidOperationException($"Shopify image upload failed: {errImg}");
                    }
                }
                catch (Exception ex) when (ex is not InvalidOperationException)
                {
                    throw new InvalidOperationException($"Shopify image upload failed: {ex.Message}", ex);
                }
            }
        }
    }
}
