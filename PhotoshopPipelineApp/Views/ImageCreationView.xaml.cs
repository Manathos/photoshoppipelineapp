using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using PhotoshopPipelineApp.Models;
using PhotoshopPipelineApp.Services;

namespace PhotoshopPipelineApp.Views;

public partial class ImageCreationView
{
    private const double MinZoom = 0.1;
    private const double MaxZoom = 5.0;
    private const double ZoomStep = 0.25;
    /// <summary>Wheel zoom sensitivity: scale factor per 120 delta (one notch).</summary>
    private const double ZoomWheelFactor = 0.08;

    private ConfigService? _configService;
    private ImageHistoryService? _imageHistoryService;
    private readonly ObservableCollection<GeneratedImageRecord> _imageHistory = new();
    private string? _displayedImagePath; // path of the image currently in main view, for delete handling
    private bool _isPanning;
    private System.Windows.Point _panLastPosition;
    private readonly OpenAIImageService _imageService = new();
    private readonly PromptRewriteService _promptRewriteService = new();

    // Crop state: non-destructive until finalize
    private byte[]? _originalImageBytes;
    private Int32Rect? _pendingCropRect;
    private bool _isCropMode;
    private double? _lockedAspectRatio; // width/height when set
    private Int32Rect _cropRectInProgress; // used during crop mode before OK
    private string _cropDragHandle = ""; // "", "move", "NW", "N", "NE", "E", "SE", "S", "SW", "W"
    private System.Windows.Point _cropDragStart;
    private Int32Rect _cropRectAtDragStart;

    public ImageCreationView()
    {
        InitializeComponent();
        ModelCombo.SelectedIndex = 0;
        PopulateSizeComboForModel("gpt-image-1.5");
        PopulateQualityComboForModel("gpt-image-1.5");
        StyleCombo.SelectedIndex = 0;
        CropRatioCombo.SelectedIndex = 0;
        UpdateImageActionButtons();
        HistoryListBox.ItemsSource = _imageHistory;
        Loaded += (_, _) => RefreshHistory();
    }

    public void SetConfigService(ConfigService configService)
    {
        _configService = configService;
    }

    public void SetImageHistoryService(ImageHistoryService imageHistoryService)
    {
        _imageHistoryService = imageHistoryService;
        RefreshHistory();
    }

    private void RefreshHistory()
    {
        if (_imageHistoryService == null) return;
        _imageHistory.Clear();
        foreach (var record in _imageHistoryService.LoadHistory())
            _imageHistory.Add(record);
    }

    private void PopulateSizeComboForModel(string model)
    {
        var currentTag = (SizeCombo.SelectedItem as ComboBoxItem)?.Tag as string;
        SizeCombo.Items.Clear();
        if (model == "dall-e-2")
        {
            SizeCombo.Items.Add(CreateSizeItem("256×256", "256x256"));
            SizeCombo.Items.Add(CreateSizeItem("512×512", "512x512"));
            SizeCombo.Items.Add(CreateSizeItem("1024×1024", "1024x1024"));
            var tags = new[] { "256x256", "512x512", "1024x1024" };
            var idx = Array.IndexOf(tags, currentTag);
            SizeCombo.SelectedIndex = idx >= 0 ? idx : 2;
        }
        else if (model == "gpt-image-1.5")
        {
            SizeCombo.Items.Add(CreateSizeItem("1024×1024", "1024x1024"));
            SizeCombo.Items.Add(CreateSizeItem("1536×1024 (landscape)", "1536x1024"));
            SizeCombo.Items.Add(CreateSizeItem("Ultra-wide cinematic panoramic (1536×1024)", "gpt-ultra-wide"));
            SizeCombo.Items.Add(CreateSizeItem("1024×1536 (portrait)", "1024x1536"));
            var tags = new[] { "1024x1024", "1536x1024", "gpt-ultra-wide", "1024x1536" };
            var idx = Array.IndexOf(tags, currentTag);
            SizeCombo.SelectedIndex = idx >= 0 ? idx : 0;
        }
        else
        {
            SizeCombo.Items.Add(CreateSizeItem("1024×1024", "1024x1024"));
            SizeCombo.Items.Add(CreateSizeItem("1792×1024 (landscape)", "1792x1024"));
            SizeCombo.Items.Add(CreateSizeItem("Ultra-wide cinematic panoramic (1792×1024)", "ultra-wide"));
            SizeCombo.Items.Add(CreateSizeItem("1024×1792 (portrait)", "1024x1792"));
            var tags = new[] { "1024x1024", "1792x1024", "ultra-wide", "1024x1792" };
            var idx = Array.IndexOf(tags, currentTag);
            SizeCombo.SelectedIndex = idx >= 0 ? idx : 0;
        }
    }

