using PhotoshopPipelineApp.Models;

namespace PhotoshopPipelineApp.Services;

public interface IPostStep
{
    Task ExecuteAsync(PipelineJobContext context, IReadOnlyDictionary<string, string> settings, CancellationToken ct = default);
}
