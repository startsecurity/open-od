using OpenOd.Core.Drive;

namespace OpenOd.Core.Tests;

public sealed class HydrationPolicyTests
{
    // Names as they appear in /proc/<pid>/comm (truncated to 15 characters).
    [Theory]
    [InlineData("gsf-office-thum")]   // gsf-office-thumbnailer (Office documents)
    [InlineData("glycin-thumbnai")]   // glycin-thumbnailer (images)
    [InlineData("glycin-image-rs")]   // glycin image loader
    [InlineData("gst-video-thumb")]
    [InlineData("gst-audio-thumb")]
    [InlineData("papers-thumbnai")]   // PDFs
    [InlineData("gnome-thumbnail")]   // gnome-thumbnail-font
    [InlineData("totem-video-thu")]
    [InlineData("ffmpegthumbnail")]
    [InlineData("localsearch-3")]
    [InlineData("localsearch-ext")]
    [InlineData("tracker-extract")]
    [InlineData("tracker-miner-f")]
    [InlineData("baloo_file")]
    [InlineData("baloo_file_extr")]
    public void BlocksThumbnailersAndIndexers(string processName)
    {
        Assert.True(HydrationPolicy.IsBackgroundScanner(processName));
    }

    [Theory]
    [InlineData("cat")]
    [InlineData("nautilus")]          // copying files must still work
    [InlineData("libreoffice")]
    [InlineData("soffice.bin")]
    [InlineData("loupe")]             // image viewer: the user opened the file
    [InlineData("papers")]            // PDF viewer
    [InlineData("cp")]
    [InlineData("rsync")]
    [InlineData("thunar")]
    [InlineData("gnome-text-edit")]   // a full 15-character name that is not a thumbnailer
    public void AllowsRegularPrograms(string processName)
    {
        Assert.False(HydrationPolicy.IsBackgroundScanner(processName));
    }
}

public sealed class PathsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "open-od-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void EnsurePrivateDirectory_CreatesOwnerOnlyDirectory()
    {
        var dir = Paths.EnsurePrivateDirectory(Path.Combine(_root, "cache"));

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(dir));
    }

    [Fact]
    public void EnsurePrivateDirectory_FixesExistingOpenDirectory()
    {
        var dir = Path.Combine(_root, "old");
        Directory.CreateDirectory(dir);
        File.SetUnixFileMode(dir, (UnixFileMode)0b111_101_101); // 0755

        Paths.EnsurePrivateDirectory(dir);

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(dir));
    }

    [Fact]
    public void RestrictToOwner_MakesFileOwnerOnly()
    {
        var file = Path.Combine(Paths.EnsurePrivateDirectory(_root), "msal.cache");
        File.WriteAllText(file, "");
        File.SetUnixFileMode(file, (UnixFileMode)0b110_100_100); // 0644

        Paths.RestrictToOwner(file);

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(file));
    }
}
