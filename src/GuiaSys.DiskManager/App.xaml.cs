using System.Net.Http;
using System.Windows;
using GuiaSys.DiskManager.Licensing;
using GuiaSys.DiskManager.Operations;
using GuiaSys.DiskManager.Safety;
using GuiaSys.DiskManager.Services;
using GuiaSys.DiskManager.ViewModels;

namespace GuiaSys.DiskManager;

public partial class App : Application
{
    private readonly AppLogger _logger = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) => { _logger.Error("application.unhandled", args.Exception); MessageBox.Show("Ocorreu um erro inesperado. Os detalhes foram gravados nos logs.", "GuiaSys Disk Manager", MessageBoxButton.OK, MessageBoxImage.Error); args.Handled = true; };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => _logger.Error("application.fatal", args.ExceptionObject as Exception ?? new InvalidOperationException("Erro fatal desconhecido."));
        TaskScheduler.UnobservedTaskException += (_, args) => { _logger.Error("application.task.unobserved", args.Exception); args.SetObserved(); };
        _logger.Information("application.started", new { version = typeof(App).Assembly.GetName().Version?.ToString(), administrator = AdministratorService.IsAdministrator(), architecture = Environment.Is64BitProcess ? "x64" : "x86" });
        var licensing = new GslV1LicensingService(
            new GslV1ApiClient(new HttpClient()), new WindowsGslV1Store(), GslV1TrustLoader.Load());
        var inventory = new PowerShellDiskInventoryService(_logger);
        var viewModel = new MainViewModel(inventory, new PowerShellStorageOperationExecutor(_logger), new SafetyService(), _logger, licensing);
        var window = new MainWindow(viewModel, licensing); MainWindow = window; window.Show();
    }
}
