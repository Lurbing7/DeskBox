using DeskBox.Services;
using DeskBox.FileSafety;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Windows.System;

namespace DeskBox.Controls.WidgetContents;

public sealed partial class DockWidgetContent
{
    private MenuFlyoutSubItem CreateInsertMenu(string directory, int? index)
    {
        var insert = new MenuFlyoutSubItem { Text = T("Dock.InsertItem"), Icon = new SymbolIcon(Symbol.Add) };
        var applications = new MenuFlyoutItem { Text = T("Dock.AddApplications"), Icon = new SymbolIcon(Symbol.OpenFile) };
        applications.Click += (_, _) => DispatcherQueue.TryEnqueue(async () =>
        {
            HideFolderPanel();
            App.Current.WidgetManager?.BeginWidgetInteraction("DockPicker");
            try
            {
                var files = await FileOpenPickerService.PickFilesAsync(_window);
                if (files.Count > 0) await ImportNativeDroppedAppsAsync(files, insertionIndex: index, directory: directory);
            }
            catch { if (!_disposed) SetStatus(T("Dock.ReadFailed")); }
            finally { App.Current.WidgetManager?.EndWidgetInteraction("DockPicker"); }
        });
        insert.Items.Add(applications);
        var folder = new MenuFlyoutItem { Text = T("Common.NewFolder"), Icon = new SymbolIcon(Symbol.Folder) };
        folder.Click += (_, _) => DispatcherQueue.TryEnqueue(() => ShowNameEditor(null, directory, index));
        insert.Items.Add(folder);
        return insert;
    }

    private void ShowNameEditor(DockEntry? entry, string? directory = null, int? insertionIndex = null)
    {
        HideFolderPanel(); _popup?.Hide();
        string parent = directory ?? System.IO.Path.GetDirectoryName(entry!.Path)!;
        var input = new TextBox { Text = entry?.Name ?? T("Common.NewFolder"), Header = T(entry is null ? "Common.NewFolder" : "Common.Rename"), Width = 220 };
        AutomationProperties.SetName(input, T(entry is null ? "Common.NewFolder" : "Common.Rename"));
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 220, Visibility = Visibility.Collapsed };
        var save = new Button { Content = T("Common.Save") };
        var cancel = new Button { Content = T("Common.Cancel") };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(save); buttons.Children.Add(cancel);
        var panel = new StackPanel { Spacing = 8 }; panel.Children.Add(input); panel.Children.Add(error); panel.Children.Add(buttons);
        var popup = new Flyout { Content = panel }; _popup = popup;
        cancel.Click += (_, _) => popup.Hide();
        popup.Opened += (_, _) => { input.Focus(FocusState.Programmatic); input.SelectAll(); };
        async Task Submit()
        {
            if (!save.IsEnabled || _disposed) return;
            save.IsEnabled = false;
            try
            {
                string name = input.Text.Trim();
                if (string.IsNullOrWhiteSpace(name) || name != FileService.SanitizeFileSystemName(name)) throw new ArgumentException("Invalid name");
                if (!DockShortcutPlacement.IsOwned(Config.MappedFolderPath!, System.IO.Path.Combine(parent, "entry"))) throw new InvalidOperationException("External directory");
                string fileName = entry is null ? name : FileService.ResolveRenameDestination(System.IO.Path.GetFileName(entry.Path), name,
                    isFolder: Directory.Exists(entry.Path), isShortcut: true, showFileExtensions: false, out _);
                string destination = System.IO.Path.Combine(parent, fileName);
                string[] current = _row.Children.OfType<Button>().Select(b => b.DataContext).OfType<DockEntry>().Select(e => System.IO.Path.GetFileName(e.Path)).ToArray();
                if (entry is not null) await App.Current.FileService.RenameEntryAsync(entry.Path, destination);
                else
                {
                    await Task.Run(() =>
                    {
                        if (Directory.Exists(destination) || File.Exists(destination)) throw new IOException("Entry already exists");
                        Directory.CreateDirectory(destination);
                    });
                }
                if (string.Equals(parent, Config.MappedFolderPath, StringComparison.OrdinalIgnoreCase))
                {
                    Config.Metadata["DockOrder"] = entry is null ? DockManualOrder.Insert(current, [fileName], insertionIndex ?? current.Length)
                        : string.Join('|', current.Select(n => n.Equals(System.IO.Path.GetFileName(entry.Path), StringComparison.OrdinalIgnoreCase) ? fileName : n));
                    SaveOptions();
                }
                popup.Hide(); await RefreshAsync();
            }
            catch { error.Text = T("Dock.NameFailed"); error.Visibility = Visibility.Visible; }
            finally { save.IsEnabled = true; }
        }
        save.Click += async (_, _) => await Submit();
        input.KeyDown += async (_, e) =>
        {
            if (e.Key == VirtualKey.Enter) { e.Handled = true; await Submit(); }
            else if (e.Key == VirtualKey.Escape) { e.Handled = true; popup.Hide(); }
        };
        Show(popup, this);
    }
}
