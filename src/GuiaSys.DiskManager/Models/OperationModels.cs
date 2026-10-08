namespace GuiaSys.DiskManager.Models;

public enum StorageOperationType
{
    SetDiskOnline,
    SetDiskOffline,
    SetDiskReadOnly,
    ClearDiskReadOnly,
    InitializeGpt,
    InitializeMbr,
    CreatePartition,
    DeletePartition,
    FormatPartition,
    SetDriveLetter,
    RemoveDriveLetter,
    ResizePartition,
    SetVolumeLabel
}

[Flags]
public enum StorageOperationRisk
{
    None = 0,
    MetadataMutation = 1 << 0,
    LayoutMutation = 1 << 1,
    Destructive = 1 << 2,
    ProtectionChange = 1 << 3,
    WholeDiskStateChange = 1 << 4
}

public sealed record StorageOperation
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required StorageOperationType Type { get; init; }
    public required int DiskNumber { get; init; }
    public int? PartitionNumber { get; init; }
    public required string ExpectedDiskIdentity { get; init; }
    public long? SizeBytes { get; init; }
    public string FileSystem { get; init; } = "NTFS";
    public string Label { get; init; } = "";
    public char? DriveLetter { get; init; }
    public bool QuickFormat { get; init; } = true;
    public int? AllocationUnitSize { get; init; }
    public DateTimeOffset QueuedAt { get; init; } = DateTimeOffset.Now;

    public StorageOperationRisk Risk => Type switch
    {
        StorageOperationType.SetDiskOnline or StorageOperationType.SetDiskOffline
            => StorageOperationRisk.WholeDiskStateChange,
        StorageOperationType.SetDiskReadOnly or StorageOperationType.ClearDiskReadOnly
            => StorageOperationRisk.ProtectionChange | StorageOperationRisk.WholeDiskStateChange,
        StorageOperationType.InitializeGpt or StorageOperationType.InitializeMbr
            => StorageOperationRisk.LayoutMutation | StorageOperationRisk.Destructive | StorageOperationRisk.WholeDiskStateChange,
        StorageOperationType.CreatePartition
            => StorageOperationRisk.LayoutMutation,
        StorageOperationType.DeletePartition
            => StorageOperationRisk.LayoutMutation | StorageOperationRisk.Destructive,
        StorageOperationType.FormatPartition
            => StorageOperationRisk.Destructive,
        StorageOperationType.ResizePartition
            => StorageOperationRisk.LayoutMutation,
        StorageOperationType.SetDriveLetter or StorageOperationType.RemoveDriveLetter or StorageOperationType.SetVolumeLabel
            => StorageOperationRisk.MetadataMutation,
        _ => StorageOperationRisk.None
    };

    public bool IsMutation => Risk != StorageOperationRisk.None;
    public bool IsDestructive => Risk.HasFlag(StorageOperationRisk.Destructive);
    public bool RequiresReinforcedConfirmation => Risk.HasFlag(StorageOperationRisk.Destructive)
        || Risk.HasFlag(StorageOperationRisk.LayoutMutation)
        || Risk.HasFlag(StorageOperationRisk.ProtectionChange);

    public string Description => Type switch
    {
        StorageOperationType.SetDiskOnline => $"Colocar o Disco {DiskNumber} online",
        StorageOperationType.SetDiskOffline => $"Colocar o Disco {DiskNumber} offline",
        StorageOperationType.SetDiskReadOnly => $"Proteger o Disco {DiskNumber} contra gravação",
        StorageOperationType.ClearDiskReadOnly => $"Liberar gravação no Disco {DiskNumber}",
        StorageOperationType.InitializeGpt => $"Inicializar o Disco {DiskNumber} como GPT",
        StorageOperationType.InitializeMbr => $"Inicializar o Disco {DiskNumber} como MBR",
        StorageOperationType.CreatePartition => $"Criar partição de {(SizeBytes is null ? "tamanho máximo" : ByteSize.Format(SizeBytes.Value))} no Disco {DiskNumber}",
        StorageOperationType.DeletePartition => $"Excluir Partição {PartitionNumber} do Disco {DiskNumber}",
        StorageOperationType.FormatPartition => $"Formatar Partição {PartitionNumber} como {FileSystem}",
        StorageOperationType.SetDriveLetter => $"Atribuir letra {DriveLetter}: à Partição {PartitionNumber}",
        StorageOperationType.RemoveDriveLetter => $"Remover letra da Partição {PartitionNumber}",
        StorageOperationType.ResizePartition => $"Redimensionar Partição {PartitionNumber} para {ByteSize.Format(SizeBytes ?? 0)}",
        StorageOperationType.SetVolumeLabel => $"Definir label “{Label}” na Partição {PartitionNumber}",
        _ => Type.ToString()
    };
}

public sealed record OperationResult(Guid OperationId, bool Succeeded, string Message, TimeSpan Duration);

public sealed record SafetyDecision(bool Allowed, string Reason, bool RequiresReinforcedConfirmation = false)
{
    public static SafetyDecision Permit(bool reinforced = false) => new(true, "Operação permitida após validações.", reinforced);
    public static SafetyDecision Deny(string reason) => new(false, reason);
}
