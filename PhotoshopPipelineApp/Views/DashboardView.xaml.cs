using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using PhotoshopPipelineApp.Models;
using PhotoshopPipelineApp.Services;
using UserControl = System.Windows.Controls.UserControl;
using DragEventArgs = System.Windows.DragEventArgs;
using DataFormats = System.Windows.DataFormats;
using DragDropEffects = System.Windows.DragDropEffects;
using Application = System.Windows.Application;
using Path = System.Windows.Shapes.Path;
using Brushes = System.Windows.Media.Brushes;

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

        var app = Application.Current;
        var idleBorder = app?.Resources["DropZoneIdleBorder"] as SolidColorBrush ?? new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x60, 0x60, 0x60));
        var idleBackground = app?.Resources["DropZoneIdleBackground"] as SolidColorBrush ?? new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2D, 0x2D, 0x2D));
        var dragOverBorder = app?.Resources["DropZoneDragOverBorder"] as SolidColorBrush ?? new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x00, 0x78, 0xD4));
        var dragOverBackground = app?.Resources["DropZoneDragOverBackground"] as SolidColorBrush ?? new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1A, 0x3A, 0x52));
        var cardPadding = app?.Resources["CardPadding"] is Thickness t ? t : new Thickness(12);
        var bodyFontSize = app?.Resources["BodyFontSize"] is double d ? d : 12.0;

        var items = new List<FrameworkElement>();
        foreach (var (name, watchPath, allowedExts) in _dropZones)
        {
            var grid = new Grid
            {
                Margin = new Thickness(0, 0, 0, 8),
                MinHeight = 56,
                AllowDrop = true,
                Tag = (name, watchPath, allowedExts)
            };
            var fillBorder = new Border
            {
                CornerRadius = new CornerRadius(8),
                Background = idleBackground,
                BorderThickness = new Thickness(0)
            };
            var dashedOutline = new Path
            {
                Data = CreateRoundedRectGeometry(),
                Stretch = Stretch.Fill,
                Stroke = idleBorder,
                StrokeThickness = 2,
                StrokeDashArray = new DoubleCollection { 4, 4 },
                Fill = Brushes.Transparent,
                Margin = new Thickness(1)
            };
            var text = new TextBlock
            {
                Text = $"Drop files here for {name}",
                FontSize = bodyFontSize,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            };
            var contentBorder = new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = cardPadding,
                Background = Brushes.Transparent,
                Child = text
            };
            grid.Children.Add(fillBorder);
            grid.Children.Add(dashedOutline);
            grid.Children.Add(contentBorder);
            grid.Tag = (name, watchPath, allowedExts);
            grid.PreviewDragOver += DropZone_PreviewDragOver;
            grid.PreviewDrop += DropZone_PreviewDrop;
            grid.DragLeave += DropZone_DragLeave;
            items.Add(grid);
        }
        DropZonesItems.ItemsSource = items.Count > 0 ? items : new List<FrameworkElement> { new TextBlock { Text = "Add queues in Settings to see drop zones.", Opacity = 0.8, Margin = new Thickness(0, 0, 0, 8) } };
    }

    private static Geometry CreateRoundedRectGeometry()
    {
        var r = 0.1;
        var fig = new PathFigure(new System.Windows.Point(r, 0), new List<PathSegment>(), true);
        fig.Segments.Add(new LineSegment(new System.Windows.Point(1 - r, 0), true));
        fig.Segments.Add(new ArcSegment(new System.Windows.Point(1, r), new System.Windows.Size(r, r), 0, false, SweepDirection.Clockwise, true));
        fig.Segments.Add(new LineSegment(new System.Windows.Point(1, 1 - r), true));
        fig.Segments.Add(new ArcSegment(new System.Windows.Point(1 - r, 1), new System.Windows.Size(r, r), 0, false, SweepDirection.Clockwise, true));
        fig.Segments.Add(new LineSegment(new System.Windows.Point(r, 1), true));
        fig.Segments.Add(new ArcSegment(new System.Windows.Point(0, 1 - r), new System.Windows.Size(r, r), 0, false, SweepDirection.Clockwise, true));
        fig.Segments.Add(new LineSegment(new System.Windows.Point(0, r), true));
        fig.Segments.Add(new ArcSegment(new System.Windows.Point(r, 0), new System.Windows.Size(r, r), 0, false, SweepDirection.Clockwise, true));
        var pg = new PathGeometry(new[] { fig });
        pg.Freeze();
        return pg;
    }

    private void DropZone_PreviewDragOver(object sender, DragEventArgs e)
    {
        var hasFiles = e.Data.GetDataPresent(DataFormats.FileDrop);
        e.Effects = hasFiles ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
        if (sender is not Grid grid || grid.Children.Count < 2) return;
        var app = Application.Current;
        var idleBorder = app?.Resources["DropZoneIdleBorder"] as SolidColorBrush ?? new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x60, 0x60, 0x60));
        var idleBackground = app?.Resources["DropZoneIdleBackground"] as SolidColorBrush ?? new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2D, 0x2D, 0x2D));
        var dragOverBorder = app?.Resources["DropZoneDragOverBorder"] as SolidColorBrush ?? new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x00, 0x78, 0xD4));
        var dragOverBackground = app?.Resources["DropZoneDragOverBackground"] as SolidColorBrush ?? new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1A, 0x3A, 0x52));
        if (grid.Children[0] is Border fillBorder)
            fillBorder.Background = hasFiles ? dragOverBackground : idleBackground;
        if (grid.Children[1] is Path path)
            path.Stroke = hasFiles ? dragOverBorder : idleBorder;
    }

    private void DropZone_DragLeave(object sender, DragEventArgs e)
    {
        RestoreDropZoneIdleLook(sender);
    }

    private static void RestoreDropZoneIdleLook(object? sender)
    {
        if (sender is not Grid grid || grid.Children.Count < 2) return;
        var app = Application.Current;
        var idleBorder = app?.Resources["DropZoneIdleBorder"] as SolidColorBrush ?? new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x60, 0x60, 0x60));
        var idleBackground = app?.Resources["DropZoneIdleBackground"] as SolidColorBrush ?? new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2D, 0x2D, 0x2D));
        if (grid.Children[0] is Border fillBorder)
            fillBorder.Background = idleBackground;
        if (grid.Children[1] is Path path)
            path.Stroke = idleBorder;
    }

    private void DropZone_PreviewDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var files = (string[]?)e.Data.GetData(DataFormats.FileDrop);
        if (files == null || files.Length == 0) return;
        var tag = (sender as Grid)?.Tag;
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