    private void PopulateQualityComboForModel(string model)
    {
        var currentTag = (QualityCombo.SelectedItem as ComboBoxItem)?.Tag as string;
        QualityCombo.Items.Clear();
        if (model == "gpt-image-1.5")
        {
            QualityCombo.Items.Add(new ComboBoxItem { Content = "Low (faster, cheaper)", Tag = "low" });
            QualityCombo.Items.Add(new ComboBoxItem { Content = "Medium", Tag = "medium" });
            QualityCombo.Items.Add(new ComboBoxItem { Content = "High (best quality)", Tag = "high" });
            var tags = new[] { "low", "medium", "high" };
            var idx = Array.IndexOf(tags, currentTag);
            QualityCombo.SelectedIndex = idx >= 0 ? idx : 1;
        }
        else if (model == "dall-e-3")
        {
            QualityCombo.Items.Add(new ComboBoxItem { Content = "Standard (faster)", Tag = "standard" });
            QualityCombo.Items.Add(new ComboBoxItem { Content = "HD (higher quality)", Tag = "hd" });
            var tags = new[] { "standard", "hd" };
            var idx = Array.IndexOf(tags, currentTag);
            QualityCombo.SelectedIndex = idx >= 0 ? idx : 0;
        }
        else
        {
            QualityCombo.Items.Add(new ComboBoxItem { Content = "Standard", Tag = "standard" });
            QualityCombo.SelectedIndex = 0;
        }
    }

    private static (string size, string effectivePrompt) ResolveSizeAndPrompt(string sizeTag, string model, string prompt)
    {
        if (sizeTag == "ultra-wide")
            return ("1792x1024", "Ultra-wide cinematic panoramic scene, " + prompt);
        if (sizeTag == "gpt-ultra-wide")
            return ("1536x1024", "Ultra-wide cinematic panoramic scene, " + prompt);
        return (sizeTag, prompt);
    }

    private static ComboBoxItem CreateSizeItem(string content, string tag)
    {
        var item = new ComboBoxItem { Content = content, Tag = tag };
        return item;
    }

