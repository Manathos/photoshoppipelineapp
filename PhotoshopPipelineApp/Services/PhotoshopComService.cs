using System.IO;
using System.Runtime.InteropServices;

namespace PhotoshopPipelineApp.Services;

public class PhotoshopComService : IPhotoshopService
{
    private const string PhotoshopProgId = "Photoshop.Application";
    private const int RpcServerUnavailable = unchecked((int)0x800706BA);
    private dynamic? _app;

    public void OpenAndRunAction(string imagePath, string actionSetName, string actionName)
    {
        var fullPath = Path.GetFullPath(imagePath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("Image file not found.", fullPath);

        var type = Type.GetTypeFromProgID(PhotoshopProgId);
        if (type == null)
            throw new InvalidOperationException("Photoshop is not installed or not registered. Please install Adobe Photoshop and run it at least once.");

        const int maxAttempts = 3;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                _app ??= Activator.CreateInstance(type)!;
                if (_app == null)
                    throw new InvalidOperationException("Failed to start Photoshop. Please ensure Photoshop is installed.");

                _app.Open(fullPath);
                _app.DoAction(actionName, actionSetName);
                return;
            }
            catch (COMException ex)
            {
                _app = null;
                bool rpcUnavailable = ex.HResult == RpcServerUnavailable;
                if (rpcUnavailable && attempt < maxAttempts)
                {
                    Thread.Sleep(2000);
                    continue;
                }
                throw new InvalidOperationException($"Photoshop COM error: {ex.Message}. Ensure Photoshop is installed and not script-blocked.", ex);
            }
        }
    }
}
