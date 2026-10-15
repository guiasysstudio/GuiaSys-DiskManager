using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using GuiaSys.DiskManager.Infrastructure;
using GuiaSys.DiskManager.Licensing;
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
    private readonly ILicenseEntitlements _licensing;
    private readonly OperationQueue _queue = new();
    private readonly IOperationPlanner _planner;
    private string _queuePreviewSummary = "Fila vazia. Nenhum efeito projetado.";
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
    private bool _loadingSelection;
    private double _progress;

    public MainViewModel(IDiskInventoryService inventory, IStorageOperationExecutor executor, ISafetyService safety, IAppLogger logger, ILicenseEntitlements licensing)
    {
        _inventory = inventory; _executor = executor; _safety = safety; _logger = logger; _licensing = licensing;
        _planner = new OperationPlanner(safety);
        PendingOperations = _queue.Pending;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
        QueueCommand = new RelayCommand(QueueSelectedOperation, () => SelectedDisk is not null && !IsBusy && SelectedOperationAvailability?.IsAvailable == true);
        UndoCommand = new RelayCommand(() => { _queue.UndoLast(); UpdateCommands(); }, () => PendingOperations.Count > 0 && !IsBusy);
        ClearCommand = new RelayCommand(() => { _queue.Clear(); UpdateCommands(); Status = "Fila limpa."; }, () => PendingOperations.Count > 0 && !IsBusy);
        ApplyCommand = new AsyncRelayCommand(() => ApplyAsync(false), () => PendingOperations.Count > 0 && !IsBusy && StorageWriteGate.ReleasedAfterSafetyAudit);
        OpenLogsCommand = new RelayCommand(OpenLogs);
        PendingOperations.CollectionChanged += (_, _) => { UpdatePreview(); UpdateCommands(); };
        UpdateContextOperations();
    }

    public ObservableCollection<DiskInfo> Disks { get; } = [];
    public ObservableCollection<PartitionInfo> Partitions { get; } = [];
    public ObservableCollection<PartitionSegmentViewModel> PartitionSegments { get; } = [];
    public ObservableCollection<StorageOperation> PendingOperations { get; }
    public ObservableCollection<ContextOperationChoice> ContextOperations { get; } = [];
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
    public string WriteModeLabel => StorageWriteGate.ReleasedAfterSafetyAudit
        ? "Execução sujeita a licença, confirmação e auditoria"
        : "MODO SEGURO · gravações desativadas";
    public bool CanApplyWrites => StorageWriteGate.ReleasedAfterSafetyAudit;
    public string DiskCountLabel => $"{Disks.Count} {(Disks.Count == 1 ? "dispositivo" : "dispositivos")}";
    public string PartitionCountLabel => $"{_snapshot.Partitions.Count} {(_snapshot.Partitions.Count == 1 ? "partição" : "partições")}";
    public string SelectedTargetTitle => SelectedPartition is not null
        ? $"Disco {SelectedPartition.DiskNumber} / Partição {SelectedPartition.PartitionNumber}"
        : SelectedDisk is not null ? $"Disco {SelectedDisk.Number} / {SelectedDisk.FriendlyName}" : "Nenhum disco selecionado";
    public string SelectedTargetHint => SelectedPartition is not null
        ? $"{SelectedPartition.Kind} · {SelectedPartition.SizeLabel} · {SelectedPartition.Identity ?? "identidade insuficiente"}"
        : SelectedDisk is not null ? $"{SelectedDisk.SizeLabel} · {SelectedDisk.PartitionStyle} · {SelectedDisk.IdentityStrengthLabel}"
            : "Selecione um dispositivo na coluna esquerda.";
    public ContextOperationChoice? SelectedOperationAvailability => ContextOperations.FirstOrDefault(x => x.Type == SelectedOperationType);
    public string SelectedActionReason => SelectedOperationAvailability?.Reason ?? "Selecione uma operação.";
    public string SelectedActionStatus => SelectedOperationAvailability?.IsAvailable == true
        ? "Pronta para adicionar à fila (simulação)"
        : "Indisponível para o alvo selecionado";
    public string CapturedAtLabel => _snapshot.CapturedAt == DateTimeOffset.MinValue ? "" : $"Atualizado {_snapshot.CapturedAt:HH:mm:ss}";
    public string QueuePreviewSummary { get => _queuePreviewSummary; private set => SetProperty(ref _queuePreviewSummary, value); }
    public string SelectedLayoutSummary
    {
        get
        {
            if (SelectedDisk is null) return "Nenhum disco selecionado.";
            try
            {
                var disk = StorageGeometry.Build(_snapshot).FindDisk(SelectedDisk.Number);
                return disk is null ? "Disco indisponível." :
                    $"Espaço livre: {ByteSize.Format(disk.TotalFreeBytes)} · maior faixa: {ByteSize.Format(disk.LargestFreeExtentBytes)}";
            }
            catch (System.IO.InvalidDataException) { return "Geometria inconsistente — operações bloqueadas."; }
        }
    }

    public DiskInfo? SelectedDisk
    {
        get => _selectedDisk;
        set
        {
            if (!SetProperty(ref _selectedDisk, value)) return;
            SelectedPartition = null;
            FilterPartitions();
            BuildPartitionMap();
            OnPropertyChanged(nameof(SelectedLayoutSummary));
            NotifyTargetChanged();
        }
    }
    public PartitionInfo? SelectedPartition
    {
        get => _selectedPartition;
        set
        {
            if (!SetProperty(ref _selectedPartition, value)) return;
            BuildPartitionMap();
            NotifyTargetChanged();
        }
    }
    public StorageOperationType SelectedOperationType
    {
        get => _selectedOperationType;
        set
        {
            if (!SetProperty(ref _selectedOperationType, value)) return;
            OnPropertyChanged(nameof(SelectedOperationAvailability));
            OnPropertyChanged(nameof(SelectedActionReason));
            OnPropertyChanged(nameof(SelectedActionStatus));
            UpdateCommands();
        }
    }
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
            var previousPartitionIdentity = SelectedPartition?.Identity;
            _loadingSelection = true;
            try
            {
                Disks.Clear(); foreach (var disk in snapshot.Disks) Disks.Add(disk);
                SelectedDisk = Disks.FirstOrDefault(d => d.Identity == previousIdentity) ?? Disks.FirstOrDefault();
                if (SelectedDisk is not null && previousPartitionIdentity is not null)
                    SelectedPartition = Partitions.FirstOrDefault(p =>
                        string.Equals(p.Identity, previousPartitionIdentity, StringComparison.Ordinal));
            }
            finally { _loadingSelection = false; }
            NotifyTargetChanged();
            OnPropertyChanged(nameof(DiskCountLabel));
            OnPropertyChanged(nameof(PartitionCountLabel));
            OnPropertyChanged(nameof(CapturedAtLabel));
            OnPropertyChanged(nameof(SelectedLayoutSummary));
            UpdatePreview();
            Progress = 100; Status = $"{Disks.Count} disco(s) e {_snapshot.Partitions.Count} partição(ões). Nenhuma alteração realizada.";
        }
        catch (Exception exception)
        {
            _logger.Error("storage.inventory.failed", exception);
            // Never leave stale inventory visible as if the device were still present.
            _snapshot = new StorageSnapshot([], [], DateTimeOffset.MinValue);
            Disks.Clear();
            SelectedDisk = null;
            Partitions.Clear();
            PartitionSegments.Clear();
            UpdateContextOperations();
            UpdatePreview();
            OnPropertyChanged(nameof(DiskCountLabel));
            OnPropertyChanged(nameof(PartitionCountLabel));
            OnPropertyChanged(nameof(CapturedAtLabel));
            Status = "Inventário indisponível: dispositivos ocultados até a próxima atualização.";
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
            ExpectedDiskIdentity = SelectedDisk.Identity,
            ExpectedPartitionIdentity = SelectedPartition?.Identity, SizeBytes = size, FileSystem = SelectedFileSystem, Label = LabelInput.Trim(), DriveLetter = letter, QuickFormat = QuickFormat, AllocationUnitSize = allocationUnit
        };
        // The planner evaluates this operation against all prior queued effects, not stale inventory.
        try
        {
            var preview = _planner.Plan(_snapshot, _queue.Snapshot().Append(operation).ToArray());
            if (!preview.CanApply)
            {
                var message = preview.FirstConflict?.Message ?? "Operações conflitantes.";
                ShowError?.Invoke("Conflito no planejamento", message);
                Status = message;
                return;
            }
            _queue.Enqueue(operation); Status = $"Adicionado à fila: {operation.Description}";
        }
        catch (System.IO.InvalidDataException exception)
        {
            ShowError?.Invoke("Geometria inválida", exception.Message);
            Status = "O inventário precisa ser atualizado antes de planejar.";
        }
    }

    public async Task ApplyAsync(bool confirmationAlreadyGranted)
    {
        var operations = _queue.Snapshot();
        if (operations.Count == 0) return;
        // C02 preview rejects conflicts; this is NOT the executor's authoritative security gate.
        try
        {
            var preview = _planner.Plan(_snapshot, operations);
            if (!preview.CanApply)
            {
                var reason = preview.FirstConflict?.Message ?? "Fila inconsistente.";
                ShowError?.Invoke("Planejamento inválido", reason);
                Status = reason;
                return;
            }
        }
        catch (System.IO.InvalidDataException exception)
        {
            ShowError?.Invoke("Inventário inválido", exception.Message);
            Status = "A operação exige um novo inventário válido.";
            return;
        }
        // C03: no commit permission yet; the executor enforces its own independent gate.
        // A license is NEVER a substitute for safe disk identity and execution policy.
        if (!StorageWriteGate.ReleasedAfterSafetyAudit)
        {
            Status = "Gravações bloqueadas nesta fase (validação do executor e testes VHDX pendentes).";
            ShowError?.Invoke("Operações não liberadas", Status);
            return;
        }
        var license = await _licensing.CheckAsync(CancellationToken.None);
        if (!StorageWriteGate.Allows(license))
        {
            Status = "Licenciamento: " + license.Message;
            ShowError?.Invoke("Licença necessária", license.Message);
            return;
        }
        if (!IsAdministrator) { ShowError?.Invoke("Elevação necessária", "Reabra o aplicativo como administrador para aplicar operações. O inventário permanece disponível sem elevação."); return; }
        var reinforced = operations.Any(x => x.RequiresReinforcedConfirmation);
        // The old 'confirmationAlreadyGranted' parameter is intentionally never a bypass.
        if (ConfirmOperationsAsync is null || !await ConfirmOperationsAsync(operations, reinforced))
        { Status = "Aplicação cancelada: confirmação explícita obrigatória."; return; }

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
                if (!result.Succeeded)
                {
                    if (result.Outcome == StorageExecutionOutcome.Indeterminate)
                    {
                        // Never leave an uncertain operation available for one-click reapply.
                        _queue.Clear();
                        Status = "Resultado indeterminado. Fila esvaziada; reconciliação manual necessária.";
                        ShowError?.Invoke("Não repetir a operação", result.Message);
                    }
                    else
                    {
                        Status = result.Message;
                        ShowError?.Invoke("Operação não iniciada ou recusada", result.Message);
                    }
                    return;
                }
                PendingOperations.Remove(operation);
            }
            Progress = 100; Status = "As operações foram confirmadas por inventário independente.";
            await RefreshAsync();
        }
        finally { IsBusy = false; Progress = 0; }
    }

    private void UpdatePreview()
    {
        if (PendingOperations.Count == 0)
        {
            QueuePreviewSummary = "Fila vazia. Nada será alterado.";
            return;
        }
        try
        {
            var plan = _planner.Plan(_snapshot, _queue.Snapshot());
            QueuePreviewSummary = plan.CanApply
                ? $"{plan.Steps.Count} operação(ões) · {plan.AffectedDisks} disco(s) · " +
                    $"{plan.Steps.Count(x => x.RequiresRefreshAfterExecution)} exige(m) novo inventário. " +
                    "Simulação apenas: não autoriza execução."
                : $"CONFLITO etapa {plan.FirstConflict?.Position}: {plan.FirstConflict?.Message}";
        }
        catch (System.IO.InvalidDataException)
        {
            QueuePreviewSummary = "CONFLITO: inventário com geometria inválida. Atualize os discos.";
        }
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

        // Same checked, half-open extents used by C02's planning engine and inventory validator.
        var layout = StorageGeometry.Build(_snapshot).FindDisk(SelectedDisk.Number);
        if (layout is null) return;
        foreach (var extent in layout.Extents)
        {
            var partition = extent.PartitionNumber is int number
                ? _snapshot.FindPartition(SelectedDisk.Number, number) : null;
            AddSegment(partition, extent.Offset, extent.Length);
        }
    }

    private void AddSegment(PartitionInfo? partition, long offset, long size)
    {
        if (size <= 0 || SelectedDisk is null) return;
        var width = Math.Max(54, Math.Min(760, size / (double)SelectedDisk.Size * 760));
        var title = partition?.DisplayName ?? "Não alocado";
        var subtitle = partition is null ? ByteSize.Format(size) : $"{partition.Kind} · {partition.SizeLabel}";
        PartitionSegments.Add(new PartitionSegmentViewModel(partition, title, subtitle,
            partition?.Kind ?? "Unallocated", size, width,
            partition is not null && SelectedPartition?.PartitionNumber == partition.PartitionNumber));
    }

    private void NotifyTargetChanged()
    {
        if (!_loadingSelection) UpdateContextOperations();
        OnPropertyChanged(nameof(SelectedTargetTitle));
        OnPropertyChanged(nameof(SelectedTargetHint));
        OnPropertyChanged(nameof(SelectedActionReason));
        OnPropertyChanged(nameof(SelectedActionStatus));
        UpdateCommands();
    }

    private void UpdateContextOperations()
    {
        ContextOperations.Clear();
        foreach (var choice in OperationChoices)
            ContextOperations.Add(ContextOperationPolicy.Evaluate(choice.Type, choice.Display,
                SelectedDisk, SelectedPartition, _snapshot, _safety));
        var selected = ContextOperations.FirstOrDefault(c => c.Type == SelectedOperationType);
        if (selected?.IsAvailable != true)
        {
            var preferred = ContextOperations.FirstOrDefault(c => c.IsAvailable &&
                c.Type == StorageOperationType.SetVolumeLabel)
                ?? ContextOperations.FirstOrDefault(c => c.IsAvailable);
            if (preferred is not null) SelectedOperationType = preferred.Type;
        }
        OnPropertyChanged(nameof(SelectedOperationAvailability));
        OnPropertyChanged(nameof(SelectedActionReason));
        OnPropertyChanged(nameof(SelectedActionStatus));
        UpdateCommands();
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
