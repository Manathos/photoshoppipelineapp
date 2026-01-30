namespace PhotoshopPipelineApp.Services;

public interface IPhotoshopService
{
    void OpenAndRunAction(string imagePath, string actionSetName, string actionName);
}
