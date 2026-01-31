using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PhotoshopPipelineApp.Models;
using PhotoshopPipelineApp.Services;
using UserControl = System.Windows.Controls.UserControl;

namespace PhotoshopPipelineApp.Views;

public partial class SettingsView : UserControl
{
    private ConfigService? _configService;
    private List<QueueConfig> _queues = new();
    private int _selectedQueueIndex = -1;

    public SettingsView()
    {
        InitializeComponent();
    }

    public void SetConfigService(ConfigService configService)
    {
        _configService = configService;
        LoadFromConfig();
    }

    private void LoadFromConfig()
    {
        if (_configService == null) return;
        var c = _configService.Load();
        _queues = c.Queues?.ToList() ?? new List<QueueConfig>();
        if (_queues.Count == 0)
            _queues.Add(CreateDefaultQueue());
        RefreshQueueList();
        SetGlobalApiKeys(c);
    }

    private void SetGlobalApiKeys(AppConfig c)
    {
        OpenAIKeyBox.Password = c.OpenAIApiKey ?? "";
        ShopifyTokenBox.Password = c.ShopifyAccessToken ?? "";
    }

    private static QueueConfig CreateDefaultQueue()
    {
        return new QueueConfig
        {
            Name = "Queue 1",
            WatchFolderPath = "",
            OutputFolderPath = "",
            ActionSetName = "Default Actions",
            ActionName = "My Action",
            AllowedExtensions = new List<string> { "*.jpg", "*.jpeg", "*.png", "*.psd" },
            RequiredFileNames = new List<string>(),
            RequiredFilesTimeoutSeconds = 120,
            PreStepType = "None",
            PreStepSettings = new Dictionary<string, string>(),
            PostStepType = "None",
            PostStepSettings = new Dictionary<string, string>()
        };
    }

    private void RefreshQueueList()
    {
        var sel = QueueListBox.SelectedIndex;
        QueueListBox.ItemsSource = null;
        QueueListBox.ItemsSource = _queues;
        if (sel >= 0 && sel < _queues.Count)
            QueueListBox.SelectedIndex = sel;
        else if (_queues.Count > 0)
            QueueListBox.SelectedIndex = 0;
        RemoveQueueButton.IsEnabled = _queues.Count > 1;
    }

