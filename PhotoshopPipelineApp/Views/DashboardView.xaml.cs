using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
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
    private const string PendingDropItemFormat = "PendingDropItem";

    private PipelineService? _pipeline;
    private ConfigService? _configService;
    private readonly ObservableCollection<PendingDropItem> _pendingDrops = new();
    private readonly List<PendingDropItem> _readyQueue = new();
    private System.Windows.Threading.DispatcherTimer? _statusRefreshTimer;

    private System.Windows.Point? _pendingDragStart;

    private System.Windows.Controls.Border? _currentDropTarget;

    private string? _openAIOverlayImagePath;
    private string? _openAIOverlayQueueName;
    private ProcessedJobRecord? _openAIOverlaySourceRecord;

    public DashboardView()
    {
        InitializeComponent();
        PendingDropsItems.ItemsSource = _pendingDrops;
        UpdateDropPlaceholderVisibility();
    }

    private void UpdateDropPlaceholderVisibility()
    {
        DropPlaceholder.Visibility = _pendingDrops.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public void SetPipeline(PipelineService pipeline)
    {
        _pipeline = pipeline;
        _pipeline.QueueStatusChanged += Pipeline_QueueStatusChanged;
        _pipeline.LogMessage += Pipeline_LogMessage;
        ProcessedHistoryList.ItemsSource = _pipeline.ProcessedHistory;
        UpdateStatus();
        StartStatusRefreshTimer();
    }

    private void StartStatusRefreshTimer()
    {
        if (_statusRefreshTimer != null) return;
        _statusRefreshTimer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1.5)
        };
        _statusRefreshTimer.Tick += (_, _) =>
        {
            if (_pipeline != null)
                UpdateStatus();
        };
        _statusRefreshTimer.Start();
    }

    public void SetConfigService(ConfigService configService)
    {
        _configService = configService;
    }

    private static readonly string[] AllowedImageExtensions = { "*.jpg", "*.jpeg", "*.png", "*.psd" };

    private void SingleDropZone_PreviewDragOver(object sender, DragEventArgs e)
    {
        // If this is an internal queue reorder drag, let it pass through to QueueItemsPanel
        if (e.Data.GetDataPresent(PendingDropItemFormat))
        {
            return;  // Don't handle - let event tunnel to child handlers
        }

        var hasFiles = e.Data.GetDataPresent(DataFormats.FileDrop);
        e.Effects = hasFiles ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
        if (sender is not Border fillBorder) return;
        var app = Application.Current;
        var idleBackground = app?.Resources["DropZoneIdleBackground"] as SolidColorBrush ?? new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2D, 0x2D, 0x2D));
        var dragOverBackground = app?.Resources["DropZoneDragOverBackground"] as SolidColorBrush ?? new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1A, 0x3A, 0x52));
        var idleBorder = app?.Resources["DropZoneIdleBorder"] as SolidColorBrush ?? new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x60, 0x60, 0x60));
        var dragOverBorder = app?.Resources["DropZoneDragOverBorder"] as SolidColorBrush ?? new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x00, 0x78, 0xD4));
        fillBorder.Background = hasFiles ? dragOverBackground : idleBackground;
        fillBorder.BorderBrush = hasFiles ? dragOverBorder : idleBorder;
        fillBorder.BorderThickness = hasFiles ? new Thickness(2) : new Thickness(0);
    }

    private void SingleDropZone_DragLeave(object sender, DragEventArgs e)
    {
        RestoreSingleDropZoneIdleLook(sender);
    }

    private void SingleDropZone_PreviewDrop(object sender, DragEventArgs e)
    {
        // If this is an internal queue reorder drag, let it pass through
        if (e.Data.GetDataPresent(PendingDropItemFormat)) return;
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var files = (string[]?)e.Data.GetData(DataFormats.FileDrop);
        if (files == null || files.Length == 0) return;
        foreach (var src in files)
        {
            if (!File.Exists(src)) continue;
            var fileName = Path.GetFileName(src);
            if (string.IsNullOrEmpty(fileName)) continue;
            var matches = AllowedImageExtensions.Any(ext =>
            {
                if (string.Equals(ext, "*.*", StringComparison.OrdinalIgnoreCase)) return true;
                if (!ext.StartsWith("*.")) return false;
                return fileName.EndsWith(ext[1..], StringComparison.OrdinalIgnoreCase);
            });
            if (!matches) continue;
            _pendingDrops.Add(new PendingDropItem { SourceFilePath = src });
        }
        UpdateDropPlaceholderVisibility();
        RestoreSingleDropZoneIdleLook(sender);
        e.Handled = true;
    }

    private static void RestoreSingleDropZoneIdleLook(object? sender)
    {
        if (sender is not Border fillBorder) return;
        var app = Application.Current;
        var idleBorder = app?.Resources["DropZoneIdleBorder"] as SolidColorBrush ?? new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x60, 0x60, 0x60));
        var idleBackground = app?.Resources["DropZoneIdleBackground"] as SolidColorBrush ?? new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2D, 0x2D, 0x2D));
        fillBorder.Background = idleBackground;
        fillBorder.BorderBrush = idleBorder;
        fillBorder.BorderThickness = new Thickness(0);
    }

    private void ProcessedDetailImageBorder_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is not Border border || e.NewSize.Width <= 0 || e.NewSize.Height <= 0) return;
        border.Clip = new RectangleGeometry(
            new Rect(0, 0, e.NewSize.Width, e.NewSize.Height), 6, 6);
    }

    private void PendingItemImageBorder_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is not Border border || e.NewSize.Width <= 0 || e.NewSize.Height <= 0) return;
        border.Clip = new RectangleGeometry(
            new Rect(0, 0, e.NewSize.Width, e.NewSize.Height), 4, 4);
    }

    private static bool IsInteractiveControl(DependencyObject? element)
    {
        while (element != null)
        {
            if (element is System.Windows.Controls.Button or System.Windows.Controls.CheckBox) return true;
            element = System.Windows.Media.VisualTreeHelper.GetParent(element);
        }
        return false;
    }

    private void PendingItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (IsInteractiveControl(e.OriginalSource as DependencyObject)) return;
        var border = sender as Border;
        if (border?.DataContext is not PendingDropItem) return;
        _pendingDragStart = e.GetPosition(border);
        (border as UIElement)?.CaptureMouse();
    }

    private void PendingItem_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _pendingDragStart == null) return;
        if (IsInteractiveControl(e.OriginalSource as DependencyObject)) return;
        var border = sender as Border;
        if (border?.DataContext is not PendingDropItem item) return;
        var pos = e.GetPosition(border);
        var dx = pos.X - _pendingDragStart.Value.X;
        var dy = pos.Y - _pendingDragStart.Value.Y;
        if (Math.Abs(dx) < 5 && Math.Abs(dy) < 5) return;
        _pendingDragStart = null;
        (border as UIElement)?.ReleaseMouseCapture();
        var data = new System.Windows.DataObject(PendingDropItemFormat, item);
        DragDrop.DoDragDrop(border, data, DragDropEffects.Move);
    }

    private void PendingItem_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var border = sender as Border;
        (border as UIElement)?.ReleaseMouseCapture();
        _pendingDragStart = null;
    }

    /// <summary>Computes insert index and indicator Y from mouse position. Uses item container positions for reliable, forgiving hit zones.</summary>
    private (int insertIndex, double indicatorY) GetInsertIndexAndIndicatorY(System.Windows.Point pos)
    {
        var mouseY = pos.Y;
        var gen = PendingDropsItems.ItemContainerGenerator;
        if (_pendingDrops.Count == 0)
            return (0, 0);

        for (var i = 0; i < _pendingDrops.Count; i++)
        {
            var container = gen.ContainerFromIndex(i) as FrameworkElement;
            if (container == null) continue;
            var transform = container.TransformToAncestor(QueueItemsPanel);
            var top = transform.Transform(new System.Windows.Point(0, 0)).Y;
            var center = top + container.ActualHeight / 2;
            if (mouseY < center)
                return (i, top);
            if (i == _pendingDrops.Count - 1)
                return (i + 1, top + container.ActualHeight);
        }
        return (_pendingDrops.Count, 0);
    }

    private void HideDropIndicator()
    {
        DropIndicatorLine.Visibility = Visibility.Collapsed;
    }

    private void ShowDropIndicator(int insertIndex, double indicatorY)
    {
        var w = QueueItemsPanel.ActualWidth;
        if (w <= 0) w = DropIndicatorCanvas.ActualWidth;
        if (w <= 0) w = 400;
        DropIndicatorLine.Width = w;
        System.Windows.Controls.Canvas.SetTop(DropIndicatorLine, indicatorY);
        DropIndicatorLine.Visibility = Visibility.Visible;
    }

    private void QueuePanel_PreviewDragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        e.Effects = DragDropEffects.None;

        if (!e.Data.GetDataPresent(PendingDropItemFormat))
        {
            RestoreDropTargetHighlight();
            HideDropIndicator();
            return;
        }

        var dragged = e.Data.GetData(PendingDropItemFormat) as PendingDropItem;
        if (dragged == null || !_pendingDrops.Contains(dragged))
        {
            RestoreDropTargetHighlight();
            HideDropIndicator();
            return;
        }

        var pos = e.GetPosition(QueueItemsPanel);
        var (insertIndex, indicatorY) = GetInsertIndexAndIndicatorY(pos);

        // Allow drop anywhere in the queue area (forgiving - no need to hit a specific card)
        e.Effects = DragDropEffects.Move;

        var oldIndex = _pendingDrops.IndexOf(dragged);
        var effectiveIndex = insertIndex > oldIndex ? insertIndex - 1 : insertIndex;
        if (effectiveIndex == oldIndex)
        {
            HideDropIndicator();
            return;
        }

        ShowDropIndicator(insertIndex, indicatorY);
    }

    private void QueuePanel_PreviewDragLeave(object sender, DragEventArgs e)
    {
        RestoreDropTargetHighlight();
        HideDropIndicator();
    }

    private void RestoreDropTargetHighlight()
    {
        if (_currentDropTarget == null) return;
        var cardBrush = Application.Current?.FindResource("CardBorderBrush") as SolidColorBrush;
        if (cardBrush != null)
        {
            _currentDropTarget.BorderBrush = cardBrush;
            _currentDropTarget.BorderThickness = new Thickness(1);
        }
        _currentDropTarget = null;
    }

    private void QueuePanel_PreviewDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(PendingDropItemFormat)) return;
        var dragged = e.Data.GetData(PendingDropItemFormat) as PendingDropItem;
        if (dragged == null || !_pendingDrops.Contains(dragged))
        {
            HideDropIndicator();
            e.Handled = true;
            return;
        }

        var pos = e.GetPosition(QueueItemsPanel);
        var (insertIndex, _) = GetInsertIndexAndIndicatorY(pos);

        var oldIndex = _pendingDrops.IndexOf(dragged);
        var newIndex = insertIndex > oldIndex ? insertIndex - 1 : insertIndex;

        if (oldIndex >= 0 && newIndex >= 0 && oldIndex != newIndex && newIndex <= _pendingDrops.Count)
        {
            _pendingDrops.Move(oldIndex, newIndex);
            SyncReadyQueueFromPendingOrder();
        }

        RestoreDropTargetHighlight();
        HideDropIndicator();
        e.Handled = true;
    }

    private void SyncReadyQueueFromPendingOrder()
    {
        _readyQueue.Clear();
        foreach (var p in _pendingDrops.Where(x => x.IsReadyToProcess))
            _readyQueue.Add(p);
    }

    private void ProcessingImageBorder_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is not Border border || e.NewSize.Width <= 0 || e.NewSize.Height <= 0) return;
        border.Clip = new RectangleGeometry(
            new Rect(0, 0, e.NewSize.Width, e.NewSize.Height), 4, 4);
    }

    private void PendingProcess_Click(object sender, RoutedEventArgs e)
    {
        var pending = (sender as FrameworkElement)?.DataContext as PendingDropItem;
        if (pending == null || string.IsNullOrEmpty(pending.SourceFilePath) || !File.Exists(pending.SourceFilePath)) return;
        if (!pending.CanQueue) return;
        if (_readyQueue.Contains(pending)) return;
        if (_configService == null) return;
        var config = _configService.Load();
        var queues = config.Queues ?? new List<QueueConfig>();
        var queueIndex = pending.IsDefault ? 0 : 1;
        if (queueIndex >= queues.Count)
        {
            _pipeline?.Log("Configure at least two queues (Default and Right Justified) in Settings.");
            return;
        }
        var queue = queues[queueIndex];
        var queueName = string.IsNullOrWhiteSpace(queue.Name) ? (queueIndex == 0 ? "Default" : "Right Justified") : queue.Name;
        var watchPath = queue.WatchFolderPath ?? "";
        if (string.IsNullOrWhiteSpace(watchPath))
        {
            _pipeline?.Log($"[{queueName}] Watch folder not set. Configure it in Settings.");
            return;
        }
        _readyQueue.Add(pending);
        pending.IsReadyToProcess = true;
        MaybeStartNextReadyItem();
    }

    private void MaybeStartNextReadyItem()
    {
        if (_pipeline == null || !_pipeline.IsRunning) return;
        if (_readyQueue.Count == 0) return;
        var currentBusy = _pipeline.QueueStates.Count > 0
            ? _pipeline.QueueStates.FirstOrDefault(q => q.Status != PipelineStatus.Idle && q.Status != PipelineStatus.Watching)
            : null;
        if (currentBusy != null) return;
        if (_configService == null) return;
        var pending = _readyQueue[0];
        _readyQueue.RemoveAt(0);
        pending.IsReadyToProcess = false;
        var config = _configService.Load();
        var queues = config.Queues ?? new List<QueueConfig>();
        var queueIndex = pending.IsDefault ? 0 : 1;
        if (queueIndex >= queues.Count) return;
        var queue = queues[queueIndex];
        var queueName = string.IsNullOrWhiteSpace(queue.Name) ? (queueIndex == 0 ? "Default" : "Right Justified") : queue.Name;
        var watchPath = queue.WatchFolderPath ?? "";
        if (string.IsNullOrWhiteSpace(watchPath)) return;
        try { Directory.CreateDirectory(watchPath); }
        catch (Exception ex) { _pipeline?.Log($"[{queueName}] Cannot create watch folder: {ex.Message}"); return; }
        var fileName = Path.GetFileName(pending.SourceFilePath);
        var dest = Path.Combine(watchPath, fileName);
        try
        {
            File.Copy(pending.SourceFilePath, dest, overwrite: true);
            _pipeline?.Log($"[{queueName}] Queued: {fileName}");
            _pendingDrops.Remove(pending);
            UpdateDropPlaceholderVisibility();
        }
        catch (Exception ex)
        {
            _pipeline?.Log($"[{queueName}] Failed to copy {fileName}: {ex.Message}");
            pending.IsReadyToProcess = true;
            _readyQueue.Insert(0, pending);
        }
    }

    private void Pipeline_QueueStatusChanged(object? sender, QueueStatusEventArgs e)
    {
        var dispatcher = Dispatcher ?? System.Windows.Application.Current.Dispatcher;
        dispatcher.BeginInvoke(DispatcherPriority.Normal, (Action)UpdateStatus);
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
        if (_pipeline.IsRunning && _pipeline.QueueStates.Count > 0)
            QueueStatusItems.ItemsSource = _pipeline.QueueStates;
        else
            QueueStatusItems.ItemsSource = null;

        var currentBusy = _pipeline.QueueStates.Count > 0
            ? _pipeline.QueueStates.FirstOrDefault(q => q.Status != PipelineStatus.Idle && q.Status != PipelineStatus.Watching)
            : null;
        var isProcessing = currentBusy != null;
        if (currentBusy != null)
        {
            var pathConverter = new PathToImageSourceConverter();
            ProcessingImage.Source = pathConverter.Convert(currentBusy.LastProcessedFile, typeof(ImageSource), string.Empty, CultureInfo.CurrentUICulture) as ImageSource;
            ProcessingQueueName.Text = currentBusy.QueueName;
            ProcessingFileName.Text = currentBusy.LastFileDisplay;
            ProcessingStepIndicators.ItemsSource = currentBusy.CurrentStepStates;
            ProcessingIdleText.Visibility = Visibility.Collapsed;
            ProcessingContentPanel.Visibility = Visibility.Visible;
            var isVerifying = currentBusy.Status == PipelineStatus.Verifying;
            ProcessingVerifyHint.Visibility = isVerifying ? Visibility.Visible : Visibility.Collapsed;
            ProcessingStatusLabel.Text = isVerifying ? "Verifying…" : "Processing…";
            var preCompleted = currentBusy.CurrentStepStates.Count > 0 &&
                currentBusy.CurrentStepStates[0].Label == "Pre" &&
                currentBusy.CurrentStepStates[0].Status == StepStatus.Completed;
            ProcessingOpenAIButton.Visibility = preCompleted ? Visibility.Visible : Visibility.Collapsed;
        }
        else
        {
            ProcessingImage.Source = null;
            ProcessingQueueName.Text = "—";
            ProcessingFileName.Text = "—";
            ProcessingStepIndicators.ItemsSource = null;
            ProcessingIdleText.Visibility = Visibility.Visible;
            ProcessingContentPanel.Visibility = Visibility.Collapsed;
            ProcessingVerifyHint.Visibility = Visibility.Collapsed;
            ProcessingStatusLabel.Text = "Processing…";
            ProcessingOpenAIButton.Visibility = Visibility.Collapsed;
        }
        MaybeStartNextReadyItem();
    }

    private void ViewOpenAIResponseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_pipeline == null) return;
        var meta = _pipeline.LastOpenAIResponse;
        var queueName = _pipeline.LastOpenAIResponseQueueName ?? "";
        var imagePath = _pipeline.LastOpenAIResponseImagePath;
        if (meta == null)
        {
            ShowOpenAIOverlay(null, queueName, "Last OpenAI response", imagePath, null);
            return;
        }
        ShowOpenAIOverlay(meta, queueName, "Last OpenAI response", imagePath, null);
    }

    private void ProcessingViewOpenAI_Click(object sender, RoutedEventArgs e)
    {
        if (_pipeline == null) return;
        var currentBusy = _pipeline.QueueStates.Count > 0
            ? _pipeline.QueueStates.FirstOrDefault(q => q.Status != PipelineStatus.Idle && q.Status != PipelineStatus.Watching)
            : null;
        if (currentBusy == null) return;
        var meta = currentBusy.LastOpenAIMetadata;
        var queueName = currentBusy.QueueName ?? "";
        var imagePath = currentBusy.LastProcessedFile;
        ShowOpenAIOverlay(meta, queueName, "Current job — OpenAI response", imagePath, null);
    }

    private void ShowOpenAIOverlay(OpenAIMetadata? meta, string queueName, string overlayTitle, string? imagePath = null, ProcessedJobRecord? sourceRecord = null)
    {
        _openAIOverlayImagePath = imagePath;
        _openAIOverlayQueueName = queueName;
        _openAIOverlaySourceRecord = sourceRecord;

        OpenAIOverlayTitle.Text = overlayTitle;
        if (meta == null)
        {
            OpenAIOverlayContent.Text = string.IsNullOrEmpty(queueName)
                ? "No OpenAI response yet. Run a job with OpenAI pre-step to see the result."
                : "No image processed yet for this queue, or this queue does not use OpenAI pre-step.";
        }
        else
        {
            SetOpenAIOverlayContentFromMeta(meta, queueName);
        }
        OpenAIOverlayContent.Opacity = 0.95;
        OpenAIOverlayProgressBar.Visibility = Visibility.Collapsed;
        OpenAIOverlayRedoButton.Visibility = meta != null && !string.IsNullOrEmpty(imagePath) ? Visibility.Visible : Visibility.Collapsed;
        OpenAIOverlayRedoButton.IsEnabled = true;
        OpenAIOverlay.Visibility = Visibility.Visible;
    }

    private void SetOpenAIOverlayContentFromMeta(OpenAIMetadata meta, string queueName)
    {
        var sb = new System.Text.StringBuilder();
        if (!string.IsNullOrEmpty(queueName))
            sb.AppendLine("Queue: " + queueName);
        sb.AppendLine();
        sb.AppendLine("Title: " + (meta.Title ?? "(empty)"));
        sb.AppendLine();
        sb.AppendLine("Description: " + (string.IsNullOrEmpty(meta.Description) ? "(empty)" : meta.Description));
        sb.AppendLine();
        sb.Append("Tags: " + (meta.Tags != null && meta.Tags.Count > 0 ? string.Join(", ", meta.Tags) : "(none)"));
        OpenAIOverlayContent.Text = sb.ToString();
    }

    private void CloseOpenAIOverlay()
    {
        OpenAIOverlay.Visibility = Visibility.Collapsed;
    }

    private void OpenAIOverlayBackdrop_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            CloseOpenAIOverlay();
    }

    private void OpenAIOverlayClose_Click(object sender, RoutedEventArgs e)
    {
        CloseOpenAIOverlay();
    }

    private async void OpenAIOverlayRedo_Click(object sender, RoutedEventArgs e)
    {
        if (_pipeline == null || string.IsNullOrEmpty(_openAIOverlayImagePath) || string.IsNullOrEmpty(_openAIOverlayQueueName))
            return;
        OpenAIOverlayRedoButton.IsEnabled = false;
        OpenAIOverlayContent.Opacity = 0.4;
        OpenAIOverlayProgressBar.Visibility = Visibility.Visible;
        var imagePath = _openAIOverlayImagePath;
        var queueName = _openAIOverlayQueueName;
        var sourceRecord = _openAIOverlaySourceRecord;
        OpenAIMetadata? newMeta = null;
        try
        {
            newMeta = await _pipeline.ReprocessOpenAIAsync(imagePath, queueName).ConfigureAwait(false);
        }
        catch
        {
            // ReprocessOpenAIAsync catches and logs; returns null
        }
        await Dispatcher.InvokeAsync(() =>
        {
            OpenAIOverlayProgressBar.Visibility = Visibility.Collapsed;
            OpenAIOverlayContent.Opacity = 0.95;
            OpenAIOverlayRedoButton.IsEnabled = true;
            if (newMeta != null)
            {
                SetOpenAIOverlayContentFromMeta(newMeta, queueName);
                if (sourceRecord != null)
                    sourceRecord.OpenAIMetadata = newMeta;
            }
            else
            {
                OpenAIOverlayContent.Text = "Reprocess failed. Check log and API key.";
            }
        });
    }

    private void ProcessedHistoryList_SelectionChanged(object sender, SelectionChangedEventArgs e) { }

    private void ProcessedHistoryList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ProcessedHistoryList.SelectedItem is not ProcessedJobRecord record) return;
        ShowProcessedDetail(record);
    }

    private void ShowProcessedDetail(ProcessedJobRecord r)
    {
        ProcessedDetailCard.DataContext = r;
        ProcessedDetailTitle.Text = r.FileNameDisplay;
        var sb = new System.Text.StringBuilder();
        if (r.TimedOut)
            sb.AppendLine("This job did not complete; it timed out.");
        sb.AppendLine("Completed: " + r.CompletedAt.ToString("f"));
        sb.AppendLine();
        sb.AppendLine("Steps:");
        sb.AppendLine("  Pre:    " + FormatStepStatus(r.PreStepStatus));
        sb.AppendLine("  PS:     " + FormatStepStatus(r.PhotoshopStatus));
        sb.AppendLine("  Verify: " + FormatStepStatus(r.VerifyStatus));
        sb.AppendLine("  Post:   " + FormatStepStatus(r.PostStepStatus));
        sb.AppendLine();
        sb.AppendLine("Source (watch folder):");
        sb.AppendLine("  " + (string.IsNullOrEmpty(r.WatchFolderPath) ? "(none)" : r.WatchFolderPath));
        sb.AppendLine("Input file:");
        sb.AppendLine("  " + (string.IsNullOrEmpty(r.InputFilePath) ? "(none)" : r.InputFilePath));
        sb.AppendLine();
        sb.AppendLine("Output folder:");
        sb.AppendLine("  " + (string.IsNullOrEmpty(r.OutputFolderPath) ? "(none)" : r.OutputFolderPath));
        sb.AppendLine();
        sb.AppendLine("Log:");
        sb.AppendLine(string.IsNullOrEmpty(r.LogText) ? "(no log)" : r.LogText);
        ProcessedDetailContent.Text = sb.ToString();
        ProcessedDetailViewOpenAIButton.Visibility = r.OpenAIMetadata != null ? Visibility.Visible : Visibility.Collapsed;
        ProcessedDetailOverlay.Visibility = Visibility.Visible;
    }

    private void ProcessedDetailViewOpenAI_Click(object sender, RoutedEventArgs e)
    {
        if (ProcessedDetailCard.DataContext is not ProcessedJobRecord record) return;
        var meta = record.OpenAIMetadata;
        var queueName = record.QueueName ?? "";
        var title = "OpenAI response — " + record.FileNameDisplay;
        ShowOpenAIOverlay(meta, queueName, title, record.InputFilePath, record);
    }

    private static string FormatStepStatus(StepStatus s)
    {
        return s switch
        {
            StepStatus.Completed => "Completed",
            StepStatus.Failed => "Failed",
            StepStatus.Pending => "Pending",
            StepStatus.NotApplicable => "N/A",
            _ => "—"
        };
    }

    private void ProcessedDetailOverlay_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            CloseProcessedDetailOverlay();
    }

    private void ProcessedDetailClose_Click(object sender, RoutedEventArgs e)
    {
        CloseProcessedDetailOverlay();
    }

    private void CloseProcessedDetailOverlay()
    {
        ProcessedDetailOverlay.Visibility = Visibility.Collapsed;
        ProcessedDetailCard.DataContext = null;
    }
}
