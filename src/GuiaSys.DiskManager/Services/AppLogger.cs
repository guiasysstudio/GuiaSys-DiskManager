using System.Text.Json;
using System.IO;

namespace GuiaSys.DiskManager.Services;

public sealed class AppLogger : IAppLogger
{
    private readonly object _gate = new();
    private readonly string _filePath;
    public string LogDirectory { get; }

    public AppLogger()
    {
        LogDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GuiaSys", "DiskManager", "Logs");
        Directory.CreateDirectory(LogDirectory);
        DeleteExpiredLogs();
        _filePath = Path.Combine(LogDirectory, $"diskmanager-{DateTime.Now:yyyyMMdd}.jsonl");
    }

    public void Information(string eventName, object? data = null) => Write("Information", eventName, data, null);
    public void Error(string eventName, Exception exception, object? data = null) => Write("Error", eventName, data, exception);

    private void Write(string level, string eventName, object? data, Exception? exception)
    {
        var entry = new { timestamp = DateTimeOffset.Now, level, eventName, data, exception = exception is null ? null : new { type = exception.GetType().FullName, exception.Message, innerMessage = exception.InnerException?.Message, exception.StackTrace } };
        lock (_gate) File.AppendAllText(_filePath, JsonSerializer.Serialize(entry) + Environment.NewLine);
    }

    private void DeleteExpiredLogs()
    {
        foreach (var file in Directory.EnumerateFiles(LogDirectory, "diskmanager-*.jsonl"))
        {
            try { if (File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddDays(-30)) File.Delete(file); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
