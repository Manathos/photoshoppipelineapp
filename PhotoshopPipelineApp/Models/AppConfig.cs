namespace PhotoshopPipelineApp.Models;

public class AppConfig
{
    public List<QueueConfig> Queues { get; set; } = new();
    public string OpenAIApiKey { get; set; } = string.Empty;
    public string ShopifyAccessToken { get; set; } = string.Empty;
}
