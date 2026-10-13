using System.Text;
using OpenOd.Core.Drive;

namespace OpenOd.Core.Tests;

public sealed class DriveCacheTests : IDisposable
{
    private readonly string _contentDir = Path.Combine(Path.GetTempPath(), "open-od-tests", Guid.NewGuid().ToString("N"));
    private readonly FakeBackend _backend = new();

    public void Dispose()
    {
        if (Directory.Exists(_contentDir))
            Directory.Delete(_contentDir, recursive: true);
    }

    private DriveCache NewCache() => new(_backend, _contentDir);

    [Fact]
    public void GetEntry_ResolvesNestedPaths()
    {
        var cache = NewCache();

        Assert.Same(DriveCache.Root, cache.GetEntry("/"));
        Assert.True(cache.GetEntry("/Docs")!.IsFolder);
        Assert.Equal("note.txt", cache.GetEntry("/Docs/note.txt")!.Name);
        Assert.Null(cache.GetEntry("/Docs/missing.txt"));
        Assert.Null(cache.GetEntry("/Nothing/note.txt"));
    }

    [Fact]
    public void GetEntry_IgnoresCaseLikeOneDrive()
    {
        Assert.NotNull(NewCache().GetEntry("/docs/NOTE.TXT"));
    }

    [Fact]
    public void GetChildren_SharesListingAcrossCaseVariants()
    {
        var cache = NewCache();

        cache.GetChildren("/Docs");
        cache.GetChildren("/DOCS");

        Assert.Equal(1, _backend.ListCalls.Values.Sum() - _backend.ListCalls.GetValueOrDefault("/"));
    }

    [Fact]
    public void GetChildren_ReturnsNullForFiles()
    {
        Assert.Null(NewCache().GetChildren("/Docs/note.txt"));
    }

    [Fact]
    public void GetChildren_ReusesListingWithinTtl()
    {
        var cache = NewCache();

        cache.GetChildren("/Docs");
        cache.GetChildren("/Docs");

        Assert.Equal(1, _backend.ListCalls["/Docs"]);
    }

    [Fact]
    public void Hydrate_DownloadsOnlyOnce()
    {
        var cache = NewCache();
        var entry = cache.GetEntry("/Docs/note.txt")!;

        Assert.False(cache.IsHydrated(entry));
        var first = cache.Hydrate("/Docs/note.txt", entry);
        var second = cache.Hydrate("/Docs/note.txt", entry);

        Assert.Equal(first, second);
        Assert.True(cache.IsHydrated(entry));
        Assert.Equal("content v1", File.ReadAllText(first));
        Assert.Equal(1, _backend.DownloadCalls);
    }

    [Fact]
    public async Task Hydrate_ConcurrentOpensShareOneDownload()
    {
        _backend.DownloadDelay = TimeSpan.FromMilliseconds(300);
        var cache = NewCache();
        var entry = cache.GetEntry("/Docs/note.txt")!;

        var paths = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => Task.Run(() => cache.Hydrate("/Docs/note.txt", entry))));

        Assert.Single(paths.Distinct());
        Assert.Equal(1, _backend.DownloadCalls);
    }

    [Fact]
    public void Hydrate_NewRemoteVersionIsDownloadedAgain()
    {
        var cache = NewCache();
        var v1 = cache.GetEntry("/Docs/note.txt")!;
        cache.Hydrate("/Docs/note.txt", v1);

        var v2 = v1 with { ContentTag = "v2" };

        Assert.False(cache.IsHydrated(v2));
        cache.Hydrate("/Docs/note.txt", v2);
        Assert.Equal(2, _backend.DownloadCalls);
    }

    [Fact]
    public void Hydrate_FailedDownloadLeavesNoPartialFile()
    {
        _backend.FailDownloads = true;
        var cache = NewCache();
        var entry = cache.GetEntry("/Docs/note.txt")!;

        Assert.Throws<IOException>(() => cache.Hydrate("/Docs/note.txt", entry));
        Assert.False(cache.IsHydrated(entry));

        _backend.FailDownloads = false;
        Assert.Equal("content v1", File.ReadAllText(cache.Hydrate("/Docs/note.txt", entry)));
    }

    private sealed class FakeBackend : IDriveBackend
    {
        private int _downloadCalls;

        public Dictionary<string, int> ListCalls { get; } = new();
        public int DownloadCalls => _downloadCalls;
        public TimeSpan DownloadDelay { get; set; }
        public bool FailDownloads { get; set; }

        public Task<IReadOnlyList<DriveEntry>> ListChildrenAsync(string folderPath, CancellationToken ct = default)
        {
            lock (ListCalls)
                ListCalls[folderPath] = ListCalls.GetValueOrDefault(folderPath) + 1;

            IReadOnlyList<DriveEntry> children = folderPath.ToLowerInvariant() switch
            {
                "/" => [new("docs-id", "Docs", true, 0, DateTimeOffset.UnixEpoch, null)],
                "/docs" => [new("note-id", "note.txt", false, 12, DateTimeOffset.UnixEpoch, "v1")],
                _ => throw new DirectoryNotFoundException(folderPath),
            };
            return Task.FromResult(children);
        }

        public async Task DownloadAsync(DriveEntry entry, Stream destination, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _downloadCalls);
            await Task.Delay(DownloadDelay, ct);
            if (FailDownloads)
                throw new IOException("network down");

            await destination.WriteAsync(Encoding.UTF8.GetBytes("content v1"), ct);
        }
    }
}
