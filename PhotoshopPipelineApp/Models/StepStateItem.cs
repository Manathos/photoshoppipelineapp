using System.ComponentModel;

namespace PhotoshopPipelineApp.Models;

public class StepStateItem : INotifyPropertyChanged
{
    private string _label = string.Empty;
    private StepStatus _status = StepStatus.Pending;

    public string Label
    {
        get => _label;
        set { _label = value ?? string.Empty; OnPropertyChanged(nameof(Label)); }
    }

    public StepStatus Status
    {
        get => _status;
        set { _status = value; OnPropertyChanged(nameof(Status)); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
