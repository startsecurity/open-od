using System.Text;

namespace OpenOd.Core.Drive;

// Fake in-memory "cloud" with an artificial download delay.
// Used to test the FUSE layer without a Microsoft account or network access.
public sealed class DemoBackend : IDriveBackend
{
    private static readonly TimeSpan DownloadDelay = TimeSpan.FromSeconds(2);

    private readonly Dictionary<string, List<(DriveEntry Entry, byte[]? Content)>> _tree = new(StringComparer.OrdinalIgnoreCase);

    public DemoBackend()
    {
        var now = DateTimeOffset.Now;
        AddFolder("/", "Documents", now);
        AddFolder("/", "Photos", now.AddDays(-3));
        AddFile("/", "README.txt", "This file came from the demo \"cloud\".\n", now);
        AddFile("/Documents", "report.md", "# Report\n\nContent downloaded on demand.\n", now.AddHours(-5));
        AddFile("/Documents", "sheet.csv", "month,value\njan,10\nfeb,20\n", now.AddDays(-1));
        AddFolder("/Photos", "2026", now.AddDays(-10));
        AddFile("/Photos/2026", "large.bin", new byte[25 * 1024 * 1024], now.AddDays(-10));
    }

    public async Task<IReadOnlyList<DriveEntry>> ListChildrenAsync(string folderPath, CancellationToken ct = default)
    {
        await Task.Delay(200, ct);
        return _tree.TryGetValue(folderPath, out var items)
            ? items.Select(i => i.Entry).ToList()
            : throw new DirectoryNotFoundException(folderPath);
    }

    public async Task DownloadAsync(DriveEntry entry, Stream destination, CancellationToken ct = default)
    {
        await Task.Delay(DownloadDelay, ct);
        var content = _tree.Values.SelectMany(v => v).First(i => i.Entry.Id == entry.Id).Content!;
        await destination.WriteAsync(content, ct);
    }

    private void AddFolder(string parent, string name, DateTimeOffset modified)
    {
        Add(parent, new DriveEntry(Guid.NewGuid().ToString(), name, true, 0, modified, null), null);
        _tree[Join(parent, name)] = [];
    }

    private void AddFile(string parent, string name, string text, DateTimeOffset modified) =>
        AddFile(parent, name, Encoding.UTF8.GetBytes(text), modified);

    private void AddFile(string parent, string name, byte[] content, DateTimeOffset modified) =>
        Add(parent, new DriveEntry(Guid.NewGuid().ToString(), name, false, content.Length, modified, "v1"), content);

    private void Add(string parent, DriveEntry entry, byte[]? content)
    {
        if (!_tree.TryGetValue(parent, out var list))
            _tree[parent] = list = [];
        list.Add((entry, content));
    }

    private static string Join(string parent, string name) => parent == "/" ? "/" + name : $"{parent}/{name}";
}
