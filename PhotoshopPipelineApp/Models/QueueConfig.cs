namespace PhotoshopPipelineApp.Models;

public class QueueConfig
{
    public string Name { get; set; } = string.Empty;
    public string WatchFolderPath { get; set; } = string.Empty;
    public string OutputFolderPath { get; set; } = string.Empty;
    public string ActionSetName { get; set; } = "Default Actions";
    public string ActionName { get; set; } = "My Action";
    public List<string> AllowedExtensions { get; set; } = new() { "*.jpg", "*.jpeg", "*.png", "*.psd" };
    public List<string> RequiredFileNames { get; set; } = new();
    public int RequiredFilesTimeoutSeconds { get; set; } = 120;

    public string PreStepType { get; set; } = "None";
    public Dictionary<string, string> PreStepSettings { get; set; } = new();

    public string PostStepType { get; set; } = "None";
    public Dictionary<string, string> PostStepSettings { get; set; } = new();
}
