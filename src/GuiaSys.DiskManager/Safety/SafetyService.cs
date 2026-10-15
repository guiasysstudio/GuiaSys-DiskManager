using GuiaSys.DiskManager.Models;
using GuiaSys.DiskManager.Services;
using GuiaSys.DiskManager.Operations;
using System.IO;

namespace GuiaSys.DiskManager.Safety;

public sealed class SafetyService : ISafetyService
{
    private static readonly HashSet<string> SupportedFileSystems = new(StringComparer.OrdinalIgnoreCase) { "NTFS", "FAT32", "exFAT" };
    private readonly char? _executableDriveLetter;

    public SafetyService() : this(GetExecutableDriveLetter()) { }
    public SafetyService(char? executableDriveLetter) => _executableDriveLetter = executableDriveLetter is char value ? char.ToUpperInvariant(value) : null;

    public SafetyDecision Evaluate(StorageOperation operation, StorageSnapshot currentSnapshot)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(currentSnapshot);
        if (!Enum.IsDefined(operation.Type) || !operation.IsMutation || operation.Id == Guid.Empty || operation.DiskNumber < 0)
            return SafetyDecision.Deny("Identificador ou tipo de operação inválido.");
        var disk = currentSnapshot.FindDisk(operation.DiskNumber);
        if (disk is null) return SafetyDecision.Deny("O disco não existe mais ou foi removido.");
        if (!string.Equals(disk.Identity, operation.ExpectedDiskIdentity, StringComparison.Ordinal))
            return SafetyDecision.Deny("A identidade do disco mudou desde a seleção. Atualize o inventário antes de continuar.");

        if (operation.IsMutation && !disk.HasStrongIdentity)
            return SafetyDecision.Deny("Identidade do disco insuficiente para operações de gravação.");

        // 0.1.0 policy: Disk 0 is inventory-only. No mutation is permitted, regardless of risk category.
        if (operation.DiskNumber == 0 && operation.IsMutation)
            return SafetyDecision.Deny("O Disco 0 é somente leitura por política de segurança e não aceita nenhuma alteração.");

        var diskPartitions = currentSnapshot.Partitions.Where(p => p.DiskNumber == disk.Number).ToArray();
        var containsProtectedPartition = disk.IsBoot || disk.IsSystem || diskPartitions.Any(partition => IsProtected(partition) || ContainsRunningExecutable(partition));
        if (containsProtectedPartition && (AffectsWholeDisk(operation) || operation.Risk.HasFlag(StorageOperationRisk.LayoutMutation)))
            return SafetyDecision.Deny("O disco contém Windows, boot, sistema, EFI ou recuperação e está protegido.");

