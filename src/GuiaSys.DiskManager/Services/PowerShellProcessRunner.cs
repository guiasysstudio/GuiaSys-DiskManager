using System.Diagnostics;
using System.IO;
using System.Text;

namespace GuiaSys.DiskManager.Services;

public sealed record ProcessOutput(int ExitCode, string StandardOutput, string StandardError);

public static class PowerShellProcessRunner
{
    public static async Task<ProcessOutput> RunEncodedAsync(string fixedScript, string? standardInput, TimeSpan timeout, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fixedScript);
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("O gerenciamento de discos requer Windows.");

        var executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            RedirectStandardInput = standardInput is not null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(fixedScript)) })
            startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        if (standardInput is not null)
        {
            await process.StandardInput.WriteAsync(standardInput.AsMemory(), cancellationToken);
            process.StandardInput.Close();
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        try { await process.WaitForExitAsync(linkedCts.Token); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            if (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                throw new TimeoutException($"O Windows não concluiu a operação em {timeout.TotalSeconds:N0} segundos.");
            throw;
        }
        return new ProcessOutput(process.ExitCode, await stdoutTask, await stderrTask);
    }
}
