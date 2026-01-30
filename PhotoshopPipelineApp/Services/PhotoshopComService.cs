using System.IO;
using System.Runtime.InteropServices;

namespace PhotoshopPipelineApp.Services;

public class PhotoshopComService : IPhotoshopService
{
    private const string PhotoshopProgId = "Photoshop.Application";
    private dynamic? _app;

    public void OpenAndRunAction(string imagePath, string actionSetName, string actionName)
    {
        var fullPath = Path.GetFullPath(imagePath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("Image file not found.", fullPath);

        try
        {
            var type = Type.GetTypeFromProgID(PhotoshopProgId);
            if (type == null)
                throw new InvalidOperationException("Photoshop is not installed or not registered. Please install Adobe Photoshop and run it at least once.");

            _app ??= Activator.CreateInstance(type)!;
            if (_app == null)
                throw new InvalidOperationException("Failed to start Photoshop. Please ensure Photoshop is installed.");

            _app.Open(fullPath);
            _app.DoAction(actionName, actionSetName);
        }
        catch (COMException ex)
        {
            throw new InvalidOperationException($"Photoshop COM error: {ex.Message}. Ensure Photoshop is installed and not script-blocked.", ex);
        }
    }
}
