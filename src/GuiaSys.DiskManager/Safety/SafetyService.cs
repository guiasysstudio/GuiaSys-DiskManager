using GuiaSys.DiskManager.Models;
using GuiaSys.DiskManager.Services;
using System.IO;

namespace GuiaSys.DiskManager.Safety;

public sealed class SafetyService : ISafetyService
{
    private static readonly HashSet<string> SupportedFileSystems = new(StringComparer.OrdinalIgnoreCase) { "NTFS", "FAT32", "exFAT" };

    public SafetyDecision Evaluate(StorageOperation operation, StorageSnapshot currentSnapshot)
    {
        var disk = currentSnapshot.FindDisk(operation.DiskNumber);
        if (disk is null) return SafetyDecision.Deny("O disco não existe mais ou foi removido.");
        if (!string.Equals(disk.Identity, operation.ExpectedDiskIdentity, StringComparison.Ordinal))
            return SafetyDecision.Deny("A identidade do disco mudou desde a seleção. Atualize o inventário antes de continuar.");

        if (operation.DiskNumber == 0 && operation.IsDestructive)
            return SafetyDecision.Deny("Operações destrutivas no Disco 0 são bloqueadas por política de segurança.");

        var diskPartitions = currentSnapshot.Partitions.Where(p => p.DiskNumber == disk.Number).ToArray();
        var containsProtectedPartition = disk.IsBoot || disk.IsSystem || diskPartitions.Any(partition => IsProtected(partition) || ContainsRunningExecutable(partition));
        if (containsProtectedPartition && AffectsWholeDisk(operation.Type))
            return SafetyDecision.Deny("O disco contém Windows, boot, sistema, EFI ou recuperação e está protegido.");

        PartitionInfo? partition = null;
        if (RequiresPartition(operation.Type))
        {
            if (operation.PartitionNumber is null) return SafetyDecision.Deny("A operação exige uma partição explícita.");
            partition = currentSnapshot.FindPartition(operation.DiskNumber, operation.PartitionNumber.Value);
            if (partition is null) return SafetyDecision.Deny("A partição não existe mais. Atualize o inventário.");
            if (IsProtected(partition) || ContainsRunningExecutable(partition)) return SafetyDecision.Deny("Partições de boot, sistema, EFI, MSR, Recovery, ocultas, C: ou que hospedam o aplicativo não podem ser alteradas.");
        }

        if (operation.Type is StorageOperationType.InitializeGpt or StorageOperationType.InitializeMbr)
        {
            if (!string.Equals(disk.PartitionStyle, "RAW", StringComparison.OrdinalIgnoreCase) || diskPartitions.Length > 0)
                return SafetyDecision.Deny("Somente discos RAW sem partições podem ser inicializados. A conversão com dados exige limpeza e não é oferecida.");
        }

        if (operation.Type == StorageOperationType.FormatPartition && !SupportedFileSystems.Contains(operation.FileSystem))
            return SafetyDecision.Deny("Sistema de arquivos inválido. Use NTFS, FAT32 ou exFAT.");
        if (operation.Label.Length > 32 || operation.Label.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return SafetyDecision.Deny("O label contém caracteres inválidos ou excede 32 caracteres.");
        if (operation.DriveLetter is char letter && (letter < 'D' || letter > 'Z'))
            return SafetyDecision.Deny("Use uma letra entre D e Z. C: permanece sempre protegida.");
        if (operation.Type == StorageOperationType.SetDriveLetter && operation.DriveLetter is null)
            return SafetyDecision.Deny("Informe uma letra entre D e Z.");
        if (operation.Type is StorageOperationType.ResizePartition && operation.SizeBytes is not > 0)
            return SafetyDecision.Deny("Informe um tamanho positivo para redimensionar.");
        if (operation.Type == StorageOperationType.CreatePartition && operation.SizeBytes is <= 0)
            return SafetyDecision.Deny("O tamanho da nova partição deve ser positivo ou deixado em branco para usar o máximo.");
        if (operation.AllocationUnitSize is int allocation && (allocation < 512 || allocation > 2_097_152 || !IsPowerOfTwo(allocation)))
            return SafetyDecision.Deny("O tamanho da unidade de alocação deve ser uma potência de 2 entre 512 bytes e 2 MiB.");

        return SafetyDecision.Permit(operation.IsDestructive);
    }

    public static bool IsProtected(PartitionInfo partition)
    {
        var type = partition.Type.ToUpperInvariant();
        return partition.IsBoot || partition.IsSystem || partition.IsHidden || string.Equals(partition.DriveLetter, "C", StringComparison.OrdinalIgnoreCase)
            || type.Contains("SYSTEM") || type.Contains("EFI") || type.Contains("RESERVED") || type.Contains("RECOVERY");
    }

    private static bool AffectsWholeDisk(StorageOperationType type) => type is StorageOperationType.SetDiskOffline or StorageOperationType.SetDiskReadOnly
        or StorageOperationType.InitializeGpt or StorageOperationType.InitializeMbr;
    private static bool RequiresPartition(StorageOperationType type) => type is StorageOperationType.DeletePartition or StorageOperationType.FormatPartition
        or StorageOperationType.SetDriveLetter or StorageOperationType.RemoveDriveLetter or StorageOperationType.ResizePartition or StorageOperationType.SetVolumeLabel;
    private static bool IsPowerOfTwo(int value) => (value & (value - 1)) == 0;
    private static bool ContainsRunningExecutable(PartitionInfo partition)
    {
        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath) || partition.DriveLetter.Length != 1) return false;
        var root = Path.GetPathRoot(executablePath);
        return root is { Length: >= 1 } && char.ToUpperInvariant(root[0]) == char.ToUpperInvariant(partition.DriveLetter[0]);
    }
}
