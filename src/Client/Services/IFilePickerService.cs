namespace Axorith.Client.Services;

public interface IFilePickerService
{
    Task<string?> PickFileAsync(string title, string? filter, string? initialPath);
    Task<string?> PickFolderAsync(string title, string? initialPath);
}