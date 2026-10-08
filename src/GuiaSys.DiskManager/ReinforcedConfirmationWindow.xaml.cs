using System.Windows;
using System.Windows.Controls;

namespace GuiaSys.DiskManager;

public partial class ReinforcedConfirmationWindow : Window
{
    private readonly string _expected;
    public ReinforcedConfirmationWindow(string summary, string expected) { InitializeComponent(); _expected = expected; SummaryText.Text = summary; InstructionText.Text = $"Para confirmar, digite exatamente o número do disco: {expected}"; }
    private void ConfirmationText_Changed(object sender, TextChangedEventArgs e) => ConfirmButton.IsEnabled = string.Equals(ConfirmationText.Text.Trim(), _expected, StringComparison.Ordinal);
    private void Confirm_Click(object sender, RoutedEventArgs e) { DialogResult = true; Close(); }
}
