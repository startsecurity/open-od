using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using OpenOd.Core;
using OpenOd.Core.Drive;
using static OpenOd.Fuse.FuseNative;

namespace OpenOd.Fuse;

// Read-only filesystem: the whole tree shows up immediately, and each file is
// only downloaded when a program opens it.
//
// libfuse calls the [UnmanagedCallersOnly] methods below from its own threads;
// they translate each call into DriveCache operations.
public static unsafe class CloudFileSystem
{
    // Extended attribute exposing the file state ("cloud" or "local").
    // A Nautilus extension can read it to show emblems.
    private const string StatusXattr = "user.openod.status";

    // Virtual empty file at the mount root. localsearch skips any directory that
    // contains one, so the GNOME indexer never crawls (and downloads) the drive.
    private const string IndexerMarker = "/.trackerignore";

    // File handle id used for the virtual marker file (real handles start at 1).
    private const ulong MarkerHandle = 0;

    private static DriveCache _cache = null!;
    private static readonly ConcurrentDictionary<ulong, SafeFileHandle> Handles = new();
    private static long _nextHandle;

    public static int Mount(DriveCache cache, string mountPoint, bool debug)
    {
        _cache = cache;

        var ops = stackalloc IntPtr[OpCount];
        new Span<IntPtr>(ops, OpCount).Clear();
        ops[OpGetattr] = (IntPtr)(delegate* unmanaged<byte*, byte*, IntPtr, int>)&GetAttr;
        ops[OpOpen] = (IntPtr)(delegate* unmanaged<byte*, byte*, int>)&Open;
        ops[OpRead] = (IntPtr)(delegate* unmanaged<byte*, byte*, nuint, long, byte*, int>)&Read;
        ops[OpRelease] = (IntPtr)(delegate* unmanaged<byte*, byte*, int>)&Release;
        ops[OpOpendir] = (IntPtr)(delegate* unmanaged<byte*, byte*, int>)&OpenDir;
        ops[OpReaddir] = (IntPtr)(delegate* unmanaged<byte*, IntPtr, IntPtr, long, byte*, int, int>)&ReadDir;
        ops[OpGetxattr] = (IntPtr)(delegate* unmanaged<byte*, byte*, byte*, nuint, int>)&GetXattr;
        ops[OpListxattr] = (IntPtr)(delegate* unmanaged<byte*, byte*, nuint, int>)&ListXattr;

        // -f: stay in the foreground (libfuse must not fork the .NET process).
        // ro: read-only for this proof of concept.
        var args = new List<string> { "open-od", "-f", "-o", "ro,fsname=open-od,subtype=open-od", mountPoint };
        if (debug)
            args.Insert(1, "-d");

        var argv = stackalloc IntPtr[args.Count + 1];
        for (var i = 0; i < args.Count; i++)
            argv[i] = Marshal.StringToCoTaskMemUTF8(args[i]);
        argv[args.Count] = IntPtr.Zero;

        var version = new LibFuseVersion { Major = 3, Minor = 18, Hotfix = 0 };
        try
        {
            return FuseMainRealVersioned(args.Count, argv, ops, (nuint)(OpCount * sizeof(IntPtr)), &version, IntPtr.Zero);
        }
        finally
        {
            for (var i = 0; i < args.Count; i++)
                Marshal.FreeCoTaskMem(argv[i]);
            foreach (var handle in Handles.Values)
                handle.Dispose();
        }
    }

    [UnmanagedCallersOnly]
    private static int GetAttr(byte* pathPtr, byte* stat, IntPtr fi)
    {
        return Guard(nameof(GetAttr), pathPtr, path =>
        {
            if (path == IndexerMarker)
            {
                FillStat(stat, S_IFREG | 0b100_100_100, 0, 0, DriveCache.Root.LastModified, 1);
                return 0;
            }

            var entry = _cache.GetEntry(path);
            if (entry is null)
                return -ENOENT;

            if (entry.IsFolder)
            {
                FillStat(stat, S_IFDIR | 0b111_101_101, 4096, 0, entry.LastModified, 2);
            }
            else
            {
                // Cloud-only files use 0 blocks: `du` reports 0, while the apparent
                // size (`ls -l`) is the real file size.
                var blocks = _cache.IsHydrated(entry) ? (entry.Size + 511) / 512 : 0;
                FillStat(stat, S_IFREG | 0b110_100_100, entry.Size, blocks, entry.LastModified, 1);
            }
            return 0;
        });
    }

    [UnmanagedCallersOnly]
    private static int OpenDir(byte* pathPtr, byte* fi)
    {
        return Guard(nameof(OpenDir), pathPtr, path =>
        {
            var entry = _cache.GetEntry(path);
            if (entry is null) return -ENOENT;
            return entry.IsFolder ? 0 : -ENOTDIR;
        });
    }

    [UnmanagedCallersOnly]
    private static int ReadDir(byte* pathPtr, IntPtr buf, IntPtr filler, long offset, byte* fi, int flags)
    {
        return Guard(nameof(ReadDir), pathPtr, path =>
        {
            var children = _cache.GetChildren(path);
            if (children is null)
                return -ENOENT;

            var fill = (delegate* unmanaged<IntPtr, byte*, IntPtr, long, int, int>)filler;
            AddDirEntry(fill, buf, ".");
            AddDirEntry(fill, buf, "..");

            var isRoot = path == "/";
            if (isRoot)
                AddDirEntry(fill, buf, IndexerMarker[1..]);

            foreach (var child in children)
            {
                if (isRoot && "/" + child.Name == IndexerMarker)
                    continue;
                AddDirEntry(fill, buf, child.Name);
            }
            return 0;
        });
    }

