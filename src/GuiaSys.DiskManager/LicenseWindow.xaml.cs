using System.Windows;
using GuiaSys.DiskManager.Licensing;

namespace GuiaSys.DiskManager;

public partial class LicenseWindow : Window
{
    private readonly ILicenseEntitlements _service;
    private bool _busy;

    public LicenseWindow(ILicenseEntitlements service)
    {
        InitializeComponent();
        _service = service;
        Loaded += async (_, _) => await RunAsync(_service.CheckAsync);
    }

    private void SetBusy(bool value)
    {
        _busy = value;
        ActivateButton.IsEnabled = !value;
        TrialButton.IsEnabled = !value;
        ValidateButton.IsEnabled = !value;
        DeactivateButton.IsEnabled = !value;
        BusyText.Text = value ? "Consultando..." : "";
    }

    private async Task RunAsync(Func<CancellationToken, Task<GslLicenseDecision>> action)
    {
        if (_busy) return;
        SetBusy(true);
        try
        {
            var result = await action(CancellationToken.None);
            StatusText.Text = result.Message;
            DetailsText.Text = $"Estado: {result.Status}\n" +
                (result.CustomerName is { Length: > 0 } ? $"Cliente: {result.CustomerName}\n" : "") +
                (result.CustomerEmail is { Length: > 0 } ? $"E-mail: {result.CustomerEmail}\n" : "") +
                (result.StartedAt is null ? "" : $"Início da avaliação: {result.StartedAt.Value.LocalDateTime:dd/MM/yyyy HH:mm}\n") +
                (result.ActivatedAt is null ? "" : $"Ativação: {result.ActivatedAt.Value.LocalDateTime:dd/MM/yyyy HH:mm}\n") +
                (result.MaxDevices is null ? "" : $"Máximo de dispositivos: {result.MaxDevices}\n" : "") +
                (result.PlanName is { Length: > 0 } ? $"Plano: {result.PlanName}\n" : "") +
                (result.ExpiresAt is null ? (result.Allowed ? "Validade: vitalícia\n" : "") :
                    $"Validade: {result.ExpiresAt.Value.LocalDateTime:dd/MM/yyyy HH:mm}\n") +
                (result.OfflineUntil is null ? "" : $"Autorização offline até: {result.OfflineUntil.Value.LocalDateTime:dd/MM/yyyy HH:mm}\n") +
                (result.LastOnlineUtc is null ? "" : $"Última validação: {result.LastOnlineUtc.Value.LocalDateTime:dd/MM/yyyy HH:mm}");
        }
        catch (Exception)
        {
            StatusText.Text = "Não foi possível concluir a consulta. Nenhuma autorização foi concedida.";
            DetailsText.Text = "";
        }
        finally { SetBusy(false); }
    }

    private async void Activate_Click(object sender, RoutedEventArgs e)
    {
        var key = KeyInput.Password;
        KeyInput.Clear();
        await RunAsync(ct => _service.ActivateAsync(key, ct));
    }

    private async void Trial_Click(object sender, RoutedEventArgs e) =>
        await RunAsync(_service.StartTrialAsync);

    private async void Validate_Click(object sender, RoutedEventArgs e) =>
        await RunAsync(_service.ValidateOnlineAsync);

    private async void Deactivate_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "Desativar a licença paga deste dispositivo?",
            "Confirmar desativação", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        await RunAsync(_service.DeactivateAsync);
    }
}
