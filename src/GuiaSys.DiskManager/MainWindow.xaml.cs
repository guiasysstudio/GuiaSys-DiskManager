using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GuiaSys.DiskManager.Models;
using GuiaSys.DiskManager.ViewModels;

namespace GuiaSys.DiskManager;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent(); _viewModel = viewModel; DataContext = viewModel;
        viewModel.ShowError = (title, message) => MessageBox.Show(this, message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
        viewModel.ConfirmOperationsAsync = ConfirmOperationsAsync;
        Loaded += async (_, _) => await viewModel.RefreshAsync();
    }
    private Task<bool> ConfirmOperationsAsync(IReadOnlyList<StorageOperation> operations, bool reinforced)
    {
        var summary = new StringBuilder("As operações abaixo serão executadas na ordem:\n\n");
        for (var index = 0; index < operations.Count; index++) summary.AppendLine($"{index + 1}. {operations[index].Description}");
        summary.AppendLine("\nO estado do disco será revalidado antes de cada etapa.");
        if (!reinforced) return Task.FromResult(MessageBox.Show(this, summary.ToString(), "Confirmar operações", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes);
        var dialog = new ReinforcedConfirmationWindow(summary.ToString(), operations[0].DiskNumber.ToString()) { Owner = this };
        return Task.FromResult(dialog.ShowDialog() == true);
    }
    private void QueuePreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string tag } && Enum.TryParse<StorageOperationType>(tag, out var type)) { _viewModel.SelectedOperationType = type; if (_viewModel.QueueCommand.CanExecute(null)) _viewModel.QueueCommand.Execute(null); }
    }
    private void PartitionSegment_Click(object sender, MouseButtonEventArgs e) { if ((sender as FrameworkElement)?.DataContext is PartitionSegmentViewModel { Partition: not null } segment) _viewModel.SelectedPartition = segment.Partition; }
    private void About_Click(object sender, RoutedEventArgs e) => new AboutWindow { Owner = this }.ShowDialog();
}
