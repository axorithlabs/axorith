using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace Axorith.Client.Services;

public class FilePickerService(IClassicDesktopStyleApplicationLifetime desktop) : IFilePickerService
{
    private IStorageProvider? StorageProvider => desktop.MainWindow?.StorageProvider;

    public async Task<string?> PickFileAsync(string title, string? filter, string? initialPath)
    {
        if (StorageProvider is null) return null;

        var options = new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        };

        if (!string.IsNullOrWhiteSpace(initialPath))
        {
            try 
            {
                var dir = Path.GetDirectoryName(initialPath);
                if (dir != null)
                {
                    options.SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(dir);
                }
            }
            catch
            {
                // ignored
            }
        }

        if (!string.IsNullOrWhiteSpace(filter))
        {
            options.FileTypeFilter = ParseFilter(filter);
        }

        var result = await StorageProvider.OpenFilePickerAsync(options);
        return result.FirstOrDefault()?.Path.LocalPath;
    }

    public async Task<string?> PickFolderAsync(string title, string? initialPath)
    {
        if (StorageProvider is null) return null;

        var options = new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        };

        if (!string.IsNullOrWhiteSpace(initialPath))
        {
            try
            {
                options.SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(initialPath);
            }
            catch
            {
                // ignored
            }
        }

        var result = await StorageProvider.OpenFolderPickerAsync(options);
        var folder = result.FirstOrDefault();
        
        if (folder == null) return null;

        return folder.Path.IsAbsoluteUri ? folder.Path.LocalPath : folder.Path.OriginalString;
    }

    private static IReadOnlyList<FilePickerFileType> ParseFilter(string filterString)
    {
        var parts = filterString.Split('|');
        var filters = new List<FilePickerFileType>();

        for (var i = 0; i < parts.Length; i += 2)
        {
            if (i + 1 >= parts.Length) break;

            var name = parts[i];
            var pattern = parts[i + 1];

            var patterns = pattern.Split(';', StringSplitOptions.RemoveEmptyEntries)
                                  .Select(p => p.Trim())
                                  .ToList();

            var fileType = new FilePickerFileType(name)
            {
                Patterns = patterns
            };
            filters.Add(fileType);
        }

        return filters;
    }
}