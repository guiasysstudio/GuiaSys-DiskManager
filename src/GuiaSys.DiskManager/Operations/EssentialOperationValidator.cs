using System.IO;
using GuiaSys.DiskManager.Models;

namespace GuiaSys.DiskManager.Operations;

/// <summary>
/// C07: validates the shape of the requested storage mutation independently of the UI.
/// Not an authorization decision: identity, topology, privilege and licensing remain
/// mandatory in SafetyService and the execution coordinator.
/// </summary>
public static class EssentialOperationValidator
{
    public static SafetyDecision Validate(StorageOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (!Enum.IsDefined(operation.Type))
            return SafetyDecision.Deny("Operação não reconhecida.");
        if (operation.Id == Guid.Empty || operation.DiskNumber < 0)
            return SafetyDecision.Deny("Identificador ou número de disco inválido.");

        var partitionAction = RequiresPartition(operation.Type);
        if (partitionAction)
        {
            if (operation.PartitionNumber is not > 0 ||
                string.IsNullOrWhiteSpace(operation.ExpectedPartitionIdentity))
                return SafetyDecision.Deny("Informe uma partição válida e sua identidade original.");
        }
        else if (operation.PartitionNumber is not null ||
                 !string.IsNullOrWhiteSpace(operation.ExpectedPartitionIdentity))
            return SafetyDecision.Deny("A operação de disco inteiro não pode conter alvo de partição.");

        var isSizeAction = operation.Type is StorageOperationType.CreatePartition or StorageOperationType.ResizePartition;
        if (isSizeAction)
        {
            if (operation.Type == StorageOperationType.ResizePartition && operation.SizeBytes is not > 0)
                return SafetyDecision.Deny("Informe um tamanho positivo para redimensionar.");
            if (operation.SizeBytes is <= 0)
                return SafetyDecision.Deny("O tamanho solicitado deve ser positivo.");
        }
        else if (operation.SizeBytes is not null)
            return SafetyDecision.Deny("Esta operação não aceita parâmetro de tamanho.");

        if (operation.Type == StorageOperationType.SetDriveLetter)
        {
            if (operation.DriveLetter is not (>= 'D' and <= 'Z'))
                return SafetyDecision.Deny("Informe uma letra de unidade entre D e Z.");
        }
        else if (operation.DriveLetter is not null)
            return SafetyDecision.Deny("Esta operação não aceita parâmetro de letra de unidade.");

        var isFormat = operation.Type == StorageOperationType.FormatPartition;
        var usesLabel = isFormat || operation.Type == StorageOperationType.SetVolumeLabel;
        if (usesLabel)
        {
            if (operation.Label is null || operation.Label.Length > 32 ||
                operation.Label.Any(char.IsControl) ||
                operation.Label.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                return SafetyDecision.Deny("O rótulo contém caracteres inválidos ou excede 32 caracteres.");
        }
        else if (!string.IsNullOrEmpty(operation.Label))
            return SafetyDecision.Deny("Esta operação não aceita rótulo de volume.");

        if (isFormat)
        {
            if (operation.FileSystem is not ("NTFS" or "FAT32" or "exFAT"))
                return SafetyDecision.Deny("Use NTFS, FAT32 ou exFAT.");
            if (operation.AllocationUnitSize is int size &&
                (size is < 512 or > 2_097_152 || (size & (size - 1)) != 0))
                return SafetyDecision.Deny("A unidade de alocação deve ser potência de 2 entre 512 bytes e 2 MiB.");
        }
        else
        {
            // NTFS is the model's default value and has no meaning for non-format commands.
            if (operation.FileSystem != "NTFS" || operation.AllocationUnitSize is not null ||
                operation.QuickFormat != true)
                return SafetyDecision.Deny("Parâmetros de formatação não pertencem a esta operação.");
        }
        return SafetyDecision.Permit(operation.RequiresReinforcedConfirmation);
    }

    public static bool RequiresPartition(StorageOperationType type) =>
        type is StorageOperationType.DeletePartition or StorageOperationType.FormatPartition
            or StorageOperationType.SetDriveLetter or StorageOperationType.RemoveDriveLetter
            or StorageOperationType.ResizePartition or StorageOperationType.SetVolumeLabel;
}
