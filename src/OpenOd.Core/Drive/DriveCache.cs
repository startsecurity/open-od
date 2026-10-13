using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace OpenOd.Core.Drive;

// Keeps folder listings in memory and the content of downloaded files on disk.
// The real implementation will store listings in SQLite, fed by the Graph API
// delta query, so the mount works offline.
public sealed class DriveCache
{
    private static readonly TimeSpan ListingTtl = TimeSpan.FromSeconds(30);

    private readonly IDriveBackend _backend;
    private readonly string _contentDir;
    // OneDrive is case-insensitive, so "/Docs" and "/docs" must map to the
    // same listing.
    private readonly ConcurrentDictionary<string, Listing> _listings = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Lazy<Task<string>>> _downloads = new();

    private sealed record Listing(DateTime FetchedAt, Dictionary<string, DriveEntry> Children);

    public static readonly DriveEntry Root = new("root", "", true, 0, DateTimeOffset.Now, null);

    public DriveCache(IDriveBackend backend, string contentDir)
    {
        _backend = backend;
        _contentDir = contentDir;
        Directory.CreateDirectory(contentDir);
    }

    /// <summary>Returns the folder's children, or null if the folder does not exist.</summary>
    public IReadOnlyCollection<DriveEntry>? GetChildren(string folderPath)
    {
        folderPath = Normalize(folderPath);
        if (folderPath != "/")
        {
            var self = GetEntry(folderPath);
            if (self is null || !self.IsFolder)
                return null;
        }

        if (_listings.TryGetValue(folderPath, out var cached) && DateTime.UtcNow - cached.FetchedAt < ListingTtl)
            return cached.Children.Values;

        IReadOnlyList<DriveEntry> children;
        try
        {
            children = _backend.ListChildrenAsync(folderPath).GetAwaiter().GetResult();
        }
        catch (DirectoryNotFoundException)
        {
            _listings.TryRemove(folderPath, out _);
            return null;
        }
        catch (Exception ex) when (cached is not null)
        {
            // Offline: serve the last known listing instead of failing.
            Log.Warn($"listing {folderPath} failed ({ex.Message}); serving cached listing");
            return cached.Children.Values;
        }

        var map = new Dictionary<string, DriveEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var child in children)
            map[child.Name] = child;

        _listings[folderPath] = new Listing(DateTime.UtcNow, map);
        Log.Info($"listed {folderPath} ({map.Count} items)");
        return map.Values;
    }

    /// <summary>Resolves an absolute mount path to its drive entry.</summary>
    public DriveEntry? GetEntry(string path)
    {
        path = Normalize(path);
        if (path == "/")
            return Root;

        var parent = ParentOf(path);
        var name = path[(path.LastIndexOf('/') + 1)..];

        if (GetChildren(parent) is null)
            return null;

        return _listings.TryGetValue(parent, out var listing) && listing.Children.TryGetValue(name, out var entry)
            ? entry
            : null;
    }

    public bool IsHydrated(DriveEntry entry) => File.Exists(ContentPath(entry));

    /// <summary>
    /// Ensures the file content is on local disk and returns its path.
    /// This is where "on demand" happens: the download only runs when someone opens the file.
    /// </summary>
    public string Hydrate(string path, DriveEntry entry)
    {
        var target = ContentPath(entry);
        if (File.Exists(target))
            return target;

        // Concurrent opens of the same file share a single download.
        var lazy = _downloads.GetOrAdd(target, _ => new Lazy<Task<string>>(() => DownloadAsync(path, entry, target)));
        try
        {
            return lazy.Value.GetAwaiter().GetResult();
        }
        finally
        {
            _downloads.TryRemove(target, out _);
        }
    }

    private async Task<string> DownloadAsync(string path, DriveEntry entry, string target)
    {
        var started = DateTime.UtcNow;
        Log.Info($"downloading {path} ({FormatSize(entry.Size)})...");

        var partial = target + ".part";
        try
        {
            await using (var file = File.Create(partial))
                await _backend.DownloadAsync(entry, file);
            File.Move(partial, target, overwrite: true);
        }
        catch
        {
            File.Delete(partial);
            throw;
        }

        Log.Info($"downloaded {path} in {(DateTime.UtcNow - started).TotalSeconds:0.0}s");
        return target;
    }

    // The cache file name includes the version (cTag), so a remote change maps to
    // a different file and a stale copy is never served.
    private string ContentPath(DriveEntry entry)
    {
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{entry.Id}|{entry.ContentTag}")));
        return Path.Combine(_contentDir, key[..32].ToLowerInvariant());
    }

    private static string Normalize(string path) =>
        path.Length > 1 ? path.TrimEnd('/') : "/";

    private static string ParentOf(string path)
    {
        var idx = path.LastIndexOf('/');
        return idx <= 0 ? "/" : path[..idx];
    }

    public static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        < 1024L * 1024 * 1024 => $"{bytes / 1024.0 / 1024:0.#} MB",
        _ => $"{bytes / 1024.0 / 1024 / 1024:0.##} GB",
    };
}
