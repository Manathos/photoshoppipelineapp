using System.Windows;
using System.Windows.Controls;
using PhotoshopPipelineApp.Services;

namespace PhotoshopPipelineApp.Views;

public partial class DashboardView : UserControl
{
    private PipelineService? _pipeline;

    public DashboardView()
    {
        InitializeComponent();
    }

    public void SetPipeline(PipelineService pipeline)
    {
        _pipeline = pipeline;
        _pipeline.StatusChanged += Pipeline_StatusChanged;
        _pipeline.LogMessage += Pipeline_LogMessage;
        UpdateStatus();
    }

    private void Pipeline_StatusChanged(object? sender, PipelineStatus status)
    {
        Dispatcher.Invoke(() => UpdateStatus());
    }

    private void Pipeline_LogMessage(object? sender, string message)
    {
        Dispatcher.Invoke(() =>
        {
            LogTextBox.AppendText(message + Environment.NewLine);
            LogTextBox.ScrollToEnd();
        });
    }

    private void UpdateStatus()
    {
        if (_pipeline == null) return;
        StatusText.Text = "Status: " + _pipeline.Status.ToString();
        LastFileText.Text = "Last file: " + (string.IsNullOrEmpty(_pipeline.LastProcessedFile) ? "—" : System.IO.Path.GetFileName(_pipeline.LastProcessedFile));
        StartButton.IsEnabled = _pipeline.Status == PipelineStatus.Idle;
        StopButton.IsEnabled = _pipeline.Status != PipelineStatus.Idle;
    }

    private void StartButton_Click(object sender, RoutedEventArgs e)
    {
        _pipeline?.Start();
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        _pipeline?.Stop();
    }
}
