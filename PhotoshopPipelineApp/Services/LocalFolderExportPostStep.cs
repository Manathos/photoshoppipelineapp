using System.IO;
using PhotoshopPipelineApp.Models;

namespace PhotoshopPipelineApp.Services;

public class LocalFolderExportPostStep : IPostStep
{
    public Task ExecuteAsync(PipelineJobContext context, IReadOnlyDictionary<string, string> settings, CancellationToken ct = default)
    {
        var basePath = settings.TryGetValue("BasePath", out var bp) ? bp?.Trim() ?? "" : "";
        if (string.IsNullOrWhiteSpace(basePath))
            throw new InvalidOperationException("Local export BasePath is not set.");

        var sanitizedName = GetSanitizedName(context);
        if (string.IsNullOrWhiteSpace(sanitizedName))
            sanitizedName = "Untitled";

        var targetFolder = Path.Combine(basePath, sanitizedName);
        Directory.CreateDirectory(targetFolder);

        foreach (var sourcePath in context.VerifiedOutputFiles)
        {
            if (!File.Exists(sourcePath)) continue;
            var ext = Path.GetExtension(sourcePath);
            var destFileName = sanitizedName + ext;
            var destPath = Path.Combine(targetFolder, destFileName);
            File.Copy(sourcePath, destPath, overwrite: true);
        }

        return Task.CompletedTask;
    }

    private static string GetSanitizedName(PipelineJobContext context)
    {
        var title = context.OpenAIMetadata?.Title?.Trim();
        string segment;
        if (!string.IsNullOrWhiteSpace(title) && title.Contains(','))
        {
            segment = title.Split(',')[0].Trim();
        }
        else if (!string.IsNullOrWhiteSpace(title))
        {
            segment = title;
        }
        else
        {
            var inputName = Path.GetFileNameWithoutExtension(context.InputFilePath);
            segment = !string.IsNullOrWhiteSpace(inputName) ? inputName : "Untitled";
        }

        if (string.IsNullOrWhiteSpace(segment))
            segment = Path.GetFileNameWithoutExtension(context.InputFilePath) ?? "Untitled";

        var invalid = Path.GetInvalidFileNameChars();
        foreach (var c in invalid)
            segment = segment.Replace(c, '_');
        return segment.Trim().Length > 0 ? segment.Trim() : "Untitled";
    }
}
