using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using GuiaSys.DiskManager.Infrastructure;
using GuiaSys.DiskManager.Models;
using GuiaSys.DiskManager.Operations;
using GuiaSys.DiskManager.Services;

namespace GuiaSys.DiskManager.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly IDiskInventoryService _inventory;
    private readonly IStorageOperationExecutor _executor;
    private readonly ISafetyService _safety;
    private readonly IAppLogger _logger;
    private readonly OperationQueue _queue = new();
    private StorageSnapshot _snapshot = new([], [], DateTimeOffset.MinValue);
    private DiskInfo? _selectedDisk;
    private PartitionInfo? _selectedPartition;
    private StorageOperationType _selectedOperationType;
    private string _status = "Pronto.";
    private string _labelInput = "";
    private string _sizeInput = "";
    private string _driveLetterInput = "";
    private string _allocationUnitInput = "";
    private string _selectedFileSystem = "NTFS";
    private bool _quickFormat = true;
    private bool _isBusy;
    private double _progress;

    public MainViewModel(IDiskInventoryService inventory, IStorageOperationExecutor executor, ISafetyService safety, IAppLogger logger)
    {
        _inventory = inventory; _executor = executor; _safety = safety; _logger = logger;
        PendingOperations = _queue.Pending;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
        QueueCommand = new RelayCommand(QueueSelectedOperation, () => SelectedDisk is not null && !IsBusy);
        UndoCommand = new RelayCommand(() => { _queue.UndoLast(); UpdateCommands(); }, () => PendingOperations.Count > 0 && !IsBusy);
        ClearCommand = new RelayCommand(() => { _queue.Clear(); UpdateCommands(); Status = "Fila limpa."; }, () => PendingOperations.Count > 0 && !IsBusy);
        ApplyCommand = new AsyncRelayCommand(() => ApplyAsync(false), () => PendingOperations.Count > 0 && !IsBusy);
        OpenLogsCommand = new RelayCommand(OpenLogs);
        PendingOperations.CollectionChanged += (_, _) => UpdateCommands();
    }

    public ObservableCollection<DiskInfo> Disks { get; } = [];
    public ObservableCollection<PartitionInfo> Partitions { get; } = [];
    public ObservableCollection<PartitionSegmentViewModel> PartitionSegments { get; } = [];
    public ObservableCollection<StorageOperation> PendingOperations { get; }
    public IReadOnlyList<OperationChoice> OperationChoices { get; } =
    [
        new(StorageOperationType.SetDiskOnline, "Colocar disco online"), new(StorageOperationType.SetDiskOffline, "Colocar disco offline"),
        new(StorageOperationType.SetDiskReadOnly, "Ativar somente leitura"), new(StorageOperationType.ClearDiskReadOnly, "Desativar somente leitura"),
        new(StorageOperationType.InitializeGpt, "Inicializar como GPT"), new(StorageOperationType.InitializeMbr, "Inicializar como MBR"),
        new(StorageOperationType.CreatePartition, "Criar partição"), new(StorageOperationType.DeletePartition, "Excluir partição"),
        new(StorageOperationType.FormatPartition, "Formatar partição"), new(StorageOperationType.SetDriveLetter, "Atribuir/trocar letra"),
        new(StorageOperationType.RemoveDriveLetter, "Remover letra"), new(StorageOperationType.ResizePartition, "Redimensionar partição"),
        new(StorageOperationType.SetVolumeLabel, "Alterar label")
    ];
    public string[] FileSystems { get; } = ["NTFS", "FAT32", "exFAT"];
    public bool IsAdministrator { get; } = AdministratorService.IsAdministrator();
    public string PrivilegeLabel => IsAdministrator ? "Administrador" : "Somente leitura · sem elevação";
    public string CapturedAtLabel => _snapshot.CapturedAt == DateTimeOffset.MinValue ? "" : $"Atualizado {_snapshot.CapturedAt:HH:mm:ss}";

    public DiskInfo? SelectedDisk
    {
        get => _selectedDisk;
        set { if (SetProperty(ref _selectedDisk, value)) { SelectedPartition = null; FilterPartitions(); BuildPartitionMap(); UpdateCommands(); } }
    }
    public PartitionInfo? SelectedPartition { get => _selectedPartition; set { if (SetProperty(ref _selectedPartition, value)) UpdateCommands(); } }
    public StorageOperationType SelectedOperationType { get => _selectedOperationType; set => SetProperty(ref _selectedOperationType, value); }
    public string LabelInput { get => _labelInput; set => SetProperty(ref _labelInput, value); }
    public string SizeInput { get => _sizeInput; set => SetProperty(ref _sizeInput, value); }
    public string DriveLetterInput { get => _driveLetterInput; set => SetProperty(ref _driveLetterInput, value); }
    public string AllocationUnitInput { get => _allocationUnitInput; set => SetProperty(ref _allocationUnitInput, value); }
    public string SelectedFileSystem { get => _selectedFileSystem; set => SetProperty(ref _selectedFileSystem, value); }
    public bool QuickFormat { get => _quickFormat; set => SetProperty(ref _quickFormat, value); }
    public string Status { get => _status; set => SetProperty(ref _status, value); }
    public bool IsBusy { get => _isBusy; private set { if (SetProperty(ref _isBusy, value)) UpdateCommands(); } }
    public double Progress { get => _progress; private set => SetProperty(ref _progress, value); }
    public AsyncRelayCommand RefreshCommand { get; }
    public RelayCommand QueueCommand { get; }
    public RelayCommand UndoCommand { get; }
    public RelayCommand ClearCommand { get; }
    public AsyncRelayCommand ApplyCommand { get; }
    public RelayCommand OpenLogsCommand { get; }

    public Func<IReadOnlyList<StorageOperation>, bool, Task<bool>>? ConfirmOperationsAsync { get; set; }
    public Action<string, string>? ShowError { get; set; }

    public async Task RefreshAsync()
    {
        IsBusy = true; Progress = 15; Status = "Consultando o armazenamento do Windows…";
        try
        {
            var snapshot = await _inventory.ReadAsync(CancellationToken.None);
            _snapshot = snapshot;
            var previousIdentity = SelectedDisk?.Identity;
            Disks.Clear(); foreach (var disk in snapshot.Disks) Disks.Add(disk);
            SelectedDisk = Disks.FirstOrDefault(d => d.Identity == previousIdentity) ?? Disks.FirstOrDefault();
            OnPropertyChanged(nameof(CapturedAtLabel));
            Progress = 100; Status = $"{Disks.Count} disco(s) e {_snapshot.Partitions.Count} partição(ões). Nenhuma alteração realizada.";
        }
        catch (Exception exception)
        {
            _logger.Error("storage.inventory.failed", exception);
            Status = "Falha ao consultar o armazenamento.";
            ShowError?.Invoke("Inventário indisponível", exception.Message);
        }
        finally { IsBusy = false; Progress = 0; }
    }

    private void QueueSelectedOperation()
    {
        if (SelectedDisk is null) return;
        long? size = null;
        if (!string.IsNullOrWhiteSpace(SizeInput))
        {
            if (!TryParseSize(SizeInput, out var parsed)) { ShowError?.Invoke("Tamanho inválido", "Use bytes ou valores como 512 MB, 20 GB ou 1,5 TB."); return; }
            size = parsed;
        }
        char? letter = string.IsNullOrWhiteSpace(DriveLetterInput) ? null : char.ToUpperInvariant(DriveLetterInput.Trim()[0]);
        int? allocationUnit = null;
        if (!string.IsNullOrWhiteSpace(AllocationUnitInput))
        {
            if (!int.TryParse(AllocationUnitInput.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedAllocation)) { ShowError?.Invoke("Unidade de alocação inválida", "Informe o tamanho em bytes, por exemplo 4096."); return; }
            allocationUnit = parsedAllocation;
        }
        var operation = new StorageOperation
        {
            Type = SelectedOperationType, DiskNumber = SelectedDisk.Number, PartitionNumber = SelectedPartition?.PartitionNumber,
            ExpectedDiskIdentity = SelectedDisk.Identity, SizeBytes = size, FileSystem = SelectedFileSystem, Label = LabelInput.Trim(), DriveLetter = letter, QuickFormat = QuickFormat, AllocationUnitSize = allocationUnit
        };
        var decision = _safety.Evaluate(operation, _snapshot);
        if (!decision.Allowed) { ShowError?.Invoke("Operação bloqueada", decision.Reason); Status = decision.Reason; return; }
        _queue.Enqueue(operation); Status = $"Adicionado à fila: {operation.Description}";
    }

    public async Task ApplyAsync(bool confirmationAlreadyGranted)
    {
        var operations = _queue.Snapshot();
        if (operations.Count == 0) return;
        if (!IsAdministrator) { ShowError?.Invoke("Elevação necessária", "Reabra o aplicativo como administrador para aplicar operações. O inventário permanece disponível sem elevação."); return; }
        var reinforced = operations.Any(x => x.RequiresReinforcedConfirmation);
        if (!confirmationAlreadyGranted && ConfirmOperationsAsync is not null && !await ConfirmOperationsAsync(operations, reinforced)) { Status = "Aplicação cancelada."; return; }

        IsBusy = true;
        try
        {
            for (var index = 0; index < operations.Count; index++)
            {
                var operation = operations[index];
                Status = $"Validando {index + 1}/{operations.Count}: {operation.Description}"; Progress = index * 100d / operations.Count;
                var current = await _inventory.ReadAsync(CancellationToken.None);
                var decision = _safety.Evaluate(operation, current);
                if (!decision.Allowed) { ShowError?.Invoke("Operação cancelada por segurança", decision.Reason); Status = decision.Reason; return; }
                Status = $"Executando {index + 1}/{operations.Count}: {operation.Description}";
                var result = await _executor.ExecuteAsync(operation, CancellationToken.None);
                if (!result.Succeeded) { ShowError?.Invoke("Operação não concluída", result.Message); Status = result.Message; return; }
                PendingOperations.Remove(operation);
            }
            Progress = 100; Status = "Todas as operações foram concluídas e verificadas pelo Windows.";
            await RefreshAsync();
        }
        finally { IsBusy = false; Progress = 0; }
    }

    private void FilterPartitions()
    {
        Partitions.Clear();
        if (SelectedDisk is null) return;
        foreach (var partition in _snapshot.Partitions.Where(p => p.DiskNumber == SelectedDisk.Number).OrderBy(p => p.Offset)) Partitions.Add(partition);
    }

    private void BuildPartitionMap()
    {
        PartitionSegments.Clear();
        if (SelectedDisk is null || SelectedDisk.Size <= 0) return;
        long cursor = 0;
        foreach (var partition in Partitions.OrderBy(p => p.Offset))
        {
            if (partition.Offset > cursor) AddSegment(null, cursor, partition.Offset - cursor);
            AddSegment(partition, partition.Offset, partition.Size);
            cursor = Math.Max(cursor, partition.Offset + partition.Size);
        }
        if (cursor < SelectedDisk.Size) AddSegment(null, cursor, SelectedDisk.Size - cursor);
    }

    private void AddSegment(PartitionInfo? partition, long offset, long size)
    {
        if (size <= 0 || SelectedDisk is null) return;
        var width = Math.Max(54, Math.Min(760, size / (double)SelectedDisk.Size * 760));
        var title = partition?.DisplayName ?? "Não alocado";
        var subtitle = partition is null ? ByteSize.Format(size) : $"{partition.Kind} · {partition.SizeLabel}";
        PartitionSegments.Add(new PartitionSegmentViewModel(partition, title, subtitle, partition?.Kind ?? "Unallocated", size, width));
    }

    private void OpenLogs()
    {
        try { Process.Start(new ProcessStartInfo { FileName = _logger.LogDirectory, UseShellExecute = true }); }
        catch (Exception exception) { ShowError?.Invoke("Não foi possível abrir os logs", exception.Message); }
    }

    public static bool TryParseSize(string input, out long bytes)
    {
        bytes = 0;
        var normalized = input.Trim().ToUpperInvariant().Replace("IB", "B", StringComparison.Ordinal).Replace(" ", "", StringComparison.Ordinal);
        var multipliers = new Dictionary<string, decimal> { ["TB"] = 1_000_000_000_000m, ["GB"] = 1_000_000_000m, ["MB"] = 1_000_000m, ["KB"] = 1_000m, ["B"] = 1m };
        foreach (var pair in multipliers)
        {
            if (!normalized.EndsWith(pair.Key, StringComparison.Ordinal)) continue;
            var numeric = normalized[..^pair.Key.Length].Replace(',', '.');
            if (!decimal.TryParse(numeric, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) || value <= 0) return false;
            try { bytes = checked((long)(value * pair.Value)); return bytes > 0; } catch (OverflowException) { return false; }
        }
        return long.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out bytes) && bytes > 0;
    }

    private void UpdateCommands()
    {
        QueueCommand.RaiseCanExecuteChanged(); UndoCommand.RaiseCanExecuteChanged(); ClearCommand.RaiseCanExecuteChanged(); ApplyCommand.RaiseCanExecuteChanged(); RefreshCommand.RaiseCanExecuteChanged();
    }
}

public sealed record OperationChoice(StorageOperationType Type, string Display);
