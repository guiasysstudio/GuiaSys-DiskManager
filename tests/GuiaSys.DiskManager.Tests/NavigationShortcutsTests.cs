using System.Windows.Input;
using GuiaSys.DiskManager.ViewModels;

namespace GuiaSys.DiskManager.Tests;

public sealed class NavigationShortcutsTests
{
    [Theory]
    [InlineData(Key.D1, "Overview")]
    [InlineData(Key.D2, "DiskMap")]
    [InlineData(Key.D3, "Partitions")]
    [InlineData(Key.D4, "Health")]
    [InlineData(Key.D5, "Recovery")]
    [InlineData(Key.D6, "Maintenance")]
    [InlineData(Key.D7, "Operations")]
    [InlineData(Key.D8, "Queue")]
    [InlineData(Key.NumPad1, "Overview")]
    [InlineData(Key.NumPad4, "Health")]
    [InlineData(Key.NumPad8, "Queue")]
    public void Control_digits_route_to_expected_sections(Key key, string expected) =>
        Assert.Equal(expected, NavigationShortcuts.Resolve(key, ModifierKeys.Control));

    [Theory]
    [InlineData(Key.D1, ModifierKeys.None)]
    [InlineData(Key.D2, ModifierKeys.Shift)]
    [InlineData(Key.D3, ModifierKeys.Alt)]
    [InlineData(Key.D4, ModifierKeys.Control | ModifierKeys.Shift)]
    [InlineData(Key.F5, ModifierKeys.Control)]
    [InlineData(Key.D0, ModifierKeys.Control)]
    public void Other_key_combinations_do_not_trigger_navigation(Key key, ModifierKeys modifiers) =>
        Assert.Null(NavigationShortcuts.Resolve(key, modifiers));
}
