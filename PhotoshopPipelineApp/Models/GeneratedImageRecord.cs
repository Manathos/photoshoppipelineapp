using System.IO;

namespace PhotoshopPipelineApp.Models;

public class GeneratedImageRecord
{
    public string FilePath { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    public string FileNameDisplay => string.IsNullOrEmpty(FilePath)
        ? "—"
        : Path.GetFileName(FilePath);

    /// <summary>Truncated prompt for display in the history list.</summary>
    public string PromptDisplay
    {
        get
        {
            if (string.IsNullOrEmpty(Prompt)) return "—";
            const int maxLen = 60;
            return Prompt.Length <= maxLen ? Prompt : Prompt[..(maxLen - 3)] + "...";
        }
    }
}
