using System.Windows;
using System.Windows.Controls;
using PhotoshopPipelineApp.Services;
using PhotoshopPipelineApp.Views;

namespace PhotoshopPipelineApp;

public partial class MainWindow : Window
{
    private readonly ConfigService _configService;
    private readonly PipelineService _pipelineService;

    public MainWindow()
    {
        InitializeComponent();
        _configService = new ConfigService();
        var photoshopService = new PhotoshopComService();
        _pipelineService = new PipelineService(photoshopService, _configService);

        _pipelineService.PreStepResolver = type =>
        {
            if (string.Equals(type, "OpenAI", StringComparison.OrdinalIgnoreCase))
                return new OpenAIPreStep();
            return null;
        };
        _pipelineService.PostStepResolver = type =>
        {
            if (string.Equals(type, "Shopify", StringComparison.OrdinalIgnoreCase))
                return new ShopifyPostStep();
            return new PlaceholderStep(msg => _pipelineService.LogMessage?.Invoke(_pipelineService, msg));
        };

        var dashboard = new DashboardView();
        dashboard.SetPipeline(_pipelineService);
        dashboard.SetConfigService(_configService);
        DashboardContent.Content = dashboard;

        var settings = new SettingsView();
        settings.SetConfigService(_configService);
        SettingsContent.Content = settings;
    }

    protected override void OnClosed(EventArgs e)
    {
        _pipelineService?.Dispose();
        base.OnClosed(e);
    }
}
