using System.Runtime.InteropServices;
using Windows.Storage;

namespace Hearthlist.Backend.Services;

/// <summary>Simplified storage handle returned by <see cref="StorageProvider"/>.</summary>
public sealed class StorageFileHandle
{
    public required string Name { get; init; }
    public bool IsPackaged { get; init; }
    public string? PackagedToken { get; init; }
    public string? FullPath { get; init; }
}

/// <summary>
/// Abstracts file storage so the backend works both:
///  - packaged (MSIX): uses ApplicationData.Current.LocalFolder (sandboxed, Store-safe)
///  - unpackaged (plain desktop exe): falls back to a local folder under AppData
///
/// ApplicationData.Current throws when the process has no package identity, so
/// we detect that and use a regular folder instead. This lets you run the app
/// right now with no packaging while keeping the Store-ready path intact.
/// </summary>
public static class StorageProvider
{
    /// <summary>Root folder used for unpackaged mode.</summary>
    public static string UnpackagedRoot { get; set; } =
        System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Hearthlist");

    private static bool? _isPackaged;

    public static bool IsPackaged
    {
        get
        {
            if (_isPackaged is not null)
                return _isPackaged.Value;

            _isPackaged = TryGetPackageIdentity();
            return _isPackaged.Value;
        }
    }

    public static async Task<StorageFileHandle?> GetFileIfExistsAsync(string fileName)
    {
        if (IsPackaged)
        {
            var folder = ApplicationData.Current.LocalFolder;
            if (await folder.TryGetItemAsync(fileName) is StorageFile existing)
                return new StorageFileHandle
                {
                    Name = fileName,
                    IsPackaged = true,
                    PackagedToken = existing.Path
                };
            return null;
        }

        var path = System.IO.Path.Combine(UnpackagedRoot, fileName);
        return System.IO.File.Exists(path)
            ? new StorageFileHandle { Name = fileName, IsPackaged = false, FullPath = path }
            : null;
    }

    public static async Task<StorageFileHandle> GetOrCreateFileAsync(string fileName)
    {
        if (IsPackaged)
        {
            var folder = ApplicationData.Current.LocalFolder;
            var file = await folder.CreateFileAsync(fileName, CreationCollisionOption.OpenIfExists);
            return new StorageFileHandle { Name = fileName, IsPackaged = true, PackagedToken = file.Path };
        }

        System.IO.Directory.CreateDirectory(UnpackagedRoot);
        var path = System.IO.Path.Combine(UnpackagedRoot, fileName);
        if (!System.IO.File.Exists(path))
            System.IO.File.WriteAllText(path, string.Empty);
        return new StorageFileHandle { Name = fileName, IsPackaged = false, FullPath = path };
    }

    public static async Task<string> ReadTextAsync(StorageFileHandle handle)
    {
        if (handle.IsPackaged)
        {
            var file = await StorageFile.GetFileFromPathAsync(handle.PackagedToken);
            return await FileIO.ReadTextAsync(file);
        }

        return System.IO.File.ReadAllText(handle.FullPath!);
    }

    public static async Task WriteTextAsync(StorageFileHandle handle, string content)
    {
        if (handle.IsPackaged)
        {
            var file = await StorageFile.GetFileFromPathAsync(handle.PackagedToken);
            await FileIO.WriteTextAsync(file, content);
            return;
        }

        System.IO.File.WriteAllText(handle.FullPath!, content);
    }

    public static async Task WriteBytesAsync(StorageFileHandle handle, byte[] bytes)
    {
        if (handle.IsPackaged)
        {
            var file = await StorageFile.GetFileFromPathAsync(handle.PackagedToken);
            await FileIO.WriteBytesAsync(file, bytes);
            return;
        }

        System.IO.File.WriteAllBytes(handle.FullPath!, bytes);
    }

    public static string GetDisplayPath(string fileName)
    {
        if (IsPackaged)
            return $"ms-appdata:///local/{fileName}";
        return System.IO.Path.Combine(UnpackagedRoot, fileName);
    }

    private static bool TryGetPackageIdentity()
    {
        try
        {
            _ = Windows.ApplicationModel.Package.Current;
            return true;
        }
        catch (COMException)
        {
            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
