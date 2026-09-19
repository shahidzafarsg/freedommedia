using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace FreedomMedia.App.Views;

public partial class ProgressDialog : Window
{
    public CancellationTokenSource Cts { get; } = new();

    public ProgressDialog()
    {
        InitializeComponent();
        Closing += (_, _) => Cts.Cancel();
    }

    public ProgressDialog(string heading) : this() => HeadingText.Text = heading;

    /// <summary>Update the bar from any thread.</summary>
    public void Report(string item, long done, long total)
    {
        Dispatcher.UIThread.Post(() =>
        {
            ItemText.Text = item;
            double pct = total > 0 ? (double)done / total * 100.0 : 0;
            Bar.Value = Math.Clamp(pct, 0, 100);
            PercentText.Text = $"{pct:0.#}%";
        });
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        Cts.Cancel();
        CancelButton.IsEnabled = false;
        CancelButton.Content = "Cancelling…";
    }
}
