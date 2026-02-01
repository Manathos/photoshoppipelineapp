using System.Collections.Generic;
using System.Windows;
using PhotoshopPipelineApp.Models;

namespace PhotoshopPipelineApp.Views;

public partial class SendToQueueDialog : Window
{
    public QueueConfig? SelectedQueue { get; private set; }
    public string? FileName { get; private set; }

    public SendToQueueDialog(IEnumerable<QueueConfig> queues)
    {
        InitializeComponent();
        foreach (var q in queues)
            QueueCombo.Items.Add(q);
        if (QueueCombo.Items.Count > 0)
            QueueCombo.SelectedIndex = 0;
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        if (QueueCombo.SelectedItem is not QueueConfig queue)
        {
            System.Windows.MessageBox.Show("Please select a queue.", "Send to Queue", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        SelectedQueue = queue;
        FileName = FileNameBox.Text?.Trim();
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
