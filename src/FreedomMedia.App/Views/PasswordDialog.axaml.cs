using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using FreedomMedia.Core;

namespace FreedomMedia.App.Views;

public enum PasswordMode { EnterExisting, SetNew }

public partial class PasswordDialog : Window
{
    private readonly PasswordMode _mode;

    // Parameterless constructor for the XAML previewer / loader.
    public PasswordDialog() : this(PasswordMode.EnterExisting, "Enter passphrase", "") { }

    public PasswordDialog(PasswordMode mode, string title, string subtitle)
    {
        InitializeComponent();
        _mode = mode;
        TitleText.Text = title;
        SubText.Text = subtitle;
        ConfirmPanel.IsVisible = mode == PasswordMode.SetNew;

        ShowBox.IsCheckedChanged += (_, _) =>
        {
            bool reveal = ShowBox.IsChecked == true;
            Password1.RevealPassword = reveal;
            Password2.RevealPassword = reveal;
        };

        Opened += (_, _) => Password1.Focus();
    }

    private void OnOk(object? sender, RoutedEventArgs e)
    {
        var pw = Password1.Text ?? "";
        if (string.IsNullOrEmpty(pw)) { ShowError("Passphrase cannot be empty."); return; }
        if (System.Text.Encoding.UTF8.GetByteCount(pw) > VaultFormat.MaxPasswordLength)
        {
            ShowError($"Passphrase must be at most {VaultFormat.MaxPasswordLength} characters.");
            return;
        }
        if (_mode == PasswordMode.SetNew && pw != (Password2.Text ?? ""))
        {
            ShowError("The two passphrases do not match.");
            return;
        }
        Close(pw);
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.IsVisible = true;
    }
}
