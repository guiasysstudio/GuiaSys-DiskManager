using GuiaSys.DiskManager.Licensing;
using GuiaSys.DiskManager.Models;
using GuiaSys.DiskManager.Operations;
using GuiaSys.DiskManager.Safety;
using GuiaSys.DiskManager.Services;
using GuiaSys.DiskManager.ViewModels;

namespace GuiaSys.DiskManager.Tests;

public sealed class MainViewModelUiTests
{
    private static DiskInfo Disk(int number = 3) => new()
    {
        Number = number, UniqueId = $"c05-test-{number}", FriendlyName = "Temporary VHDX",
        Size = 8_000_000_000, PartitionStyle = "GPT", LogicalSectorSize = 512
    };

    private static PartitionInfo Partition(int diskNumber = 3) => new()
    {
        DiskNumber = diskNumber, PartitionNumber = 1,
        PartitionGuid = "a911ce1e-d646-4af0-b245-c346c5600c65",
        GptType = "ebd0a0a2-b9e5-4433-87c0-68b6b72699c7",
        Type = "Basic", Offset = 1_048_576, Size = 2_000_000_000,
        DriveLetter = "E", AccessPaths = "", FileSystem = "NTFS",
        ResizeMinBytes = 1_000_000_000, ResizeMaxBytes = 6_000_000_000
    };

    private static StorageSnapshot Snapshot(int diskNumber = 3) =>
        new([Disk(diskNumber)], [Partition(diskNumber)], DateTimeOffset.UtcNow);

    private static MainViewModel Build(IDiskInventoryService inventory) =>
        new(inventory, new RejectingExecutor(), new SafetyService((char?)null),
            new QuietLogger(), new RejectingLicense());

    [Fact]
    public async Task Refresh_populates_navigation_metrics_and_selected_disk()
    {
        var vm = Build(new StaticInventory(Snapshot()));
        await vm.RefreshAsync();
        Assert.Single(vm.Disks);
        Assert.Single(vm.Partitions);
        Assert.NotNull(vm.SelectedDisk);
        Assert.Equal("1 dispositivo", vm.DiskCountLabel);
        Assert.Equal("1 partição", vm.PartitionCountLabel);
        Assert.Contains("Disco 3", vm.SelectedTargetTitle);
        Assert.NotEmpty(vm.PartitionSegments);
        Assert.Equal(8_000_000_000L, vm.OverviewMetrics.CapacityBytes);
        Assert.Equal(6_000_000_000L, vm.OverviewMetrics.UnallocatedBytes);
        Assert.Equal(6_000_000_000L, vm.SelectedDiskMetrics!.UnallocatedBytes);
        Assert.Equal(25d, vm.SelectedAllocatedPercent);
        Assert.Equal("0 volumes monitorados", vm.OverviewMetrics.MonitoredVolumesLabel);
        Assert.Contains(vm.ContextOperations, choice => choice.Type == StorageOperationType.SetDiskOnline && choice.IsAvailable);
    }

    [Fact]
    public async Task Selecting_partition_marks_map_segment_and_changes_available_actions()
    {
        var vm = Build(new StaticInventory(Snapshot()));
        await vm.RefreshAsync();
        vm.SelectedPartition = Assert.Single(vm.Partitions);
        Assert.Contains(vm.PartitionSegments, part => part.IsSelected);
        Assert.Contains(vm.ContextOperations, action =>
            action.Type == StorageOperationType.SetVolumeLabel && action.IsAvailable);
        Assert.Contains(vm.ContextOperations, action =>
            action.Type == StorageOperationType.SetDiskOnline && !action.IsAvailable);
        Assert.True(vm.QueueCommand.CanExecute(null));
        vm.SelectedPartition = null;
        Assert.DoesNotContain(vm.PartitionSegments, segment => segment.IsSelected);
    }

    [Fact]
    public async Task Disk_zero_shows_explanations_but_no_available_preparations()
    {
        var vm = Build(new StaticInventory(Snapshot(0)));
        await vm.RefreshAsync();
        Assert.Equal(0, vm.SelectedDisk!.Number);
        Assert.All(vm.ContextOperations, action => Assert.False(action.IsAvailable));
        Assert.False(vm.QueueCommand.CanExecute(null));
    }