    private void ModelCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ModelCombo.SelectedItem is not ComboBoxItem modelItem) return;
        var model = modelItem.Tag as string;
        if (string.IsNullOrEmpty(model)) return;
        PopulateSizeComboForModel(model);
        PopulateQualityComboForModel(model);
    }

    private void ApplyZoom(double scale)
    {
        if (ImageScaleTransform == null) return;
        scale = Math.Clamp(scale, MinZoom, MaxZoom);
        ImageScaleTransform.ScaleX = scale;
        ImageScaleTransform.ScaleY = scale;
        ZoomLabel.Text = $"{Math.Round(scale * 100)}%";
        ZoomOutButton.IsEnabled = scale > MinZoom;
        ZoomInButton.IsEnabled = scale < MaxZoom;
    }

    private double CalculateFitZoom()
    {
        if (ResultImage.Source is not BitmapSource bitmap) return 1.0;
        
        // Get the available viewport size (ScrollViewer's actual size minus some padding)
        var viewportWidth = ImageScrollViewer.ActualWidth - 20;
        var viewportHeight = ImageScrollViewer.ActualHeight - 20;
        
        if (viewportWidth <= 0 || viewportHeight <= 0) return 1.0;
        
        var imageWidth = bitmap.PixelWidth;
        var imageHeight = bitmap.PixelHeight;
        
        if (imageWidth <= 0 || imageHeight <= 0) return 1.0;
        
        // Calculate scale to fit both dimensions
        var scaleX = viewportWidth / imageWidth;
        var scaleY = viewportHeight / imageHeight;
        
        // Use the smaller scale to ensure entire image fits
        return Math.Min(scaleX, scaleY);
    }

    private void FitToWindow()
    {
        var fitZoom = CalculateFitZoom();
        ApplyZoom(fitZoom);
    }

    private void FitButton_Click(object sender, RoutedEventArgs e)
    {
        FitToWindow();
    }

    private void ImageZoomBorder_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (ImageScaleTransform == null || ImageScrollViewer == null || _isCropMode) return;
        
        var oldScale = ImageScaleTransform.ScaleX;
        
        // Smooth proportional zoom: scale *= (1 + factor * delta/120)
        var factor = 1.0 + (e.Delta / 120.0) * ZoomWheelFactor;
        var newScale = Math.Clamp(oldScale * factor, MinZoom, MaxZoom);
        
        // Get cursor position in viewport coordinates
        var cursorPos = e.GetPosition(ImageScrollViewer);
        
        // Calculate the content point under the cursor before zoom
        var contentX = ImageScrollViewer.HorizontalOffset + cursorPos.X;
        var contentY = ImageScrollViewer.VerticalOffset + cursorPos.Y;
        
        // Apply the zoom
        ImageScaleTransform.ScaleX = newScale;
        ImageScaleTransform.ScaleY = newScale;
        ZoomLabel.Text = $"{Math.Round(newScale * 100)}%";
        ZoomOutButton.IsEnabled = newScale > MinZoom;
        ZoomInButton.IsEnabled = newScale < MaxZoom;
        
        // After zoom, adjust scroll so the same content point is under the cursor
        // The content point scales proportionally with the zoom
        var scaleRatio = newScale / oldScale;
        var newContentX = contentX * scaleRatio;
        var newContentY = contentY * scaleRatio;
        
        // Set new scroll offsets to keep cursor over the same content point
        ImageScrollViewer.ScrollToHorizontalOffset(newContentX - cursorPos.X);
        ImageScrollViewer.ScrollToVerticalOffset(newContentY - cursorPos.Y);
        
        e.Handled = true;
    }

    private void ZoomInButton_Click(object sender, RoutedEventArgs e)
    {
        ApplyZoom(ImageScaleTransform.ScaleX + ZoomStep);
    }

    private void ZoomOutButton_Click(object sender, RoutedEventArgs e)
    {
        ApplyZoom(ImageScaleTransform.ScaleX - ZoomStep);
    }

    private void ImageZoomBorder_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ImageScrollViewer == null || _isCropMode) return;
        _isPanning = true;
        _panLastPosition = e.GetPosition(ImageScrollViewer);
        ((Border)sender).CaptureMouse();
        ((Border)sender).Cursor = System.Windows.Input.Cursors.Hand;
        e.Handled = true;
    }

    private void ImageZoomBorder_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_isPanning || ImageScrollViewer == null) return;
        var pos = e.GetPosition(ImageScrollViewer);
        var deltaX = pos.X - _panLastPosition.X;
        var deltaY = pos.Y - _panLastPosition.Y;
        _panLastPosition = pos;
        var newH = Math.Clamp(ImageScrollViewer.HorizontalOffset - deltaX, 0, ImageScrollViewer.ScrollableWidth);
        var newV = Math.Clamp(ImageScrollViewer.VerticalOffset - deltaY, 0, ImageScrollViewer.ScrollableHeight);
        ImageScrollViewer.ScrollToHorizontalOffset(newH);
        ImageScrollViewer.ScrollToVerticalOffset(newV);
        e.Handled = true;
    }

    private void ImageZoomBorder_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isPanning) return;
        _isPanning = false;
        var border = (Border)sender;
        border.ReleaseMouseCapture();
        border.Cursor = System.Windows.Input.Cursors.Arrow;
        e.Handled = true;
    }

    private async void EnhancePromptButton_Click(object sender, RoutedEventArgs e)
    {
        var config = _configService?.Load();
        var apiKey = config?.OpenAIApiKey?.Trim() ?? "";
        var envKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(apiKey) && string.IsNullOrWhiteSpace(envKey))
        {
            ShowStatus("Set your OpenAI API key in Settings to use AI enhance.");
            return;
        }
        var key = !string.IsNullOrWhiteSpace(apiKey) ? apiKey : envKey;

        var prompt = PromptBox.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(prompt))
        {
            ShowStatus("Enter a prompt first, then click Enhance to improve it.");
            return;
        }

        EnhancePromptButton.IsEnabled = false;
        ShowStatus("Enhancing prompt…");

        try
        {
            var rewritten = await _promptRewriteService.RewritePromptAsync(key, prompt).ConfigureAwait(true);
            if (!string.IsNullOrWhiteSpace(rewritten))
            {
                PromptBox.Text = rewritten;
                ShowStatus("Prompt enhanced. You can edit it or generate.");
            }
            else
            {
                ShowStatus("Enhance failed. Check API key and network.");
            }
        }
        catch (Exception ex)
        {
            ShowStatus($"Enhance error: {ex.Message}");
        }
        finally
        {
            EnhancePromptButton.IsEnabled = true;
        }
    }

    private async void GenerateButton_Click(object sender, RoutedEventArgs e)
    {
        var config = _configService?.Load();
        var apiKey = config?.OpenAIApiKey?.Trim() ?? "";
        var envKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(apiKey) && string.IsNullOrWhiteSpace(envKey))
        {
            ShowStatus("Set your OpenAI API key in Settings first.");
            return;
        }
        var key = !string.IsNullOrWhiteSpace(apiKey) ? apiKey : envKey;

        var prompt = PromptBox.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(prompt))
        {
            ShowStatus("Enter a prompt to generate an image.");
            return;
        }

        var model = (ModelCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "gpt-image-1.5";
        var sizeTag = (SizeCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "1024x1024";
        var (size, effectivePrompt) = ResolveSizeAndPrompt(sizeTag, model, prompt);
        var quality = (QualityCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "standard";
        var style = (StyleCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "vivid";

        SetBusy(true);
        ShowStatus("");

        try
        {
            var bytes = await _imageService.GenerateImageAsync(key, model, effectivePrompt, size, quality, style).ConfigureAwait(true);

            SetBusy(false);
            if (bytes != null && bytes.Length > 0)
            {
                try
                {
                    _originalImageBytes = bytes;
                    _pendingCropRect = null;
                    _lockedAspectRatio = null;
                    ExitCropMode();
                    var bitmap = BytesToBitmapImage(bytes);
                    ResultImage.Source = bitmap;
                    ImageContainer.Visibility = Visibility.Visible;
                    PlaceholderText.Visibility = Visibility.Collapsed;
                    UpdateImageActionButtons();
                    // Wait for layout to update then fit to window
                    _ = Dispatcher.InvokeAsync(() => FitToWindow(), System.Windows.Threading.DispatcherPriority.Loaded);
                    var modelLabel = model switch { "gpt-image-1.5" => "GPT Image 1.5", "dall-e-3" => "DALL-E 3", _ => "DALL-E 2" };
                    ShowStatus($"Generated with {modelLabel}");

                    // Save to history
                    if (_imageHistoryService != null)
                    {
                        var record = await _imageHistoryService.SaveGeneratedImageAsync(bytes, prompt, model).ConfigureAwait(true);
                        await Dispatcher.InvokeAsync(() =>
                        {
                            _imageHistory.Insert(0, record);
                            _displayedImagePath = record.FilePath;
                        });
                    }
                    else
                    {
                        _displayedImagePath = null;
                    }
                }
                catch
                {
                    ShowStatus("Failed to display the image.");
                }
            }
            else
            {
                ShowStatus("Generation failed. Check your API key, prompt, and network.");
            }
        }
        catch (Exception ex)
        {
            SetBusy(false);
            ShowStatus($"Error: {ex.Message}");
        }
    }

    private void SetBusy(bool busy)
    {
        GenerateButton.IsEnabled = !busy;
        ProgressBar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowStatus(string message)
    {
        StatusText.Text = message;
        StatusText.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
    }

    private static BitmapImage BytesToBitmapImage(byte[] bytes)
    {
        var bi = new BitmapImage();
        using var ms = new MemoryStream(bytes);
        bi.BeginInit();
        bi.StreamSource = ms;
        bi.CacheOption = BitmapCacheOption.OnLoad;
        bi.EndInit();
        bi.Freeze();
        return bi;
    }

    private BitmapSource GetSourceBitmap()
    {
        if (_originalImageBytes == null || _originalImageBytes.Length == 0) return null!;
        return BytesToBitmapImage(_originalImageBytes);
    }

    /// <summary>Updates ResultImage.Source based on _pendingCropRect. Non-destructive: original bytes unchanged.</summary>
    private void UpdateDisplayFromCrop()
    {
        var source = GetSourceBitmap();
        if (source == null) return;
        if (_pendingCropRect is { } rect && rect.Width > 0 && rect.Height > 0)
        {
            var cropped = new CroppedBitmap(source, rect);
            cropped.Freeze();
            ResultImage.Source = cropped;
        }
        else
        {
            ResultImage.Source = source;
        }
        UpdateImageActionButtons();
    }

    /// <summary>Returns final image bytes (cropped if pending, else original) for export/queue.</summary>
    private byte[]? GetFinalImageBytes()
    {
        if (_originalImageBytes == null || _originalImageBytes.Length == 0) return null;
        var source = BytesToBitmapImage(_originalImageBytes);
        if (_pendingCropRect is { } rect && rect.Width > 0 && rect.Height > 0)
        {
            var cropped = new CroppedBitmap(source, rect);
            return EncodeBitmapToBytes(cropped);
        }
        return _originalImageBytes;
    }

    private static byte[] EncodeBitmapToBytes(BitmapSource bitmap)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    private bool HasImage => _originalImageBytes != null && _originalImageBytes.Length > 0;

    private void UpdateImageActionButtons()
    {
        var hasImage = HasImage;
        if (CropButton != null) CropButton.IsEnabled = hasImage;
        if (ClearCropButton != null) ClearCropButton.IsEnabled = hasImage && _pendingCropRect != null;
        if (ExportButton != null) ExportButton.IsEnabled = hasImage;
        if (SendToQueueButton != null) SendToQueueButton.IsEnabled = hasImage;
        if (CropToolbarPanel != null) CropToolbarPanel.Visibility = _isCropMode ? Visibility.Visible : Visibility.Collapsed;
        if (NormalToolbarPanel != null) NormalToolbarPanel.Visibility = _isCropMode ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ExitCropMode()
    {
        _isCropMode = false;
        _cropDragHandle = "";
        CropOverlayCanvas.Visibility = Visibility.Collapsed;
        CropOverlayCanvas.ReleaseMouseCapture();
        UpdateImageActionButtons();
        ImageZoomBorder.Cursor = System.Windows.Input.Cursors.Arrow;
    }

    private void CropButton_Click(object sender, RoutedEventArgs e)
    {
        if (!HasImage) return;
        EnterCropMode();
    }

    private void EnterCropMode()
    {
        var source = GetSourceBitmap() as BitmapSource;
        if (source == null) return;
        var w = source.PixelWidth;
        var h = source.PixelHeight;
        if (w <= 0 || h <= 0) return;

        _isCropMode = true;
        _lockedAspectRatio = null;
        CropRatioCombo.SelectedIndex = 0; // Free

        if (_pendingCropRect is { } existing && existing.Width > 0 && existing.Height > 0)
        {
            _cropRectInProgress = ClampCropRect(existing, w, h);
        }
        else
        {
            _cropRectInProgress = new Int32Rect(0, 0, w, h);
        }

        // Show full image (not cropped) so overlay coordinates match
        ResultImage.Source = source;
        CropOverlayCanvas.Visibility = Visibility.Visible;
        UpdateImageActionButtons();
        UpdateCropOverlayVisuals();
        // Defer overlay update until layout has run (ActualWidth/Height may be 0 until first visible)
        _ = Dispatcher.InvokeAsync(UpdateCropOverlayVisuals, System.Windows.Threading.DispatcherPriority.Loaded);
        ImageZoomBorder.Cursor = System.Windows.Input.Cursors.Arrow;
    }

    private void CropCancelButton_Click(object sender, RoutedEventArgs e)
    {
        ExitCropMode();
        UpdateDisplayFromCrop(); // Restore display (full or previous crop)
    }

    private void CropOkButton_Click(object sender, RoutedEventArgs e)
    {
        var source = GetSourceBitmap() as BitmapSource;
        if (source == null) { ExitCropMode(); return; }
        var w = source.PixelWidth;
        var h = source.PixelHeight;
        var rect = ClampCropRect(_cropRectInProgress, w, h);
        if (rect.Width > 0 && rect.Height > 0)
        {
            _pendingCropRect = rect;
            UpdateDisplayFromCrop();
        }
        ExitCropMode();
    }

    private void ClearCropButton_Click(object sender, RoutedEventArgs e) => ClearCrop();
    private void ClearCrop_Click(object sender, RoutedEventArgs e) => ClearCrop();

    private void ClearCrop()
    {
        _pendingCropRect = null;
        UpdateDisplayFromCrop();
        if (_isCropMode) ExitCropMode();
    }

    private void CropRatioCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CropRatioCombo.SelectedItem is not ComboBoxItem item) return;
        var tag = item.Tag?.ToString();
        _lockedAspectRatio = double.TryParse(tag, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var ratio) ? ratio : null;

        if (!_isCropMode && _lockedAspectRatio.HasValue && HasImage)
        {
            // Quick crop: apply centered crop with this ratio
            var source = GetSourceBitmap() as BitmapSource;
            if (source != null)
            {
                var w = source.PixelWidth;
                var h = source.PixelHeight;
                var rect = CenteredCropForRatio(w, h, _lockedAspectRatio.Value);
                if (rect.Width > 0 && rect.Height > 0)
                {
                    _pendingCropRect = rect;
                    UpdateDisplayFromCrop();
                }
            }
        }
        else if (_isCropMode && _cropRectInProgress.Width > 0 && _cropRectInProgress.Height > 0)
        {
            // Constrain current rect to aspect ratio
            _cropRectInProgress = ConstrainToAspectRatio(_cropRectInProgress, _lockedAspectRatio);
            UpdateCropOverlayVisuals();
        }
    }

    private static Int32Rect CenteredCropForRatio(int imgW, int imgH, double aspectRatio)
    {
        double cropW, cropH;
        if (imgW / (double)imgH >= aspectRatio)
        {
            cropH = imgH;
            cropW = imgH * aspectRatio;
        }
        else
        {
            cropW = imgW;
            cropH = imgW / aspectRatio;
        }
        var x = (int)Math.Round((imgW - cropW) / 2);
        var y = (int)Math.Round((imgH - cropH) / 2);
        var w = Math.Max(1, (int)Math.Round(cropW));
        var h = Math.Max(1, (int)Math.Round(cropH));
        x = Math.Clamp(x, 0, imgW - 1);
        y = Math.Clamp(y, 0, imgH - 1);
        w = Math.Min(w, imgW - x);
        h = Math.Min(h, imgH - y);
        return new Int32Rect(x, y, w, h);
    }

    private Int32Rect ConstrainToAspectRatio(Int32Rect rect, double? aspectRatio)
    {
        if (!aspectRatio.HasValue || aspectRatio.Value <= 0) return rect;
        var w = rect.Width;
        var h = rect.Height;
        double newW, newH;
        if (w / (double)h >= aspectRatio.Value)
        {
            newH = h;
            newW = h * aspectRatio.Value;
        }
        else
        {
            newW = w;
            newH = w / aspectRatio.Value;
        }
        var newRect = new Int32Rect(rect.X, rect.Y, Math.Max(1, (int)Math.Round(newW)), Math.Max(1, (int)Math.Round(newH)));
        var src = GetSourceBitmap() as BitmapSource;
        return ClampCropRect(newRect, src?.PixelWidth ?? 0, src?.PixelHeight ?? 0);
    }

    private static Int32Rect ClampCropRect(Int32Rect rect, int imgW, int imgH)
    {
        if (imgW <= 0 || imgH <= 0) return rect;
        var x = Math.Clamp(rect.X, 0, imgW - 1);
        var y = Math.Clamp(rect.Y, 0, imgH - 1);
        var w = Math.Clamp(rect.Width, 1, imgW - x);
        var h = Math.Clamp(rect.Height, 1, imgH - y);
        return new Int32Rect(x, y, w, h);
    }

    private double GetImageScale() => ImageScaleTransform?.ScaleX ?? 1.0;

    /// <summary>Gets the offset of the image within the overlay canvas (for centering when image is smaller than viewport).</summary>
    private (double offsetX, double offsetY) GetImageOffsetInCanvas()
    {
        if (CropOverlayCanvas == null || ResultImage?.Source is not BitmapSource bs) return (0, 0);
        var scale = GetImageScale();
        var imgRenderW = bs.PixelWidth * scale;
        var imgRenderH = bs.PixelHeight * scale;
        var canvasW = CropOverlayCanvas.ActualWidth;
        var canvasH = CropOverlayCanvas.ActualHeight;
        if (canvasW <= 0 || canvasH <= 0) return (0, 0);
        var offsetX = Math.Max(0, (canvasW - imgRenderW) / 2);
        var offsetY = Math.Max(0, (canvasH - imgRenderH) / 2);
        return (offsetX, offsetY);
    }

    private void UpdateCropOverlayVisuals()
    {
        if (CropOverlayCanvas == null || ResultImage?.Source is not BitmapSource bs) return;
        var scale = GetImageScale();
        var imgW = bs.PixelWidth;
        var imgH = bs.PixelHeight;
        var (offsetX, offsetY) = GetImageOffsetInCanvas();

        var r = _cropRectInProgress;
        var x = offsetX + r.X * scale;
        var y = offsetY + r.Y * scale;
        var w = r.Width * scale;
        var h = r.Height * scale;

        const double handleSize = 5;
        CropRectBorder.Width = w;
        CropRectBorder.Height = h;
        Canvas.SetLeft(CropRectBorder, x);
        Canvas.SetTop(CropRectBorder, y);

        void SetHandle(FrameworkElement el, double left, double top)
        {
            Canvas.SetLeft(el, left - handleSize);
            Canvas.SetTop(el, top - handleSize);
        }
        SetHandle(CropHandleNW, x, y);
        SetHandle(CropHandleN, x + w / 2, y);
        SetHandle(CropHandleNE, x + w, y);
        SetHandle(CropHandleE, x + w, y + h / 2);
        SetHandle(CropHandleSE, x + w, y + h);
        SetHandle(CropHandleS, x + w / 2, y + h);
        SetHandle(CropHandleSW, x, y + h);
        SetHandle(CropHandleW, x, y + h / 2);

        // Dimming: four rectangles for top, bottom, left, right (in canvas coords)
        CropDimmingCanvas.Children.Clear();
        var brush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(100, 0, 0, 0));
        brush.Freeze();
        void AddRect(double left, double top, double width, double height)
        {
            if (width <= 0 || height <= 0) return;
            var rect = new System.Windows.Shapes.Rectangle { Fill = brush, Width = width, Height = height };
            Canvas.SetLeft(rect, left);
            Canvas.SetTop(rect, top);
            CropDimmingCanvas.Children.Add(rect);
        }
        var imgRenderW = imgW * scale;
        var imgRenderH = imgH * scale;
        // Top dimming
        AddRect(offsetX, offsetY, imgRenderW, y - offsetY);
        // Bottom dimming
        AddRect(offsetX, y + h, imgRenderW, (offsetY + imgRenderH) - (y + h));
        // Left dimming
        AddRect(offsetX, y, x - offsetX, h);
        // Right dimming
        AddRect(x + w, y, (offsetX + imgRenderW) - (x + w), h);
    }

    private void CropOverlay_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_isCropMode) return;
        var pos = e.GetPosition(CropOverlayCanvas);
        var scale = GetImageScale();
        var (offsetX, offsetY) = GetImageOffsetInCanvas();
        var px = (int)((pos.X - offsetX) / scale);
        var py = (int)((pos.Y - offsetY) / scale);
        var r = _cropRectInProgress;
        const int handleHit = 12;

        bool InHandle(double hx, double hy, int tx, int ty)
        {
            var dx = hx - tx;
            var dy = hy - ty;
            return Math.Abs(dx) <= handleHit && Math.Abs(dy) <= handleHit;
        }

        var sx = r.X;
        var sy = r.Y;
        var ex = r.X + r.Width;
        var ey = r.Y + r.Height;

        if (InHandle(sx, sy, px, py)) _cropDragHandle = "NW";
        else if (InHandle((sx + ex) / 2.0, sy, px, py)) _cropDragHandle = "N";
        else if (InHandle(ex, sy, px, py)) _cropDragHandle = "NE";
        else if (InHandle(ex, (sy + ey) / 2.0, px, py)) _cropDragHandle = "E";
        else if (InHandle(ex, ey, px, py)) _cropDragHandle = "SE";
        else if (InHandle((sx + ex) / 2.0, ey, px, py)) _cropDragHandle = "S";
        else if (InHandle(sx, ey, px, py)) _cropDragHandle = "SW";
        else if (InHandle(sx, (sy + ey) / 2.0, px, py)) _cropDragHandle = "W";
        else if (px >= r.X && px < r.X + r.Width && py >= r.Y && py < r.Y + r.Height)
            _cropDragHandle = "move";
        else return;

        _cropDragStart = pos;
        _cropRectAtDragStart = r;
        CropOverlayCanvas.CaptureMouse();
        e.Handled = true;
    }

    private void CropOverlay_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_cropDragHandle == "" || !_isCropMode) return;
        var pos = e.GetPosition(CropOverlayCanvas);
        var scale = GetImageScale();
        var (offsetX, offsetY) = GetImageOffsetInCanvas();
        var dx = (int)((pos.X - _cropDragStart.X) / scale);
        var dy = (int)((pos.Y - _cropDragStart.Y) / scale);
        var r = _cropRectAtDragStart;
        var bs = GetSourceBitmap() as BitmapSource;
        var imgW = bs?.PixelWidth ?? 0;
        var imgH = bs?.PixelHeight ?? 0;

        int x1 = r.X, y1 = r.Y, x2 = r.X + r.Width, y2 = r.Y + r.Height;

        switch (_cropDragHandle)
        {
            case "move":
                x1 = Math.Clamp(r.X + dx, 0, imgW - (x2 - x1));
                y1 = Math.Clamp(r.Y + dy, 0, imgH - (y2 - y1));
                x2 = x1 + r.Width;
                y2 = y1 + r.Height;
                break;
            case "NW":
                x1 = Math.Clamp(r.X + dx, 0, x2 - 1);
                y1 = Math.Clamp(r.Y + dy, 0, y2 - 1);
                if (_lockedAspectRatio.HasValue)
                {
                    var aspect = _lockedAspectRatio.Value;
                    var w = x2 - x1;
                    var h = y2 - y1;
                    if (w / (double)h > aspect) x1 = (int)(x2 - (y2 - y1) * aspect);
                    else y1 = (int)(y2 - (x2 - x1) / aspect);
                }
                break;
            case "N":
                y1 = Math.Clamp(r.Y + dy, 0, y2 - 1);
                if (_lockedAspectRatio.HasValue) x1 = (int)(x2 - (y2 - y1) * _lockedAspectRatio.Value);
                break;
            case "NE":
                x2 = Math.Clamp(r.X + r.Width + dx, x1 + 1, imgW);
                y1 = Math.Clamp(r.Y + dy, 0, y2 - 1);
                if (_lockedAspectRatio.HasValue)
                {
                    var w = x2 - x1;
                    var h = y2 - y1;
                    if (w / (double)h > _lockedAspectRatio.Value) y1 = (int)(y2 - w / _lockedAspectRatio.Value);
                    else x2 = (int)(x1 + h * _lockedAspectRatio.Value);
                }
                break;
            case "E":
                x2 = Math.Clamp(r.X + r.Width + dx, x1 + 1, imgW);
                if (_lockedAspectRatio.HasValue) y2 = (int)(y1 + (x2 - x1) / _lockedAspectRatio.Value);
                break;
            case "SE":
                x2 = Math.Clamp(r.X + r.Width + dx, x1 + 1, imgW);
                y2 = Math.Clamp(r.Y + r.Height + dy, y1 + 1, imgH);
                if (_lockedAspectRatio.HasValue)
                {
                    var w = x2 - x1;
                    var h = y2 - y1;
                    if (w / (double)h > _lockedAspectRatio.Value) y2 = (int)(y1 + w / _lockedAspectRatio.Value);
                    else x2 = (int)(x1 + h * _lockedAspectRatio.Value);
                }
                break;
            case "S":
                y2 = Math.Clamp(r.Y + r.Height + dy, y1 + 1, imgH);
                if (_lockedAspectRatio.HasValue) x2 = (int)(x1 + (y2 - y1) * _lockedAspectRatio.Value);
                break;
            case "SW":
                x1 = Math.Clamp(r.X + dx, 0, x2 - 1);
                y2 = Math.Clamp(r.Y + r.Height + dy, y1 + 1, imgH);
                if (_lockedAspectRatio.HasValue)
                {
                    var w = x2 - x1;
                    var h = y2 - y1;
                    if (w / (double)h > _lockedAspectRatio.Value) x1 = (int)(x2 - h * _lockedAspectRatio.Value);
                    else y2 = (int)(y1 + w / _lockedAspectRatio.Value);
                }
                break;
            case "W":
                x1 = Math.Clamp(r.X + dx, 0, x2 - 1);
                if (_lockedAspectRatio.HasValue) y2 = (int)(y1 + (x2 - x1) / _lockedAspectRatio.Value);
                break;
        }

        _cropRectInProgress = ClampCropRect(new Int32Rect(x1, y1, x2 - x1, y2 - y1), imgW, imgH);
        UpdateCropOverlayVisuals();
        e.Handled = true;
    }

    private void CropOverlay_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_cropDragHandle != "")
        {
            _cropDragHandle = "";
            CropOverlayCanvas.ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    private void CropRect_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Let the overlay handler deal with move - this ensures Border clicks are routed
        e.Handled = false;
    }

    private void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        var bytes = GetFinalImageBytes();
        if (bytes == null || bytes.Length == 0) { ShowStatus("No image to export."); return; }

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "PNG image|*.png|JPEG image|*.jpg",
            DefaultExt = ".png",
            FileName = "image.png"
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            File.WriteAllBytes(dlg.FileName, bytes);
            ShowStatus($"Exported to {Path.GetFileName(dlg.FileName)}");
        }
        catch (Exception ex)
        {
            ShowStatus($"Export failed: {ex.Message}");
        }
    }

    private void SendToQueueButton_Click(object sender, RoutedEventArgs e)
    {
        var bytes = GetFinalImageBytes();
        if (bytes == null || bytes.Length == 0) { ShowStatus("No image to send."); return; }

        var config = _configService?.Load();
        var queues = config?.Queues ?? new List<QueueConfig>();
        var valid = queues.Where(q => !string.IsNullOrWhiteSpace(q.WatchFolderPath) && Directory.Exists(q.WatchFolderPath.Trim())).ToList();
        if (valid.Count == 0)
        {
            ShowStatus("No queues with valid watch folders. Configure queues in Settings.");
            return;
        }

        var dlg = new SendToQueueDialog(valid)
        {
            Owner = Window.GetWindow(this)
        };
        if (dlg.ShowDialog() != true) return;
        var queue = dlg.SelectedQueue;
        var fileName = dlg.FileName?.Trim();
        if (string.IsNullOrWhiteSpace(fileName)) fileName = "image_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png";
        if (!fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase) && !fileName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
            fileName += ".png";

        var folder = queue!.WatchFolderPath.Trim();
        var path = Path.Combine(folder, fileName);
        try
        {
            File.WriteAllBytes(path, bytes);
            ShowStatus($"Sent to {queue.Name}");
        }
        catch (Exception ex)
        {
            ShowStatus($"Send failed: {ex.Message}");
        }
    }

    private void HistoryListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (HistoryListBox.SelectedItem is not GeneratedImageRecord record) return;
        LoadHistoryItem(record);
    }

    private void HistoryListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (HistoryListBox.SelectedItem is not GeneratedImageRecord record) return;
        LoadHistoryItem(record);
    }

    private void LoadHistoryItem(GeneratedImageRecord record)
    {
        if (_imageHistoryService == null || string.IsNullOrEmpty(record.FilePath)) return;
        var bytes = _imageHistoryService.GetImageBytes(record.FilePath);
        if (bytes == null || bytes.Length == 0) return;

        _originalImageBytes = bytes;
        _pendingCropRect = null;
        _lockedAspectRatio = null;
        ExitCropMode();
        var bitmap = BytesToBitmapImage(bytes);
        ResultImage.Source = bitmap;
        ImageContainer.Visibility = Visibility.Visible;
        PlaceholderText.Visibility = Visibility.Collapsed;
        _displayedImagePath = record.FilePath;
        UpdateImageActionButtons();
        _ = Dispatcher.InvokeAsync(() => FitToWindow(), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void HistoryDeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button btn || btn.Tag is not GeneratedImageRecord record) return;
        if (_imageHistoryService == null) return;

        var wasDisplayed = string.Equals(_displayedImagePath, record.FilePath, StringComparison.OrdinalIgnoreCase);
        _imageHistoryService.DeleteRecord(record);
        _imageHistory.Remove(record);

        if (wasDisplayed)
        {
            _originalImageBytes = null;
            _displayedImagePath = null;
            _pendingCropRect = null;
            ExitCropMode();
            ResultImage.Source = null;
            ImageContainer.Visibility = Visibility.Collapsed;
            PlaceholderText.Visibility = Visibility.Visible;
            UpdateImageActionButtons();
        }
    }
}
