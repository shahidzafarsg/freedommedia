using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Interactivity;

namespace FreedomMedia.App.Views;

/// <summary>Small code-built message and confirmation dialogs (Avalonia has no built-in MessageBox).</summary>
public static class Dialogs
{
    public static Task InfoAsync(Window owner, string title, string message)
        => ShowAsync(owner, title, message, new[] { ("OK", true) }).ContinueWith(_ => { });

    public static Task<bool> ConfirmAsync(Window owner, string title, string message, string confirmLabel = "OK")
        => ShowAsync(owner, title, message, new[] { ("Cancel", false), (confirmLabel, true) });

    /// <summary>A single-line text prompt. Returns the entered text, or null if cancelled.</summary>
    public static Task<string?> PromptAsync(Window owner, string title, string label, string initial)
    {
        var tcs = new TaskCompletionSource<string?>();

        var box = new TextBox { Text = initial, FontSize = 14 };
        var window = new Window
        {
            Title = title,
            Width = 440,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brush.Parse("#16171C"),
        };

        var ok = new Button { Content = "OK", Width = 104, MinHeight = 34, IsDefault = true };
        ok.Classes.Add("primary");
        ok.Click += (_, _) => { var t = box.Text ?? ""; window.Close(); tcs.TrySetResult(string.IsNullOrWhiteSpace(t) ? null : t.Trim()); };
        var cancel = new Button { Content = "Cancel", Width = 104, MinHeight = 34 };
        cancel.Click += (_, _) => { window.Close(); tcs.TrySetResult(null); };

        window.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(26),
            Children =
            {
                new TextBlock { Text = label, Foreground = Brush.Parse("#9AA0AC"), FontSize = 12, Margin = new Avalonia.Thickness(0,0,0,6) },
                box,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 10,
                    Margin = new Avalonia.Thickness(0,20,0,0),
                    Children = { cancel, ok },
                },
            },
        };

        window.Opened += (_, _) => { box.SelectAll(); box.Focus(); };
        window.Closed += (_, _) => tcs.TrySetResult(null);
        _ = window.ShowDialog(owner);
        return tcs.Task;
    }

    private static Task<bool> ShowAsync(Window owner, string title, string message, (string label, bool result)[] buttons)
    {
        var tcs = new TaskCompletionSource<bool>();

        var text = new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush.Parse("#F1F2F6"),
            FontSize = 13,
        };

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 10,
            Margin = new Avalonia.Thickness(0, 20, 0, 0),
        };

        var window = new Window
        {
            Title = title,
            Width = 440,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brush.Parse("#16171C"),
        };

        foreach (var (label, result) in buttons)
        {
            var b = new Button { Content = label, Width = 104, MinHeight = 34 };
            if (result) b.Classes.Add("primary");
            b.Click += (_, _) => { window.Close(); tcs.TrySetResult(result); };
            buttonPanel.Children.Add(b);
        }

        window.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(26),
            Children =
            {
                new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeight.SemiBold, Foreground = Brush.Parse("#F1F2F6"), Margin = new Avalonia.Thickness(0,0,0,10) },
                text,
                buttonPanel,
            },
        };

        window.Closed += (_, _) => tcs.TrySetResult(false);
        _ = window.ShowDialog(owner);
        return tcs.Task;
    }
}