        PartitionInfo? partition = null;
        if (RequiresPartition(operation.Type))
        {
            if (operation.PartitionNumber is null) return SafetyDecision.Deny("A operação exige uma partição explícita.");
            partition = currentSnapshot.FindPartition(operation.DiskNumber, operation.PartitionNumber.Value);
            if (partition is null) return SafetyDecision.Deny("A partição não existe mais. Atualize o inventário.");
            if (IsProtected(partition) || ContainsRunningExecutable(partition)) return SafetyDecision.Deny("Partições de boot, sistema, EFI, MSR, Recovery, ocultas, C: ou que hospedam o aplicativo não podem ser alteradas.");
            if (!partition.HasStrongIdentity)
                return SafetyDecision.Deny("Partição sem identidade GPT forte; operações MBR ainda não autorizadas.");
            if (!string.Equals(disk.PartitionStyle, "GPT", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(partition.GptType))
                return SafetyDecision.Deny("Partição sem esquema GPT e tipo GUID confirmado; a mutação não é autorizada.");
            if (string.IsNullOrWhiteSpace(operation.ExpectedPartitionIdentity) ||
                !string.Equals(partition.Identity, operation.ExpectedPartitionIdentity, StringComparison.Ordinal))
                return SafetyDecision.Deny("A identidade ou geometria da partição mudou. Atualize o inventário.");
        }

        if (operation.Type is StorageOperationType.InitializeGpt or StorageOperationType.InitializeMbr)
        {
            if (disk.IsOffline || disk.IsReadOnly)
                return SafetyDecision.Deny("O disco precisa estar online e com gravação liberada para ser inicializado.");
            if (!string.Equals(disk.PartitionStyle, "RAW", StringComparison.OrdinalIgnoreCase) || diskPartitions.Length > 0)
                return SafetyDecision.Deny("Somente discos RAW sem partições podem ser inicializados. A conversão com dados exige limpeza e não é oferecida.");
        }

        if (operation.Type == StorageOperationType.CreatePartition)
        {
            if (disk.IsOffline || disk.IsReadOnly)
                return SafetyDecision.Deny("O disco precisa estar online e gravável para criar uma partição.");
            // The C04 postcondition verifier requires a strong GPT partition GUID.
            // Until MBR creation has a separately audited identity/postcondition model, deny it.
            if (!string.Equals(disk.PartitionStyle, "GPT", StringComparison.OrdinalIgnoreCase))
                return SafetyDecision.Deny("Criação autorizável somente em disco GPT. MBR/RAW requer homologação específica.");
        }

        if (partition is not null && (disk.IsOffline || disk.IsReadOnly || partition.IsOffline || partition.IsReadOnly))
            return SafetyDecision.Deny("O disco e a partição precisam estar online e com gravação liberada.");

        if (operation.Type == StorageOperationType.FormatPartition && !SupportedFileSystems.Contains(operation.FileSystem))
            return SafetyDecision.Deny("Sistema de arquivos inválido. Use NTFS, FAT32 ou exFAT.");
        if (operation.Label.Length > 32 || operation.Label.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return SafetyDecision.Deny("O label contém caracteres inválidos ou excede 32 caracteres.");
        if (operation.DriveLetter is char letter && (letter < 'D' || letter > 'Z'))
            return SafetyDecision.Deny("Use uma letra entre D e Z. C: permanece sempre protegida.");
        if (operation.Type == StorageOperationType.SetDriveLetter && operation.DriveLetter is char requested &&
            currentSnapshot.Partitions.Any(p => (p.DiskNumber != operation.DiskNumber || p.PartitionNumber != operation.PartitionNumber) &&
                string.Equals(p.DriveLetter, requested.ToString(), StringComparison.OrdinalIgnoreCase)))
            return SafetyDecision.Deny("A letra solicitada já está em uso.");
        if (operation.Type == StorageOperationType.SetDriveLetter && operation.DriveLetter is null)
            return SafetyDecision.Deny("Informe uma letra entre D e Z.");
        if (operation.Type is StorageOperationType.ResizePartition && operation.SizeBytes is not > 0)
            return SafetyDecision.Deny("Informe um tamanho positivo para redimensionar.");
        if (operation.Type == StorageOperationType.CreatePartition && operation.SizeBytes is <= 0)
            return SafetyDecision.Deny("O tamanho da nova partição deve ser positivo ou deixado em branco para usar o máximo.");
        if (operation.Type == StorageOperationType.RemoveDriveLetter &&
            string.IsNullOrWhiteSpace(partition?.DriveLetter))
            return SafetyDecision.Deny("A partição não possui letra de unidade para remover.");
        if (operation.AllocationUnitSize is int allocation && (allocation < 512 || allocation > 2_097_152 || !IsPowerOfTwo(allocation)))
            return SafetyDecision.Deny("O tamanho da unidade de alocação deve ser uma potência de 2 entre 512 bytes e 2 MiB.");

        // The executor calls this policy independently with a fresh inventory. Essential
        // geometry checks must not depend solely on the UI's preview/planning stage.
        if (operation.Type is StorageOperationType.CreatePartition or StorageOperationType.ResizePartition)
        {
            StorageLayout layout;
            try { layout = StorageGeometry.Build(currentSnapshot); }
            catch (InvalidDataException)
            {
                return SafetyDecision.Deny("Geometria inconsistente; nenhuma operação será preparada.");
            }
            var diskLayout = layout.FindDisk(disk.Number);
            if (diskLayout is null) return SafetyDecision.Deny("Layout do disco não encontrado.");
            if (operation.Type == StorageOperationType.CreatePartition)
            {
                if (diskLayout.LargestFreeExtentBytes <= 0 ||
                    operation.SizeBytes is long newSize && newSize > diskLayout.LargestFreeExtentBytes)
                    return SafetyDecision.Deny("Não existe uma faixa não alocada contígua suficiente.");
            }
            else if (partition is not null && operation.SizeBytes is long requested &&
                requested > partition.Size)
            {
                var adjacent = diskLayout.Extents.FirstOrDefault(e =>
                    e.IsUnallocated && e.Offset == partition.Offset + partition.Size);
                if (adjacent is null || requested - partition.Size > adjacent.Length)
                    return SafetyDecision.Deny("Expansão ultrapassa o espaço contíguo não alocado.");
            }
        }

        // Independently enforced schema: reject stale parameters, wrong targets and
        // noncanonical text rather than silently accepting hidden UI inputs.
        var shape = EssentialOperationValidator.Validate(operation);
        if (!shape.Allowed) return shape;
        return SafetyDecision.Permit(operation.RequiresReinforcedConfirmation);
    }

    public static bool IsProtected(PartitionInfo partition)
    {
        var type = partition.Type.ToUpperInvariant();
        var gptType = partition.GptType.Trim().Trim('{', '}');
        var nonBasicGptType = gptType.Length > 0 &&
            !string.Equals(gptType, "ebd0a0a2-b9e5-4433-87c0-68b6b72699c7", StringComparison.OrdinalIgnoreCase);
        var systemMount = partition.AccessPaths.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(path => path.StartsWith("C:\\", StringComparison.OrdinalIgnoreCase));
        return partition.IsBoot || partition.IsSystem || partition.IsHidden || systemMount ||
            nonBasicGptType || string.Equals(partition.DriveLetter, "C", StringComparison.OrdinalIgnoreCase)
            || type.Contains("SYSTEM") || type.Contains("EFI") || type.Contains("RESERVED") ||
            type.Contains("RECOVERY") || type == "MSR";
    }

    private static bool AffectsWholeDisk(StorageOperation operation) => operation.Risk.HasFlag(StorageOperationRisk.WholeDiskStateChange);
    private static bool RequiresPartition(StorageOperationType type) => type is StorageOperationType.DeletePartition or StorageOperationType.FormatPartition
        or StorageOperationType.SetDriveLetter or StorageOperationType.RemoveDriveLetter or StorageOperationType.ResizePartition or StorageOperationType.SetVolumeLabel;
    private static bool IsPowerOfTwo(int value) => (value & (value - 1)) == 0;
    private bool ContainsRunningExecutable(PartitionInfo partition) => _executableDriveLetter is char protectedDrive && partition.DriveLetter.Length == 1 && protectedDrive == char.ToUpperInvariant(partition.DriveLetter[0]);
    private static char? GetExecutableDriveLetter()
    {
        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath)) return null;
        var root = Path.GetPathRoot(executablePath);
        return root is { Length: >= 1 } && char.IsLetter(root[0]) ? char.ToUpperInvariant(root[0]) : null;
    }
}
