namespace Deskverse.App.Services;

using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

/// <summary>
/// File and folder pickers for the unpackaged app. Pickers must be parented to
/// the main window handle, otherwise they cannot be shown.
/// </summary>
public sealed class PickerService
{
    private static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".bmp", ".webp", ".gif"];
    private static readonly string[] VideoExtensions = [".mp4", ".webm", ".mkv", ".mov", ".avi"];

    private IntPtr _ownerHandle;

    public void SetOwner(IntPtr ownerHandle) => _ownerHandle = ownerHandle;

    public async Task<IReadOnlyList<string>?> PickWallpaperFilesAsync()
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.PicturesLibrary,
            ViewMode = PickerViewMode.Thumbnail,
        };

        foreach (var extension in ImageExtensions.Concat(VideoExtensions))
        {
            picker.FileTypeFilter.Add(extension);
        }

        InitializeWithWindow.Initialize(picker, _ownerHandle);
        var files = await picker.PickMultipleFilesAsync();
        return files is { Count: > 0 } ? files.Select(f => f.Path).ToArray() : null;
    }

    public async Task<string?> PickFolderAsync(string suggestLocation)
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary };
        picker.FileTypeFilter.Add("*");

        InitializeWithWindow.Initialize(picker, _ownerHandle);
        var folder = await picker.PickSingleFolderAsync();
        if (folder is null)
        {
            return null;
        }

        return folder.Path;
    }
}
