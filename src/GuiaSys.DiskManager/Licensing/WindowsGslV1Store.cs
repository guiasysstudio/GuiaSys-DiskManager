using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace GuiaSys.DiskManager.Licensing;

/// <summary>Identity in HKCU survives upgrades; cached credentials are DPAPI protected per Windows user.</summary>
public sealed class WindowsGslV1Store : IGslV1Store
{
    private const string DeviceRegistryKey = @"Software\GuiaSys Studio\DiskManager\Licensing";
    private readonly string _file;

    public WindowsGslV1Store()
    {
        _file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GuiaSys", "DiskManager", "license-state.dpapi");
    }

    public string GetDeviceId()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        using var registry = Registry.CurrentUser.CreateSubKey(DeviceRegistryKey, writable: true)
            ?? throw new InvalidOperationException("Não foi possível acessar a identidade local.");
        var saved = registry.GetValue("DeviceId") as string;
        if (!string.IsNullOrWhiteSpace(saved) && saved.StartsWith("GDM-", StringComparison.Ordinal) &&
            saved.Length <= 256) return saved;
        if (saved is not null) throw new InvalidOperationException("Identidade local inválida. Interrompido para proteger a ativação.");
        var deviceId = "GDM-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        registry.SetValue("DeviceId", deviceId, RegistryValueKind.String);
        return deviceId;
    }

    public GslV1SavedState Load()
    {
        if (!File.Exists(_file)) return new GslV1SavedState();
        var protectedBytes = File.ReadAllBytes(_file);
        if (protectedBytes.Length > 32_768) throw new InvalidDataException("Cache de licença inválido.");
        var bytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
        try { return JsonSerializer.Deserialize<GslV1SavedState>(bytes) ?? throw new InvalidDataException("Cache de licença vazio."); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    public void Save(GslV1SavedState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(state);
        try
        {
            var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
            var directory = Path.GetDirectoryName(_file)!;
            Directory.CreateDirectory(directory);
            var temporary = Path.Combine(directory, "license-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.WriteAllBytes(temporary, encrypted);
                File.Move(temporary, _file, overwrite: true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
}