    private static void AddDirEntry(delegate* unmanaged<IntPtr, byte*, IntPtr, long, int, int> fill, IntPtr buf, string name)
    {
        var bytes = Encoding.UTF8.GetBytes(name + '\0');
        fixed (byte* p = bytes)
            fill(buf, p, IntPtr.Zero, 0, 0);
    }

    [UnmanagedCallersOnly]
    private static int Open(byte* pathPtr, byte* fi)
    {
        return Guard(nameof(Open), pathPtr, path =>
        {
            if (path == IndexerMarker)
            {
                *(ulong*)(fi + FiFh) = MarkerHandle;
                return 0;
            }

            var entry = _cache.GetEntry(path);
            if (entry is null) return -ENOENT;
            if (entry.IsFolder) return -EISDIR;

            var caller = CallerProcessName();
            if (caller is not null && !_cache.IsHydrated(entry) && HydrationPolicy.IsBackgroundScanner(caller))
            {
                Log.Info($"not downloading {path} for {caller} (thumbnailer/indexer)");
                return -EACCES;
            }

            // Blocks until the download finishes. A real implementation can start
            // serving the first bytes earlier (Range requests).
            var localPath = _cache.Hydrate(path, entry, caller);

            var handle = File.OpenHandle(localPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var id = (ulong)Interlocked.Increment(ref _nextHandle);
            Handles[id] = handle;
            *(ulong*)(fi + FiFh) = id;
            return 0;
        });
    }

    [UnmanagedCallersOnly]
    private static int Read(byte* pathPtr, byte* buffer, nuint size, long offset, byte* fi)
    {
        var id = *(ulong*)(fi + FiFh);
        if (id == MarkerHandle)
            return 0;
        if (!Handles.TryGetValue(id, out var handle))
            return -EIO;

        try
        {
            return RandomAccess.Read(handle, new Span<byte>(buffer, checked((int)size)), offset);
        }
        catch (Exception ex)
        {
            Log.Error($"read failed: {ex.Message}");
            return -EIO;
        }
    }

    [UnmanagedCallersOnly]
    private static int Release(byte* pathPtr, byte* fi)
    {
        if (Handles.TryRemove(*(ulong*)(fi + FiFh), out var handle))
            handle.Dispose();
        return 0;
    }

    [UnmanagedCallersOnly]
    private static int GetXattr(byte* pathPtr, byte* namePtr, byte* value, nuint size)
    {
        return Guard(nameof(GetXattr), pathPtr, path =>
        {
            if (path == IndexerMarker || Marshal.PtrToStringUTF8((IntPtr)namePtr) != StatusXattr)
                return -ENODATA;

            var entry = _cache.GetEntry(path);
            if (entry is null) return -ENOENT;
            if (entry.IsFolder) return -ENODATA;

            var status = Encoding.UTF8.GetBytes(_cache.IsHydrated(entry) ? "local" : "cloud");
            return CopyOut(status, value, size);
        });
    }

    [UnmanagedCallersOnly]
    private static int ListXattr(byte* pathPtr, byte* list, nuint size)
    {
        return Guard(nameof(ListXattr), pathPtr, path =>
        {
            if (path == IndexerMarker)
                return 0;

            var entry = _cache.GetEntry(path);
            if (entry is null) return -ENOENT;
            return entry.IsFolder ? 0 : CopyOut(Encoding.UTF8.GetBytes(StatusXattr + '\0'), list, size);
        });
    }

    // Name of the process behind the current FUSE request, from /proc/<pid>/comm.
    // The kernel reports the pid in our pid namespace, so this also works for
    // thumbnailers running inside a bubblewrap sandbox.
    private static string? CallerProcessName()
    {
        var pid = *(int*)(FuseGetContext() + CtxPid);
        if (pid <= 0)
            return null;

        try
        {
            return File.ReadAllText($"/proc/{pid}/comm").TrimEnd('\n');
        }
        catch (IOException)
        {
            return null; // the process already exited
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    // getxattr/listxattr convention: when size == 0, return the required size.
    private static int CopyOut(byte[] data, byte* dest, nuint size)
    {
        if (size == 0) return data.Length;
        if ((int)size < data.Length) return -ERANGE;
        data.CopyTo(new Span<byte>(dest, data.Length));
        return data.Length;
    }

    // An exception must not cross the native boundary: it would crash the process
    // and leave the mount point stuck. Everything becomes -EIO.
    private static int Guard(string op, byte* pathPtr, Func<string, int> body)
    {
        var path = PathFrom(pathPtr);
        try
        {
            return body(path);
        }
        catch (UnauthorizedAccessException)
        {
            return -EACCES;
        }
        catch (Exception ex)
        {
            Log.Error($"{op} {path}: {ex.GetType().Name}: {ex.Message}");
            return -EIO;
        }
    }
}
