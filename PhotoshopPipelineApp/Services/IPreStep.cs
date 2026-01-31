using PhotoshopPipelineApp.Models;

namespace PhotoshopPipelineApp.Services;

public interface IPreStep
{
    Task ExecuteAsync(PipelineJobContext context, IReadOnlyDictionary<string, string> settings, CancellationToken ct = default);
}
