namespace OpenOd.Core.Drive;

// Decides whether opening a cloud-only file may trigger a download.
//
// Desktop background services open files on their own: Nautilus spawns
// thumbnailers for every document and image in a folder, and the GNOME
// indexer (localsearch) extracts text from everything under $HOME. Letting
// them hydrate files would silently download the whole drive. Windows Cloud
// Files has the same problem and blocks such processes too.
public static class HydrationPolicy
{
    // Matched against /proc/<pid>/comm, which the kernel truncates to 15 characters
    // (e.g. "gsf-office-thumbnailer" shows up as "gsf-office-thum").
    private static readonly string[] ScannerPrefixes =
    [
        "localsearch",   // GNOME indexer: localsearch-3, localsearch-extractor-3
        "tracker-",      // older GNOME indexer: tracker-miner-fs, tracker-extract
        "baloo",         // KDE indexer: baloo_file, baloo_file_extractor
        "glycin-",       // GNOME image loaders used by the image thumbnailers
    ];

    /// <summary>
    /// True for thumbnailers and indexers, which must never download a file
    /// that is still cloud-only.
    /// </summary>
    public static bool IsBackgroundScanner(string processName) =>
        processName.Contains("thumbnail", StringComparison.OrdinalIgnoreCase)
        || IsTruncatedThumbnailer(processName)
        || ScannerPrefixes.Any(p => processName.StartsWith(p, StringComparison.OrdinalIgnoreCase));

    private const int CommMaxLength = 15;
    private const string ThumbnailerSuffix = "-thumbnailer";

    // "totem-video-thumbnailer" arrives as "totem-video-thu": a full-length name
    // that ends with the start of "-thumbnailer" ("-thu", "-thum", ...).
    private static bool IsTruncatedThumbnailer(string processName)
    {
        if (processName.Length != CommMaxLength)
            return false;

        for (var len = 4; len <= ThumbnailerSuffix.Length; len++)
        {
            if (processName.EndsWith(ThumbnailerSuffix[..len], StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
