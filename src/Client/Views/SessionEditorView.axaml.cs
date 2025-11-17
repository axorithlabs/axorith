using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Axorith.Client.ViewModels;
using Axorith.Sdk.Settings;

namespace Axorith.Client.Views;

public partial class SessionEditorView : UserControl
{
    public SessionEditorView()
    {
        InitializeComponent();
    }

    private async void OnBrowseFileClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: SettingViewModel vm } button) return;
        if (vm.Setting.ControlType != SettingControlType.FilePicker) return;
        var fileSetting = vm.Setting;

        var topLevel = TopLevel.GetTopLevel(button);
        if (topLevel?.StorageProvider is null) return;

        IStorageFolder? startLocation = null;
        if (!string.IsNullOrWhiteSpace(vm.StringValue))
        {
            var initialPath = Path.GetDirectoryName(vm.StringValue);
            if (initialPath != null)
                startLocation = await topLevel.StorageProvider.TryGetFolderFromPathAsync(initialPath);
        }

        var filePickerOptions = new FilePickerOpenOptions
        {
            Title = $"Select {vm.Label}",
            AllowMultiple = false,
            SuggestedStartLocation = startLocation
        };

        if (!string.IsNullOrWhiteSpace(fileSetting.Filter))
        {
            var patterns = fileSetting.Filter.Split('|');
            var fileTypeFilters = new FilePickerFileType[patterns.Length / 2];
            for (var i = 0; i < patterns.Length; i += 2)
                fileTypeFilters[i / 2] = new FilePickerFileType(patterns[i])
                {
                    Patterns = [patterns[i + 1]]
                };
            filePickerOptions.FileTypeFilter = fileTypeFilters;
        }

        var result = await topLevel.StorageProvider.OpenFilePickerAsync(filePickerOptions);
        var selectedFile = result.FirstOrDefault();
        if (selectedFile is not null) vm.StringValue = selectedFile.Path.LocalPath;
    }

    private async void OnBrowseDirectoryClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: SettingViewModel vm } button) return;
        if (vm.Setting.ControlType != SettingControlType.DirectoryPicker) return;

        var topLevel = TopLevel.GetTopLevel(button);
        if (topLevel?.StorageProvider is null) return;

        IStorageFolder? startLocation = null;
        if (!string.IsNullOrWhiteSpace(vm.StringValue))
            startLocation = await topLevel.StorageProvider.TryGetFolderFromPathAsync(vm.StringValue);

        var folderPickerOptions = new FolderPickerOpenOptions
        {
            Title = $"Select {vm.Label}",
            AllowMultiple = false,
            SuggestedStartLocation = startLocation
        };

        var result = await topLevel.StorageProvider.OpenFolderPickerAsync(folderPickerOptions);
        var selectedFolder = result.FirstOrDefault();
        if (selectedFolder is not null)
        {
            // Path может быть относительным URI - берем AbsolutePath или LocalPath
            var uri = selectedFolder.Path;
            vm.StringValue = uri.IsAbsoluteUri ? uri.LocalPath : uri.OriginalString;
        }
    }
}