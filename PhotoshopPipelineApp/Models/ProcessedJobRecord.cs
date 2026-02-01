namespace PhotoshopPipelineApp.Models;

public class ProcessedJobRecord
{
    public string QueueName { get; set; } = string.Empty;
    public string InputFilePath { get; set; } = string.Empty;
    public string WatchFolderPath { get; set; } = string.Empty;
    public string OutputFolderPath { get; set; } = string.Empty;
    public DateTime CompletedAt { get; set; }
    public StepStatus PreStepStatus { get; set; }
    public StepStatus PhotoshopStatus { get; set; }
    public StepStatus VerifyStatus { get; set; }
    public StepStatus PostStepStatus { get; set; }
    public string LogText { get; set; } = string.Empty;
    public bool TimedOut { get; set; }
    public OpenAIMetadata? OpenAIMetadata { get; set; }

    public string FileNameDisplay => string.IsNullOrEmpty(InputFilePath)
        ? "—"
        : System.IO.Path.GetFileName(InputFilePath);
}
