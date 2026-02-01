using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using PhotoshopPipelineApp.Models;

namespace PhotoshopPipelineApp.Services;

public class ImageHistoryService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _storageFolder;
    private readonly string _indexPath;

    public ImageHistoryService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appFolder = Path.Combine(appData, "PhotoshopPipelineApp");
        _storageFolder = Path.Combine(appFolder, "GeneratedImages");
        _indexPath = Path.Combine(_storageFolder, "generated-images-index.json");
    }

    /// <summary>Saves a generated image to disk and adds it to the history index. Newest first in index.</summary>
    public async Task<GeneratedImageRecord> SaveGeneratedImageAsync(byte[] imageBytes, string prompt, string model, CancellationToken ct = default)
    {
        Directory.CreateDirectory(_storageFolder);
        var fileName = "image_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png";
        var filePath = Path.Combine(_storageFolder, fileName);
        await File.WriteAllBytesAsync(filePath, imageBytes, ct).ConfigureAwait(false);

        var record = new GeneratedImageRecord
        {
            FilePath = filePath,
            Prompt = prompt ?? string.Empty,
            Model = model ?? string.Empty,
            CreatedAt = DateTime.Now
        };

        var list = LoadIndexList();
        list.Insert(0, record);
        SaveIndexList(list);
        return record;
    }

    /// <summary>Loads history from the index, filtering out missing files. Newest first.</summary>
    public ObservableCollection<GeneratedImageRecord> LoadHistory()
    {
        var list = LoadIndexList();
        var valid = list.Where(r => !string.IsNullOrEmpty(r.FilePath) && File.Exists(r.FilePath)).ToList();
        return new ObservableCollection<GeneratedImageRecord>(valid);
    }

    /// <summary>Deletes the image file and removes the record from the index.</summary>
    public void DeleteRecord(GeneratedImageRecord record)
    {
        if (string.IsNullOrEmpty(record.FilePath)) return;
        try
        {
            if (File.Exists(record.FilePath))
                File.Delete(record.FilePath);
        }
        catch
        {
            // Ignore file delete errors
        }

        var list = LoadIndexList();
        list.RemoveAll(r => string.Equals(r.FilePath, record.FilePath, StringComparison.OrdinalIgnoreCase));
        SaveIndexList(list);
    }

    /// <summary>Reads image bytes from disk for loading into the main view.</summary>
    public byte[]? GetImageBytes(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            return null;
        try
        {
            return File.ReadAllBytes(filePath);
        }
        catch
        {
            return null;
        }
    }

    private List<GeneratedImageRecord> LoadIndexList()
    {
        if (!File.Exists(_indexPath))
            return new List<GeneratedImageRecord>();
        try
        {
            var json = File.ReadAllText(_indexPath);
            var list = JsonSerializer.Deserialize<List<GeneratedImageRecord>>(json, JsonOptions);
            return list ?? new List<GeneratedImageRecord>();
        }
        catch
        {
            return new List<GeneratedImageRecord>();
        }
    }

    private void SaveIndexList(List<GeneratedImageRecord> list)
    {
        Directory.CreateDirectory(_storageFolder);
        var json = JsonSerializer.Serialize(list, JsonOptions);
        File.WriteAllText(_indexPath, json);
    }
}
