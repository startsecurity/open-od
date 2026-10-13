namespace OpenOd.Core;

public static class Paths
{
    private static string Xdg(string variable, string fallback) =>
        Environment.GetEnvironmentVariable(variable) is { Length: > 0 } value
            ? value
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), fallback);

    public static string ConfigDir => Path.Combine(Xdg("XDG_CONFIG_HOME", ".config"), "open-od");
    public static string CacheDir => Path.Combine(Xdg("XDG_CACHE_HOME", ".cache"), "open-od");
    public static string ContentDir(string profile) => Path.Combine(CacheDir, "content", profile);
    public static string ClientIdFile => Path.Combine(ConfigDir, "client-id");
}
