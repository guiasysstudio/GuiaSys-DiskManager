using System.Diagnostics;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using GuiaSys.DiskManager.Models;
using GuiaSys.DiskManager.Operations;
using GuiaSys.DiskManager.Safety;
using GuiaSys.DiskManager.Services;

namespace GuiaSys.DiskManager.IntegrationRunner;

internal static class Program
{
    private const long MiB = 1024L * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private static readonly List<GateStep> Steps = [];
    private static readonly List<string> Cleanup = [];
    private static string _reportPath = "";
    private static string? _failure;

    public static async Task<int> Main(string[] args)
    {
        _reportPath = args.Length == 1 ? Path.GetFullPath(args[0]) : Path.Combine(AppContext.BaseDirectory, "vhdx-integration-report.json");
        Directory.CreateDirectory(Path.GetDirectoryName(_reportPath)!);
        var startedAt = DateTimeOffset.Now;
        var success = false;
        var baseRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "GuiaSysDiskManager.Tests"));
        var testRoot = Path.GetFullPath(Path.Combine(baseRoot, Guid.NewGuid().ToString("N")));
        EnsureChildPath(baseRoot, testRoot);
        Directory.CreateDirectory(testRoot);
        var vhdxPaths = new[] { Path.Combine(testRoot, "gpt-test.vhdx"), Path.Combine(testRoot, "mbr-test.vhdx") };

        try
        {
            RequireAdministrator();
            var logger = new AppLogger();
            var inventory = new PowerShellDiskInventoryService(logger);
            var executor = new PowerShellStorageOperationExecutor(logger);
            var safety = new SafetyService();
            await RunInvalidSafetyChecksAsync(inventory, safety);
            await RunGptScenarioAsync(vhdxPaths[0], inventory, executor, safety);
            await RunMbrScenarioAsync(vhdxPaths[1], inventory, executor, safety);
            success = true;
            return 0;
        }
        catch (Exception exception)
        {
            _failure = exception.ToString();
            return 1;
        }
        finally
        {
            foreach (var path in vhdxPaths.Reverse()) await CleanupVhdxAsync(path);
            try
            {
                EnsureChildPath(baseRoot, testRoot);
                if (Directory.Exists(testRoot)) Directory.Delete(testRoot, recursive: false);
                Cleanup.Add($"Diretório temporário removido: {!Directory.Exists(testRoot)}");
            }
            catch (Exception exception) { Cleanup.Add($"Falha ao remover diretório temporário: {exception.Message}"); success = false; }
            var leftovers = vhdxPaths.Where(File.Exists).ToArray();
            if (leftovers.Length > 0) { success = false; _failure = $"Cleanup incompleto: {string.Join(", ", leftovers)}\n{_failure}"; }
            await WriteReportAsync(startedAt, DateTimeOffset.Now, success, testRoot);
        }
    }

    private static async Task RunGptScenarioAsync(string path, IDiskInventoryService inventory, IStorageOperationExecutor executor, ISafetyService safety)
    {
        var target = await CreateAndAttachVhdxAsync(path, 3072);
        try
        {
            await AssertInvalidAsync("GPT: criação antes da inicialização bloqueada", inventory, safety, target.Number,
                disk => NewOperation(StorageOperationType.CreatePartition, disk), "Inicialize");
            await ExecuteAsync("GPT: disco offline", inventory, executor, safety, target.Number,
                disk => NewOperation(StorageOperationType.SetDiskOffline, disk), state => state.Disk.IsOffline);
            await ExecuteAsync("GPT: disco online", inventory, executor, safety, target.Number,
                disk => NewOperation(StorageOperationType.SetDiskOnline, disk), state => !state.Disk.IsOffline);
            await ExecuteAsync("GPT: ativar somente leitura", inventory, executor, safety, target.Number,
                disk => NewOperation(StorageOperationType.SetDiskReadOnly, disk), state => state.Disk.IsReadOnly);
            await ExecuteAsync("GPT: remover somente leitura", inventory, executor, safety, target.Number,
                disk => NewOperation(StorageOperationType.ClearDiskReadOnly, disk), state => !state.Disk.IsReadOnly);
            await ExecuteAsync("GPT: inicializar", inventory, executor, safety, target.Number,
                disk => NewOperation(StorageOperationType.InitializeGpt, disk), state => state.Disk.PartitionStyle.Equals("GPT", StringComparison.OrdinalIgnoreCase));
            await AssertInvalidAsync("GPT: reinicialização bloqueada", inventory, safety, target.Number,
                disk => NewOperation(StorageOperationType.InitializeMbr, disk), "protegido");

            await ExecuteAsync("GPT: criar partição de 1536 MiB", inventory, executor, safety, target.Number,
                disk => NewOperation(StorageOperationType.CreatePartition, disk) with { SizeBytes = 1536 * MiB }, state => state.Partitions.Any(partition => partition.Size >= 1534 * MiB));
            var partitionNumber = (await QueryStateAsync(target.Number)).Partitions.MaxBy(partition => partition.Size)!.PartitionNumber;
            await AssertInvalidAsync("Safety: identidade divergente bloqueada", inventory, safety, target.Number,
                disk => NewOperation(StorageOperationType.FormatPartition, disk, partitionNumber) with { ExpectedDiskIdentity = $"invalid-{disk.Identity}", FileSystem = "NTFS" }, "identidade");
            await AssertInvalidAsync("Safety: sistema de arquivos não suportado bloqueado", inventory, safety, target.Number,
                disk => NewOperation(StorageOperationType.FormatPartition, disk, partitionNumber) with { FileSystem = "ReFS" }, "Sistema de arquivos");
            await AssertInvalidAsync("Safety: letra ausente bloqueada", inventory, safety, target.Number,
                disk => NewOperation(StorageOperationType.SetDriveLetter, disk, partitionNumber), "Informe uma letra");
            await AssertInvalidAsync("Safety: tamanho de redução inválido bloqueado", inventory, safety, target.Number,
                disk => NewOperation(StorageOperationType.ResizePartition, disk, partitionNumber) with { SizeBytes = -1 }, "tamanho positivo");
            await AssertInvalidAsync("Safety: partição inexistente bloqueada", inventory, safety, target.Number,
                disk => NewOperation(StorageOperationType.DeletePartition, disk, 999), "não existe");
            await ExecuteAsync("GPT: formatar NTFS e definir label", inventory, executor, safety, target.Number,
                disk => NewOperation(StorageOperationType.FormatPartition, disk, partitionNumber) with { FileSystem = "NTFS", Label = "GSDM_NTFS", QuickFormat = true, AllocationUnitSize = 4096 },
                state => state.Partitions.Single(x => x.PartitionNumber == partitionNumber) is { FileSystem: "NTFS", Label: "GSDM_NTFS" });

            var letters = await FindFreeLettersAsync(2);
            await ExecuteAsync($"GPT: atribuir letra {letters[0]}:", inventory, executor, safety, target.Number,
                disk => NewOperation(StorageOperationType.SetDriveLetter, disk, partitionNumber) with { DriveLetter = letters[0] },
                state => state.Partitions.Single(x => x.PartitionNumber == partitionNumber).DriveLetter == letters[0].ToString());
            await ExecuteAsync("GPT: alterar label", inventory, executor, safety, target.Number,
                disk => NewOperation(StorageOperationType.SetVolumeLabel, disk, partitionNumber) with { Label = "GSDM_LABEL" },
                state => state.Partitions.Single(x => x.PartitionNumber == partitionNumber).Label == "GSDM_LABEL");
            await ExecuteAsync($"GPT: trocar letra para {letters[1]}:", inventory, executor, safety, target.Number,
                disk => NewOperation(StorageOperationType.SetDriveLetter, disk, partitionNumber) with { DriveLetter = letters[1] },
                state => state.Partitions.Single(x => x.PartitionNumber == partitionNumber).DriveLetter == letters[1].ToString());
            await ExecuteAsync("GPT: remover letra", inventory, executor, safety, target.Number,
                disk => NewOperation(StorageOperationType.RemoveDriveLetter, disk, partitionNumber),
                state => string.IsNullOrEmpty(state.Partitions.Single(x => x.PartitionNumber == partitionNumber).DriveLetter));
            await ExecuteAsync($"GPT: reatribuir letra {letters[0]}:", inventory, executor, safety, target.Number,
                disk => NewOperation(StorageOperationType.SetDriveLetter, disk, partitionNumber) with { DriveLetter = letters[0] },
                state => state.Partitions.Single(x => x.PartitionNumber == partitionNumber).DriveLetter == letters[0].ToString());

            var supportedBeforeShrink = await QuerySupportedSizeAsync(target.Number, partitionNumber);
            var currentSize = (await QueryStateAsync(target.Number)).Partitions.Single(x => x.PartitionNumber == partitionNumber).Size;
            var shrinkSize = AlignMiB(Math.Max(supportedBeforeShrink.SizeMin + 64 * MiB, currentSize - 256 * MiB));
            if (shrinkSize >= currentSize) throw new InvalidOperationException("O Windows não ofereceu margem para redução do volume NTFS de teste.");
            await ExecuteAsync($"GPT: reduzir para {shrinkSize / MiB} MiB", inventory, executor, safety, target.Number,
                disk => NewOperation(StorageOperationType.ResizePartition, disk, partitionNumber) with { SizeBytes = shrinkSize },
                state => Approximately(state.Partitions.Single(x => x.PartitionNumber == partitionNumber).Size, shrinkSize));

            var supportedBeforeExpand = await QuerySupportedSizeAsync(target.Number, partitionNumber);
            var expandSize = AlignMiB(supportedBeforeExpand.SizeMax - MiB);
            if (expandSize <= shrinkSize) throw new InvalidOperationException("O Windows não ofereceu margem para expansão do volume NTFS de teste.");
            await ExecuteAsync($"GPT: expandir para {expandSize / MiB} MiB", inventory, executor, safety, target.Number,
                disk => NewOperation(StorageOperationType.ResizePartition, disk, partitionNumber) with { SizeBytes = expandSize },
                state => Approximately(state.Partitions.Single(x => x.PartitionNumber == partitionNumber).Size, expandSize));

            await ExecuteAsync("GPT: formatar exFAT", inventory, executor, safety, target.Number,
                disk => NewOperation(StorageOperationType.FormatPartition, disk, partitionNumber) with { FileSystem = "exFAT", Label = "GSDM_EXFAT", QuickFormat = true },
                state => state.Partitions.Single(x => x.PartitionNumber == partitionNumber) is { FileSystem: "exFAT", Label: "GSDM_EXFAT" });
            await ExecuteAsync("GPT: formatar FAT32", inventory, executor, safety, target.Number,
                disk => NewOperation(StorageOperationType.FormatPartition, disk, partitionNumber) with { FileSystem = "FAT32", Label = "GSDM_FAT32", QuickFormat = true },
                state => state.Partitions.Single(x => x.PartitionNumber == partitionNumber) is { FileSystem: "FAT32", Label: "GSDM_FAT32" });
            await ExecuteAsync("GPT: excluir partição", inventory, executor, safety, target.Number,
                disk => NewOperation(StorageOperationType.DeletePartition, disk, partitionNumber), state => state.Partitions.All(partition => partition.PartitionNumber != partitionNumber));
        }
        finally { await DetachAndDeleteVhdxAsync(target, path); }
    }

    private static async Task RunMbrScenarioAsync(string path, IDiskInventoryService inventory, IStorageOperationExecutor executor, ISafetyService safety)
    {
        var target = await CreateAndAttachVhdxAsync(path, 2048);
        try
        {
            await ExecuteAsync("MBR: inicializar", inventory, executor, safety, target.Number,
                disk => NewOperation(StorageOperationType.InitializeMbr, disk), state => state.Disk.PartitionStyle.Equals("MBR", StringComparison.OrdinalIgnoreCase));
            await ExecuteAsync("MBR: criar partição máxima", inventory, executor, safety, target.Number,
                disk => NewOperation(StorageOperationType.CreatePartition, disk), state => state.Partitions.Any(partition => partition.Size > 1900 * MiB));
            var partitionNumber = (await QueryStateAsync(target.Number)).Partitions.MaxBy(partition => partition.Size)!.PartitionNumber;
            await ExecuteAsync("MBR: formatar NTFS", inventory, executor, safety, target.Number,
                disk => NewOperation(StorageOperationType.FormatPartition, disk, partitionNumber) with { FileSystem = "NTFS", Label = "GSDM_MBR", QuickFormat = true },
                state => state.Partitions.Single(x => x.PartitionNumber == partitionNumber) is { FileSystem: "NTFS", Label: "GSDM_MBR" });
            await ExecuteAsync("MBR: excluir partição", inventory, executor, safety, target.Number,
                disk => NewOperation(StorageOperationType.DeletePartition, disk, partitionNumber), state => state.Partitions.Count == 0);
        }
        finally { await DetachAndDeleteVhdxAsync(target, path); }
    }

    private static async Task RunInvalidSafetyChecksAsync(IDiskInventoryService inventory, ISafetyService safety)
    {
        var snapshot = await inventory.ReadAsync(CancellationToken.None);
        var systemPartition = snapshot.Partitions.FirstOrDefault(p => p.IsBoot || p.IsSystem || p.DriveLetter.Equals("C", StringComparison.OrdinalIgnoreCase));
        if (systemPartition is null) throw new InvalidOperationException("Nenhuma partição de sistema foi identificada para o teste de bloqueio.");
        var disk = snapshot.FindDisk(systemPartition.DiskNumber) ?? throw new InvalidOperationException("Disco de sistema ausente no snapshot.");
        var operation = NewOperation(StorageOperationType.FormatPartition, disk, systemPartition.PartitionNumber) with { FileSystem = "NTFS" };
        var decision = safety.Evaluate(operation, snapshot);
        AddSafetyStep("Safety: bloqueio de partição Windows/boot", decision, expectedAllowed: false);

        var diskZero = snapshot.FindDisk(0) ?? throw new InvalidOperationException("Disco 0 não foi encontrado para validar o bloqueio absoluto.");
        decision = safety.Evaluate(NewOperation(StorageOperationType.InitializeGpt, diskZero), snapshot);
        AddSafetyStep("Safety: bloqueio destrutivo do Disco 0", decision, expectedAllowed: false);
    }

    private static async Task ExecuteAsync(string name, IDiskInventoryService inventory, IStorageOperationExecutor executor, ISafetyService safety,
        int diskNumber, Func<DiskInfo, StorageOperation> operationFactory, Func<IndependentState, bool> verify)
    {
        var before = await QueryStateAsync(diskNumber);
        var snapshot = await inventory.ReadAsync(CancellationToken.None);
        var disk = snapshot.FindDisk(diskNumber) ?? throw new InvalidOperationException($"Disco virtual {diskNumber} não encontrado antes de '{name}'.");
        VerifyVirtualTarget(disk, before.Disk.Size);
        var operation = operationFactory(disk);
        var decision = safety.Evaluate(operation, snapshot);
        if (!decision.Allowed) throw new InvalidOperationException($"SafetyService recusou '{name}': {decision.Reason}");
        var result = await executor.ExecuteAsync(operation, CancellationToken.None);
        if (!result.Succeeded) throw new InvalidOperationException($"Executor falhou em '{name}': {result.Message}");
        await Task.Delay(350);
        var after = await QueryStateAsync(diskNumber);
        if (!verify(after)) throw new InvalidOperationException($"Consulta independente não confirmou '{name}'. Antes={Serialize(before)} Depois={Serialize(after)}");
        Steps.Add(new GateStep(name, operation.Description, true, Serialize(before), Serialize(after), $"Duração do executor: {result.Duration.TotalMilliseconds:N0} ms"));
        await FlushProgressReportAsync();
    }

    private static async Task AssertInvalidAsync(string name, IDiskInventoryService inventory, ISafetyService safety, int diskNumber,
        Func<DiskInfo, StorageOperation> operationFactory, string expectedReasonFragment)
    {
        var state = await QueryStateAsync(diskNumber);
        var snapshot = await inventory.ReadAsync(CancellationToken.None);
        var disk = snapshot.FindDisk(diskNumber) ?? throw new InvalidOperationException("Disco virtual ausente no teste inválido.");
        var decision = safety.Evaluate(operationFactory(disk), snapshot);
        if (decision.Allowed || !decision.Reason.Contains(expectedReasonFragment, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Bloqueio inválido '{name}' não produziu a razão esperada. Resultado: {decision}");
        Steps.Add(new GateStep(name, "Somente SafetyService; executor não chamado", true, Serialize(state), Serialize(state), decision.Reason));
        await FlushProgressReportAsync();
    }

    private static void AddSafetyStep(string name, SafetyDecision decision, bool expectedAllowed)
    {
        if (decision.Allowed != expectedAllowed) throw new InvalidOperationException($"Resultado inesperado em {name}: {decision}");
        Steps.Add(new GateStep(name, "Somente SafetyService; nenhuma escrita executada", true, "n/a", "n/a", decision.Reason));
    }

    private static StorageOperation NewOperation(StorageOperationType type, DiskInfo disk, int? partitionNumber = null) => new()
    {
        Type = type, DiskNumber = disk.Number, PartitionNumber = partitionNumber, ExpectedDiskIdentity = disk.Identity
    };

    private static async Task<VirtualTarget> CreateAndAttachVhdxAsync(string path, int sizeMiB)
    {
        ValidateVhdxPath(path);
        var before = await QueryAllDisksAsync();
        await RunDiskPartAsync($"create vdisk file=\"{path}\" maximum={sizeMiB} type=expandable\r\nselect vdisk file=\"{path}\"\r\nattach vdisk\r\n");
        for (var attempt = 0; attempt < 20; attempt++)
        {
            await Task.Delay(250);
            var after = await QueryAllDisksAsync();
            var candidates = after.Where(d => !before.Any(previous => previous.Number == d.Number)
                && d.Size >= (sizeMiB * MiB) - 2 * MiB && d.Size <= (sizeMiB * MiB) + 2 * MiB
                && IsVirtualBus(d.BusType) && !d.IsBoot && !d.IsSystem && d.Number != 0).ToArray();
            if (candidates.Length == 1)
            {
                var candidate = candidates[0];
                Steps.Add(new GateStep($"Criar/anexar VHDX {Path.GetFileName(path)}", "diskpart create/attach por caminho controlado", true, Serialize(before), Serialize(after), $"Disco virtual validado: {candidate.Number}, {candidate.BusType}, {candidate.Size} bytes"));
                return new VirtualTarget(candidate.Number, candidate.UniqueId, candidate.Size, candidate.BusType);
            }
        }
        throw new InvalidOperationException("Não foi possível identificar inequivocamente o VHDX recém-anexado.");
    }

    private static async Task DetachAndDeleteVhdxAsync(VirtualTarget target, string path)
    {
        if (!File.Exists(path)) return;
        var state = await QueryStateAsync(target.Number);
        if (!IsVirtualBus(state.Disk.BusType) || state.Disk.Number == 0 || state.Disk.IsBoot || state.Disk.IsSystem || !string.Equals(state.Disk.UniqueId, target.UniqueId, StringComparison.Ordinal))
            throw new InvalidOperationException("Recusa de cleanup: a identidade do disco virtual não corresponde ao alvo criado.");
        await RunDiskPartAsync($"select vdisk file=\"{path}\"\r\ndetach vdisk\r\n");
        for (var attempt = 0; attempt < 20 && File.Exists(path); attempt++)
        {
            try { File.Delete(path); }
            catch (IOException) { await Task.Delay(250); }
        }
        if (File.Exists(path)) throw new IOException($"O VHDX permaneceu no disco após detach: {path}");
        Cleanup.Add($"Desmontado e excluído: {path}");
    }

    private static async Task CleanupVhdxAsync(string path)
    {
        if (!File.Exists(path)) return;
        try
        {
            ValidateVhdxPath(path);
            await RunDiskPartAsync($"select vdisk file=\"{path}\"\r\ndetach vdisk noerr\r\n");
            File.Delete(path);
            Cleanup.Add($"Cleanup de contingência concluído: {path}");
        }
        catch (Exception exception) { Cleanup.Add($"Cleanup de contingência falhou para {path}: {exception.Message}"); }
    }

    private static async Task RunDiskPartAsync(string script)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "diskpart.exe"),
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true
        };
        using var process = new Process { StartInfo = startInfo };
        process.Start();
        await process.StandardInput.WriteAsync(script);
        process.StandardInput.Close();
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { if (!process.HasExited) process.Kill(entireProcessTree: true); throw; }
        var output = await stdout + await stderr;
        if (process.ExitCode != 0) throw new InvalidOperationException($"diskpart retornou {process.ExitCode}: {output}");
    }

    private const string StateQueryScript = """
        $ErrorActionPreference='Stop'; [Console]::InputEncoding=[Text.UTF8Encoding]::new($false); [Console]::OutputEncoding=[Text.UTF8Encoding]::new($false)
        $r=[Console]::In.ReadToEnd()|ConvertFrom-Json; $d=Get-Disk -Number ([int]$r.DiskNumber) -ErrorAction Stop
        $parts=@(Get-Partition -DiskNumber $d.Number -ErrorAction SilentlyContinue | ForEach-Object { $p=$_; $v=$null; try{$v=$p|Get-Volume -ErrorAction Stop}catch{}; [pscustomobject]@{PartitionNumber=[int]$p.PartitionNumber;Type=[string]$p.Type;Size=[int64]$p.Size;DriveLetter=if($p.DriveLetter){[string]$p.DriveLetter}else{''};FileSystem=if($v){[string]$v.FileSystem}else{''};Label=if($v){[string]$v.FileSystemLabel}else{''};IsOffline=[bool]$p.IsOffline;IsReadOnly=[bool]$p.IsReadOnly} })
        [pscustomobject]@{Disk=[pscustomobject]@{Number=[int]$d.Number;UniqueId=[string]$d.UniqueId;Size=[int64]$d.Size;BusType=[string]$d.BusType;PartitionStyle=[string]$d.PartitionStyle;IsBoot=[bool]$d.IsBoot;IsSystem=[bool]$d.IsSystem;IsOffline=[bool]$d.IsOffline;IsReadOnly=[bool]$d.IsReadOnly};Partitions=$parts}|ConvertTo-Json -Depth 5 -Compress
        """;
    private const string AllDisksQueryScript = """
        $ErrorActionPreference='Stop'; [Console]::OutputEncoding=[Text.UTF8Encoding]::new($false)
        @(Get-Disk|ForEach-Object{[pscustomobject]@{Number=[int]$_.Number;UniqueId=[string]$_.UniqueId;Size=[int64]$_.Size;BusType=[string]$_.BusType;PartitionStyle=[string]$_.PartitionStyle;IsBoot=[bool]$_.IsBoot;IsSystem=[bool]$_.IsSystem;IsOffline=[bool]$_.IsOffline;IsReadOnly=[bool]$_.IsReadOnly}})|ConvertTo-Json -Depth 4 -Compress
        """;
    private const string SupportedSizeScript = """
        $ErrorActionPreference='Stop'; [Console]::InputEncoding=[Text.UTF8Encoding]::new($false); [Console]::OutputEncoding=[Text.UTF8Encoding]::new($false)
        $r=[Console]::In.ReadToEnd()|ConvertFrom-Json; Get-PartitionSupportedSize -DiskNumber ([int]$r.DiskNumber) -PartitionNumber ([int]$r.PartitionNumber)|Select-Object SizeMin,SizeMax|ConvertTo-Json -Compress
        """;
    private const string UsedLettersScript = """
        [Console]::OutputEncoding=[Text.UTF8Encoding]::new($false)
        $letters=@()
        $letters += @(Get-Volume -ErrorAction SilentlyContinue|Where-Object DriveLetter|ForEach-Object{[string]$_.DriveLetter})
        $letters += @(Get-PSDrive -PSProvider FileSystem -ErrorAction SilentlyContinue|ForEach-Object{[string]$_.Name})
        $letters += @(Get-CimInstance Win32_LogicalDisk -ErrorAction SilentlyContinue|ForEach-Object{([string]$_.DeviceID).TrimEnd(':')})
        $letters += @([IO.DriveInfo]::GetDrives()|ForEach-Object{([string]$_.Name).TrimEnd(':','\')})
        @($letters|Where-Object{$_ -match '^[A-Za-z]$'}|ForEach-Object{$_.ToUpperInvariant()}|Sort-Object -Unique)|ConvertTo-Json -Compress
        """;

    private static async Task<IndependentState> QueryStateAsync(int diskNumber)
    {
        var output = await PowerShellProcessRunner.RunEncodedAsync(StateQueryScript, JsonSerializer.Serialize(new { DiskNumber = diskNumber }), TimeSpan.FromSeconds(30), CancellationToken.None);
        if (output.ExitCode != 0) throw new InvalidOperationException(output.StandardError);
        return JsonSerializer.Deserialize<IndependentState>(output.StandardOutput, JsonOptions) ?? throw new InvalidOperationException("Estado independente vazio.");
    }

    private static async Task<IndependentDisk[]> QueryAllDisksAsync()
    {
        var output = await PowerShellProcessRunner.RunEncodedAsync(AllDisksQueryScript, null, TimeSpan.FromSeconds(30), CancellationToken.None);
        if (output.ExitCode != 0) throw new InvalidOperationException(output.StandardError);
        using var document = JsonDocument.Parse(output.StandardOutput);
        return document.RootElement.ValueKind == JsonValueKind.Array
            ? JsonSerializer.Deserialize<IndependentDisk[]>(output.StandardOutput, JsonOptions) ?? []
            : [JsonSerializer.Deserialize<IndependentDisk>(output.StandardOutput, JsonOptions) ?? throw new InvalidOperationException("Disco inválido.")];
    }

    private static async Task<SupportedSize> QuerySupportedSizeAsync(int diskNumber, int partitionNumber)
    {
        var input = JsonSerializer.Serialize(new { DiskNumber = diskNumber, PartitionNumber = partitionNumber });
        var output = await PowerShellProcessRunner.RunEncodedAsync(SupportedSizeScript, input, TimeSpan.FromSeconds(30), CancellationToken.None);
        if (output.ExitCode != 0) throw new InvalidOperationException(output.StandardError);
        return JsonSerializer.Deserialize<SupportedSize>(output.StandardOutput, JsonOptions) ?? throw new InvalidOperationException("Limites de tamanho ausentes.");
    }

    private static async Task<char[]> FindFreeLettersAsync(int count)
    {
        var output = await PowerShellProcessRunner.RunEncodedAsync(UsedLettersScript, null, TimeSpan.FromSeconds(30), CancellationToken.None);
        if (output.ExitCode != 0) throw new InvalidOperationException(output.StandardError);
        using var document = JsonDocument.Parse(output.StandardOutput);
        var used = new HashSet<char>();
        if (document.RootElement.ValueKind == JsonValueKind.Array) foreach (var item in document.RootElement.EnumerateArray()) if (item.ToString().Length > 0) used.Add(char.ToUpperInvariant(item.ToString()[0]));
        else if (document.RootElement.ValueKind == JsonValueKind.String && document.RootElement.GetString() is { Length: > 0 } value) used.Add(char.ToUpperInvariant(value[0]));
        var result = Enumerable.Range('D', 'Z' - 'D' + 1).Select(x => (char)x).Where(letter => !used.Contains(letter)).Take(count).ToArray();
        if (result.Length != count) throw new InvalidOperationException("Não há letras livres suficientes para o teste.");
        return result;
    }

    private static void VerifyVirtualTarget(DiskInfo disk, long expectedSize)
    {
        if (disk.Number == 0 || disk.IsBoot || disk.IsSystem || !IsVirtualBus(disk.BusType) || Math.Abs(disk.Size - expectedSize) > 2 * MiB)
            throw new InvalidOperationException($"Recusa de segurança: alvo não é o VHDX validado. Disco={Serialize(disk)}");
    }
    private static bool IsVirtualBus(string busType) => busType.Contains("Virtual", StringComparison.OrdinalIgnoreCase) || busType.Contains("File Backed", StringComparison.OrdinalIgnoreCase);
    private static bool Approximately(long actual, long expected) => Math.Abs(actual - expected) <= 2 * MiB;
    private static long AlignMiB(long value) => value / MiB * MiB;
    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, JsonOptions);
    private static void RequireAdministrator() { using var identity = WindowsIdentity.GetCurrent(); if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator)) throw new InvalidOperationException("O runner exige elevação administrativa."); }
    private static void ValidateVhdxPath(string path)
    {
        var baseRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "GuiaSysDiskManager.Tests"));
        var fullPath = Path.GetFullPath(path); EnsureChildPath(baseRoot, fullPath);
        if (!fullPath.EndsWith(".vhdx", StringComparison.OrdinalIgnoreCase) || fullPath.Contains('"') || fullPath.Contains('\n') || fullPath.Contains('\r')) throw new InvalidOperationException("Caminho VHDX inválido.");
    }
    private static void EnsureChildPath(string parent, string child)
    {
        var prefix = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(child).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("O caminho escapou da raiz temporária permitida.");
    }

    private static async Task FlushProgressReportAsync() => await WriteReportAsync(DateTimeOffset.Now, DateTimeOffset.Now, false, "em execução");
    private static async Task WriteReportAsync(DateTimeOffset startedAt, DateTimeOffset completedAt, bool success, string testRoot)
    {
        var report = new GateReport("GuiaSys Disk Manager VHDX integration", startedAt, completedAt, success, Environment.MachineName, Environment.OSVersion.ToString(), testRoot, Steps, Cleanup, _failure);
        await File.WriteAllTextAsync(_reportPath, JsonSerializer.Serialize(report, JsonOptions), new UTF8Encoding(false));
    }
}

internal sealed record VirtualTarget(int Number, string UniqueId, long Size, string BusType);
internal sealed record IndependentState(IndependentDisk Disk, List<IndependentPartition> Partitions);
internal sealed record IndependentDisk(int Number, string UniqueId, long Size, string BusType, string PartitionStyle, bool IsBoot, bool IsSystem, bool IsOffline, bool IsReadOnly);
internal sealed record IndependentPartition(int PartitionNumber, string Type, long Size, string DriveLetter, string FileSystem, string Label, bool IsOffline, bool IsReadOnly);
internal sealed record SupportedSize(long SizeMin, long SizeMax);
internal sealed record GateStep(string Name, string Operation, bool Passed, string Before, string After, string Details);
internal sealed record GateReport(string Name, DateTimeOffset StartedAt, DateTimeOffset CompletedAt, bool Success, string Machine, string Os, string TestRoot, IReadOnlyList<GateStep> Steps, IReadOnlyList<string> Cleanup, string? Failure);
