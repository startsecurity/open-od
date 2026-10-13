namespace OpenOd.Core;

public static class Paths
{
    private static string Xdg(string variable, string fallback) =>
        Environment.GetEnvironmentVariable(variable) is { Length: > 0 } value
            ? value
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), fallback);

    public static string ConfigDir => Path.Combine(Xdg("XDG_CONFIG_HOME", ".config"), "open-od");
    public static string CacheDir => Path.Combine(Xdg("XDG_CACHE_HOME", ".cache"), "open-od");
    public static string ContentRoot => Path.Combine(CacheDir, "content");
    public static string ContentDir(string profile) => Path.Combine(ContentRoot, profile);
    public static string ClientIdFile => Path.Combine(ConfigDir, "client-id");

    private const UnixFileMode OwnerOnlyDir = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private const UnixFileMode OwnerOnlyFile = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    /// <summary>
    /// Creates the directory (and any missing parents) and restricts it to the
    /// current user (0700). The mode is enforced on every call, so directories
    /// created by older versions get fixed too.
    /// </summary>
    public static string EnsurePrivateDirectory(string path)
    {
        Directory.CreateDirectory(path, OwnerOnlyDir);
        File.SetUnixFileMode(path, OwnerOnlyDir);
        return path;
    }

    /// <summary>Restricts an existing file to the current user (0600).</summary>
    public static void RestrictToOwner(string file)
    {
        if (File.Exists(file))
            File.SetUnixFileMode(file, OwnerOnlyFile);
    }
}