    [Fact]
    public async Task Apply_button_remains_disabled_even_when_operations_are_queued()
    {
        var disk = Disk();
        var vm = Build(new StaticInventory(new StorageSnapshot([disk], [], DateTimeOffset.UtcNow)));
        await vm.RefreshAsync();
        Assert.True(vm.QueueCommand.CanExecute(null));
        vm.QueueCommand.Execute(null);
        Assert.Single(vm.PendingOperations);
        Assert.False(vm.CanApplyWrites);
        Assert.False(vm.ApplyCommand.CanExecute(null));
        Assert.Contains("desativadas", vm.WriteModeLabel);
    }

    [Fact]
    public async Task Refresh_preserves_selected_partition_by_identity()
    {
        var vm = Build(new StaticInventory(Snapshot()));
        await vm.RefreshAsync();
        vm.SelectedPartition = Assert.Single(vm.Partitions);
        await vm.RefreshAsync();
        Assert.NotNull(vm.SelectedPartition);
        Assert.Equal(Partition().Identity, vm.SelectedPartition.Identity);
        Assert.Contains(vm.PartitionSegments, part => part.IsSelected);
    }

    [Fact]
    public async Task Inventory_error_removes_stale_disks_instead_of_leaving_them_available()
    {
        var inventory = new SwitchableInventory(Snapshot());
        var vm = Build(inventory);
        await vm.RefreshAsync();
        Assert.Single(vm.Disks);
        inventory.Fail = true;
        await vm.RefreshAsync();
        Assert.Empty(vm.Disks);
        Assert.Null(vm.SelectedDisk);
        Assert.Empty(vm.Partitions);
        Assert.Empty(vm.PartitionSegments);
        Assert.Equal(0, vm.OverviewMetrics.DiskCount);
        Assert.Null(vm.SelectedDiskMetrics);
        Assert.Equal(0d, vm.SelectedAllocatedPercent);
        Assert.All(vm.ContextOperations, choice => Assert.False(choice.IsAvailable));
        Assert.False(vm.QueueCommand.CanExecute(null));
    }

    [Fact]
    public async Task Unrelated_hidden_inputs_are_not_attached_to_whole_disk_operation()
    {
        var disk = Disk();
        var vm = Build(new StaticInventory(new StorageSnapshot([disk], [], DateTimeOffset.UtcNow)));
        await vm.RefreshAsync();
        vm.SelectedOperationType = StorageOperationType.SetDiskOnline;
        vm.SizeInput = "invalid stale size";
        vm.DriveLetterInput = "ABC";
        vm.LabelInput = "old label from another operation";
        vm.AllocationUnitInput = "invalid stale cluster";
        vm.SelectedFileSystem = "exFAT";
        vm.QuickFormat = false;
        Assert.True(vm.QueueCommand.CanExecute(null));
        vm.QueueCommand.Execute(null);
        var queued = Assert.Single(vm.PendingOperations);
        Assert.Null(queued.SizeBytes);
        Assert.Null(queued.DriveLetter);
        Assert.Null(queued.AllocationUnitSize);
        Assert.Equal("", queued.Label);
        Assert.Equal("NTFS", queued.FileSystem);
        Assert.True(queued.QuickFormat);
        Assert.Null(queued.PartitionNumber);
    }

    [Theory]
    [InlineData("AB")]
    [InlineData("C")]
    [InlineData("1")]
    public async Task Drive_letter_form_refuses_invalid_exact_inputs(string rawInput)
    {
        var vm = Build(new StaticInventory(Snapshot()));
        await vm.RefreshAsync();
        vm.SelectedPartition = Assert.Single(vm.Partitions);
        vm.SelectedOperationType = StorageOperationType.SetDriveLetter;
        vm.DriveLetterInput = rawInput;
        Assert.True(vm.QueueCommand.CanExecute(null));
        vm.QueueCommand.Execute(null);
        Assert.Empty(vm.PendingOperations);
    }

