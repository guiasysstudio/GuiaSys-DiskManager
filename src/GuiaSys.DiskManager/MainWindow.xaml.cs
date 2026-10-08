using System.Collections.ObjectModel;
using System.Windows;
using GuiaSys.DiskManager.Models;
using GuiaSys.DiskManager.Services;

namespace GuiaSys.DiskManager;

public partial class MainWindow : Window
{
    private readonly IDiskInventoryService _inventory = new PowerShellDiskInventoryService();
    public ObservableCollection<DiskRecord> Disks { get; } = new();
    public ObservableCollection<PartitionRecord> Partitions { get; } = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        Loaded += async (_, _) => await RefreshAsync();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async Task RefreshAsync()
    {
        RefreshButton.IsEnabled = false;
        StatusText.Text = "Consultando armazenamento...";
        try
        {
            var result = await _inventory.ReadAsync(CancellationToken.None);
            Disks.Clear();
            Partitions.Clear();
            foreach (var disk in result.Disks.OrderBy(d => d.Number)) Disks.Add(disk);
            foreach (var partition in result.Partitions.OrderBy(p => p.DiskNumber).ThenBy(p => p.PartitionNumber)) Partitions.Add(partition);
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
