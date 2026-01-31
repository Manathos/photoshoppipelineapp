namespace PhotoshopPipelineApp.Services;

public class QueueStatusEventArgs : EventArgs
{
    public string QueueName { get; init; } = string.Empty;
    public PipelineStatus Status { get; init; }
    public string? LastProcessedFile { get; init; }
}
