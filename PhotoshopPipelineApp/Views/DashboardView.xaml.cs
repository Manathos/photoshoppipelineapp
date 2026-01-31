using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PhotoshopPipelineApp.Models;
using PhotoshopPipelineApp.Services;
using UserControl = System.Windows.Controls.UserControl;
using DragEventArgs = System.Windows.DragEventArgs;
using DataFormats = System.Windows.DataFormats;
using DragDropEffects = System.Windows.DragDropEffects;
using Application = System.Windows.Application;

namespace PhotoshopPipelineApp.Views;

public partial class DashboardView : UserControl
{
    private PipelineService? _pipeline;
    private ConfigService? _configService;
    private readonly ObservableCollection<DropZoneItem> _dropZoneItems = new();

    public DashboardView()
    {
        InitializeComponent();
        Loaded += (_, _) => { if (_configService != null) BuildDropZones(); };
        IsVisibleChanged += (_, e) => { if (e.NewValue is true && _configService != null) BuildDropZones(); };
    }

    public void SetPipeline(PipelineService pipeline)
    {
        _pipeline = pipeline;
        _pipeline.QueueStatusChanged += Pipeline_QueueStatusChanged;
        _pipeline.LogMessage += Pipeline_LogMessage;
        UpdateStatus();
    }

    public void SetConfigService(ConfigService configService)
    {
        _configService = configService;
        BuildDropZones();
    }

