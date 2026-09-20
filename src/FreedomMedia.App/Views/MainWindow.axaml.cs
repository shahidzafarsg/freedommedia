using System.Collections;
using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using FreedomMedia.App.Models;
using FreedomMedia.App.Services;
using FreedomMedia.Core;

namespace FreedomMedia.App.Views;

public partial class MainWindow : Window
{
    private enum ViewMode { Icons, List, Details }

    private readonly ObservableCollection<MediaItemViewModel> _items = new();
    private readonly ThumbnailService _thumbs = new();
    private VaultSession? _session;
    private string? _vaultPath;
    private string? _password;
    private ViewMode _viewMode = ViewMode.Icons;

    // Commands bound from the menu, toolbar and keyboard.
    public RelayCommand NewVaultCommand { get; }
    public RelayCommand OpenVaultCommand { get; }
    public RelayCommand CloseVaultCommand { get; }
    public RelayCommand ImportCommand { get; }
    public RelayCommand ExportCommand { get; }
    public RelayCommand RenameCommand { get; }
    public RelayCommand RemoveCommand { get; }
    public RelayCommand ChangePassphraseCommand { get; }
    public RelayCommand SelectAllCommand { get; }
    public RelayCommand SelectNoneCommand { get; }

    public MainWindow()
    {
        NewVaultCommand = new RelayCommand(async () => await NewVaultAsync());
        OpenVaultCommand = new RelayCommand(async () => await OpenVaultAsync());
        CloseVaultCommand = new RelayCommand(CloseVault, () => _session != null);
        ImportCommand = new RelayCommand(async () => await ImportAsync(), () => _session != null);
        ExportCommand = new RelayCommand(async () => await ExportAsync(), () => _session != null && ActiveSelection().Count > 0);
        RenameCommand = new RelayCommand(async () => await RenameAsync(), () => _session != null && ActiveSelection().Count == 1);
        RemoveCommand = new RelayCommand(async () => await RemoveAsync(), () => _session != null && ActiveSelection().Count > 0);
        ChangePassphraseCommand = new RelayCommand(async () => await ChangePassphraseAsync(), () => _session != null);
        SelectAllCommand = new RelayCommand(SelectAll, () => _session != null);
        SelectNoneCommand = new RelayCommand(SelectNone, () => _session != null);

        InitializeComponent();
        DataContext = this;

        IconsView.ItemsSource = _items;
        ListView.ItemsSource = _items;
        DetailsGrid.ItemsSource = _items;

        SetViewMode(ViewMode.Icons);
        UpdateUiState();
    }

    // ===== State / UI =====

    private void UpdateUiState()
    {
        bool open = _session != null;
        WelcomePanel.IsVisible = !open;
        GalleryRoot.IsVisible = open;
        EmptyHint.IsVisible = open && _items.Count == 0;

        if (open)
        {
            long total = _items.Sum(i => i.SizeBytes);
            int photos = _items.Count(i => !i.IsVideo);
            int videos = _items.Count(i => i.IsVideo);
            CountText.Text = $"{System.IO.Path.GetFileName(_vaultPath)}  •  {_items.Count} item{(_items.Count == 1 ? "" : "s")} " +
                             $"({photos} photo{(photos == 1 ? "" : "s")}, {videos} video{(videos == 1 ? "" : "s")})  •  {MediaItemViewModel.FormatSize(total)}";
        }
        else
        {
            CountText.Text = "No vault open.";
            SelectionText.Text = "";
        }

        RefreshCommands();
        UpdateSelectionStatus();
    }

    private void RefreshCommands()
    {
        CloseVaultCommand.RaiseCanExecuteChanged();
        ImportCommand.RaiseCanExecuteChanged();
        ExportCommand.RaiseCanExecuteChanged();
        RenameCommand.RaiseCanExecuteChanged();
        RemoveCommand.RaiseCanExecuteChanged();
        ChangePassphraseCommand.RaiseCanExecuteChanged();
        SelectAllCommand.RaiseCanExecuteChanged();
        SelectNoneCommand.RaiseCanExecuteChanged();
    }

