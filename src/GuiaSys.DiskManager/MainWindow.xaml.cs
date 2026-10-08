using System.Collections.ObjectModel;
using System.Windows;
using GuiaSys.DiskManager.Models;
using GuiaSys.DiskManager.Services;

namespace GuiaSys.DiskManager;

public partial class MainWindow : Window
{
    private readonly IDiskInventoryService _inventory = new PowerShellDiskInventoryService();
    private IReadOnlyList<PartitionRecord> _allPartitions = Array.Empty<PartitionRecord>();
    public ObservableCollection<DiskRecord> Disks { get; } = new();
    public ObservableCollection<PartitionRecord> Partitions { get; } = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        Loaded += async (_, _) => await RefreshAsync();
        DisksGrid.SelectionChanged += (_, _) => FilterPartitions();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private void FilterPartitions()
    {
        Partitions.Clear();
        int? selected = (DisksGrid.SelectedItem as DiskRecord)?.Number;
        foreach (var partition in _allPartitions.Where(p => selected is null || p.DiskNumber == selected.Value).OrderBy(p => p.DiskNumber).ThenBy(p => p.PartitionNumber))
            Partitions.Add(partition);
    }

    private async Task RefreshAsync()
    {
        RefreshButton.IsEnabled = false;
        StatusText.Text = "Consultando armazenamento...";
        try
        {
            var result = await _inventory.ReadAsync(CancellationToken.None);
            Disks.Clear();
            _allPartitions = result.Partitions;
            Partitions.Clear();
            foreach (var disk in result.Disks.OrderBy(d => d.Number)) Disks.Add(disk);
            FilterPartitions();
            StatusText.Text = $"{Disks.Count} disco(s), {Partitions.Count} partição(ões). Sem alterações realizadas.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Não foi possível consultar o armazenamento.";
            MessageBox.Show(this, ex.Message, "Erro no inventário", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            RefreshButton.IsEnabled = true;
        }
    }
}
