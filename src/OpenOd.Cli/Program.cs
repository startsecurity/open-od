using System.Diagnostics;
using System.Runtime.InteropServices;
using OpenOd.Core;
using OpenOd.Core.Drive;
using OpenOd.Fuse;
using OpenOd.Core.Graph;

if (RuntimeInformation.ProcessArchitecture != Architecture.X64)
{
    Console.Error.WriteLine("This proof of concept only supports x86_64 (the struct stat layout is hard-coded).");
    return 1;
}

try
{
    var command = args.FirstOrDefault();
    switch (command)
    {
        case "login":
            return await LoginAsync(args.Skip(1).ToArray());
        case "logout":
            await (await Auth.CreateAsync(RequireClientId())).LogoutAsync();
            Console.WriteLine("Saved session removed.");
            return 0;
        case "mount" when args.Length >= 2:
            return await MountAsync(args[1], demo: args.Contains("--demo"), debug: args.Contains("--debug"));
        default:
            Console.WriteLine("""
                open-od — Microsoft OneDrive client with Files On-Demand

                Usage:
                  open-od login [--client-id <ID>]    sign in to your Microsoft account
                  open-od mount <dir>                 mount OneDrive at <dir>
                  open-od mount <dir> --demo          mount a fake cloud (no account needed)
                  open-od logout                      remove the saved token

                Mount options: --debug prints every FUSE operation.
                To unmount: Ctrl+C, or fusermount3 -u <dir>.
                """);
            return command is null or "help" or "--help" ? 0 : 1;
    }
}
catch (InvalidOperationException e)
{
    Console.Error.WriteLine(e.Message);
    return 1;
}
catch (Microsoft.Identity.Client.MsalException e)
{
    Console.Error.WriteLine($"Falha no login ({e.ErrorCode}): {e.Message}");
    return 1;
}

static async Task<int> LoginAsync(string[] args)
{
    var idx = Array.IndexOf(args, "--client-id");
    if (idx >= 0 && idx + 1 < args.Length)
    {
        Directory.CreateDirectory(Paths.ConfigDir);
        File.WriteAllText(Paths.ClientIdFile, args[idx + 1].Trim());
    }

    var auth = await Auth.CreateAsync(RequireClientId());
    await auth.LoginAsync();

    var backend = new GraphBackend(auth);
    Console.WriteLine(await backend.ConnectAsync());
    return 0;
}

static async Task<int> MountAsync(string mountPoint, bool demo, bool debug)
{
    mountPoint = Path.GetFullPath(mountPoint);
    Directory.CreateDirectory(mountPoint);
    if (Directory.EnumerateFileSystemEntries(mountPoint).Any())
    {
        Console.Error.WriteLine($"{mountPoint} is not empty.");
        return 1;
    }

    IDriveBackend backend;
    if (demo)
    {
        backend = new DemoBackend();
        Log.Info("using the demo cloud");
    }
    else
    {
        var graph = new GraphBackend(await Auth.CreateAsync(RequireClientId()));
        Log.Info($"connected: {await graph.ConnectAsync()}");
        backend = graph;
    }

    var cache = new DriveCache(backend, Paths.ContentDir(demo ? "demo" : "onedrive"));

    // libfuse does not install its own Ctrl+C handler because .NET already has one.
    // So on Ctrl+C we request an unmount and the libfuse loop exits on its own.
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        Log.Info("unmounting...");
        Process.Start("fusermount3", ["-u", mountPoint]);
    };

    Log.Info($"mounted at {mountPoint} (Ctrl+C to quit)");
    var rc = CloudFileSystem.Mount(cache, mountPoint, debug);
    Log.Info($"unmounted (exit code {rc})");
    return rc;
}

static string RequireClientId()
{
    if (Environment.GetEnvironmentVariable("OPENOD_CLIENT_ID") is { Length: > 0 } fromEnv)
        return fromEnv;
    if (File.Exists(Paths.ClientIdFile))
        return File.ReadAllText(Paths.ClientIdFile).Trim();

    return Auth.DefaultClientId;
}
