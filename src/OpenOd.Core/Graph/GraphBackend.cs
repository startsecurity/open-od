using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
using Microsoft.Kiota.Abstractions.Authentication;
using OpenOd.Core.Drive;

namespace OpenOd.Core.Graph;

// Talks to OneDrive through the Microsoft Graph API using the official SDK.
public sealed class GraphBackend : IDriveBackend
{
    private static readonly string[] SelectFields =
        ["id", "name", "size", "folder", "file", "lastModifiedDateTime", "cTag"];

    private readonly GraphServiceClient _client;
    private string? _driveId;

    public GraphBackend(Auth auth)
    {
        _client = new GraphServiceClient(new BaseBearerTokenAuthenticationProvider(auth));
    }

    public async Task<string> ConnectAsync(CancellationToken ct = default)
    {
        var drive = await _client.Me.Drive.GetAsync(cancellationToken: ct)
            ?? throw new InvalidOperationException("Could not open the account's OneDrive.");
        _driveId = drive.Id;

        var quota = drive.Quota is { Used: { } used, Total: { } total }
            ? $" — {DriveCache.FormatSize(used)} of {DriveCache.FormatSize(total)} used"
            : "";
        return $"{drive.DriveType} drive of {drive.Owner?.User?.DisplayName}{quota}";
    }

    public async Task<IReadOnlyList<DriveEntry>> ListChildrenAsync(string folderPath, CancellationToken ct = default)
    {
        var root = _client.Drives[DriveId].Items["root"];
        var children = folderPath == "/"
            ? root.Children
            : root.ItemWithPath(folderPath.TrimStart('/')).Children;

        var result = new List<DriveEntry>();
        try
        {
            var page = await children.GetAsync(r =>
            {
                r.QueryParameters.Select = SelectFields;
                r.QueryParameters.Top = 999;
            }, ct);

            while (page is not null)
            {
                foreach (var item in page.Value ?? [])
                    result.Add(ToEntry(item));

                if (page.OdataNextLink is null)
                    break;
                page = await children.WithUrl(page.OdataNextLink).GetAsync(cancellationToken: ct);
            }
        }
        catch (ODataError e) when (e.ResponseStatusCode == 404)
        {
            throw new DirectoryNotFoundException(folderPath);
        }

        return result;
    }

    public async Task DownloadAsync(DriveEntry entry, Stream destination, CancellationToken ct = default)
    {
        await using var content = await _client.Drives[DriveId].Items[entry.Id].Content.GetAsync(cancellationToken: ct)
            ?? throw new IOException($"Empty content for {entry.Name}");
        await content.CopyToAsync(destination, ct);
    }

    private string DriveId => _driveId ?? throw new InvalidOperationException("Chame ConnectAsync primeiro.");

    private static DriveEntry ToEntry(DriveItem item) => new(
        item.Id!,
        item.Name!,
        item.Folder is not null,
        item.Size ?? 0,
        item.LastModifiedDateTime ?? DateTimeOffset.UnixEpoch,
        item.CTag);
}
