namespace OpenOd.Core.Drive;

public sealed record DriveEntry(
    string Id,
    string Name,
    bool IsFolder,
    long Size,
    DateTimeOffset LastModified,
    string? ContentTag);

// Source of cloud data. The filesystem does not know whether it is talking to
// the real Graph API or to the demo backend.
public interface IDriveBackend
{
    // Paths follow OneDrive semantics: they are case-insensitive.
    Task<IReadOnlyList<DriveEntry>> ListChildrenAsync(string folderPath, CancellationToken ct = default);

    Task DownloadAsync(DriveEntry entry, Stream destination, CancellationToken ct = default);
}
