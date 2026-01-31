using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Media;
using PhotoshopPipelineApp.Services;

namespace PhotoshopPipelineApp.Views;

public class DropZoneItem : INotifyPropertyChanged
{
    private string _queueName = string.Empty;
    private string _currentImagePath = string.Empty;
    private PipelineStatus _status;
    private string _watchFolderPath = string.Empty;
    private List<string> _allowedExtensions = new();
    private ImageSource? _currentImage;

    public string QueueName
    {
        get => _queueName;
        set { _queueName = value ?? string.Empty; OnPropertyChanged(nameof(QueueName)); }
    }

    public string CurrentImagePath
    {
        get => _currentImagePath;
        set { _currentImagePath = value ?? string.Empty; OnPropertyChanged(nameof(CurrentImagePath)); }
    }

    public PipelineStatus Status
    {
        get => _status;
        set { _status = value; OnPropertyChanged(nameof(Status)); OnPropertyChanged(nameof(IsBusy)); }
    }

    public string WatchFolderPath
    {
        get => _watchFolderPath;
        set { _watchFolderPath = value ?? string.Empty; OnPropertyChanged(nameof(WatchFolderPath)); }
    }

    public List<string> AllowedExtensions
    {
        get => _allowedExtensions;
        set { _allowedExtensions = value ?? new List<string>(); OnPropertyChanged(nameof(AllowedExtensions)); }
    }

    public bool IsBusy => _status != PipelineStatus.Idle && _status != PipelineStatus.Watching;

    public ImageSource? CurrentImage
    {
        get => _currentImage;
        set { _currentImage = value; OnPropertyChanged(nameof(CurrentImage)); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
