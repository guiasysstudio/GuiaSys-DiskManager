using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GuiaSys.DiskManager.Licensing;
using GuiaSys.DiskManager.Models;
using GuiaSys.DiskManager.Operations;
using GuiaSys.DiskManager.ViewModels;

namespace GuiaSys.DiskManager;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly ILicenseEntitlements _licensing;
    public MainWindow(MainViewModel viewModel, ILicenseEntitlements licensing)
    {
        InitializeComponent(); _viewModel = viewModel; _licensing = licensing; DataContext = viewModel;
        viewModel.ShowError = (title, message) => MessageBox.Show(this, message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
        viewModel.ConfirmOperationsAsync = ConfirmOperationsAsync;
        Loaded += async (_, _) => await viewModel.RefreshAsync();
    }
    private Task<bool> ConfirmOperationsAsync(IReadOnlyList<StorageOperation> operations, bool reinforced)
    {
        var challenge = OperationConfirmation.Create(operations);
        if (!reinforced)
            return Task.FromResult(MessageBox.Show(this, challenge.Summary,
                "Confirmar todas as operações", MessageBoxButton.YesNo,
                MessageBoxImage.Question) == MessageBoxResult.Yes);
        var dialog = new ReinforcedConfirmationWindow(challenge.Summary, challenge.ExpectedResponse) { Owner = this };
        return Task.FromResult(dialog.ShowDialog() == true);
    }
    private void ContextAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: StorageOperationType type }) return;
        _viewModel.SelectedOperationType = type;
        OperationEditor.BringIntoView();
        OperationTypeSelector.Focus();
    }

    private void ClearPartition_Click(object sender, RoutedEventArgs e) =>
        _viewModel.SelectedPartition = null;

    private void Navigation_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string target }) return;
        switch (target)
        {
            case "Overview":
                WorkAreaScroll.ScrollToTop();
                break;
            case "DiskMap":
                DiskMapSection.BringIntoView();
                break;
            case "Partitions":
                PartitionsSection.BringIntoView();
                break;
            case "Health":
                HealthSection.BringIntoView();
                break;
            case "Operations":
                OperationEditor.BringIntoView();
                OperationTypeSelector.Focus();
                break;
            case "Queue":
                QueueSection.BringIntoView();
                break;
        }
    }

    private void PartitionSegment_Click(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not PartitionSegmentViewModel segment) return;
        // Selecting unallocated space selects the whole disk, never an invented partition.
        _viewModel.SelectedPartition = segment.Partition;
        e.Handled = true;
    }

    private void License_Click(object sender, RoutedEventArgs e) => new LicenseWindow(_licensing) { Owner = this }.ShowDialog();
    private void About_Click(object sender, RoutedEventArgs e) => new AboutWindow { Owner = this }.ShowDialog();
}
