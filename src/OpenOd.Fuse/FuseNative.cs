using System.Runtime.InteropServices;

namespace OpenOd.Fuse;

// Minimal bindings for the libfuse3 high-level API (fuse.h).
// Offsets were verified against the libfuse 3.18 headers on x86_64
// (struct stat = 144 bytes, struct fuse_operations = 43 pointers).
internal static unsafe class FuseNative
{
    private const string LibFuse = "libfuse3.so.4";
    private const string LibC = "libc";

    // Index of each callback in struct fuse_operations (all are 8-byte pointers).
    public const int OpGetattr = 0;
    public const int OpOpen = 12;
    public const int OpRead = 13;
    public const int OpStatfs = 15;
    public const int OpRelease = 17;
    public const int OpGetxattr = 20;
    public const int OpListxattr = 21;
    public const int OpOpendir = 23;
    public const int OpReaddir = 24;
    public const int OpCount = 43;

    [StructLayout(LayoutKind.Sequential)]
    public struct LibFuseVersion
    {
        public uint Major, Minor, Hotfix, Padding;
    }

    [DllImport(LibFuse, EntryPoint = "fuse_main_real_versioned")]
    public static extern int FuseMainRealVersioned(
        int argc, IntPtr* argv, IntPtr* ops, nuint opSize, LibFuseVersion* version, IntPtr userData);

    [DllImport(LibFuse, EntryPoint = "fuse_get_context")]
    public static extern byte* FuseGetContext();

    // Offset of pid in struct fuse_context (fuse*, uid_t, gid_t, pid_t, ...).
    public const int CtxPid = 16;

    [DllImport(LibC)] public static extern uint getuid();
    [DllImport(LibC)] public static extern uint getgid();

    // errno (Linux)
    public const int ENOENT = 2;
    public const int EIO = 5;
    public const int EACCES = 13;
    public const int ENOTDIR = 20;
    public const int EISDIR = 21;
    public const int ERANGE = 34;
    public const int ENODATA = 61;

    // st_mode
    public const uint S_IFDIR = 0x4000;
    public const uint S_IFREG = 0x8000;

    // Offsets in struct stat (x86_64)
    private const int StSize = 144;
    private const int StNlink = 16;
    private const int StMode = 24;
    private const int StUid = 28;
    private const int StGid = 32;
    private const int StSizeOff = 48;
    private const int StBlksize = 56;
    private const int StBlocks = 64;
    private const int StAtim = 72;
    private const int StMtim = 88;
    private const int StCtim = 104;

    // Offset of fh in struct fuse_file_info
    public const int FiFh = 16;

    public static void FillStat(byte* st, uint mode, long size, long blocks, DateTimeOffset mtime, ulong nlink)
    {
        new Span<byte>(st, StSize).Clear();
        *(ulong*)(st + StNlink) = nlink;
        *(uint*)(st + StMode) = mode;
        *(uint*)(st + StUid) = getuid();
        *(uint*)(st + StGid) = getgid();
        *(long*)(st + StSizeOff) = size;
        *(long*)(st + StBlksize) = 4096;
        *(long*)(st + StBlocks) = blocks;

        long sec = mtime.ToUnixTimeSeconds();
        foreach (var off in new[] { StAtim, StMtim, StCtim })
        {
            *(long*)(st + off) = sec;
            *(long*)(st + off + 8) = 0;
        }
    }

    public static string PathFrom(byte* path) => Marshal.PtrToStringUTF8((IntPtr)path) ?? "/";
}
