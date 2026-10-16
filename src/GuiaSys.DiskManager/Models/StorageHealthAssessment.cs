namespace GuiaSys.DiskManager.Models;

/// <summary>C13. Windows inventory evidence only; not a SMART self-test, scan or warranty of health.</summary>
public enum DiskHealthLevel { Unknown, Normal, Warning, Critical }
public sealed record DiskHealthCheck(string Component, string Status, string Detail);
public sealed record DiskHealthReport(int DiskNumber, DateTimeOffset CapturedAt,
    DiskHealthLevel Level, string Summary, IReadOnlyList<DiskHealthCheck> Checks)
{
    public bool SmartVerified => false;
    public bool HasObservedIssues => Level is DiskHealthLevel.Warning or DiskHealthLevel.Critical;
}

public static class StorageHealthAssessment
{
    public static DiskHealthReport Analyze(StorageSnapshot snapshot, int diskNumber)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var disk = snapshot.FindDisk(diskNumber);
        if (disk is null)
            return new(diskNumber, snapshot.CapturedAt, DiskHealthLevel.Unknown,
                "Disco ausente do inventário. Nenhum diagnóstico confiável disponível.",
                [new("Inventário", "Não verificado", "Atualize a lista de dispositivos.")]);

        var checks = new List<DiskHealthCheck>();
        var level = DiskHealthLevel.Unknown;
        void Promote(DiskHealthLevel next) { if (next > level) level = next; }

        var state = Normalize(disk.HealthStatus);
        Promote(state);
        checks.Add(new("Saúde do dispositivo (Get-Disk)", Label(state),
            $"Windows: {Readable(disk.HealthStatus)}. Não equivale a um teste SMART completo."));

        if (disk.IsOffline)
        {
            Promote(DiskHealthLevel.Warning);
            checks.Add(new("Acesso ao disco", "Atenção", "Dispositivo offline: disponibilidade dos volumes não confirmada."));
        }
        else
            checks.Add(new("Acesso ao disco", disk.IsReadOnly ? "Somente leitura" : "Online",
                $"Estado operacional: {Readable(disk.OperationalStatus)}. Online não comprova integridade física."));

        int? temperature = disk.TemperatureC is >= -20 and <= 125 ? disk.TemperatureC : null;
        if (temperature is int t)
        {
            if (t >= 65) Promote(DiskHealthLevel.Warning);
            checks.Add(new("Temperatura", t >= 65 ? "Atenção" : "Informada",
                $"{t} °C, obtidos do Windows. 65 °C é apenas referência de atenção; consulte o limite do fabricante."));
        }
        else checks.Add(new("Temperatura", "Não disponível", "Sensor não fornecido ou sem correspondência inequívoca por serial."));

        if (disk.ReliabilityCountersAvailable)
        {
            int? wear = disk.WearPercent is >= 0 and <= 100 ? disk.WearPercent : null;
            long? hours = disk.PowerOnHours is >= 0 ? disk.PowerOnHours : null;
            if (wear >= 95) Promote(DiskHealthLevel.Warning);
            checks.Add(new("Desgaste e tempo ligado", wear >= 95 ? "Atenção" : "Informações parciais",
                $"Desgaste: {Percent(wear)} · horas: {Number(hours)}. Desgaste informado não é percentual de vida restante."));
            long? read = disk.ReadErrorsUncorrected is >= 0 ? disk.ReadErrorsUncorrected : null;
            long? write = disk.WriteErrorsUncorrected is >= 0 ? disk.WriteErrorsUncorrected : null;
            if (read > 0 || write > 0) Promote(DiskHealthLevel.Warning);
            checks.Add(new("Erros não corrigidos", read > 0 || write > 0 ? "Atenção" :
                read.HasValue || write.HasValue ? "Contadores informados" : "Não disponível",
                $"Leitura: {Number(read)} · gravação: {Number(write)}. Contadores históricos não atestam integridade."));
            checks.Add(new("Erros totais", "Informações parciais",
                $"Leitura: {Number(disk.ReadErrorsTotal)} · gravação: {Number(disk.WriteErrorsTotal)}. Podem incluir erros corrigidos."));
        }
        else checks.Add(new("Confiabilidade do hardware", "Não disponível",
            "Get-StorageReliabilityCounter não disponível ou dispositivo físico não associado inequivocamente pelo serial."));

        var parts = snapshot.Partitions.Where(p => p.DiskNumber == diskNumber).ToArray();
        var critical = parts.Count(p => Normalize(p.HealthStatus) == DiskHealthLevel.Critical);
        var warning = parts.Count(p => Normalize(p.HealthStatus) == DiskHealthLevel.Warning);
        var full = parts.Count(p => p.UsagePercent is >= 95d);
        var known = parts.Count(p => Normalize(p.HealthStatus) == DiskHealthLevel.Normal);
        if (critical > 0) Promote(DiskHealthLevel.Critical);
        else if (warning > 0 || full > 0) Promote(DiskHealthLevel.Warning);
        checks.Add(new("Volumes e espaço livre", critical > 0 ? "Crítico" :
            warning > 0 || full > 0 ? "Atenção" : known > 0 ? "Status Windows informado" : "Não verificado",
            $"{parts.Length} partições; {critical} volumes críticos, {warning} com alerta, " +
            $"{full} com uso igual ou acima de 95%. Uso de espaço não é falha física."));

        checks.Add(new("S.M.A.R.T. integral e autotestes", "Não verificado",
            "Sem atributos SMART brutos, limites do fabricante, NVMe health log ou autotestes. Contadores Windows não os substituem."));
        checks.Add(new("Sistema de arquivos / superfície", "Não executado",
            "Não houve CHKDSK, Repair-Volume, varredura de superfície ou reparo; apenas leitura de estados fornecidos pelo Windows."));

        var summary = level switch
        {
            DiskHealthLevel.Critical => "CRÍTICO · Windows informou problema grave em disco/volume. Faça backup e diagnóstico especializado. Somente leitura.",
            DiskHealthLevel.Warning => "ATENÇÃO · Há avisos no estado, temperatura, desgaste, contadores ou volumes. Avalie backup e diagnóstico. Somente leitura.",
            DiskHealthLevel.Normal => "SEM ALERTAS NOS ESTADOS WINDOWS CONSULTADOS · Não certifica SMART completo, integridade ou ausência de falha. Somente leitura.",
            _ => "INDETERMINADO · Indicadores Windows insuficientes para classificar a saúde. Somente leitura."
        };
        return new(diskNumber, snapshot.CapturedAt, level, summary, checks);
    }

    private static DiskHealthLevel Normalize(string? status) => (status ?? "").Trim().ToUpperInvariant() switch
    {
        "HEALTHY" => DiskHealthLevel.Normal,
        "WARNING" => DiskHealthLevel.Warning,
        "UNHEALTHY" => DiskHealthLevel.Critical,
        _ => DiskHealthLevel.Unknown
    };
    private static string Label(DiskHealthLevel level) => level switch
    {
        DiskHealthLevel.Normal => "Saudável (Windows)",
        DiskHealthLevel.Warning => "Atenção",
        DiskHealthLevel.Critical => "Crítico",
        _ => "Não verificado"
    };
    private static string Readable(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "Não informado" : value.Trim();
    private static string Number(long? value) =>
        value is >= 0 ? value.Value.ToString("N0") : "não disponível";
    private static string Percent(int? value) =>
        value is >= 0 and <= 100 ? $"{value}%" : "não disponível";
}
