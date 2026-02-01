using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Media;
using PhotoshopPipelineApp.Models;
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
    private OpenAIMetadata? _lastOpenAIMetadata;

    public ObservableCollection<StepStateItem> StepStates { get; } = new();

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

    public OpenAIMetadata? LastOpenAIMetadata
    {
        get => _lastOpenAIMetadata;
        set { _lastOpenAIMetadata = value; OnPropertyChanged(nameof(LastOpenAIMetadata)); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
