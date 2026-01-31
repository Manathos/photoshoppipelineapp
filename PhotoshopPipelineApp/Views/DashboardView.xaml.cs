using System.Collections.Generic;
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

namespace PhotoshopPipelineApp.Views;

public partial class DashboardView : UserControl
{
    private PipelineService? _pipeline;
    private ConfigService? _configService;
    private List<(string Name, string WatchFolderPath, List<string> AllowedExtensions)> _dropZones = new();

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
        _dropZones.Clear();
        var queues = config.Queues ?? new List<QueueConfig>();
        _dropZones = queues.Select((q, i) =>
        {
            var name = string.IsNullOrWhiteSpace(q.Name) ? $"Queue {i + 1}" : q.Name;
            var exts = q.AllowedExtensions?.Any() == true ? q.AllowedExtensions : new List<string> { "*.jpg", "*.jpeg", "*.png", "*.psd" };
            return (Name: name, WatchFolderPath: q.WatchFolderPath ?? "", AllowedExtensions: exts);
        }).ToList();

        var items = new List<FrameworkElement>();
        foreach (var (name, watchPath, allowedExts) in _dropZones)
        {
            var border = new Border
            {
                Margin = new Thickness(0, 0, 0, 8),
                Padding = new Thickness(16),
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(2),
                BorderBrush = new SolidColorBrush(System.Windows.Media.Colors.Gray),
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2D, 0x2D, 0x2D)),
                AllowDrop = true,
                Tag = (name, watchPath, allowedExts)
            };
            var text = new TextBlock
            {
                Text = $"Drop files here for {name}",
                FontSize = 14,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            };
            border.Child = text;
            border.PreviewDragOver += DropZone_PreviewDragOver;
            border.PreviewDrop += DropZone_PreviewDrop;
            items.Add(border);
        }
        DropZonesItems.ItemsSource = items.Count > 0 ? items : new List<FrameworkElement> { new TextBlock { Text = "Add queues in Settings to see drop zones.", Opacity = 0.8, Margin = new Thickness(0, 0, 0, 8) } };
    }

    private void DropZone_PreviewDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void DropZone_PreviewDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var files = (string[]?)e.Data.GetData(DataFormats.FileDrop);
        if (files == null || files.Length == 0) return;
        var tag = (sender as Border)?.Tag;
        if (tag is not (string name, string watchPath, List<string> allowedExts))
            return;
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
            var fileName = Path.GetFileName(src);
            if (string.IsNullOrEmpty(fileName)) continue;
            var matches = allowedExts.Any(ext =>
            {
                if (string.Equals(ext, "*.*", StringComparison.OrdinalIgnoreCase)) return true;
                if (!ext.StartsWith("*.")) return false;
                return fileName.EndsWith(ext.AsSpan(1), StringComparison.OrdinalIgnoreCase);
            });
            if (!matches) continue;
            var dest = Path.Combine(watchPath, fileName);
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
        StartButton.IsEnabled = !_pipeline.IsRunning;
        StopButton.IsEnabled = _pipeline.IsRunning;
        if (_pipeline.IsRunning && _pipeline.QueueStates.Count > 0)
        {
            QueueStatusItems.ItemsSource = _pipeline.QueueStates;
        }
        else
        {
            QueueStatusItems.ItemsSource = null;
        }
    }

    private void StartButton_Click(object sender, RoutedEventArgs e)
    {
        _pipeline?.Start();
        UpdateStatus();
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        _pipeline?.Stop();
        UpdateStatus();
    }
}
