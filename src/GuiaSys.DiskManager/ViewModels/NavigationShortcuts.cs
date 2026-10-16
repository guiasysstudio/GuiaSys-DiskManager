using System.Windows.Input;

namespace GuiaSys.DiskManager.ViewModels;

/// <summary>Only exact Control+number combinations; never intercept typing or Shift/Alt.</summary>
public static class NavigationShortcuts
{
    public static string? Resolve(Key key, ModifierKeys modifiers)
    {
        if (modifiers != ModifierKeys.Control) return null;
        return key switch
        {
            Key.D1 or Key.NumPad1 => "Overview",
            Key.D2 or Key.NumPad2 => "DiskMap",
            Key.D3 or Key.NumPad3 => "Partitions",
            Key.D4 or Key.NumPad4 => "Health",
            Key.D5 or Key.NumPad5 => "Recovery",
            Key.D6 or Key.NumPad6 => "Maintenance",
            Key.D7 or Key.NumPad7 => "Operations",
            Key.D8 or Key.NumPad8 => "Queue",
            _ => null
        };
    }
}
