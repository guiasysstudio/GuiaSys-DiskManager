using GuiaSys.DiskManager.Licensing;
using GuiaSys.DiskManager.Models;
using GuiaSys.DiskManager.Safety;
using GuiaSys.DiskManager.Services;
using GuiaSys.DiskManager.ViewModels;

namespace GuiaSys.DiskManager.Tests;

public sealed class MainViewModelUiTests
{
    private static DiskInfo Disk(int number = 3) => new()
    {
        Number = number, UniqueId = $"c05-test-{number}", FriendlyName = "Temporary VHDX",
        Size = 8_000_000_000, PartitionStyle = "GPT"
    };

    private static PartitionInfo Partition(int diskNumber = 3) => new()
    {
        DiskNumber = diskNumber, PartitionNumber = 1,
        PartitionGuid = "a911ce1e-d646-4af0-b245-c346c5600c65",
        GptType = "ebd0a0a2-b9e5-4433-87c0-68b6b72699c7",
        Type = "Basic", Offset = 1_048_576, Size = 2_000_000_000,
        DriveLetter = "E", AccessPaths = "", FileSystem = "NTFS"
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

    private sealed class StaticInventory(StorageSnapshot snapshot) : IDiskInventoryService
    {
        public Task<StorageSnapshot> ReadAsync(CancellationToken token) => Task.FromResult(snapshot);
    }

    private sealed class SwitchableInventory(StorageSnapshot snapshot) : IDiskInventoryService
    {
        public bool Fail { get; set; }
        public Task<StorageSnapshot> ReadAsync(CancellationToken token) =>
            Fail ? Task.FromException<StorageSnapshot>(new IOException("Fake IO failure")) : Task.FromResult(snapshot);
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
