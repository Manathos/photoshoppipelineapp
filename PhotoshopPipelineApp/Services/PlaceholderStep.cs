namespace PhotoshopPipelineApp.Services;

public class PlaceholderStep : IFollowUpStep
{
    private readonly Action<string>? _log;

    public PlaceholderStep(Action<string>? log = null)
    {
        _log = log;
    }

    public Task ExecuteAsync(string inputPath, string outputFolder, IReadOnlyList<string> verifiedFiles, CancellationToken ct = default)
    {
        _log?.Invoke($"[Follow-up] Placeholder step: input={inputPath}, output={outputFolder}, verified files={verifiedFiles.Count}");
        return Task.CompletedTask;
    }
}
