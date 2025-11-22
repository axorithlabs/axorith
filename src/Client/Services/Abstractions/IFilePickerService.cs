namespace Axorith.Client.Services.Abstractions;

public interface IFilePickerService
{
    Task<string?> PickFileAsync(string title, string? filter, string? initialPath);
    Task<string?> PickFolderAsync(string title, string? initialPath);
}