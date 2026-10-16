using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Windows;
using GuiaSys.DiskManager.Updates;

namespace GuiaSys.DiskManager;

public partial class AboutWindow : Window
{
    // Shared client only accesses fixed public GitHub endpoint. No tokens, no redirects.
    private static readonly HttpClient UpdateHttp = new(new HttpClientHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false
    });
    private readonly CancellationTokenSource _closed = new();
    private Uri? _verifiedReleasePage;

    public AboutWindow()
    {
        InitializeComponent();
        var assembly = Assembly.GetExecutingAssembly();
        VersionText.Text = $"Versão: {assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? assembly.GetName().Version?.ToString()}";
        ArchitectureText.Text = $"Arquitetura: {(Environment.Is64BitProcess ? "x64" : "x86")}";
        Closed += (_, _) => _closed.Cancel();
    }

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        CheckUpdateButton.IsEnabled = false;
        OpenUpdatePageButton.IsEnabled = false;
        _verifiedReleasePage = null;
        UpdateStatusText.Text = "Consultando o GitHub…";
        try
        {
            var installed = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
            var checker = new ReleaseUpdateChecker(UpdateHttp);
            var result = await checker.CheckAsync(installed, _closed.Token);
            if (_closed.IsCancellationRequested) return;
            UpdateStatusText.Text = result.Message;
            _verifiedReleasePage = result.State == UpdateCheckState.Available ? result.ReleasePage : null;
            OpenUpdatePageButton.IsEnabled = _verifiedReleasePage is not null;
        }
        catch (Exception ex) when (ex is HttpRequestException or ObjectDisposedException)
        {
            if (!_closed.IsCancellationRequested)
                UpdateStatusText.Text = "Verificação indisponível. Nenhum instalador foi iniciado.";
        }
        finally
        {
            if (!_closed.IsCancellationRequested) CheckUpdateButton.IsEnabled = true;
        }
    }

    private void OpenUpdatePage_Click(object sender, RoutedEventArgs e)
    {
        if (_verifiedReleasePage is null) return;
        try
        {
            Process.Start(new ProcessStartInfo(_verifiedReleasePage.AbsoluteUri)
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            MessageBox.Show(this, "Não foi possível abrir a página oficial no navegador.",
                "GuiaSys Disk Manager", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
