using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Aether.Installer.App;

/// <summary>Small modal message and yes/no boxes built in code, in the same look as the windows.</summary>
internal static class Dialogs
{
    public static Task<bool> Confirm(Window owner, string title, string text, string yes = "OK", string no = "Cancel") => Show(owner, title, text, yes, no);

    public static Task Message(Window owner, string title, string text) => Show(owner, title, text, "OK", null);

    private static async Task<bool> Show(Window owner, string title, string text, string yes, string? no)
    {
        var dialog = new Window
        {
            Title = title, Icon = owner.Icon, Width = 520, SizeToContent = SizeToContent.Height, CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        var ok = new Button { Content = yes, Classes = { "accent" }, MinWidth = 96 };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        if (no != null)
        {
            var cancel = new Button { Content = no, MinWidth = 96 };
            cancel.Click += (_, _) => dialog.Close(false);
            buttons.Children.Add(cancel);
        }
        buttons.Children.Add(ok);
        ok.Click += (_, _) => dialog.Close(true);
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(20), Spacing = 16,
            Children = { new TextBlock { Text = text, TextWrapping = Avalonia.Media.TextWrapping.Wrap }, buttons },
        };
        return await dialog.ShowDialog<bool>(owner);
    }
}
