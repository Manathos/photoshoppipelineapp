using System.Collections.ObjectModel;
using System.ComponentModel;
using PhotoshopPipelineApp.Models;

namespace PhotoshopPipelineApp.Services;

public class QueueStateItem : INotifyPropertyChanged
{
    private string _queueName = string.Empty;
    private PipelineStatus _status;
    private string _lastProcessedFile = string.Empty;
    private OpenAIMetadata? _lastOpenAIMetadata;

    public ObservableCollection<StepStateItem> CurrentStepStates { get; } = new();

    public string QueueName
    {
        get => _queueName;
        set { _queueName = value ?? string.Empty; OnPropertyChanged(nameof(QueueName)); OnPropertyChanged(nameof(LastFileDisplay)); }
    }

    public PipelineStatus Status
    {
        get => _status;
        set { _status = value; OnPropertyChanged(nameof(Status)); }
    }

    public string LastProcessedFile
    {
        get => _lastProcessedFile;
        set { _lastProcessedFile = value ?? string.Empty; OnPropertyChanged(nameof(LastProcessedFile)); OnPropertyChanged(nameof(LastFileDisplay)); }
    }

    public string LastFileDisplay => string.IsNullOrEmpty(_lastProcessedFile) ? "—" : System.IO.Path.GetFileName(_lastProcessedFile);

    public OpenAIMetadata? LastOpenAIMetadata
    {
        get => _lastOpenAIMetadata;
        set { _lastOpenAIMetadata = value; OnPropertyChanged(nameof(LastOpenAIMetadata)); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
