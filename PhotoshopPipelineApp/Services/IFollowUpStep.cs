namespace PhotoshopPipelineApp.Services;

public interface IFollowUpStep
{
    Task ExecuteAsync(string inputPath, string outputFolder, IReadOnlyList<string> verifiedFiles, CancellationToken ct = default);
}