    [Fact]
    public async Task Formatting_does_not_inherit_stale_resize_size_or_letter()
    {
        var vm = Build(new StaticInventory(Snapshot()));
        await vm.RefreshAsync();
        vm.SelectedPartition = Assert.Single(vm.Partitions);
        vm.SelectedOperationType = StorageOperationType.FormatPartition;
        vm.SizeInput = "not a number";
        vm.DriveLetterInput = "AB";
        vm.AllocationUnitInput = "4096";
        vm.LabelInput = "Novo";
        Assert.True(vm.QueueCommand.CanExecute(null));
        vm.QueueCommand.Execute(null);
        var queued = Assert.Single(vm.PendingOperations);
        Assert.Null(queued.SizeBytes);
        Assert.Null(queued.DriveLetter);
        Assert.Equal(4096, queued.AllocationUnitSize);
        Assert.Equal("Novo", queued.Label);
        Assert.False(vm.ApplyCommand.CanExecute(null));
    }

    [Fact]
    public async Task Format_preview_rejects_invalid_cluster_size_before_adding_to_queue()
    {
        var vm = Build(new StaticInventory(Snapshot()));
        await vm.RefreshAsync();
        vm.SelectedPartition = Assert.Single(vm.Partitions);
        vm.SelectedOperationType = StorageOperationType.FormatPartition;
        vm.AllocationUnitInput = "750";
        vm.QueueCommand.Execute(null);
        Assert.Empty(vm.PendingOperations);
    }

    [Fact]
    public async Task Resize_selection_shows_actual_windows_bounds_and_fills_exact_bytes()
    {
        var vm = Build(new StaticInventory(Snapshot()));
        await vm.RefreshAsync();
        vm.SelectedPartition = Assert.Single(vm.Partitions);
        vm.SelectedOperationType = StorageOperationType.ResizePartition;
        Assert.Contains(vm.ContextOperations, c =>
            c.Type == StorageOperationType.ResizePartition && c.IsAvailable);
        Assert.NotNull(vm.SelectedResizeRange);
        Assert.True(vm.UseResizeMinimumCommand.CanExecute(null));
        Assert.True(vm.UseResizeMaximumCommand.CanExecute(null));
        Assert.Contains("Mínimo", vm.ResizeBoundsSummary);
        vm.UseResizeMinimumCommand.Execute(null);
        Assert.Equal("1000000000 B", vm.SizeInput);
        vm.QueueCommand.Execute(null);
        Assert.Equal(1_000_000_000, Assert.Single(vm.PendingOperations).SizeBytes);
        Assert.False(vm.ApplyCommand.CanExecute(null));
    }

    [Fact]
    public async Task Resizing_to_windows_maximum_uses_unrounded_bytes()
    {
        var vm = Build(new StaticInventory(Snapshot()));
        await vm.RefreshAsync();
        vm.SelectedPartition = Assert.Single(vm.Partitions);
        vm.SelectedOperationType = StorageOperationType.ResizePartition;
        vm.UseResizeMaximumCommand.Execute(null);
        Assert.Equal("6000000000 B", vm.SizeInput);
        vm.QueueCommand.Execute(null);
        Assert.Equal(6_000_000_000, Assert.Single(vm.PendingOperations).SizeBytes);
    }

    [Fact]
    public async Task Missing_windows_resize_capabilities_disables_actions_and_presets()
    {
        var part = Partition() with { ResizeMinBytes = null, ResizeMaxBytes = null };
        var vm = Build(new StaticInventory(new StorageSnapshot([Disk()], [part], DateTimeOffset.UtcNow)));
        await vm.RefreshAsync();
        vm.SelectedPartition = Assert.Single(vm.Partitions);
        vm.SelectedOperationType = StorageOperationType.ResizePartition;
        Assert.Contains("não disponíveis", vm.ResizeBoundsSummary, StringComparison.OrdinalIgnoreCase);
        Assert.False(vm.QueueCommand.CanExecute(null));
        Assert.False(vm.UseResizeMinimumCommand.CanExecute(null));
        Assert.False(vm.UseResizeMaximumCommand.CanExecute(null));
        Assert.Empty(vm.PendingOperations);
    }