    private void QueueListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        SaveCurrentQueueToModel();
        _selectedQueueIndex = QueueListBox.SelectedIndex;
        if (_selectedQueueIndex >= 0 && _selectedQueueIndex < _queues.Count)
        {
            QueueDetailPanel.Visibility = Visibility.Visible;
            LoadQueueIntoForm(_queues[_selectedQueueIndex]);
        }
        else
        {
            QueueDetailPanel.Visibility = Visibility.Collapsed;
        }
    }

    private void LoadQueueIntoForm(QueueConfig q)
    {
        QueueNameBox.Text = q.Name ?? "";
        WatchFolderBox.Text = q.WatchFolderPath ?? "";
        OutputFolderBox.Text = q.OutputFolderPath ?? "";
        ActionSetNameBox.Text = q.ActionSetName ?? "Default Actions";
        ActionNameBox.Text = q.ActionName ?? "My Action";
        AllowedExtensionsBox.Text = q.AllowedExtensions != null ? string.Join(", ", q.AllowedExtensions) : "*.jpg, *.jpeg, *.png, *.psd";
        RequiredFileNamesBox.Text = q.RequiredFileNames != null ? string.Join(Environment.NewLine, q.RequiredFileNames) : "";
        TimeoutBox.Text = q.RequiredFilesTimeoutSeconds.ToString();

        PreStepTypeCombo.SelectedIndex = string.Equals(q.PreStepType, "OpenAI", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        PreStepOpenAIKeyBox.Password = q.PreStepSettings != null && q.PreStepSettings.TryGetValue("ApiKey", out var key) ? key : "";

        PostStepTypeCombo.SelectedIndex = string.Equals(q.PostStepType, "Shopify", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        if (q.PostStepSettings != null)
        {
            ShopifyStoreUrlBox.Text = q.PostStepSettings.TryGetValue("StoreUrl", out var url) ? url : "";
            ShopifyAccessTokenBox.Password = q.PostStepSettings.TryGetValue("AccessToken", out var tok) ? tok : "";
            ShopifyCreateAsDraftCheckBox.IsChecked = q.PostStepSettings.TryGetValue("CreateAsDraft", out var draft) && string.Equals(draft, "true", StringComparison.OrdinalIgnoreCase);
            ShopifyDefaultPriceBox.Text = q.PostStepSettings.TryGetValue("DefaultPrice", out var price) ? price : "";
            ShopifySkuPatternBox.Text = q.PostStepSettings.TryGetValue("SkuPattern", out var sku) ? sku : "IMG-{filename}";
            ShopifyVariantOptionNameBox.Text = q.PostStepSettings.TryGetValue("VariantOptionName", out var optName) ? optName : "";
            ShopifyVariantOptionValuesBox.Text = q.PostStepSettings.TryGetValue("VariantOptionValues", out var optVals) ? optVals : "";
        }
        else
        {
            ShopifyStoreUrlBox.Text = "";
            ShopifyAccessTokenBox.Password = "";
            ShopifyCreateAsDraftCheckBox.IsChecked = true;
            ShopifyDefaultPriceBox.Text = "";
            ShopifySkuPatternBox.Text = "IMG-{filename}";
            ShopifyVariantOptionNameBox.Text = "";
            ShopifyVariantOptionValuesBox.Text = "";
        }
        UpdatePreStepPanelVisibility();
        UpdatePostStepPanelVisibility();
    }

    private void SaveCurrentQueueToModel()
    {
        if (_selectedQueueIndex < 0 || _selectedQueueIndex >= _queues.Count) return;
        var q = _queues[_selectedQueueIndex];
        q.Name = QueueNameBox.Text?.Trim() ?? "Queue";
        q.WatchFolderPath = WatchFolderBox.Text?.Trim() ?? "";
        q.OutputFolderPath = OutputFolderBox.Text?.Trim() ?? "";
        q.ActionSetName = ActionSetNameBox.Text?.Trim() ?? "Default Actions";
        q.ActionName = ActionNameBox.Text?.Trim() ?? "My Action";
        q.AllowedExtensions = (AllowedExtensionsBox.Text ?? "").Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
        if (q.AllowedExtensions.Count == 0)
            q.AllowedExtensions = new List<string> { "*.jpg", "*.jpeg", "*.png", "*.psd" };
        q.RequiredFileNames = (RequiredFileNamesBox.Text ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
        if (int.TryParse(TimeoutBox.Text, out var timeout) && timeout > 0)
            q.RequiredFilesTimeoutSeconds = timeout;

        q.PreStepType = (PreStepTypeCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "None";
        q.PreStepSettings ??= new Dictionary<string, string>();
        if (q.PreStepType == "OpenAI" && !string.IsNullOrEmpty(PreStepOpenAIKeyBox.Password))
            q.PreStepSettings["ApiKey"] = PreStepOpenAIKeyBox.Password;

        q.PostStepType = (PostStepTypeCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "None";
        q.PostStepSettings ??= new Dictionary<string, string>();
        if (q.PostStepType == "Shopify")
        {
            q.PostStepSettings["StoreUrl"] = ShopifyStoreUrlBox.Text?.Trim() ?? "";
            if (!string.IsNullOrEmpty(ShopifyAccessTokenBox.Password))
                q.PostStepSettings["AccessToken"] = ShopifyAccessTokenBox.Password;
            q.PostStepSettings["CreateAsDraft"] = ShopifyCreateAsDraftCheckBox.IsChecked == true ? "true" : "false";
            q.PostStepSettings["DefaultPrice"] = ShopifyDefaultPriceBox.Text?.Trim() ?? "";
            q.PostStepSettings["SkuPattern"] = ShopifySkuPatternBox.Text?.Trim() ?? "IMG-{filename}";
            q.PostStepSettings["VariantOptionName"] = ShopifyVariantOptionNameBox.Text?.Trim() ?? "";
            q.PostStepSettings["VariantOptionValues"] = ShopifyVariantOptionValuesBox.Text?.Trim() ?? "";
        }
    }

    private void UpdatePreStepPanelVisibility()
    {
        var tag = (PreStepTypeCombo.SelectedItem as ComboBoxItem)?.Tag as string;
        PreStepOpenAIPanel.Visibility = string.Equals(tag, "OpenAI", StringComparison.OrdinalIgnoreCase) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdatePostStepPanelVisibility()
    {
        var tag = (PostStepTypeCombo.SelectedItem as ComboBoxItem)?.Tag as string;
        PostStepShopifyPanel.Visibility = string.Equals(tag, "Shopify", StringComparison.OrdinalIgnoreCase) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void PreStepTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdatePreStepPanelVisibility();
    private void PostStepTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdatePostStepPanelVisibility();

    private void AddQueue_Click(object sender, RoutedEventArgs e)
    {
        SaveCurrentQueueToModel();
        var next = _queues.Count + 1;
        _queues.Add(new QueueConfig
        {
            Name = $"Queue {next}",
            WatchFolderPath = "",
            OutputFolderPath = "",
            ActionSetName = "Default Actions",
            ActionName = "My Action",
            AllowedExtensions = new List<string> { "*.jpg", "*.jpeg", "*.png", "*.psd" },
            RequiredFileNames = new List<string>(),
            RequiredFilesTimeoutSeconds = 120,
            PreStepType = "None",
            PreStepSettings = new Dictionary<string, string>(),
            PostStepType = "None",
            PostStepSettings = new Dictionary<string, string>()
        });
        RefreshQueueList();
        QueueListBox.SelectedIndex = _queues.Count - 1;
    }

    private void RemoveQueue_Click(object sender, RoutedEventArgs e)
    {
        if (_queues.Count <= 1) return;
        SaveCurrentQueueToModel();
        var idx = QueueListBox.SelectedIndex;
        if (idx >= 0 && idx < _queues.Count)
        {
            _queues.RemoveAt(idx);
            RefreshQueueList();
            if (_queues.Count > 0)
                QueueListBox.SelectedIndex = Math.Min(idx, _queues.Count - 1);
        }
    }

    private void BrowseWatchFolder_Click(object sender, RoutedEventArgs e)
    {
        using var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select watch folder",
            UseDescriptionForTitle = true
        };
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK && !string.IsNullOrEmpty(dlg.SelectedPath))
            WatchFolderBox.Text = dlg.SelectedPath;
    }

    private void BrowseOutputFolder_Click(object sender, RoutedEventArgs e)
    {
        using var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select output folder",
            UseDescriptionForTitle = true
        };
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK && !string.IsNullOrEmpty(dlg.SelectedPath))
            OutputFolderBox.Text = dlg.SelectedPath;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        SaveCurrentQueueToModel();
        if (_configService == null) return;
        var c = _configService.Load();
        c.Queues = _queues.ToList();
        c.OpenAIApiKey = OpenAIKeyBox.Password ?? "";
        c.ShopifyAccessToken = ShopifyTokenBox.Password ?? "";
        _configService.Save(c);
        MessageBox.Show("Settings saved.", "Photoshop Pipeline", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