    private void UpdateSelectionStatus()
    {
        if (_session == null) { SelectionText.Text = ""; return; }
        var sel = ActiveSelection();
        if (sel.Count == 0)
            SelectionText.Text = "";
        else if (sel.Count == 1)
        {
            var v = sel[0];
            var parts = new List<string> { v.Title, v.KindLabel, v.SizeDisplay };
            if (v.Entry.Width > 0) parts.Add(v.DimensionsDisplay);
            if (v.IsVideo && !string.IsNullOrEmpty(v.DurationDisplay)) parts.Add(v.DurationDisplay);
            SelectionText.Text = string.Join("   •   ", parts);
        }
        else
        {
            long total = sel.Sum(i => i.SizeBytes);
            SelectionText.Text = $"{sel.Count} selected  •  {MediaItemViewModel.FormatSize(total)}";
        }
    }

    private IReadOnlyList<MediaItemViewModel> ActiveSelection()
    {
        IList? sel = _viewMode switch
        {
            ViewMode.Icons => IconsView.SelectedItems,
            ViewMode.List => ListView.SelectedItems,
            _ => DetailsGrid.SelectedItems,
        };
        return sel?.Cast<MediaItemViewModel>().ToList() ?? new List<MediaItemViewModel>();
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        UpdateSelectionStatus();
        RefreshCommands();
    }

    private void SetViewMode(ViewMode mode)
    {
        _viewMode = mode;
        IconsView.IsVisible = mode == ViewMode.Icons;
        ListView.IsVisible = mode == ViewMode.List;
        DetailsGrid.IsVisible = mode == ViewMode.Details;
        IconsToggle.IsChecked = mode == ViewMode.Icons;
        ListToggle.IsChecked = mode == ViewMode.List;
        DetailsToggle.IsChecked = mode == ViewMode.Details;
        UpdateSelectionStatus();
        RefreshCommands();
    }

    private void OnViewIcons(object? sender, RoutedEventArgs e) => SetViewMode(ViewMode.Icons);
    private void OnViewList(object? sender, RoutedEventArgs e) => SetViewMode(ViewMode.List);
    private void OnViewDetails(object? sender, RoutedEventArgs e) => SetViewMode(ViewMode.Details);
    private void OnRefresh(object? sender, RoutedEventArgs e) { if (_session != null) _thumbs.QueueAll(_items, _session); }

    // ===== Opening items =====

    private void OnItemActivated(object? sender, TappedEventArgs e)
    {
        object? sel = _viewMode switch
        {
            ViewMode.Icons => IconsView.SelectedItem,
            ViewMode.List => ListView.SelectedItem,
            _ => DetailsGrid.SelectedItem,
        };
        if (sel is MediaItemViewModel vm) OpenItem(vm);
    }

    private async void OpenItem(MediaItemViewModel vm)
    {
        if (_session == null) return;
        try
        {
            if (vm.IsVideo)
            {
                _ = new VideoPlayerWindow(_session, vm.Entry).ShowDialog(this);
            }
            else
            {
                var images = _items.Where(i => !i.IsVideo).Select(i => i.Entry);
                _ = new PhotoViewerWindow(_session, images, vm.Entry).ShowDialog(this);
            }
        }
        catch (Exception ex)
        {
            AppLog.Exception("OpenItem", ex);
            await Dialogs.InfoAsync(this, "Could not open item",
                $"Something went wrong opening \"{vm.Title}\".\n\n{ex.Message}\n\nA log was written to:\n{AppLog.LogPath}");
        }
    }

    // ===== Vault lifecycle =====

