using System.ComponentModel;
using System.IO;

namespace PhotoshopPipelineApp.Views;

public class PendingDropItem : INotifyPropertyChanged
{
    private string _sourceFilePath = string.Empty;
    private bool _isDefault;
    private bool _isRightJustified;
    private bool _isReadyToProcess;

    public string SourceFilePath
    {
        get => _sourceFilePath;
        set { _sourceFilePath = value ?? string.Empty; OnPropertyChanged(nameof(SourceFilePath)); OnPropertyChanged(nameof(FileNameDisplay)); }
    }

    public string FileNameDisplay => string.IsNullOrEmpty(_sourceFilePath) ? "—" : Path.GetFileName(_sourceFilePath);

    public bool IsDefault
    {
        get => _isDefault;
        set
        {
            if (_isDefault == value) return;
            _isDefault = value;
            if (value) _isRightJustified = false;
            OnPropertyChanged(nameof(IsDefault));
            OnPropertyChanged(nameof(IsRightJustified));
            OnPropertyChanged(nameof(CanQueue));
        }
    }

    public bool IsRightJustified
    {
        get => _isRightJustified;
        set
        {
            if (_isRightJustified == value) return;
            _isRightJustified = value;
            if (value) _isDefault = false;
            OnPropertyChanged(nameof(IsRightJustified));
            OnPropertyChanged(nameof(IsDefault));
            OnPropertyChanged(nameof(CanQueue));
        }
    }

    public bool CanQueue => _isDefault || _isRightJustified;

    /// <summary>True after user clicked Process; item shows green hue and will be sent when processing spot is free.</summary>
    public bool IsReadyToProcess
    {
        get => _isReadyToProcess;
        set { if (_isReadyToProcess == value) return; _isReadyToProcess = value; OnPropertyChanged(nameof(IsReadyToProcess)); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
