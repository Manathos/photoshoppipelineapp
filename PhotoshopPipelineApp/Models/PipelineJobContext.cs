namespace PhotoshopPipelineApp.Models;

public class PipelineJobContext
{
    public string InputFilePath { get; set; } = string.Empty;
    public OpenAIMetadata? OpenAIMetadata { get; set; }
    public IReadOnlyList<string> VerifiedOutputFiles { get; set; } = Array.Empty<string>();
    public Dictionary<string, object> StepData { get; set; } = new();
}