    private async Task NewVaultAsync()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Create a new vault",
            SuggestedFileName = "My Media.dvault",
            DefaultExtension = "dvault",
            FileTypeChoices = new[] { new FilePickerFileType("FreedomMedia vault") { Patterns = new[] { "*.dvault" } } },
        });
        var path = file?.TryGetLocalPath();
        if (string.IsNullOrEmpty(path)) return;

        var pw = await new PasswordDialog(PasswordMode.SetNew, "Set a passphrase",
            "This passphrase is the only key to the vault. There is no way to recover it if it is lost.").ShowDialog<string?>(this);
        if (string.IsNullOrEmpty(pw)) return;

        bool ok = await RebuildAsync(path, Array.Empty<PendingItem>(), pw, null, "Creating vault…");
        if (ok) OpenVaultFile(path, pw);
    }

    private async Task OpenVaultAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open a vault",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("FreedomMedia vault") { Patterns = new[] { "*.dvault" } } },
        });
        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrEmpty(path)) return;

        while (true)
        {
            var pw = await new PasswordDialog(PasswordMode.EnterExisting, "Enter passphrase",
                "Enter the passphrase for this vault.").ShowDialog<string?>(this);
            if (string.IsNullOrEmpty(pw)) return;

            try
            {
                using (var probe = VaultSession.Open(path, pw)) { }
                OpenVaultFile(path, pw);
                return;
            }
            catch (WrongPasswordException)
            {
                await Dialogs.InfoAsync(this, "Incorrect passphrase", "That passphrase did not open the vault. Please try again.");
            }
            catch (VaultCorruptException ex)
            {
                await Dialogs.InfoAsync(this, "Cannot open vault", $"This file is not a valid FreedomMedia vault.\n\n{ex.Message}");
                return;
            }
            catch (Exception ex)
            {
                await Dialogs.InfoAsync(this, "Cannot open vault", ex.Message);
                return;
            }
        }
    }

    private void OpenVaultFile(string path, string password)
    {
        VaultSession session;
        try
        {
            session = VaultSession.Open(path, password);
        }
        catch (Exception ex)
        {
            _ = Dialogs.InfoAsync(this, "Cannot open vault", ex.Message);
            return;
        }

        _session?.Dispose();
        _thumbs.Reset();
        _session = session;
        _vaultPath = path;
        _password = password;
        LoadItemsFromSession();
        UpdateUiState();
    }

    private void LoadItemsFromSession()
    {
        _items.Clear();
        if (_session == null) return;
        foreach (var entry in _session.Entries.OrderBy(e => e.Title, StringComparer.OrdinalIgnoreCase))
            _items.Add(new MediaItemViewModel(entry));
        _thumbs.QueueAll(_items, _session);
    }

    private void CloseVault()
    {
        _thumbs.Reset();
        _session?.Dispose();
        _session = null;
        _vaultPath = null;
        _password = null;
        _items.Clear();
        UpdateUiState();
    }

    // ===== Editing (import / remove / rename / passphrase) =====

    private async Task ImportAsync()
    {
        if (_session == null || _vaultPath == null || _password == null) return;

        var patterns = new[]
        {
            "*.jpg", "*.jpeg", "*.png", "*.gif", "*.bmp", "*.webp", "*.tif", "*.tiff", "*.heic", "*.heif", "*.avif",
            "*.mp4", "*.m4v", "*.mov", "*.mkv", "*.webm", "*.avi", "*.wmv", "*.flv", "*.mpg", "*.mpeg", "*.3gp",
        };
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import photos or videos",
            AllowMultiple = true,
            FileTypeFilter = new[] { new FilePickerFileType("Photos and videos") { Patterns = patterns } },
        });

        var paths = files.Select(f => f.TryGetLocalPath()).Where(p => !string.IsNullOrEmpty(p)).Cast<string>()
            .Where(MediaProbe.IsSupported).ToList();
        if (paths.Count == 0) return;

        var pending = new List<PendingItem>();
        foreach (var item in _items)
            pending.Add(new KeepExistingItem { Entry = item.Entry, SourceSession = _session, Title = item.Title });
        foreach (var p in paths)
            pending.Add(new NewFileItem { SourceFilePath = p, Title = System.IO.Path.GetFileNameWithoutExtension(p) });

        await RebuildAndReopenAsync(pending, _password, "Importing…");
    }

    private async Task RemoveAsync()
    {
        if (_session == null || _vaultPath == null || _password == null) return;
        var sel = ActiveSelection();
        if (sel.Count == 0) return;

        bool confirm = await Dialogs.ConfirmAsync(this, "Remove from vault",
            $"Remove {sel.Count} item{(sel.Count == 1 ? "" : "s")} from the vault? This permanently deletes {(sel.Count == 1 ? "it" : "them")} from this vault. It does not affect any copies outside the vault.",
            "Remove");
        if (!confirm) return;

        var removeIds = sel.Select(v => v.Entry.Id).ToHashSet();
        var pending = new List<PendingItem>();
        foreach (var item in _items)
            if (!removeIds.Contains(item.Entry.Id))
                pending.Add(new KeepExistingItem { Entry = item.Entry, SourceSession = _session, Title = item.Title });

        await RebuildAndReopenAsync(pending, _password, "Removing…");
    }

    private async Task RenameAsync()
    {
        if (_session == null || _vaultPath == null || _password == null) return;
        var sel = ActiveSelection();
        if (sel.Count != 1) return;

        var newName = await Dialogs.PromptAsync(this, "Rename", "New name", sel[0].Title);
        if (string.IsNullOrWhiteSpace(newName) || newName == sel[0].Title) return;

        var pending = new List<PendingItem>();
        foreach (var item in _items)
        {
            string title = item.Entry.Id == sel[0].Entry.Id ? newName : item.Title;
            pending.Add(new KeepExistingItem { Entry = item.Entry, SourceSession = _session, Title = title });
        }

        await RebuildAndReopenAsync(pending, _password, "Renaming…");
    }

    private async Task ChangePassphraseAsync()
    {
        if (_session == null || _vaultPath == null) return;
        var pw = await new PasswordDialog(PasswordMode.SetNew, "Change passphrase",
            "Set a new passphrase for this vault. The media inside is not re-encrypted; only the passphrase that unlocks it changes.").ShowDialog<string?>(this);
        if (string.IsNullOrEmpty(pw)) return;

        var pending = new List<PendingItem>();
        foreach (var item in _items)
            pending.Add(new KeepExistingItem { Entry = item.Entry, SourceSession = _session, Title = item.Title });

        await RebuildAndReopenAsync(pending, pw, "Changing passphrase…");
    }

    /// <summary>Rebuild the vault on disk from the given items, then reopen it fresh.</summary>
    private async Task RebuildAndReopenAsync(IReadOnlyList<PendingItem> pending, string password, string heading)
    {
        if (_vaultPath == null || _session == null) return;
        byte[]? masterKey = _session.ExportMasterKeyForRebuild();
        try
        {
            bool ok = await RebuildAsync(_vaultPath, pending, password, masterKey, heading);
            if (ok) OpenVaultFile(_vaultPath, password);
        }
        finally
        {
            if (masterKey != null) Array.Clear(masterKey);
        }
    }

    private async Task<bool> RebuildAsync(string targetPath, IReadOnlyList<PendingItem> pending, string password, byte[]? masterKey, string heading)
    {
        var dialog = new ProgressDialog(heading);
        _ = dialog.ShowDialog(this);
        var progress = new Progress<VaultBuildProgress>(p => dialog.Report(p.CurrentItem, p.BytesDone, p.BytesTotal));
        try
        {
            await Task.Run(() => VaultWriter.Build(targetPath, password, masterKey, pending, progress, dialog.Cts.Token), dialog.Cts.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            await Dialogs.InfoAsync(this, "Could not save vault", ex.Message);
            return false;
        }
        finally
        {
            dialog.Close();
        }
    }

    // ===== Export =====

    private async Task ExportAsync()
    {
        if (_session == null) return;
        var sel = ActiveSelection();
        if (sel.Count == 0) return;

        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose a folder to export into",
            AllowMultiple = false,
        });
        var dest = folders.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrEmpty(dest)) return;

        var entries = sel.Select(v => v.Entry).ToList();
        var session = _session;
        var dialog = new ProgressDialog("Exporting…");
        _ = dialog.ShowDialog(this);
        var progress = new Progress<ExtractProgress>(p => dialog.Report(p.CurrentItem, p.BytesDone, p.BytesTotal));
        bool ok = false;
        try
        {
            await Task.Run(() => VaultExtractor.Extract(session, entries, dest, progress, dialog.Cts.Token), dialog.Cts.Token);
            ok = true;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { await Dialogs.InfoAsync(this, "Export failed", ex.Message); }
        finally { dialog.Close(); }

        if (ok)
            await Dialogs.InfoAsync(this, "Export complete",
                $"Exported {entries.Count} file{(entries.Count == 1 ? "" : "s")} to:\n{dest}");
    }

    // ===== Selection helpers =====

    private void SelectAll()
    {
        switch (_viewMode)
        {
            case ViewMode.Icons: IconsView.SelectAll(); break;
            case ViewMode.List: ListView.SelectAll(); break;
            default: DetailsGrid.SelectAll(); break;
        }
    }

    private void SelectNone()
    {
        IconsView.SelectedItems?.Clear();
        ListView.SelectedItems?.Clear();
        DetailsGrid.SelectedItems?.Clear();
        UpdateSelectionStatus();
        RefreshCommands();
    }

    // ===== Help / misc =====

    private void OnExit(object? sender, RoutedEventArgs e) => Close();

    private async void OnAbout(object? sender, RoutedEventArgs e)
    {
        await Dialogs.InfoAsync(this, "About FreedomMedia",
            "FreedomMedia\n\n" +
            "A private, encrypted vault for your photographs and videos, with its own built-in " +
            "photo viewer and media player. Media is decrypted only in memory, for the moment it " +
            "is shown, and never written to a temporary folder for another program to find.\n\n" +
            "Each vault is a single file on your own computer, protected by a passphrase and " +
            "AES-256 authenticated encryption with an Argon2id key. No account and no internet " +
            "connection are required.\n\n" +
            "Free and open source under the MIT licence.\n" +
            "Part of FreedomSoft — https://freedomsoft.uk");
    }

    private async void OnHelp(object? sender, RoutedEventArgs e)
    {
        await Dialogs.InfoAsync(this, "FreedomMedia Help",
            "Getting started\n\n" +
            "1. Create a vault (File ▸ New Vault) and choose a passphrase.\n" +
            "2. Import photos or videos (File ▸ Import, or Ctrl+I).\n" +
            "3. Double-click any item to view a photo or play a video inside FreedomMedia.\n" +
            "4. Export selected items back out to ordinary files with File ▸ Export.\n\n" +
            "Switch between Icons, List and Details with the View bar at the top right. The status " +
            "bar shows the number of items, the total size, and the details of the current selection.\n\n" +
            "Keep your passphrase safe: it is the only key to the vault, and it cannot be recovered.");
    }

    private void OnVisitSite(object? sender, RoutedEventArgs e) => OpenUrl("https://freedomsoft.uk");

    private static void OpenUrl(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch { /* ignore */ }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Enter && _session != null)
        {
            object? sel = _viewMode switch
            {
                ViewMode.Icons => IconsView.SelectedItem,
                ViewMode.List => ListView.SelectedItem,
                _ => DetailsGrid.SelectedItem,
            };
            if (sel is MediaItemViewModel vm) { OpenItem(vm); e.Handled = true; }
        }
        base.OnKeyDown(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _thumbs.Reset();
        _session?.Dispose();
        base.OnClosed(e);
    }
}
