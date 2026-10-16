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

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var target = NavigationShortcuts.Resolve(e.Key, Keyboard.Modifiers);
        if (target is null) return;
        NavigateTo(target);
        e.Handled = true;
    }

    private void Navigation_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string target }) NavigateTo(target);
    }

    private void NavigateTo(string target)
    {
        switch (target)
        {
            case "Overview":
                OverviewSection.BringIntoView();
                OverviewSection.Focus();
                break;
            case "DiskMap":
                DiskMapSection.BringIntoView();
                DiskMapSection.Focus();
                break;
            case "Partitions":
                PartitionsSection.BringIntoView();
                PartitionsGrid.Focus();
                break;
            case "Health":
                HealthSection.BringIntoView();
                HealthSection.Focus();
                break;
            case "Recovery":
                RecoverySection.BringIntoView();
                RecoverySection.Focus();
                break;
            case "Maintenance":
                MaintenanceSection.BringIntoView();
                MaintenanceSection.Focus();
                break;
            case "Operations":
                OperationEditor.BringIntoView();
                OperationTypeSelector.Focus();
                break;
            case "Queue":
                QueueSection.BringIntoView();
                QueueSection.Focus();
                break;
        }
    }

    private void DiskRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: DiskInfo disk }) return;
        // Selection only; no disk process or executor is invoked.
        var current = _viewModel.Disks.FirstOrDefault(d => d.Number == disk.Number);
        if (current is null) return;
        _viewModel.SelectedDisk = current;
        _viewModel.SelectedPartition = null;
        e.Handled = true;
    }

    private void PartitionSegment_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not PartitionSegmentViewModel segment) return;
        if (segment.DiskNumber >= 0)
        {
            var current = _viewModel.Disks.FirstOrDefault(d => d.Number == segment.DiskNumber);
            if (current is null) return;
            // The owning disk must be chosen BEFORE the partition. An unallocated
            // visual extent is never presented as a real Windows partition.
            _viewModel.SelectedDisk = current;
        }
        _viewModel.SelectedPartition = segment.Partition;
        e.Handled = true;
    }

    private void License_Click(object sender, RoutedEventArgs e) => new LicenseWindow(_licensing) { Owner = this }.ShowDialog();
    private void About_Click(object sender, RoutedEventArgs e) => new AboutWindow { Owner = this }.ShowDialog();
}
