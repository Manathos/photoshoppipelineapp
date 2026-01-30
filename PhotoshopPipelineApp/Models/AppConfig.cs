namespace PhotoshopPipelineApp.Models;

public class AppConfig
{
    public string WatchFolderPath { get; set; } = string.Empty;
    public string OutputFolderPath { get; set; } = string.Empty;
    public string ActionSetName { get; set; } = "Default Actions";
    public string ActionName { get; set; } = "My Action";
    public List<string> RequiredFileNames { get; set; } = new();
    public List<string> AllowedExtensions { get; set; } = new() { "*.jpg", "*.jpeg", "*.png", "*.psd" };
    public int RequiredFilesTimeoutSeconds { get; set; } = 120;
    public string FollowUpType { get; set; } = "None";
    public Dictionary<string, string> FollowUpSettings { get; set; } = new();
}
