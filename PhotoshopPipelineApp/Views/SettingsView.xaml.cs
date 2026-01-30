using System.Windows;
using System.Windows.Controls;
using PhotoshopPipelineApp.Models;
using PhotoshopPipelineApp.Services;
using UserControl = System.Windows.Controls.UserControl;

namespace PhotoshopPipelineApp.Views;

public partial class SettingsView : UserControl
{
    private ConfigService? _configService;

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
        WatchFolderBox.Text = c.WatchFolderPath;
        OutputFolderBox.Text = c.OutputFolderPath;
        ActionSetNameBox.Text = c.ActionSetName;
        ActionNameBox.Text = c.ActionName;
        AllowedExtensionsBox.Text = string.Join(", ", c.AllowedExtensions);
        RequiredFileNamesBox.Text = string.Join(Environment.NewLine, c.RequiredFileNames);
        TimeoutBox.Text = c.RequiredFilesTimeoutSeconds.ToString();
        FollowUpTypeCombo.SelectedIndex = 0;
    }

    private void SaveToConfig()
    {
        if (_configService == null) return;
        var c = new AppConfig
        {
            WatchFolderPath = WatchFolderBox.Text?.Trim() ?? "",
            OutputFolderPath = OutputFolderBox.Text?.Trim() ?? "",
            ActionSetName = ActionSetNameBox.Text?.Trim() ?? "Default Actions",
            ActionName = ActionNameBox.Text?.Trim() ?? "My Action",
            AllowedExtensions = (AllowedExtensionsBox.Text ?? "").Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList(),
            RequiredFileNames = (RequiredFileNamesBox.Text ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList(),
            FollowUpType = "None"
        };
        if (int.TryParse(TimeoutBox.Text, out var timeout) && timeout > 0)
            c.RequiredFilesTimeoutSeconds = timeout;
        if (c.AllowedExtensions.Count == 0)
            c.AllowedExtensions = new List<string> { "*.jpg", "*.jpeg", "*.png", "*.psd" };
        _configService.Save(c);
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
        SaveToConfig();
        MessageBox.Show("Settings saved.", "Photoshop Pipeline", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
