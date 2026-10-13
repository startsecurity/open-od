# open-od

A Microsoft OneDrive client for Linux with **Files On-Demand**, focused on GNOME. Written in C# (.NET 10) on top of `libfuse3`.

Your whole OneDrive shows up in a folder, but no file takes disk space until it is opened. When a program opens a file, it is downloaded right away and served from a local cache from then on, just like Files On-Demand on Windows.

> Independent project, not affiliated with or endorsed by Microsoft. OneDrive is a trademark of Microsoft Corporation. This project is also unrelated to `open-onedrive`.

**Status:** proof of concept, read-only. Do not rely on it as the only copy of your files.

## What works

- Mounts OneDrive (personal or work/school) in a folder through FUSE, read-only.
- Lists folders on demand through the Microsoft Graph API (30 s in-memory cache).
- Downloads a file only on `open()` and keeps it in `~/.cache/open-od/content/`.
- Concurrent opens of the same file share a single download.
- Cloud-only files report 0 blocks (`du` shows 0; `ls -l` shows the real size).
- Each file's state is exposed in the `user.openod.status` extended attribute (`cloud` or `local`).
- Thumbnailers and indexers cannot download cloud-only files (see below).
- Clean unmount on Ctrl+C and `SIGTERM`, even when the mount is busy.
- The token cache and downloaded files live in owner-only directories (`0700`).
- A demo cloud (`--demo`) for testing without an account.

### Thumbnailers and indexers

Desktop services open files on their own: Nautilus runs a thumbnailer for every document and image in a folder, and the GNOME indexer (`localsearch`) reads everything under your home folder. Left alone, they would silently download your whole drive.

open-od looks up the process behind each `open()` (through `/proc/<pid>/comm`, which also works inside bubblewrap sandboxes) and refuses to download a cloud-only file for thumbnailers and indexers (`*-thumbnailer`, `glycin-*`, `localsearch*`, `tracker-*`, `baloo*`). Files that are already local are served normally. The mount root also exposes an empty, read-only `.trackerignore`, so `localsearch` skips the drive entirely.

The trade-off: cloud-only files show no thumbnail, and a thumbnailer may remember the failure until the file changes.

## Known limitations

- No writing, renaming or deleting yet.
- No offline mode: listings live in memory only.
- No "Free up space" or "Always keep on this device".
- Opening a large file waits for the whole download (no `Range` reads).
- x86_64 only: the `struct stat` layout is hard-coded.

## Requirements

```bash
sudo pacman -S --needed fuse3 dotnet-sdk   # Arch Linux
```

## Build and test

```bash
dotnet build
dotnet test
alias open-od="dotnet $PWD/src/OpenOd.Cli/bin/Debug/net10.0/open-od.dll"
```

## Usage

Without an account, using the demo cloud:

```bash
mkdir -p ~/DemoCloud
open-od mount ~/DemoCloud --demo
```

With your OneDrive:

```bash
open-od login               # prints a URL and a code to enter in the browser
mkdir -p ~/OneDrive
open-od mount ~/OneDrive
```

In another terminal:

```bash
ls -la ~/OneDrive                                   # shows up instantly, nothing downloaded
getfattr -n user.openod.status ~/OneDrive/somefile  # "cloud"
cat ~/OneDrive/somefile                             # downloads now
getfattr -n user.openod.status ~/OneDrive/somefile  # "local"
```

Ctrl+C unmounts. `open-od logout` removes the saved token.

## Sign-in and the Entra ID app

open-od signs in through its own app registered in Microsoft Entra ID (client ID `b369d8b9-f994-44c5-a502-107689ab42dc`). It accepts personal and work/school accounts and only requests `Files.Read`, `User.Read` and `offline_access`. The client ID is not a secret: desktop apps are public clients and use no client secret.

The token is stored in the system keyring (libsecret). If no keyring is available, it falls back to `~/.cache/open-od/msal.cache` (mode `0600`, inside a `0700` directory).

To use your own app instead (for example, if your organization blocks third-party apps), register an app for personal and organizational accounts, enable "Allow public client flows", add the delegated `Files.Read` permission, and run `open-od login --client-id <ID>` or set `OPENOD_CLIENT_ID`.

## Layout

```
src/
├── OpenOd.Core/            logic independent of FUSE
│   ├── Drive/              IDriveBackend, DriveCache (listings + on-demand download), DemoBackend
│   └── Graph/              Auth (MSAL device code) and GraphBackend (Microsoft Graph SDK)
├── OpenOd.Fuse/            libfuse3 P/Invoke and filesystem callbacks
└── OpenOd.Cli/             the `open-od` executable: login, logout, mount
tests/
└── OpenOd.Core.Tests/      DriveCache tests with a fake backend
```

### How FUSE is wired

There is no maintained FUSE binding on nuget.org, so the project calls `libfuse3` directly. `FuseNative.cs` declares `fuse_main_real_versioned` and fills `struct fuse_operations` with pointers to `[UnmanagedCallersOnly]` methods. The offsets of `struct stat`, `fuse_file_info` and the operations table were checked by compiling a C program against the libfuse 3.18 headers.

libfuse calls these methods from its own threads (multithreaded mode). No exception may escape into native code, so every callback goes through `Guard`, which turns errors into `-EIO`.

## Roadmap

1. Local state in SQLite, keyed by item ID and fed by Graph delta queries (offline mode).
2. "Always keep on this device" and "Free up space".
3. Partial reads with `Range`.
4. Writing, with uploads using `If-Match` and a conflict copy when Graph answers 412.
5. D-Bus service, Nautilus extension, GNOME Shell extension and a GTK4/libadwaita settings window.

## Security

Please report vulnerabilities privately. See [SECURITY.md](SECURITY.md).

## License

MIT. See [LICENSE](LICENSE).