    [Fact]
    public async Task Refresh_removes_resize_presets_if_windows_capabilities_disappear()
    {
        var inventory = new SwitchableInventory(Snapshot());
        var vm = Build(inventory);
        await vm.RefreshAsync();
        vm.SelectedPartition = Assert.Single(vm.Partitions);
        vm.SelectedOperationType = StorageOperationType.ResizePartition;
        Assert.True(vm.UseResizeMaximumCommand.CanExecute(null));
        inventory.OverrideSnapshot = new StorageSnapshot([Disk()],
            [Partition() with { ResizeMinBytes = null, ResizeMaxBytes = null }], DateTimeOffset.UtcNow);
        await vm.RefreshAsync();
        Assert.False(vm.UseResizeMaximumCommand.CanExecute(null));
        Assert.False(vm.QueueCommand.CanExecute(null));
    }

    [Fact]
    public async Task Move_preview_is_available_but_never_queued_or_executed()
    {
        var vm = Build(new StaticInventory(Snapshot()));
        await vm.RefreshAsync();
        vm.SelectedPartition = Assert.Single(vm.Partitions);
        vm.SelectedOperationType = StorageOperationType.MovePartition;
        Assert.True(vm.PreviewMoveCommand.CanExecute(null));
        Assert.True(vm.SuggestMoveDestinationCommand.CanExecute(null));
        Assert.False(vm.QueueCommand.CanExecute(null));
        Assert.False(vm.ApplyCommand.CanExecute(null));
        vm.SuggestMoveDestinationCommand.Execute(null);
        Assert.Contains("SIMULAÇÃO", vm.MovePreviewSummary);
        Assert.Contains("Sem mecanismo de cópia", vm.MovePreviewSummary);
        Assert.NotEmpty(vm.MoveDestinationInput);
        Assert.Empty(vm.PendingOperations);
        Assert.False(vm.CanApplyWrites);
    }

    [Fact]
    public async Task Move_preview_rejects_overlapping_destination_and_invalidates_on_refresh()
    {
        var vm = Build(new StaticInventory(Snapshot()));
        await vm.RefreshAsync();
        vm.SelectedPartition = Assert.Single(vm.Partitions);
        vm.SelectedOperationType = StorageOperationType.MovePartition;
        vm.MoveDestinationInput = "1048576 B";
        vm.PreviewMoveCommand.Execute(null);
        Assert.Contains("recusada", vm.MovePreviewSummary, StringComparison.OrdinalIgnoreCase);
        vm.SuggestMoveDestinationCommand.Execute(null);
        Assert.Contains("SIMULAÇÃO", vm.MovePreviewSummary);
        await vm.RefreshAsync();
        Assert.DoesNotContain("SIMULAÇÃO", vm.MovePreviewSummary);
        Assert.False(vm.QueueCommand.CanExecute(null));
    }

    [Fact]
    public async Task Move_preview_disappears_when_selection_or_inventory_is_invalid()
    {
        var inventory = new SwitchableInventory(Snapshot());
        var vm = Build(inventory);
        await vm.RefreshAsync();
        vm.SelectedPartition = Assert.Single(vm.Partitions);
        vm.SelectedOperationType = StorageOperationType.MovePartition;
        vm.SuggestMoveDestinationCommand.Execute(null);
        Assert.Contains("SIMULAÇÃO", vm.MovePreviewSummary);
        vm.SelectedPartition = null;
        Assert.False(vm.PreviewMoveCommand.CanExecute(null));
        Assert.Contains("Alvo alterado", vm.MovePreviewSummary);
        inventory.Fail = true;
        await vm.RefreshAsync();
        Assert.False(vm.PreviewMoveCommand.CanExecute(null));
        Assert.Null(vm.SelectedDisk);
        Assert.Empty(vm.PendingOperations);
    }

    [Fact]
    public async Task Conversion_inspector_shows_warnings_but_does_not_offer_or_queue_conversion()
    {
        var vm = Build(new StaticInventory(Snapshot()));
        await vm.RefreshAsync();
        Assert.True(vm.InspectGptConversionCommand.CanExecute(null));
        Assert.True(vm.InspectMbrConversionCommand.CanExecute(null));
        Assert.Null(vm.ConversionReport);
        vm.InspectMbrConversionCommand.Execute(null);
        Assert.NotNull(vm.ConversionReport);
        Assert.Equal(DiskPartitionScheme.Mbr, vm.ConversionReport.DesiredScheme);
        Assert.False(vm.ConversionReport.DataPreservingConversionSupported);
        Assert.Contains("CONVERSÃO INDISPONÍVEL", vm.ConversionSummary);
        Assert.Empty(vm.PendingOperations);
        Assert.False(vm.ApplyCommand.CanExecute(null));
        Assert.False(vm.CanApplyWrites);
    }

