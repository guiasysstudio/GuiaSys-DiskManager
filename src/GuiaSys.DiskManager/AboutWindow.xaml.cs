using System.Reflection;
using System.Windows;

namespace GuiaSys.DiskManager;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent(); var assembly = Assembly.GetExecutingAssembly();
        VersionText.Text = $"Versão: {assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? assembly.GetName().Version?.ToString()}";
        ArchitectureText.Text = $"Arquitetura: {(Environment.Is64BitProcess ? "x64" : "x86")}";
    }
}