    private void BuildDropZones()
    {
        if (_configService == null) return;
        var config = _configService.Load();
        _dropZoneItems.Clear();
        var queues = config.Queues ?? new List<QueueConfig>();
        foreach (var (q, i) in queues.Select((q, i) => (q, i)))
        {
            var name = string.IsNullOrWhiteSpace(q.Name) ? $"Queue {i + 1}" : q.Name;
            var exts = q.AllowedExtensions?.Any() == true ? q.AllowedExtensions.ToList() : new List<string> { "*.jpg", "*.jpeg", "*.png", "*.psd" };
            _dropZoneItems.Add(new DropZoneItem
            {
                QueueName = name,
                WatchFolderPath = q.WatchFolderPath ?? "",
                AllowedExtensions = exts
            });
        }
        DropZonesItems.ItemsSource = _dropZoneItems;
        DropZonesPlaceholder.Visibility = _dropZoneItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        DropZonesItems.Visibility = _dropZoneItems.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void DropZone_PreviewDragOver(object sender, DragEventArgs e)
    {
        var hasFiles = e.Data.GetDataPresent(DataFormats.FileDrop);
        e.Effects = hasFiles ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
        if (sender is not Grid grid || grid.Children.Count == 0 || grid.Children[0] is not Border fillBorder) return;
        var app = Application.Current;
        var idleBackground = app?.Resources["DropZoneIdleBackground"] as SolidColorBrush ?? new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2D, 0x2D, 0x2D));
        var dragOverBackground = app?.Resources["DropZoneDragOverBackground"] as SolidColorBrush ?? new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1A, 0x3A, 0x52));
        var idleBorder = app?.Resources["DropZoneIdleBorder"] as SolidColorBrush ?? new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x60, 0x60, 0x60));
        var dragOverBorder = app?.Resources["DropZoneDragOverBorder"] as SolidColorBrush ?? new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x00, 0x78, 0xD4));
        fillBorder.Background = hasFiles ? dragOverBackground : idleBackground;
        fillBorder.BorderBrush = hasFiles ? dragOverBorder : idleBorder;
    }

    private void DropZone_DragLeave(object sender, DragEventArgs e)
    {
        RestoreDropZoneIdleLook(sender);
    }

    private static void RestoreDropZoneIdleLook(object? sender)
    {
        if (sender is not Grid grid || grid.Children.Count == 0 || grid.Children[0] is not Border fillBorder) return;
        var app = Application.Current;
        var idleBorder = app?.Resources["DropZoneIdleBorder"] as SolidColorBrush ?? new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x60, 0x60, 0x60));
        var idleBackground = app?.Resources["DropZoneIdleBackground"] as SolidColorBrush ?? new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2D, 0x2D, 0x2D));
        fillBorder.Background = idleBackground;
        fillBorder.BorderBrush = idleBorder;
    }

    private void DropZone_PreviewDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var files = (string[]?)e.Data.GetData(DataFormats.FileDrop);
        if (files == null || files.Length == 0) return;
        var item = (sender as FrameworkElement)?.DataContext as DropZoneItem;
        if (item == null) return;
        var name = item.QueueName;
        var watchPath = item.WatchFolderPath;
        var allowedExts = item.AllowedExtensions;
        if (string.IsNullOrWhiteSpace(watchPath))
        {
            _pipeline?.Log($"[{name}] Cannot drop: watch folder not set. Configure it in Settings.");
            return;
        }
        try
        {
            Directory.CreateDirectory(watchPath);
        }
        catch (Exception ex)
        {
            _pipeline?.Log($"[{name}] Cannot create watch folder: {ex.Message}");
            return;
        }
        var copied = 0;
        foreach (var src in files)
        {
            if (!File.Exists(src)) continue;
            var fileName = System.IO.Path.GetFileName(src);
            if (string.IsNullOrEmpty(fileName)) continue;
            var matches = allowedExts.Any(ext =>
            {
                if (string.Equals(ext, "*.*", StringComparison.OrdinalIgnoreCase)) return true;
                if (!ext.StartsWith("*.")) return false;
                return fileName.EndsWith(ext[1..], StringComparison.OrdinalIgnoreCase);
            });
            if (!matches) continue;
            var dest = System.IO.Path.Combine(watchPath, fileName);
            try
            {
                File.Copy(src, dest, overwrite: true);
                copied++;
            }
            catch (Exception ex)
            {
                _pipeline?.Log($"[{name}] Failed to copy {fileName}: {ex.Message}");
            }
        }
        if (copied > 0)
            _pipeline?.Log($"[{name}] Copied {copied} file(s) to watch folder.");
        RestoreDropZoneIdleLook(sender);
        e.Handled = true;
    }

    private void Pipeline_QueueStatusChanged(object? sender, QueueStatusEventArgs e)
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
        StatusSummaryText.Text = _pipeline.IsRunning ? "Status: Running" : "Status: Idle";
        PipelineToggleButton.IsChecked = _pipeline.IsRunning;
        PipelineToggleButton.Content = _pipeline.IsRunning ? "Pipeline active" : "Pipeline inactive";
        if (_pipeline.IsRunning && _pipeline.QueueStates.Count > 0)
            QueueStatusItems.ItemsSource = _pipeline.QueueStates;
        else
            QueueStatusItems.ItemsSource = null;
        for (var i = 0; i < _pipeline.QueueStates.Count && i < _dropZoneItems.Count; i++)
        {
            var state = _pipeline.QueueStates[i];
            var item = _dropZoneItems[i];
            item.CurrentImagePath = state.LastProcessedFile ?? "";
            item.Status = state.Status;
        }
    }

    private void PipelineToggleButton_Checked(object sender, RoutedEventArgs e)
    {
        _pipeline?.Start();
        UpdateStatus();
    }

    private void PipelineToggleButton_Unchecked(object sender, RoutedEventArgs e)
    {
        _pipeline?.Stop();
        UpdateStatus();
    }

    private void ViewOpenAIResponseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_pipeline == null) return;
        var meta = _pipeline.LastOpenAIResponse;
        var queueName = _pipeline.LastOpenAIResponseQueueName ?? "";
        if (meta == null)
        {
            System.Windows.MessageBox.Show(
                "No OpenAI response yet. Run a job with OpenAI pre-step to see the result.",
                "Last OpenAI response",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
            return;
        }
        var sb = new System.Text.StringBuilder();
        if (!string.IsNullOrEmpty(queueName))
            sb.AppendLine("Queue: " + queueName);
        sb.AppendLine();
        sb.AppendLine("Title: " + (meta.Title ?? "(empty)"));
        sb.AppendLine();
        sb.AppendLine("Description: " + (string.IsNullOrEmpty(meta.Description) ? "(empty)" : meta.Description));
        sb.AppendLine();
        sb.Append("Tags: " + (meta.Tags != null && meta.Tags.Count > 0 ? string.Join(", ", meta.Tags) : "(none)"));
        System.Windows.MessageBox.Show(
            sb.ToString(),
            "Last OpenAI response",
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Information);
    }
}