    [Fact]
    public async Task Conversion_inspector_identifies_raw_empty_only_as_initialization_candidate()
    {
        var raw = Disk() with { PartitionStyle = "RAW" };
        var vm = Build(new StaticInventory(new StorageSnapshot([raw], [], DateTimeOffset.UtcNow)));
        await vm.RefreshAsync();
        vm.InspectGptConversionCommand.Execute(null);
        Assert.True(vm.ConversionReport!.IsEmptyRawInitializationCandidate);
        Assert.Contains("APENAS INICIALIZAÇÃO", vm.ConversionSummary);
        Assert.Empty(vm.PendingOperations);
    }

    [Fact]
    public async Task Conversion_inspection_requires_disk_selection_and_invalidates_on_refresh()
    {
        var inventory = new SwitchableInventory(Snapshot());
        var vm = Build(inventory);
        await vm.RefreshAsync();
        vm.InspectMbrConversionCommand.Execute(null);
        Assert.NotNull(vm.ConversionReport);
        vm.SelectedPartition = Assert.Single(vm.Partitions);
        Assert.False(vm.InspectMbrConversionCommand.CanExecute(null));
        Assert.Null(vm.ConversionReport);
        vm.SelectedPartition = null;
        Assert.True(vm.InspectGptConversionCommand.CanExecute(null));
        vm.InspectGptConversionCommand.Execute(null);
        Assert.NotNull(vm.ConversionReport);
        await vm.RefreshAsync();
        Assert.Null(vm.ConversionReport);
        Assert.Contains("refaça", vm.ConversionSummary, StringComparison.OrdinalIgnoreCase);
        inventory.Fail = true;
        await vm.RefreshAsync();
        Assert.False(vm.InspectGptConversionCommand.CanExecute(null));
        Assert.Null(vm.ConversionReport);
        Assert.Contains("indisponível", vm.ConversionSummary, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class StaticInventory(StorageSnapshot snapshot) : IDiskInventoryService
    {
        public Task<StorageSnapshot> ReadAsync(CancellationToken token) => Task.FromResult(snapshot);
    }

    private sealed class SwitchableInventory(StorageSnapshot snapshot) : IDiskInventoryService
    {
        public bool Fail { get; set; }
        public StorageSnapshot? OverrideSnapshot { get; set; }
        public Task<StorageSnapshot> ReadAsync(CancellationToken token) =>
            Fail ? Task.FromException<StorageSnapshot>(new IOException("Fake IO failure"))
                : Task.FromResult(OverrideSnapshot ?? snapshot);
    }

    private sealed class RejectingExecutor : IStorageOperationExecutor
    {
        public Task<OperationResult> ExecuteAsync(StorageOperation op, CancellationToken token) =>
            throw new InvalidOperationException("Executor must not be invoked in UI tests.");
    }

    private sealed class QuietLogger : IAppLogger
    {
        public string LogDirectory => "";
        public void Information(string eventName, object? data = null) { }
        public void Error(string eventName, Exception exception, object? data = null) { }
    }

    private sealed class RejectingLicense : ILicenseEntitlements
    {
        private static Task<GslLicenseDecision> Rejected() =>
            Task.FromResult(GslLicenseDecision.Deny(GslLicenseStatus.NotActivated, "Offline"));
        public Task<GslLicenseDecision> CheckAsync(CancellationToken token) => Rejected();
        public Task<GslLicenseDecision> ActivateAsync(string key, CancellationToken token) => Rejected();
        public Task<GslLicenseDecision> StartTrialAsync(CancellationToken token) => Rejected();
        public Task<GslLicenseDecision> ValidateOnlineAsync(CancellationToken token) => Rejected();
        public Task<GslLicenseDecision> DeactivateAsync(CancellationToken token) => Rejected();
    }
}
