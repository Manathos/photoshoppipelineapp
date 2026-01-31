using PhotoshopPipelineApp.Models;

namespace PhotoshopPipelineApp.Services;

public class PlaceholderStep : IPostStep
{
    private readonly Action<string>? _log;

    public PlaceholderStep(Action<string>? log = null)
    {
        _log = log;
    }

    public Task ExecuteAsync(PipelineJobContext context, IReadOnlyDictionary<string, string> settings, CancellationToken ct = default)
    {
        _log?.Invoke($"[Follow-up] Placeholder step: input={context.InputFilePath}, verified files={context.VerifiedOutputFiles.Count}");
        return Task.CompletedTask;
    }
}
