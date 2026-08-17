# File Management, Root Folders and the Import File-Operation Layer — Design (v0.3.0 milestone)

> **Status:** Design. No code has been written. This document is the direct input to the implementation
> phase for the subsystem that touches users' actual media files.
>
> **Scope:** root folders, the transfer decision matrix, crash safety, permissions, remote path mapping,
> deletion safety, library scanning, and upgrade replacement. Media-agnostic by construction: no TV,
> movie, music or book concept appears anywhere in this design.
>
> **Governing documents:** `ARCHITECTURE.md` §5/§10/§11/§12/§16, `docs/arronix-common-extraction-plan.md`
> (three-tier promotion rule), `docs/contracts/stability.md` (experimental gate), and
> `docs/design/unified-host-runtime.md` §4.1 (capability matrix), §4.2 (scoping decorators), §4.3
> (`HostFileSystem`), §4.6 (the storage seam), §5.6 (`IDownloadClient`).
>
> **Related, not duplicated here:** `docs/design/acquisition-pipeline.md` owns grab→queue→completed-download
> handling and the import *decision*; this document owns everything that happens once a decision exists and
> a byte has to move. `docs/design/storage-layer.md` owns the relational schema; this document specifies
> only the two tables the file layer requires and hands the DDL to that document.
> `docs/design/threat-model.md` owns the adversarial analysis of path traversal; this document specifies
> the enforcement point.
>
> **Empirical basis:** the six survey reports, plus direct reading of Sonarr's `MediaFiles/`,
> `RootFolders/`, `RemotePathMappings/`, `HealthCheck/Checks/` and `NzbDrone.Common/Disk/`, compared
> against the same folders in Radarr, Lidarr and Readarr. Every non-obvious decision below cites the
> finding that forced it.

---

## 0. How the contested calls were resolved

The file layer is where the surveyed applications agree most on *structure* and are most consistently
wrong about *ordering*. Where a decision is contested — between what the \*arrs do, what
`Arronix.Abstractions` 0.2.0 already promises, and what a greenfield host should do — one position is
chosen and the reason is recorded on one line.

| # | Question | Positions | Resolution | One-line reason |
|---|----------|-----------|------------|-----------------|
| 1 | Who physically moves an imported file — host or plugin? | All four \*arrs: the media layer moves it (`EpisodeFileMovingService.cs:110-175`). Alternative: the host places it and the plugin only decides where. | **Host places** | Crash safety is unimplementable if placement is distributed: any unjournaled transfer a plugin could perform puts holes in the journal, and a journal with holes in it is worse than none. (This is also why #3 withdraws the transfer contract from the plugin surface rather than merely asking plugins not to use it.) |
| 2 | Then what is `IImportPipeline.ImportAsync` for? | It currently reads as "move the file into the library". | **A commit hook called after placement, idempotent — and renamed to `OnPlacedAsync`** | `ImportDecision.DestinationPath` (`DTOs/ImportDecision.cs:14`) shows the plugin was always *declaring* a destination, not owning the write. But the meaning is now the reverse of the name, and a method whose documentation has to contradict its name is a defect: rename it (§12.3). Zero implementers, so the rename is compiler-found call sites and nothing else. |
| 3 | Does `IFileTransferService` stay plugin-visible? | Spec §4.1 gates it on `import`. | **No — withdrawn from `Arronix.Abstractions`; the engine stays, host-internal** | It has zero implementers and zero plugin callers, and #1 has just made "a plugin transfers a library file" the thing the journal exists to prevent. The features cited to keep it (Lidarr tag writeback, Readarr's Calibre backend) are other products' features, not Arronix requirements — §12 refuses to *promote* six types on exactly that test, and the same test withdraws this one. Re-admit it when a plugin needs it. |
| 4 | Are root folders a contract? | Plausible: plugins need to know where the library lives. | **No — root folders never cross the plugin boundary** | `ILibraryLayout` already receives the root as `LibraryPathSpec.RootPath` (`DTOs/LibraryPathSpec.cs:8`) and `LibraryFacet.RootFolderPath` is a plain string (spec §4.6); a plugin that can *enumerate* roots can probe the host's storage topology for no gain. |
| 5 | Does the root folder carry per-media-kind defaults? | Sonarr/Radarr: no (`RootFolders/RootFolder.cs:6-14`, 4 fields). Lidarr/Readarr: yes — `Name`, `DefaultQualityProfileId`, `DefaultMetadataProfileId`, `DefaultMonitorOption`, `DefaultNewItemMonitorOption`, `DefaultTags` (Lidarr `RootFolder.cs:7-20`). | **Yes, but in a separate binding row** | Two of four invented it independently and Readarr added a *library backend* on top (`IsCalibreLibrary`, `CalibreSettings`, `RootFolder.cs:17-18`); putting kind-specific defaults on the core entity would make the core entity kind-aware, which §16 forbids. |
| 6 | Can several media kinds share one root folder? | Sonarr/Radarr have no notion of kind; Lidarr/Readarr bind a root to the one kind the app has. | **Yes, via N bindings per root** | Users with `/media` as a single share are the common case; forbidding it would force an artificial subdirectory convention on people who already have one. Collisions are caught by the layout-overlap validator, not by the schema. |
| 7 | Is volume sameness predicted or probed? | Sonarr predicts: `sourceMount.RootDirectory == targetMount.RootDirectory` (`DiskTransferService.cs:331`). | **Probed — attempt and fall back** | Mount root equality is wrong for bind mounts, btrfs subvolumes and Windows junctions; `TryCreateHardLink` returning `false` on `EXDEV` *is* the authoritative same-device test and it costs one syscall. |
| 8 | Is the filesystem type branched on by name? | Sonarr: `DriveFormat == "cifs"`, `== "btrfs"`, `== "zfs"` (`DiskTransferService.cs:336-338`). | **No — capability probes, not name matching** | The list is unmaintainable (no `xfs`, no `apfs`, no `nfs4`, no `overlay`, no `virtiofs`, `zfs` has no reflink ioctl at all), and every new filesystem needs a code change in a media-agnostic core. |
| 9 | Reflink / copy-on-write: is it achievable on `net10.0`? | Optimistic: "the runtime does it for us". Pessimistic: "impossible, drop it". | **Neither — platform-pack P/Invoke seam, and never *reported* as anything but `Copy`** | .NET 10 exposes **no** block-clone API; `FICLONE`, `clonefile(2)` and `FSCTL_DUPLICATE_EXTENTS_TO_FILE` are three unrelated syscalls, and there is no portable way to *confirm* a clone happened, so claiming one in a return value would be a lie. See §3.4. |
| 10 | Transfer verification | Sonarr: destination size equals source size, retried after `Thread.Sleep(3000)` (`DiskTransferService.cs:461-518`). | **Tiered policy; size is the floor, not the ceiling** | A size check catches truncation and nothing else; a full hash of a 60 GB file doubles the I/O of every import. Four levels, defaulted per transfer mode — §3.5. |
| 11 | Is the sleep-and-retry kept? | It is load-bearing on NAS devices, and it is a blocking `Thread.Sleep` on a pool thread. | **Kept as behavior, deleted as implementation** | Async delay with `TimeProvider`, bounded exponential backoff, count and delay bound; the *reason* (NFS/SMB attribute cache staleness) is real, the 3-second magic number is not. |
| 12 | Crash safety mechanism | All four \*arrs: none. Alternatives: SQLite transaction around the file op; a write-ahead intent journal. | **Write-ahead intent journal** | There is no two-phase commit between POSIX and SQLite, so the only honest design is to make the *intent* durable before the irreversible act and reconcile forward — §4. |
| 13 | Upgrade ordering | All four: delete old → move new (Sonarr `UpgradeMediaFileService.cs:62-73` then `:78-85`; Radarr `:58-70` then `:74-81`; Lidarr `:63-75` then `:77-84`). | **Inverted: stage → verify → displace → rename → commit** | With the recycle bin unconfigured — the shipped default — `RecycleBinProvider.DeleteFile` hard-deletes (`RecycleBinProvider.cs:74-87`) and a subsequent move failure leaves the user with neither file. This is the single most damaging ordering in the surveyed code. |
| 14 | Recycle bin: one global path or per root folder? | All four: one global (`ConfigService.cs:95-98`). | **Per root folder, falling back to global** | A global bin on a different volume turns every replacement into a full copy of the outgoing file; the bin must be able to live on the same volume as the media it receives. |
| 15 | Recycle bin collision handling | Sonarr: `_2`, `_3` … suffix loop with a stat per attempt (`RecycleBinProvider.cs:105-117`). | **Operation-scoped subdirectory, no suffixing** | `<bin>/<yyyy-MM-dd>/<operationId>/<relative path>` is collision-free by construction, preserves provenance for restore, and makes retention a directory sweep rather than a per-file `stat`. |
| 16 | Recycle bin retention clock | Sonarr rewrites the file's last-write time on entry (`RecycleBinProvider.cs:130`). | **Journal timestamp, never the file's mtime** | Mutating the user's file metadata to store our bookkeeping is a side effect on data we were asked to preserve; a directory named by date needs no mtime at all. |
| 17 | May the platform ever hard-delete media? | Sonarr does, silently, whenever the bin is unset. | **Only on an explicit, audited user action, or the retention sweep** | "Recoverable by default" is the whole point of a recycle bin; a default that quietly disables it is a default that quietly loses data. |
| 18 | Remote path mapping key | Sonarr keys by hostname string (`RemotePathMappingService.cs:128,147`). | **Keyed by download-client `ProviderId`** | Two clients on one host (a Usenet and a torrent daemon in separate containers) cannot be distinguished by hostname, and this is the exact configuration that produces the most support traffic. |
| 19 | Path grammar in a mapping | Sonarr infers it from the string via `OsPath` and reports "wrong OS path" when the inference disagrees with the host (`RemotePathMappingCheck.cs:245-291`). | **Declared per mapping** | Inference is what makes `C:\downloads` on a Linux host an *error* instead of a *mapping*; with the grammar declared, `PlatformPath` translation is total and the error class disappears. |
| 20 | Misconfiguration diagnostics | Sonarr: a 371-line hand-written decision tree (`RemotePathMappingCheck.cs:48-369`) producing 12 messages. | **A data table plus a proposal generator** | The tree is correct and unmaintainable; the same 12 outcomes fall out of six booleans, and the same probe that classifies a failure can *propose the fix* — §6.4. |
| 21 | Filesystem watching | Absent from all four. | **Hint only; the scheduled scan remains the correctness mechanism** | inotify does not work on NFS/CIFS, has a per-user watch limit (`fs.inotify.max_user_watches`, frequently 8192) that a 100 k-file library exhausts, and drops events under load; a watcher that is *sometimes* right is a cache, not a source of truth. |
| 22 | Ignore lists and special folders | Hardcoded across `DiskScanService.cs:72-75`, `RootFolderService.cs:36-47`, `DiskTransferService.cs:520-540` — naming Synology, QNAP, Plex, Apple, Windows and one third-party transcoder. | **Options-bound data, shipped as defaults** | A media-agnostic core that compiles in `@eadir` and `.@__thumb` names three storage vendors; the extraction plan already made this call for `SystemPathCatalog` and it applies identically here. |
| 23 | Sample / extras detection | Sonarr runs `ffprobe` and compares runtime (`EpisodeImport/DetectSample.cs:36-42`). | **Split: cheap path/size filters in the host, semantic detection in the plugin** | Runtime-versus-expected-runtime is irreducibly media-specific; "the path contains a `sample` segment" and "the file is under N MB" are not, and doing them host-side keeps 99 % of samples out of the plugin's decision set for free. |
| 24 | Free-space guard | Checked unconditionally before import (`Specifications/FreeSpaceSpecification.cs:41-54`). | **Conditional on the *resolved* transfer mode** | A hardlink consumes no space, so the current check rejects perfectly valid imports on a nearly-full volume — which requires resolving hardlink-versus-copy *before* the specification runs, not after. |
| 25 | Do any new types go into `Arronix.Abstractions`? | Tempting: `RootFolderDescriptor`, `TransferOutcome`, `IReflinkProvider`. | **None. Zero additions.** | Every one of them has exactly one implementer and no plugin audience; the three-tier rule says hold, and holding costs nothing because promotion is additive. Two XML-doc clarifications are the entire contract delta — §12. |

---

## 1. Topology — where this subsystem lives

```text
src/Arronix.Abstractions/FileSystem/            ← IFileTransferService + FileTransferMode DELETED (§12)
src/Arronix.Abstractions/Import/                ← ImportAsync renamed to OnPlacedAsync (§12)

src/Arronix.Common/FileSystem/
├── HostFileSystem.cs                 IFileSystem + IHostFileSystem over System.IO
├── FileTransferService.cs            the transfer engine — host-internal, unjournaled
├── FileTransferMode.cs               moved here with it; nothing plugin-facing names it
├── TransferPlanner.cs                the decision matrix (§3), pure and unit-testable
├── TransferVerifier.cs               tiered verification (§3.5)
├── PathComparer.cs / PathExtensions.cs / PathGuard.cs      (extraction plan §3.2)
├── DriveInfoStorageMount.cs          portable IStorageMountProvider fallback
└── FileSystemOptions.cs

src/Arronix.Host/Files/
├── RootFolders/                      registration, validation, capacity, bindings (§2)
├── Placement/
│   ├── IFilePlacementService.cs      Tier B — the journaled, host-only placer (§4)
│   ├── FilePlacementService.cs
│   ├── PlacementJournal.cs           the write-ahead intent log
│   └── PlacementRecovery.cs          startup + scheduled reconciliation (§4.4)
├── Recycling/                        recycle bin, retention, restore, audit (§7)
├── Scanning/                         full/incremental scan, watcher hints (§8)
├── Mapping/                          remote path translation + diagnostics (§6)
└── Health/                           the eight IHealthContributor implementations (§10)

src/Arronix.Platform.Linux/  (platform pack)   ProcMountProvider, PosixFilePermissions, LinuxBlockClone
src/Arronix.Platform.Windows/(platform pack)   DriveMountProvider, WindowsFilePermissions, RefsBlockClone
src/Arronix.Platform.MacOs/  (platform pack)   ApfsBlockClone
```

Three rules govern the split, and each is testable:

1. **`Arronix.Abstractions` gains nothing, and loses one thing.** What a plugin should see is `IFileSystem`
   (22 members, `FileSystem/IFileSystem.cs:23-211`), `IFilePermissions`, `IStorageMount`,
   `IStorageMountProvider`, `StorageMountOptions`, `DestinationExistsException` and `PlatformPath` — and no
   more. `IFileTransferService` and `FileTransferMode` leave for `Arronix.Common` (resolution #3, §12); the
   engine is unchanged and the host keeps using it, it simply stops being a contract.
2. **The journaled placer is host-only and is never registered into a plugin scope.** It is not on the
   §4.1 capability matrix because it is not a contract; `IPluginRegistry` has no method that can reach it.
3. **Everything platform-specific is behind a probe interface that returns `false`/`null` rather than
   throwing.** `IFilePermissions.IsSupported`, `IStorageMountProvider.IsSupported` already establish this
   idiom; §3.4's block-clone seam follows it.

> **Note on `HostFileSystem`.** `unified-host-runtime.md` §4.3 places a minimal `HostFileSystem :
> IFileSystem` in `Arronix.Host` because `Arronix.Common` has no implementation yet, and warns that
> `IFileHasher` cannot otherwise be activated. This design assumes the implementation ends up in
> `Arronix.Common/FileSystem/` where the extraction plan §3.2 put it (`HostFileSystemBase`,
> `IHostFileSystem`). Either seat works; what must **not** happen is two implementations. WP-1 below
> resolves the seat before anything else is built on it.

---

## 2. Root folders

### 2.1 What a root folder is, media-agnostically

A root folder is **a directory the operator has declared the platform may write library content into**.
That is the entire core semantics. It is not a library, not a media kind, not a profile.

```csharp
// src/Arronix.Host/Files/RootFolders/RootFolder.cs   (Tier B — host-side, NOT promoted)
public readonly record struct RootFolderId(long Value);

public sealed record RootFolder
{
    public required RootFolderId Id { get; init; }

    /// <summary>Operator-supplied label. Lidarr and Readarr both added this; Sonarr and Radarr force the
    /// UI to display a raw path. Required, unique, and never derived from the path.</summary>
    public required string Name { get; init; }

    /// <summary>Absolute, in the host's own grammar, with no trailing separator.</summary>
    public required string Path { get; init; }

    /// <summary>When false the folder is retained but excluded from placement, scanning and capacity
    /// reporting. Deleting a root folder must never be the way to temporarily detach a disk.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Null means "use the global recycle bin". See §7.2.</summary>
    public string? RecycleBinPath { get; init; }

    public DateTimeOffset AddedAt { get; init; }
}
```

Everything kind-specific hangs off a **binding**, one row per `(root folder, media kind)`:

```csharp
// src/Arronix.Host/Files/RootFolders/RootFolderBinding.cs   (Tier B)
public sealed record RootFolderBinding
{
    public required RootFolderId RootFolder { get; init; }
    public required MediaKindId MediaKind { get; init; }

    /// <summary>Relative to the root folder. Empty means the root itself. This is what makes
    /// "one share, several kinds" work without the core knowing what a kind is.</summary>
    public string SubPath { get; init; } = string.Empty;

    /// <summary>Whether new library entries of this kind default to this root.</summary>
    public bool IsDefault { get; init; }

    /// <summary>Opaque to the host. Keys are validated against the kind's declared field descriptors
    /// (spec §2.8) at write time; values are stored verbatim and handed back to the plugin.
    /// This is Lidarr's DefaultQualityProfileId / DefaultMetadataProfileId / DefaultMonitorOption /
    /// DefaultTags (RootFolder.cs:11-15) and Readarr's Calibre settings (RootFolder.cs:17-18),
    /// generalized without the core learning what any of them mean.</summary>
    public IReadOnlyDictionary<string, string> Defaults { get; init; }
        = ReadOnlyDictionary<string, string>.Empty;
}
```

**Why a binding rather than a bag on the root** (resolution #5): Readarr's `IsCalibreLibrary` is not a
default, it is a *delegation of file layout to a third party* — and it is per-kind by nature. Putting that
on the root entity makes the root entity kind-aware. Putting it in an opaque per-kind bag makes the root
entity permanent.

### 2.2 Registration validation

Sonarr validates four things on add (`RootFolderService.cs:102-120`): rooted, exists, not a duplicate,
writable. That is the right *shape* and an incomplete list. The full gate, evaluated in order, each with a
distinct `CoreErrorCode.InvalidConfiguration` diagnostic and a remediation string:

| # | Check | How | Why it is here |
|---|-------|-----|----------------|
| 1 | Path is absolute and well-formed in the host grammar | `PathGuard.ThrowIfInvalid` | A relative root resolves differently depending on the working directory, which under a service is not the directory the operator thinks. |
| 2 | Directory exists | `IFileSystem.FolderExists` | Sonarr's check (`:107`). Creating it for the operator is refused: a typo would silently create `/mnt/medai`. |
| 3 | Directory is **writable by probe, not by inspection** | `IFileSystem.IsFolderWritableAsync` | The 0.2.0 XML doc already states the reason (`IFileSystem.cs:45-48`): reported permissions and accepted writes differ on exported volumes. |
| 4 | Mount is not read-only | `GetMount(path)?.MountOptions?.IsReadOnly` | Sonarr checks this only in a periodic health check (`MountCheck.cs:26`), i.e. after the user has already added the folder and queued work against it. |
| 5 | Not inside the application data directory | prefix test against `IAppPaths.AppDataFolder` | A root folder containing the database means a scan enumerates the database and a recursive delete can reach it. |
| 6 | Not inside, and does not contain, an existing root folder | `PathComparer` prefix test both ways | Nested roots make "which root does this file belong to" ambiguous, and `GetBestRootFolderPath`'s longest-prefix rule (`RootFolderService.cs:216`) silently picks one. |
| 7 | Not a known download-client output folder | translated `DownloadClientInfo.OutputRootFolders` (spec §5.6) | Sonarr detects this and warns (`DownloadClientRootFolderCheck.cs:51`); it should be refused at the point of configuration, where the operator is looking at the setting. |
| 8 | On Windows running as a service: not a mapped network drive (`X:\`) or a UNC path without stored credentials | `IHostRuntimeInfo.IsWindowsService` + drive type | This is Sonarr's most-repeated support answer, currently delivered as a log line at import-failure time (`DownloadedEpisodesImportService.cs:386-401`). |

Checks 4, 7 and 8 are **warnings with an override**, not hard failures: an operator adding a root ahead of
mounting the disk is a legitimate workflow. Checks 1–3, 5, 6 are hard.

### 2.3 Capacity reporting

Sonarr computes free space, total space and the unmapped-folder list inside `Task.Run(...).Wait(5000)`
(`RootFolderService.cs:200-212`) on the request thread, once per root, on every list call. On a NAS whose
disks have spun down this blocks a request thread for the full five seconds per root, and `.Wait()` on a
pool thread is the classic starvation shape.

Design:

- Capacity is a **background sample**, not a request-time call. A scheduled job
  (`root-folder-capacity`, default `every 5m`, plus on-demand after any placement) writes
  `RootFolderCapacity { RootFolderId, AvailableBytes, TotalBytes, SampledAt, Probe }` into memory.
- The API returns the last sample with its age. A sample older than three intervals renders as *stale*
  rather than as a number the operator will trust.
- The probe itself is `IStorageMount.AvailableFreeSpace` where a mount is known, falling back to
  `IFileSystem.GetAvailableSpace`. `AvailableFreeSpace` is the quota-aware figure and the 0.2.0 contract
  already says to prefer it (`IStorageMount.cs:52-56`).
- A probe that throws or times out (2 s budget) marks the root **inaccessible** and raises the health
  contribution in §10; it never propagates to the caller.

**Unmapped-folder discovery is not part of capacity.** Sonarr fuses them and pays a full directory
enumeration — at a depth derived from the naming format (`RootFolderService.cs:154-163`) — on every root
folder list. It is a *library-add* affordance, so it becomes its own on-demand endpoint with its own
budget, and it is per media kind because the folder depth is a kind's layout concern.

---

## 3. The transfer decision matrix

### 3.1 The inputs

The planner is a pure function. It is given:

| Input | Source | Notes |
|-------|--------|-------|
| `sourcePath`, `targetPath` | caller | Both already scope-checked. |
| `requested` | `FileTransferMode` | `Move`, `Copy`, `HardLink`, `HardLinkOrCopy` (`FileTransferMode.cs:15-31`, host-internal per §12.1). |
| `sourceMount`, `targetMount` | `IFileSystem.GetMount` | May be `null`; the plan must survive that. |
| `sameDirectory` | path algebra | Rename within a directory is the only genuinely atomic placement. |
| `sourceIsRemovable` | download-client `CanMoveFiles` (spec §5.6) | A seeding torrent's payload must not be moved. |
| `blockClone` | `IBlockCloneProvider.IsSupported` | §3.4. |

It is **not** given the filesystem type by name. Resolution #8.

### 3.2 The matrix

Read as: for the requested mode and the observed topology, the ordered list of attempts. The first that
succeeds wins and is what the operation reports.

| Requested | Same directory | Same volume, different directory | Different volume, local | Network share (SMB/NFS) | Read-only target |
|---|---|---|---|---|---|
| `Move` | `rename` | `rename` → clone+unlink → copy+verify+unlink | clone+unlink → copy+verify+unlink | copy+verify+unlink (never `rename`) | refuse, `CoreErrorCode.InvalidConfiguration` |
| `Copy` | clone → copy+verify | clone → copy+verify | clone → copy+verify | copy+verify | refuse |
| `HardLink` | `link` | `link` | fail (`EXDEV`) | fail | refuse |
| `HardLinkOrCopy` | `link` → clone → copy+verify | `link` → clone → copy+verify | clone → copy+verify | copy+verify | refuse |

Four points the surveyed code gets wrong and this table fixes:

- **`rename` is never attempted across a network share even when the mount table says the two paths share
  a root.** Sonarr special-cases only `cifs` by name (`DiskTransferService.cs:336,372-379`); NFS, SSHFS
  and virtiofs have the same partial-rename exposure and are not in the list. The rule here is *drive
  type*, from `IStorageMount.DriveType`, which the portable `DriveInfo` fallback already supplies.
- **Cross-volume `Move` is a copy, a verify, and only then an unlink.** Never `File.Move` with its silent
  copy-delete fallback, because that fallback deletes on the runtime's judgment rather than on ours.
- **A hardlink is not a copy.** `FileTransferMode` doubles as the return value precisely so the caller can
  tell (`FileTransferMode.cs:8-11`), and §9.4 depends on it.
- **`link` is attempted before any volume prediction.** Resolution #7: `TryCreateHardLink` returning
  `false` is the same-device test, and the 0.2.0 contract already specifies the non-throwing shape
  (`IFileSystem.cs:192-196`).

### 3.3 Folder transfer

`TransferFolderAsync` and `MirrorFolderAsync` keep the surveyed behavior, with three fixes:

- **The ignore list is options-bound**, not compiled in. Sonarr's `.nfs*` / `debug.log` / `*.socket`
  (`DiskTransferService.cs:520-540`) become defaults in `Arronix:Files:Transfer:IgnoredNames`. A core
  that knows what a specific torrent client's `debug.log` is has a media-agnostic problem.
- **The 100 MB post-move guard is kept and made an option.** `DiskTransferService.cs:118-128` refuses to
  delete a source folder that still holds more than 100 MB after a move. That is a genuinely good safety
  valve, and it is a magic number in a `throw`.
- **The `.backup~` case-insensitive rename dance** (`DiskTransferService.cs:53-71`) is kept — renaming
  `/media/Show` to `/media/show` on a case-insensitive volume genuinely requires an intermediate — with
  the suffix bound to `Arronix:Files:Transfer:IntermediateSuffix` and the intermediate name made
  operation-scoped so a crash mid-dance is recoverable by the journal rather than by a human.

### 3.4 Reflink / copy-on-write — what is actually achievable

**Stated plainly: `net10.0` has no block-clone API.** There is no `File.Copy` overload, no
`FileStreamOptions` flag, no `System.IO` surface for it. Anything that exists is a per-platform syscall:

| Platform | Mechanism | Reachable from managed code | Notes |
|---|---|---|---|
| Linux, btrfs / XFS (reflink=1) / bcachefs | `ioctl(dst, FICLONE, src)` | `LibraryImport` on `ioctl`, request code `0x40049409` on most 64-bit ABIs | Sonarr does exactly this and guards it to `x86_64` because the constant is ABI-dependent (`NzbDrone.Mono/Disk/RefLinkCreator.cs:25`). arm64 Linux uses the same value; the guard is over-conservative but the *reason* for it is correct. |
| Linux, any fs | `copy_file_range(2)` | Not exposed; .NET's own `File.Copy` uses it internally where available | On kernels ≥ 5.3 this dispatches to `->remap_file_range`, so on btrfs/XFS **`File.Copy` may already produce a reflink**. Unverifiable from managed code — see below. |
| macOS, APFS | `clonefile(2)`, or `copyfile(3)` with `COPYFILE_CLONE` | `LibraryImport` | .NET's copy path does not set `COPYFILE_CLONE`. |
| Windows, ReFS / Dev Drive | `FSCTL_DUPLICATE_EXTENTS_TO_FILE` | `DeviceIoControl` P/Invoke | NTFS has no equivalent. Windows 11 24H2 `CopyFile` block-clones on ReFS automatically. |
| ZFS | none | — | ZFS block cloning (OpenZFS 2.2) is not exposed as `FICLONE`; Sonarr's `isZfs` branch (`DiskTransferService.cs:338,342`) calls an ioctl that ZFS does not implement, so it always falls through to the copy. Harmless, and dead. |

**Decisions:**

1. Block cloning is a **platform-pack seam**, held at Tier B, host-side, never promoted:

   ```csharp
   // src/Arronix.Common/FileSystem/IBlockCloneProvider.cs   (Tier B)
   public interface IBlockCloneProvider
   {
       /// <summary>Whether this provider understands block cloning on the current platform. False makes
       /// every call a no-op; it does not make cloning an error.</summary>
       bool IsSupported { get; }

       /// <summary>Attempts a copy-on-write clone. Returns false — never throws — when the volume, the
       /// file system or the platform refuses. A failed attempt must leave no partial destination.</summary>
       bool TryClone(string sourcePath, string destinationPath);
   }
   ```

   `Arronix.Common` ships `NullBlockCloneProvider` (`IsSupported => false`). Platform packs ship the
   three real ones. The planner consults it exactly where the matrix says "clone".

2. **A successful clone is reported as `FileTransferMode.Copy`, never as a distinct mode.** The caller's
   only legitimate question is "does the destination share storage with the source?", and after a clone
   the answer is *initially yes and permanently unknowable* — the extents diverge on write. `HardLink` is
   different: it is a directory entry to the same inode, permanently. Conflating them would make the
   free-space accounting in §9.4 wrong.

3. **The "`File.Copy` may already reflink on Linux" case is deliberately not detected.** Confirming it
   requires `FIEMAP` extent comparison or `st_blocks` heuristics, both of which are wrong on compressed
   and sparse files. The design is correct whether or not it happens; recording it would be a guess.
   Marked **OPEN** only as a benchmark item: WP-6 measures import throughput on btrfs with and without the
   explicit `FICLONE` path, and if the runtime already achieves it, `LinuxBlockClone` is deleted.

4. **`RefLinkCreator`'s unlink-on-failure discipline is carried forward** (`RefLinkCreator.cs:53-60,69`):
   a failed clone must `unlink` the destination it created before returning `false`, or the fallback copy
   finds a zero-length file in the way. This is the kind of detail that only appears in code that has been
   in production for a decade, and it is preserved verbatim in intent.

### 3.5 Verification

Four levels, defaulted by transfer mode. Resolution #10.

```csharp
// src/Arronix.Common/FileSystem/TransferVerification.cs   (Tier B)
public enum TransferVerification
{
    /// <summary>No post-transfer read. Correct for hard links and clones: there is nothing to compare.</summary>
    None = 0,
    /// <summary>Destination length equals source length. Catches truncation, ENOSPC and a killed writer.</summary>
    Size = 1,
    /// <summary>Size, plus content hash of the first and last 1 MiB and three interior 1 MiB blocks at
    /// deterministic offsets (1/4, 1/2, 3/4 of the length). Catches the silent-corruption class that
    /// network filesystems actually produce, at a bounded 5 MiB read.</summary>
    Sampled = 2,
    /// <summary>Full content hash of both files via IFileHasher (XxHash128). Doubles the read I/O.</summary>
    Full = 3
}
```

| Transfer that happened | Default verification | Why |
|---|---|---|
| `HardLink` | `None` | Same inode. |
| clone | `None` | Same extents at creation; the kernel either cloned or reported failure. |
| `rename` within a volume | `None` | `rename(2)` is atomic; there is no partial state to detect. |
| `Copy`, local | `Size` | Matches the surveyed behavior and catches everything a local copy realistically produces. |
| `Copy`, network target | `Sampled` | The failure mode SMB and NFS produce is a short or stale read, not a truncation; a size check on a cached attribute can pass on a file that was never written. |
| any, when `Arronix:Files:Transfer:Verification` is raised | as configured | Operators with checksumming filesystems set `None`; operators who have been burned set `Full`. |

The **retry** replaces `WaitForIO`'s `Thread.Sleep(3000)` (`DiskTransferService.cs:461-465`). The reason
for it is real — NFS and SMB attribute caches make `stat` lag a completed write — so verification failure
does not fail immediately:

```csharp
// bounded, async, TimeProvider-driven, no pool thread blocked
private static readonly TimeSpan[] VerificationBackoff = [ 250.ms, 1.s, 3.s ];
```

Three attempts, then `IOException` with the observed and expected lengths, matching the message quality of
`DiskTransferService.cs:517` — which is the one thing in that method that is unambiguously good.

---

## 4. Atomicity and crash safety

This is the section that exists because none of the four surveyed applications has one.

### 4.1 The principle

> **The filesystem is the source of truth. The database is a derived index of it.**

Every recovery resolves in the direction disk → database, never the reverse. This is already latent in the
surveyed code — `MediaFileTableCleanupService.Clean` deletes rows for files that are gone
(`MediaFileTableCleanupService.cs:45-50`) and never deletes a file because a row is gone — and making it
an explicit invariant is what allows recovery to be *safe by construction*: the worst outcome of a
mis-recovery is a re-scan, not a deleted file.

There is no two-phase commit between POSIX and SQLite. The standard answer, and the one adopted, is a
**write-ahead intent journal**: make the intention durable before the irreversible act, and on restart
either complete it or undo it.

### 4.2 The journal

One table, owned by `docs/design/storage-layer.md`, specified here because this subsystem's correctness
depends on its exact shape.

```csharp
// src/Arronix.Host/Files/Placement/PlacementRecord.cs   (Tier B)
public sealed record PlacementRecord
{
    public required Guid OperationId { get; init; }          // Guid.CreateVersion7() — time-ordered
    public required PlacementState State { get; init; }
    public required string SourcePath { get; init; }
    public required string TargetPath { get; init; }
    public required string StagingPath { get; init; }        // targetDir/.arronix-{opId:N}.partial
    public required FileTransferMode RequestedMode { get; init; }
    public FileTransferMode ActualMode { get; init; }
    public long SourceLength { get; init; }

    /// <summary>Files this placement displaces, with where each one went. Populated at Displaced.</summary>
    public IReadOnlyList<DisplacedFile> Displaced { get; init; } = [];

    /// <summary>Set once the source may be released. False for copy-only (seeding) placements.</summary>
    public bool ReleaseSource { get; init; }

    public MediaKindId? MediaKind { get; init; }             // for diagnostics and scoping only
    public string? CorrelationId { get; init; }              // the scheduler's job correlation id
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public string? LastError { get; init; }
}

public sealed record DisplacedFile(string OriginalPath, string? RecyclePath, long Length);

public enum PlacementState
{
    Planned    = 0,   // intent durable; nothing has been written
    Staged     = 1,   // bytes at StagingPath, verified
    Displaced  = 2,   // previous occupants moved out; TargetPath is free
    Placed     = 3,   // atomic rename done; TargetPath is the new file
    Committed  = 4,   // database link written
    Released   = 5,   // source deleted (Move) or deliberately retained (Copy)
    Done       = 6,   // terminal success; retained for the audit window then purged
    RolledBack = 7,   // terminal failure; disk restored to its pre-operation state
    Wedged     = 8    // terminal failure that recovery could NOT undo; requires an operator
}
```

The journal is written with `PRAGMA synchronous = FULL` regardless of the main database's setting. A
journal entry that is not durable before the act it describes is not a journal.

### 4.3 The ordering

**Place before displace.** Every state transition is preceded by its journal write.

```
 1. Planned     write intent (source, target, staging, mode, displacement plan)   ── fsync
 2.             transfer source → staging, per the §3 matrix
                staging is in the TARGET directory, so step 6 is a same-directory rename
 3.             verify staging per §3.5
 4. Staged      journal
 5. Displaced   for each occupant of target (and each file this placement replaces):
                  move → recycle bin (§7); journal each DisplacedFile as it completes
 6.             File.Move(staging, target, overwrite: false)     ── atomic rename(2)
 7. Placed      journal
 8. Committed   ONE database transaction:
                  UpsertFileAsync(new record) + LinkAsync(...) + UnlinkAsync(displaced links)
                  + IImportPipeline.OnPlacedAsync(decision) ← the plugin's commit hook (#2)
 9. Released    if ReleaseSource: delete source. Publish the domain event.
10. Done        journal; retained for Arronix:Files:Placement:AuditRetention, then purged
```

Compare with the surveyed order, which is steps 5, 2, 6, 8 — displace first, transfer second. With the
recycle bin unconfigured (`RecycleBin` defaults to `string.Empty`, `ConfigService.cs:95-98`) step 5 is an
`unlink` (`RecycleBinProvider.cs:74-87`) and any failure in the transfer leaves the user with neither
file. Sonarr acknowledges the hazard for exactly one case — it throws if the root folder is missing
*before* deleting, with the comment "so the old file isn't left behind during the import process"
(`UpgradeMediaFileService.cs:49-53`, and verbatim in Radarr `:46-50` and Lidarr `:51-55`) — which is the
right instinct applied to one of the many ways step 2 can fail.

**Why staging in the target directory and not in a temp root:** a rename is atomic only within a
filesystem. Staging beside the target guarantees step 6 is `rename(2)` and therefore that the target path
is *never* observed half-written by a scanner, a media server or a user.

**Why `overwrite: false` at step 6:** the target was made free at step 5. If it is occupied at step 6,
something else placed a file there concurrently and the correct response is to abort and roll back, not to
clobber. .NET's `File.Move(src, dst, overwrite: false)` is check-then-rename and therefore racy, so step 6
additionally takes the per-target-path keyed lock (`Arronix.Common/Threading`, extraction plan §3.2) —
which also serializes two plugins racing to place different files at the same computed path.

### 4.4 Recovery

`PlacementRecovery` runs at startup, before the scheduler starts, and again as a scheduled job
(`placement-reconcile`, default `every 1h`). For every non-terminal record, in `OperationId` order:

| State on restart | On disk | Action | Outcome |
|---|---|---|---|
| `Planned` | nothing written | delete staging if it somehow exists; mark `RolledBack` | Nothing happened. |
| `Staged` | staging file present, target untouched | delete staging; mark `RolledBack` | Source intact; the import will be retried by the normal pipeline. |
| `Staged` | staging missing | mark `RolledBack` | Interrupted mid-transfer; the partial was never named the target. |
| `Displaced` | staging present, target free | **resume from step 6** | The expensive work is done; finishing is strictly safer than undoing. |
| `Displaced` | staging missing | restore each `DisplacedFile` from its `RecyclePath`; mark `RolledBack` | The only state with a real undo, and it is a set of renames. |
| `Displaced` | a displaced file cannot be restored (recycle path gone, or the bin was not configured) | mark `Wedged`, raise a health error naming every affected path | Honest. The operator is told exactly what is missing rather than discovering it in six months. |
| `Placed` | target present | **resume from step 8** | The file is correct; only the index is behind. |
| `Placed` | target missing | mark `Wedged` | Someone or something removed the file between the rename and the restart. |
| `Committed` | — | resume from step 9 | Idempotent. |
| `Released` | — | resume from step 10 | Bookkeeping only. |

**The one irreducible window is step 6 → step 8**: the file is at its final path and the database does not
know. It is bounded (one transaction), non-destructive, and self-healing — the library scan (§8) finds an
unindexed file inside a root folder and imports it as an existing file, which is exactly what
`DiskScanService` already does for unmapped files (`DiskScanService.cs:136-143`). Stating the window
plainly is better than pretending a design without it exists.

### 4.5 Orphans

Three classes, three sweepers:

1. **Staging files** — `*.arronix-*.partial` with no live journal record. Swept at startup and by the
   scan. A staging file whose journal record *is* live belongs to a running operation and is left alone.
2. **Files on disk inside a root folder with no database link** — not orphans; they are unindexed media
   and are imported by the scan.
3. **Database links whose file is gone** — the row is deleted, which is
   `MediaFileTableCleanupService.cs:45-50`'s behavior and correct under §4.1. It never deletes the file.

An empty directory left behind by a move is swept only when
`Arronix:Files:Scanning:RemoveEmptyDirectories` is on, and never above the root-folder boundary. Sonarr's
`RemoveEmptySeriesFolder` (`DiskScanService.cs:266-277`) deletes the item folder when it empties, which
directly contradicts `CreateEmptySeriesFolders` and is worked around by an explicit "don't create because
delete is enabled" branch (`DiskScanService.cs:104-117`). Two options that must know about each other are
one option: `EmptyDirectoryPolicy { Keep, Remove }`.

### 4.6 What is deliberately *not* done

- **No `fsync` of file contents before the rename.** The correctness argument is the journal, not
  durability of the payload; a media file that loses its tail to a power cut is caught by the next scan's
  size comparison. Forcing `fsync` on every 60 GB import would be a large, permanent throughput cost for a
  case the scan already covers.
- **No directory `fsync` after the rename.** POSIX requires it for rename durability across a power loss
  and .NET exposes no way to do it. Recovery treats "journal says `Placed`, target missing" as `Wedged`
  rather than silently re-running, which is the conservative reading. Marked **OPEN**: a platform-pack
  `IDirectorySync` would close it and has exactly one caller, so it is held under the promotion rule.

---

## 5. Permissions and ownership

### 5.1 The Docker PUID/PGID convention — what it actually is

Every \*arr container image (`linuxserver/*`, `hotio/*`, `ghcr.io/*arr`) accepts `PUID` and `PGID`
environment variables. The entrypoint script `usermod`s the service account to those ids, `chown`s the
config directory, and `exec`s the application **as that user**. Nothing in the application reads `PUID`.

Three consequences the design must respect, and which the surveyed code half-respects:

1. **The host must never assume it can `chown`.** Changing a file's owner requires `CAP_CHOWN`, which the
   container does not have after dropping to `PUID`. Only the *group* is changeable, and only to a group
   the process is in. `IFilePermissions.SetPermissions(path, mask, group)` already has exactly this shape
   (`IFilePermissions.cs:35`) — group, not owner. Correct, and worth saying out loud.
2. **Setting permissions is off by default.** `SetPermissionsLinux` defaults to `false`
   (`ConfigService.cs:278-282`) and that default is kept. When the entrypoint has aligned uid/gid with the
   media share, chmod is unnecessary; when it has not, chmod usually fails.
3. **The effective identity is a first-class diagnostic.** The host reports `uid`, `gid`, supplementary
   groups and umask in system status and in every permission-denied diagnostic. "Permission denied" without
   "running as uid 1000, gid 1000; the file is owned by 99:100 mode 0640" is a support ticket.

### 5.2 Create with the mode; do not chmod afterwards

`net10.0` can create files and directories with an explicit mode, which the Mono-era code could not:

```csharp
// directories
Directory.CreateDirectory(path, unixCreateMode: UnixFileMode.UserRead | UnixFileMode.UserWrite |
                                                UnixFileMode.UserExecute | /* … */);

// files
var options = new FileStreamOptions
{
    Mode = FileMode.CreateNew,
    Access = FileAccess.Write,
    Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
    UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite |
                     UnixFileMode.GroupRead | UnixFileMode.OtherRead   // 0644
};
using var stream = new FileStream(path, options);
```

This removes the window in which a freshly created media file is world-readable before the chmod lands,
and it removes an entire syscall from the hot path. Both throw `PlatformNotSupportedException` on Windows,
so the call sites are `OperatingSystem.IsWindows()`-guarded — which also satisfies the platform-compatibility
analyzer.

**OPEN:** whether `FileStreamOptions.UnixCreateMode` is masked by the process umask. If it is, an
explicitly requested `0664` silently becomes `0644` under the default `umask 022`. WP-4 verifies this with
a test that creates a file under a known umask and asserts the resulting mode; if the umask applies, the
creation is followed by a conditional `File.SetUnixFileMode` when the observed mode differs from the
requested one. The design tolerates either answer; guessing would not.

There is no managed `umask(2)`. Setting the process umask, if it proves necessary, is a platform-pack
P/Invoke and is listed as deferred.

### 5.3 Masks

Sonarr has one configured mask, `ChmodFolder`, default `"755"` (`ConfigService.cs:285-289`), applied to
folders *and* to files — the Mono provider strips the execute bits when the target is a file
(`NzbDrone.Mono/Disk/DiskProvider.cs`, `GetFilePermissions`). That derivation is sensible and completely
invisible from the setting's name.

Design: derivation kept, made explicit, overridable.

```jsonc
"Arronix": {
  "Files": {
    "Permissions": {
      "Apply": false,              // default; see §5.1
      "FolderMode": "0755",
      "FileMode": null,            // null ⇒ FolderMode with the execute bits cleared (0644)
      "Group": null                // null ⇒ leave the group unchanged
    }
  }
}
```

`IFilePermissions.IsValidPermissionMask` (`IFilePermissions.cs:56`) validates both at options-binding
time, so a malformed mask is a startup failure rather than a surprise during the first import. That is
what the contract's own XML doc says it is for (`IFilePermissions.cs:50-53`), and nothing currently calls
it at bind time.

### 5.4 Windows ACLs

Windows gets one operation: `IFilePermissions.InheritPermissions(path)` — remove explicit ACEs so the file
inherits from its parent directory. `SetEveryonePermissions` from the legacy interface
(`NzbDrone.Common/Disk/IDiskProvider.cs:11`) is already absent from `IFilePermissions`, and the contract's
own remark explains why (`IFilePermissions.cs:16-18`). Nothing here reintroduces it.

The behavior worth documenting for operators, because it is the source of the "why are permissions
different after an upgrade" question:

- A file **moved** within a volume keeps its explicit ACL, including any the download client applied.
- A file **copied** (or moved across volumes) gets the destination directory's inherited ACL.

So on Windows the permission outcome depends on the transfer mode, which depends on the topology. The host
therefore calls `InheritPermissions` after every placement, not only after copies — which is what
`MediaFileAttributeService.SetFilePermissions` does (`MediaFileAttributeService.cs:34-54`) — and does it
in a `try`/`catch` that degrades to a warning, because it fails routinely on NAS-backed SMB shares.

### 5.5 Ordering

Permissions are applied to the **staging file**, before the rename at step 6, and to each directory at
creation. Applying them after the rename — which is what
`EpisodeFileMovingService.cs:172` does — means the file is visible at its final path with the wrong mode
for a window, which is exactly when a media server indexes it.

One further defect not to carry: `EpisodeFileMovingService.CreateFolder` swallows the `IOException` from
`CreateFolder` and then calls `SetFolderPermissions` on a directory that may not exist
(`EpisodeFileMovingService.cs:239-248`), turning a directory-creation failure into a permissions warning
several lines away from its cause.

---

## 6. Remote path mapping

### 6.1 The problem, stated generically

A download client reports the location of a completed download **in its own filesystem namespace**. The
host must translate that into its own. Nothing about this is media-specific, and nothing about it is
optional: it is the single most frequent \*arr support issue.

Three namespaces are in play, and conflating any two of them produces a distinct failure:

| Namespace | Example | Who owns it |
|---|---|---|
| The download client's view | `/downloads/complete/Some.Release` | the client's container or host |
| The Arronix host's view | `/data/torrents/complete/Some.Release` | this host's mounts and bind mounts |
| The path grammar | `Windows` vs `Unix` | neither — it is a property of the *client*, not of the string |

The spec's `DownloadClientItem.OutputPath` is `string?` and `DownloadClientInfo.OutputRootFolders` is
`IReadOnlyList<string>` (spec §5.6). Both are **in the client's namespace, untranslated**. That is a
documentation clarification on those members, not a contract change — a plugin must not translate, because
it cannot see the host's mapping table and must not be able to.

### 6.2 The mapping

```csharp
// src/Arronix.Host/Files/Mapping/RemotePathMapping.cs   (Tier B)
public sealed record RemotePathMapping
{
    public required long Id { get; init; }

    /// <summary>The download-client instance. Keyed by provider id, not by hostname:
    /// two clients on one host (a Usenet and a torrent daemon in separate containers) are the exact
    /// configuration Sonarr's host-string key cannot express (RemotePathMappingService.cs:128,147).</summary>
    public required ProviderId Client { get; init; }

    /// <summary>The prefix as the client reports it, in the grammar declared below.</summary>
    public required PlatformPath RemotePrefix { get; init; }

    /// <summary>Declared, never inferred. Inference is what turns "C:\downloads on a Linux host"
    /// into an error instead of a mapping (RemotePathMappingCheck.cs:245-291).</summary>
    public required PlatformPathKind RemoteGrammar { get; init; }

    /// <summary>The equivalent prefix in this host's namespace.</summary>
    public required PlatformPath LocalPrefix { get; init; }

    public string? Note { get; init; }
}
```

Translation is `PlatformPath` arithmetic — `Contains` for the prefix test, `+` and subtraction for the
re-rooting — which is precisely what that type was built for and what its 430 lines exist to make total
rather than string-hacked. The algorithm is Sonarr's (`RemotePathMappingService.cs:128-157`) with three
changes: the key is the provider, the grammar is declared, and the longest matching prefix wins rather
than the first (mappings are not ordered by the operator and should not depend on insertion order).

Both directions are needed. `RemapLocalToRemote` (`RemotePathMappingService.cs:159-188`) exists because
some clients must be *told* a path — a category directory, a "move to completed" instruction — and it is
the same table read backwards.

### 6.3 Validation on save

Sonarr validates four things (`RemotePathMappingService.cs:92-126`): host non-empty, no leading space on
the remote path, remote non-empty, local rooted and existing. All four are kept. Added:

- **The local prefix is inside, or contains, a known root folder or a known mount.** A mapping to a path
  that exists but is on no mount the host can see is almost always a typo inside a container.
- **The remote prefix parses in the declared grammar.** Free, now that the grammar is declared.
- **No two mappings for one client have overlapping remote prefixes** where the shorter is not a proper
  ancestor of the longer. Overlapping mappings are how "it worked yesterday" happens.

### 6.4 Self-diagnosis — the part that matters

Sonarr's `RemotePathMappingCheck` is 371 lines of nested conditionals producing twelve distinct messages
(`RemotePathMappingCheck.cs:48-369`). It is *correct* — the messages are genuinely well targeted — and it
is unmaintainable. Every one of those twelve outcomes is a function of six booleans:

| | Symbol | Source |
|---|---|---|
| a | `grammarMatchesHost` | does the reported path parse in the host's grammar? |
| b | `mappingMatched` | did any mapping's remote prefix match? |
| c | `translatedExists` | does the translated path exist locally? |
| d | `clientIsLocalhost` | client host resolves to a loopback address |
| e | `hostIsContainerized` | `IOperatingSystemInfo.IsContainerized` |
| f | `parentVisible` | does an ancestor of the translated path exist locally? |

The full table. Each row is one `HealthCheck` with a stable diagnostic id, a remediation sentence and a
docs anchor. **The anchors are Arronix's**, under `docs/troubleshooting/remote-path-mapping`, and they are
derived mechanically from the diagnostic id — `RPM.MappingMissingRemote` → `#rpm-mapping-missing-remote` —
so the link is a function of the id rather than a string somebody maintains in a table.

An earlier draft used Sonarr's anchors (`#bad-remote-path-mapping`, `#permissions-error`, …) on the grounds
that the community already links to them. That would hard-code a dependency on a third party's
documentation site into Arronix's shipped diagnostic data: every health item a user sees would send them to
another product's troubleshooting page, describing another product's settings screens, and it would break
whenever that site reorganizes — which nobody here would notice or could fix. It also inverts the wiki
decision `servarr-prior-art.md` §8.3 already made: the wiki is *reference for us while specifying*, not
*content we ship to users*.

The pages behind these anchors do not exist yet. That is a documentation work item, not a reason to point
at somebody else's: a health check whose anchor 404s is a missing page, while one that leads to Sonarr's
page is a wrong answer delivered confidently.

| a | b | c | d | e | f | Diagnosis | Remediation |
|---|---|---|---|---|---|---|---|
| ✗ | – | – | ✗ | – | – | `RPM.RemoteGrammarMismatch` | The client runs a different OS. Add a mapping declaring its grammar. `#rpm-remote-grammar-mismatch` |
| ✗ | – | – | ✓ | ✓ | – | `RPM.ContainerGrammarMismatch` | The client is reporting a path from outside your container. `#rpm-container-grammar-mismatch` |
| ✗ | – | – | ✓ | ✗ | – | `RPM.LocalGrammarMismatch` | The client's configured download directory is wrong. `#rpm-local-grammar-mismatch` |
| ✓ | ✗ | ✓ | – | – | – | *healthy* | No mapping needed; the namespaces coincide. |
| ✓ | ✗ | ✗ | – | ✓ | ✗ | `RPM.MappingMissingContainer` | The path is not mounted into this container. **Proposal available** (§6.5). `#rpm-mapping-missing-container` |
| ✓ | ✗ | ✗ | ✗ | ✗ | ✗ | `RPM.MappingMissingRemote` | The client is on another machine and no mapping exists. **Proposal available.** `#rpm-mapping-missing-remote` |
| ✓ | ✗ | ✗ | ✓ | ✗ | ✗ | `RPM.PathMissingLocal` | Same machine, same grammar, path absent — the client's directory does not exist. `#rpm-path-missing-local` |
| ✓ | ✓ | ✗ | – | – | ✓ | `RPM.MappingPrefixWrong` | A mapping matched but the result does not exist while its parent does — the mapping is off by a segment. **Proposal available.** `#rpm-mapping-prefix-wrong` |
| ✓ | ✓ | ✗ | – | – | ✗ | `RPM.MappingTargetMissing` | The mapped local prefix does not exist. Check the mount. `#rpm-mapping-target-missing` |
| ✓ | – | ✓ | – | – | – | + file unreadable | `RPM.Permissions` — the path exists and cannot be read. Report uid/gid/mode. `#rpm-permissions` |
| ✓ | – | ✓ | – | – | – | + file vanished mid-import | `RPM.FileRemoved` — the client removed the file during import. Disable the client's "remove completed". `#rpm-file-removed` |

The last two rows are Sonarr's import-failure branch (`RemotePathMappingCheck.cs:186-217`) and they are
the two that need the *event*, not the periodic check: they are evaluated when a placement fails, with the
failing path in hand.

### 6.5 The proposal generator

Three rows above say "proposal available". This is the piece the surveyed applications do not have and the
brief explicitly asks for.

When a translated path does not exist, `MappingProposalService` searches for a plausible mapping by
**longest common suffix**:

```csharp
// src/Arronix.Host/Files/Mapping/MappingProposalService.cs   (Tier B)
public sealed record MappingProposal(
    PlatformPath RemotePrefix,
    PlatformPathKind RemoteGrammar,
    string LocalPrefix,
    int MatchedSegments,
    ProposalConfidence Confidence,
    string Evidence);          // "found 3 of 4 trailing segments under /mnt/user/downloads"

public enum ProposalConfidence { Weak = 0, Likely = 1, Strong = 2 }
```

Algorithm, for a remote path `R = r₁/r₂/…/rₙ`:

1. Build the candidate root set: every configured root folder's parent, every mount from
   `IStorageMountProvider.EnumerateMounts()` that is not special, and every configured local prefix from
   existing mappings.
2. For each candidate `C` and each `k` from `n-1` down to `1`, test whether `C/rₖ₊₁/…/rₙ` exists.
3. The first hit yields the proposal `r₁/…/rₖ → C`, with `MatchedSegments = n-k`.
4. Confidence: `Strong` when three or more segments matched *and* exactly one candidate hit; `Likely` when
   two matched or several candidates hit; otherwise `Weak`.

Concretely: the client reports `/downloads/complete/Some.Release/file.mkv`; the host has
`/mnt/user/downloads` mounted and `complete/Some.Release/file.mkv` exists under it; the proposal is
`/downloads → /mnt/user/downloads` with three matched segments and `Strong` confidence.

**Never auto-applied.** A proposal is presented, with its evidence sentence, as a one-click action. The
vehicle is spec §6.5's `WorkbenchDescriptor` — a declared grid of candidate mappings with an apply action
— which is exactly the shape it was specified for and requires no new intent vocabulary.

### 6.6 The adjacent misconfigurations

Two more, both already detected by Sonarr and both belonging here rather than in an acquisition document,
because they are *path topology* problems:

- **A download client writes into a root folder** (`DownloadClientRootFolderCheck.cs:51`). The importer
  then races the client, and a "delete after import" sweep can reach live media. Refused at root-folder
  registration (§2.2 check 7) *and* re-checked when a client is added, because the order of configuration
  is not fixed.
- **A download client has category sorting enabled** (`DownloadClientSortingCheck.cs:45`). The client
  moves the payload after reporting its path, so the host's translated path goes stale mid-import.
  Reported as a warning naming the sorting mode, which is what the surveyed check does and is the right
  level.

---

## 7. Recycle bin and deletion safety

### 7.1 The rule

> The platform never removes user media from the filesystem except (a) as the immediately-recorded result
> of an explicit user action, or (b) by the recycle-bin retention sweep, which acts only on files it
> previously placed there.

Everything else — a failed import, a missing metadata record, an uninstalled plugin, a media kind whose
shape changed — is a **database** operation. This is §4.1 restated at the level the operator cares about,
and it is the deliberate inversion of Prowlarr's `RemoveMissingImplementations` pattern that spec
resolution #42 already rejects for plugins.

### 7.2 Resolution and layout

The bin for a given file is resolved: root-folder `RecycleBinPath` → global
`Arronix:Files:RecycleBin:Path` → **refuse** when `RequireRecycleBin` is `true`, or hard-delete with an
audit record when it is `false`.

`RequireRecycleBin` defaults to **`true`**, which inverts the surveyed default. Resolution #17: a recycle
bin that silently disables itself is not a safety feature. An operator who genuinely wants no bin sets one
flag and gets an audit trail either way.

Layout inside the bin:

```
<bin>/2026-08-10/019219f3-…-7c4a/Series Name/Season 01/Old.File.mkv
      │          │                └── the path relative to the root folder, preserved verbatim
      │          └── the placement OperationId that displaced it
      └── the UTC date of displacement
```

This replaces the `_2`/`_3` suffix loop (`RecycleBinProvider.cs:105-117`), which costs a `stat` per
collision and destroys the provenance needed to restore a file to where it came from. Retention becomes a
directory-level sweep over date folders, and restore becomes a rename of a subtree.

**Cross-volume bins are a health warning, not an error.** If the resolved bin is on a different volume
from the file being displaced, displacement is a full copy — of the file the user is *discarding* — which
can add tens of minutes to an upgrade. The warning names both paths and suggests a per-root bin. This is
the concrete reason resolution #14 made bins per-root.

### 7.3 Retention

```jsonc
"Arronix": {
  "Files": {
    "RecycleBin": {
      "Path": null,                 // global fallback; null ⇒ per-root only
      "RequireRecycleBin": true,
      "RetentionDays": 7,           // 0 ⇒ never sweep
      "MaxSizeBytes": null          // null ⇒ unbounded; otherwise oldest-first eviction
    }
  }
}
```

The retention clock is the **date directory name and the journal record**, never the file's mtime.
Resolution #16: Sonarr rewrites the last-write time of every file entering the bin
(`RecycleBinProvider.cs:130,60-63`) so that `Cleanup` can age it (`RecycleBinProvider.cs:181`). That
mutates data the user asked us to preserve, and it means a restored file has lost its original timestamp.

`MaxSizeBytes` is new and addresses the failure the surveyed design cannot: a bin on the media volume with
`RetentionDays = 0` fills the disk, and the free-space guard then rejects every import.

### 7.4 Audit and restore

Every displacement and every deletion writes a record, retained independently of the file:

```csharp
public sealed record DeletionRecord(
    Guid OperationId,
    string OriginalPath,
    string? RecyclePath,          // null ⇒ hard-deleted
    long Length,
    DeletionReason Reason,        // Upgrade | UserRequested | RetentionSweep | ManualOverride
    string? Actor,                // API principal, or null for a scheduled job
    string? CorrelationId,
    DateTimeOffset At);
```

`POST /api/v1/files/recycle-bin/{operationId}/restore` re-places the subtree via the §4 journal, so a
restore is itself crash-safe, and refuses when the original path is now occupied rather than clobbering.

Emptying the bin is the only unconditional hard delete in the platform. It is an explicit endpoint, it
requires the current item count and total size to be echoed back by the caller, and it is never reachable
from a plugin: `IFileSystem.DeleteFile` is scoped to the plugin's granted roots by `ScopedFileSystem`
(spec §4.2), and no plugin is ever granted the recycle bin as a root.

> **The scope tension, resolved.** A plugin granted `storage` over a library root *can* call
> `IFileSystem.DeleteFile` on media inside it. That is not a hole in this design; it is the meaning of
> granting the capability, and the threat model owns the argument. What this design guarantees is that the
> *platform's own* code paths never do it, so an operator can attribute any deletion to either a named
> plugin or a named user action.

---

## 8. Library scanning

### 8.1 Full versus incremental

**Full scan** enumerates a root folder's subtree, projects `(path, length, lastWriteUtc)` and reconciles
against the store. It is the correctness mechanism and runs on a schedule
(`library-scan`, default `daily 03:00`) and on demand.

**Incremental scan** exists to make the common case cheap. It keeps a per-directory fingerprint:

```csharp
public readonly record struct DirectoryFingerprint(
    string Path, DateTimeOffset LastWriteUtc, int EntryCount, long TotalLength);
```

A subtree whose fingerprint is unchanged is skipped. This is not perfect — an in-place content change that
preserves both mtime and length is invisible — and the failure is stated rather than hidden: it is caught
by the next full scan, and the class of tools that rewrites media in place (transcoders, tag editors)
almost always changes the length. `TotalLength` in the fingerprint is what makes tag-writeback visible,
because tag edits change size while some editors preserve mtime.

### 8.2 Watching versus polling

**Decision: watch as a hint; never as truth.** Resolution #21.

`FileSystemWatcher` is offered, off by default, per root folder. When enabled it does exactly one thing:
debounce events for 30 seconds and enqueue an *incremental scan of the affected subtree*. It never causes
an import directly and it never causes a deletion.

The reasons, all of which are reportable as health information rather than discovered as bugs:

| Failure | Detection | Reported as |
|---|---|---|
| inotify watch limit exhausted (`fs.inotify.max_user_watches`, often 8192; a 100 k-directory library needs one per directory) | `IOException` / `Error` event from the watcher | `Scan.WatchLimitExceeded`, naming the sysctl and the current value |
| Network filesystem (NFS, SMB, SSHFS) delivers no events | mount `DriveType == Network` at enable time | `Scan.WatchUnsupportedOnNetworkMount`, refuse to enable |
| Buffer overflow under a bulk change | `InternalBufferOverflowException` | escalate that root to a full scan; do not attempt to recover the event stream |
| macOS recursive watching | `FileSystemWatcher` maps to FSEvents with coarse granularity | treated as a subtree hint, which is all we use it for anyway |

Because the watcher only ever *schedules* work the scheduled scan would eventually do, every one of these
failures degrades to "the library updates on the next scan" rather than to a wrong result.

### 8.3 Filters

All of it is configuration data, shipped with defaults. Resolution #22. The surveyed code hardcodes the
same knowledge in three places — `DiskScanService.cs:72-75` (four regexes), `RootFolderService.cs:36-47`
(nine names), `DiskTransferService.cs:520-540` (three patterns) — with overlapping and inconsistent
coverage.

```jsonc
"Arronix": {
  "Files": {
    "Scanning": {
      // Directory names skipped entirely, matched on the segment, ordinal-ignore-case.
      "IgnoredDirectories": [
        "@eadir", ".@__thumb", "#recycle", "lost+found", "$RECYCLE.BIN",
        "System Volume Information", ".AppleDouble", ".AppleDB", ".AppleDesktop",
        ".grab", "plex versions", ".actors", "extrafanart"
      ],
      // File name patterns skipped entirely.
      "IgnoredFilePatterns": [ "^\\._", "^\\.nfs", "^Thumbs\\.db$", "^\\.DS_Store$", "\\.socket$",
                               "^debug\\.log$", "\\.arronix-[0-9a-f]{32}\\.partial$" ],
      // Any dot-prefixed directory.
      "IgnoreHiddenDirectories": true,
      // Path segments that mark supplementary content; surfaced to the plugin, never imported as primary.
      "ExtrasDirectories": [ "extras", "behind the scenes", "deleted scenes", "featurettes",
                             "interviews", "other", "scenes", "shorts", "trailers", "samples" ],
      "RemoveEmptyDirectories": false,
      "MinimumFileBytes": 1048576,
      "MaxDegreeOfParallelismPerVolume": 1
    }
  }
}
```

Two extension policies are **not** here and belong to the plugin, because "which extensions are media" is
the definition of a media kind: the include list and the runtime-based sample test. What the host keeps is
the *safety* list — `DangerousExtensions`, `ExecutableExtensions` and the operator's own rejection list
(`DownloadedEpisodesImportService.cs:267-306`) — because refusing to place an `.exe` into a media library
is a platform decision, not a media decision.

### 8.4 Sample and extras detection

Two tiers. Resolution #23.

**Host tier** (cheap, media-agnostic, runs during enumeration):
- length below `MinimumFileBytes`;
- a path segment in `ExtrasDirectories`;
- a filename ending in one of the `ExtrasSuffixes` (`-trailer`, `-featurette`, …).

Files failing these are not discarded — they are marked `IsSupplementary` on the scan result and handed to
the plugin, because a plugin that manages short-form content will legitimately want them.

**Plugin tier**: `IImportPipeline.EvaluateImportAsync` rejects with a reason. Sonarr's runtime comparison
(`DetectSample.cs:36-42`) needs `ffprobe` and the expected runtime of the specific episode; that is a
media semantic and it stays in the plugin, where the expected runtime lives.

### 8.5 Scaling

Target: 250 000 files across 8 root folders, on spinning disks behind SMB, without pinning a core or
holding a request thread.

| Concern | Design |
|---|---|
| Enumeration cost | One `FileSystemEnumerable<ScannedFile>` per root with a transform that projects `(FullPath, Length, LastWriteTimeUtc, Attributes)` **from the directory entry**, and `EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = ReparsePoint \| System, BufferSize = 65536 }`. Sonarr enumerates once and then calls `GetFileSize` per file in a second loop (`DiskScanService.cs:150-166`) — a second `stat` for data the enumeration already read. |
| Reparse points | Skipped by `AttributesToSkip`, so symlink loops cannot hang a scan. A root folder that *is* reached through a symlink still works; only links *inside* it are skipped. Recorded as a limitation: users who symlink individual items into a library need `FollowSymlinks = true`, which then requires cycle detection by `(device, inode)` — deferred, one caller. |
| Store round-trips | The scan reads the store's file records for a root **once** into a `FrozenDictionary` keyed by `PathComparer`, then reconciles in memory. Sonarr does one repository call per media item (`DiskScanService.cs:136`). |
| Concurrency | One scan per volume (`MaxDegreeOfParallelismPerVolume`), because two scans on one spindle are slower than one. Roots on distinct mounts scan in parallel up to a global cap. |
| Cancellation and progress | Every stage takes a `CancellationToken` and reports through `IProgressReporter`, which already carries the scheduler's correlation id (`Diagnostics/IProgressReporter.cs`). A scan is resumable at directory granularity via the fingerprint table. |
| Memory | The pipeline is streaming: enumerate → filter → batch of 500 → reconcile → next. No `ToList()` over a root folder. Sonarr materializes three arrays per scan (`DiskScanService.cs:206-214`). |

---

## 9. Upgrade replacement

### 9.1 The defect, precisely

All four surveyed applications implement upgrade as: *delete the existing file, then move the new one in.*

- Sonarr: `MediaFiles/UpgradeMediaFileService.cs:62-73` (recycle/delete each existing file) then `:78-85`
  (move or copy the new one).
- Radarr: `MediaFiles/UpgradeMediaFileService.cs:58-70` then `:74-81`.
- Lidarr: `MediaFiles/UpgradeMediaFileService.cs:63-75` then `:77-84`.
- Readarr: the same shape.

They share one guard — throw if the root folder is missing *before* deleting anything, with the identical
comment "so the old file isn't left behind during the import process" (Sonarr `:49-53`, Radarr `:46-50`,
Lidarr `:51-55`) — which shows the hazard was recognized and addressed for the one failure mode that was
observed in the field.

The failure modes it does not address, each of which leaves the user with neither file when the recycle
bin is unconfigured (the shipped default, `ConfigService.cs:95-98`, hard-deleting at
`RecycleBinProvider.cs:74-87`):

1. The destination volume fills between the delete and the move (`ENOSPC`).
2. The source disappears — the download client removed it, which is common enough that Sonarr has a
   dedicated health message for it (`RemotePathMappingCheck.cs:206-216`).
3. The destination path becomes unwritable (mount went read-only, permissions changed).
4. The process is killed, the container is restarted, or the host loses power.
5. The transfer itself fails verification.

`ImportApprovedEpisodes` catches `DestinationAlreadyExistsException` and queues a rescan
(`ImportApprovedEpisodes.cs:204-210`) and catches `RecycleBinException` (`:211-217`) — recovery for the
index, none for the bytes.

### 9.2 The corrected ordering

Upgrade is not a special case. It is §4.3 with a non-empty displacement set:

```
Planned    → intent recorded, including EVERY file this placement will displace
Staged     → new file transferred to <targetDir>/.arronix-{opId}.partial and verified
Displaced  → each existing file moved to the recycle bin; each recorded as it completes
Placed     → atomic rename of staging onto the target path
Committed  → one store transaction: new file record + link, old links removed
Released   → source deleted if the mode was Move
```

At every point before `Placed`, **at least one complete copy of the content exists at a known path**:
before `Staged` the source; between `Staged` and `Displaced` both; between `Displaced` and `Placed` the
staged file plus the recycled originals. There is no window in which the user has neither.

### 9.3 The set case

A single release frequently replaces several files — a season pack over ten individual episodes, a
re-rip over a multi-disc album. The set is one journal record with one `Displaced` list, and the phases
are batched:

1. Stage **all** new files (each in its own target directory).
2. Verify all.
3. Displace all existing files.
4. Rename all staged files into place.
5. One store transaction for the whole set.

If step 4 fails partway, recovery renames the placed files back to staging and restores the displaced
originals — all renames, all within their directories, all fast. This is why staging lives beside the
target: a set-level rollback that had to copy would not be a rollback, it would be a second import.

### 9.4 Hardlinks, seeding and free space

When the source is a seeding torrent (`DownloadClientItem.CanMoveFiles == false`, spec §5.6) the mode is
`HardLinkOrCopy` and `ReleaseSource` is false. Two consequences the surveyed code gets wrong:

- **The free-space guard must run against the resolved mode.** `FreeSpaceSpecification.cs:41-54` checks
  `freeSpace < size + minimum` unconditionally, so on a volume with 2 GB free a 40 GB hardlinked import is
  rejected even though it consumes nothing. Resolution #24: `TransferPlanner` resolves the mode first, and
  the guard is skipped entirely for `HardLink` and for a clone.
- **Displacing the old file may free nothing.** If the outgoing file was itself hardlinked from a torrent
  that is still seeding, its inode has another link and the space is not returned. Capacity reporting must
  therefore never *predict* reclaimed space from a displacement; it samples after the fact (§2.3).

### 9.5 What "upgrade" means is not this subsystem's business

Whether the new file *is* an upgrade — quality comparison, custom formats, cutoff — is decided upstream by
`IQualityModel` and the import specifications, and is owned by `docs/design/acquisition-pipeline.md`. This
subsystem receives a decision and a destination and is responsible only for the property that the operator
never loses both files. Keeping that boundary is what lets the ordering above be identical for every media
kind.

---

## 10. Health contributions

Eight `IHealthContributor` implementations, all in `Arronix.Host/Files/Health/`, all reusing the stable
`HealthCheck` / `HealthStatus` / `HealthSeverity` DTOs verbatim (extraction plan §3.1).

| Contributor | Trigger | Severity | Surveyed equivalent |
|---|---|---|---|
| `RootFolderAccessibility` | root missing, unreadable, or capacity probe timing out | Error | `RootFolderCheck.cs:37` |
| `RootFolderReadOnly` | mount reports `MountOptions.IsReadOnly` | Error | `MountCheck.cs:26` |
| `RootFolderCapacity` | free space below `MinimumFreeSpaceBytes` or below the largest queued item | Warning | none — Sonarr only fails the import |
| `RecycleBinWritable` | configured bin not writable by probe; or cross-volume from a root | Error / Warning | `RecyclingBinCheck.cs:33` (writability only) |
| `RemotePathMapping` | the §6.4 table, on schedule and on placement failure | Error | `RemotePathMappingCheck.cs:48-369` |
| `DownloadClientPathTopology` | client output inside a root folder; client sorting enabled | Warning | `DownloadClientRootFolderCheck.cs:51`, `DownloadClientSortingCheck.cs:45` |
| `PlacementJournal` | any record in `Wedged`; or a non-terminal record older than 24 h | Error | none |
| `ScanWatcher` | inotify limit, unsupported mount, buffer overflow | Warning | none |

`PlacementJournal` is the one with no surveyed ancestor and the one an operator most needs: it is how
"there is a file the platform could not finish placing, and here is its path" reaches a human.

---

## 11. Configuration, consolidated

```jsonc
{
  "Arronix": {
    "Files": {
      "Transfer": {
        "Verification": "Default",            // None | Size | Sampled | Full | Default (per §3.5 table)
        "VerificationBackoffMs": [ 250, 1000, 3000 ],
        "IgnoredNames": [ ".nfs*", "debug.log", "*.socket" ],
        "IntermediateSuffix": ".backup~",
        "FolderMoveResidueLimitBytes": 104857600,   // DiskTransferService.cs:122
        "PreferHardLinks": true,                     // ConfigService.cs:204-208 (CopyUsingHardlinks)
        "EnableBlockClone": true
      },
      "Placement": {
        "StagingSuffix": ".partial",
        "AuditRetentionDays": 30,
        "ReconcileSchedule": "every 1h"
      },
      "Permissions": {
        "Apply": false, "FolderMode": "0755", "FileMode": null, "Group": null
      },
      "RecycleBin": {
        "Path": null, "RequireRecycleBin": true, "RetentionDays": 7, "MaxSizeBytes": null,
        "CleanupSchedule": "daily 02:00"
      },
      "Scanning": {
        "Schedule": "daily 03:00",
        "EnableWatcher": false,
        "WatcherDebounceSeconds": 30,
        "IgnoredDirectories": [ /* §8.3 */ ],
        "IgnoredFilePatterns": [ /* §8.3 */ ],
        "IgnoreHiddenDirectories": true,
        "ExtrasDirectories": [ /* §8.3 */ ],
        "RemoveEmptyDirectories": false,
        "MinimumFileBytes": 1048576,
        "MaxDegreeOfParallelismPerVolume": 1,
        "GlobalScanConcurrency": 3
      },
      "Safety": {
        "MinimumFreeSpaceBytes": 104857600,          // ConfigService.cs:197-201
        "SkipFreeSpaceCheck": false,                 // ConfigService.cs:190-194
        "DangerousExtensions": [ ".lnk", ".ps1", ".vbs", ".bat", ".cmd", ".scr" ],
        "ExecutableExtensions": [ ".exe", ".dll", ".sh", ".app" ],
        "RejectedExtensions": []                     // operator-supplied
      }
    }
  }
}
```

All of it binds through `AddValidatedOptions<T>` with one `SectionName` const per options type, matching
the `Arronix.Common` idiom (`arronix-current-state.md` §7). Every mask is validated at bind time via
`IFilePermissions.IsValidPermissionMask`; every schedule string is validated against the §22 grammar
(`manual | startup | every <duration> | daily HH:mm`).

---

## 12. Contract delta

**New types in `Arronix.Abstractions`: none. One interface and one enum are removed, and one method is
renamed.** Resolution #25 for the additions, #3 for the removal, #2 for the rename.

### 12.1 What is removed: `IFileTransferService` and `FileTransferMode`

Both move to `Arronix.Common/FileSystem/`. `IPluginContext` loses `TryGetFileTransfer` and
`RequireFileTransfer`; `CapabilityMatrix` loses its `[typeof(IFileTransferService)] = [Capability.Import]`
row; `PluginContext`'s constructor loses the parameter and the field. (Those three files belong to
`unified-host-runtime.md`'s owner — recorded here as a request, per the idiom below.) The `import`
capability is unaffected: it still gates the import pipeline, which is what it was always about.

The reasoning, since this reverses a decision:

- **Nothing implements it and nothing calls it.** Outside `Arronix.Abstractions` the only references are
  registry plumbing — `Arronix.Plugins/Registration/PluginContext.cs:121,332,345` and
  `CapabilityMatrix.cs:103`. Zero plugins take it, and the four reference plugins that would are unwritten.
  §12.2's table refuses to *promote* six types on precisely this test ("exactly one implementer and no
  plugin audience"); a rule that decides what may enter has to decide what may stay.
- **Its only documented behavior is that it bypasses the crash-safety mechanism.** Resolution #1 says a
  plugin calling it for placement puts holes in the journal, and "a journal with holes in it is worse than
  none". Keeping a plugin-facing contract whose one distinguishing property is that it defeats §4 is
  keeping a loaded footgun in the front door.
- **The features that justified it belong to other products.** Lidarr's tag writeback and Readarr's Calibre
  backend are real, and they are Lidarr's and Readarr's. Arronix has no requirement for either today, and
  designing a contract around a requirement imported from a product being replaced is exactly the reasoning
  this codebase is meant not to inherit. A plugin that later needs post-placement mutation gets a contract
  shaped by *that* need — most likely a narrow "rewrite tags in this file you were just given" seam rather
  than a general transfer engine, which is a better contract than the one being withdrawn.
- **Withdrawal is free and reversible.** No published package references it; deletion is one interface file,
  one enum file and four lines of plumbing, and the compiler finds all of them. Re-admitting it later costs
  the same edit in reverse. Below 1.0.0 nothing about this is a one-way door
  (`docs/contracts/stability.md`).

`FileTransferMode` goes with it because nothing plugin-facing names it once the interface is gone: it is the
planner's input and return (§3.1, §3.2) and the placer's `RequestedMode`/`ActualMode` (§4), all of which are
Tier B. Leaving an enum in the contract assembly that no contract mentions is how vestigial surfaces start.

### 12.2 What is not admitted, and why

| Type one might expect to promote | Held where | Un-defer trigger |
|---|---|---|
| `IBlockCloneProvider` | `Arronix.Common`, Tier B | A plugin has a legitimate reason to clone — none exists, and none is imaginable, since a plugin never places library files (#1). |
| `IFilePlacementService` | `Arronix.Host`, Tier B | Never. Promoting it would hand plugins the journal and dissolve #1. |
| `RootFolder` / `RootFolderBinding` | `Arronix.Host`, Tier B | A plugin needs to *enumerate* roots. It does not: it receives `LibraryPathSpec.RootPath` and `LibraryFacet.RootFolderPath`. |
| `RemotePathMapping` | `Arronix.Host`, Tier B | Never. A download-client plugin that could translate could also see the host's storage topology. |
| `TransferVerification` | `Arronix.Common`, Tier B | A plugin needs per-call verification control. Today the host's policy applies to plugin transfers too, which is the correct default. |
| A richer transfer outcome record | `Arronix.Host`, Tier B | There is no plugin-facing transfer case left (§12.1); the host's own placer returns `PlacementRecord`, which is Tier B and free to grow. |

### 12.3 What is renamed: `ImportAsync` → `OnPlacedAsync`

```csharp
// src/Arronix.Abstractions/Import/IImportPipeline.cs
/// <summary>
/// Called after the host has placed the file at <see cref="ImportDecision.DestinationPath"/>, to let the
/// plugin record the import in its own catalog.
/// </summary>
/// <remarks>
/// The file is guaranteed to exist at that path when this is called. The implementation MUST be
/// idempotent — recovery calls it again after a crash between <c>Placed</c> and <c>Committed</c> (§4) —
/// and MUST NOT move, rename or delete the file. Placement is the host's, and it is journaled;
/// a plugin that moves the file puts a hole in the journal (#1).
/// </remarks>
Task<bool> OnPlacedAsync(ImportDecision decision, CancellationToken cancellationToken = default);
```

The signature is otherwise untouched — only the name and the documentation change. (The `bool` return is
`acquisition-pipeline.md`'s to reconsider, not this document's.)

Resolution #2 inverted this method's meaning: it read as *"move the file into the library"* and now means
*"the host already moved it; commit your side"*. That is not a clarification, it is the opposite
instruction, and a previous draft shipped it as a `<remarks>` addition under an explicit no-signature-change
budget. There is no such budget — below 1.0.0 a contract may be renamed in a MINOR
(`docs/contracts/stability.md`) — and there is no reason to want one: `IImportPipeline` has no implementers
outside this repository, so the rename costs a compiler error at each internal call site and nothing else.

Keeping the old name would have cost far more, and permanently. Every plugin author who reads `ImportAsync`
will believe it imports; the ones who act on that belief write the exact code §4 exists to make impossible,
and they will have been told to by the method's name while a `<remarks>` said otherwise three lines down.
A name that must be contradicted by its own documentation is a defect, not a naming preference.
`OnPlacedAsync` says when it is called and implies what it is for; `CommitImportAsync` was the alternative
and is equally honest — `OnPlacedAsync` wins narrowly because it names the *event*, which is what makes the
idempotency requirement obvious.

If `IImportPipeline` is replaced outright by `IImportPlanner` (`acquisition-pipeline.md` §9 — that
document's decision, not this one), this rename is subsumed: the successor's commit hook is named correctly
from the start. Either way the misnamed method does not ship.

### 12.4 One request to another document

`unified-host-runtime.md` §5.6 — `DownloadClientItem.OutputPath` and `DownloadClientInfo.OutputRootFolders`
should gain a `<remarks>` stating they are **in the download client's own namespace and must not be
translated by the provider**. (That document is owned elsewhere; this is a request to its owner, not an
edit.)

---

## 13. Work packages

Ordered. Each is independently testable and each has a stated done-condition.

| WP | Package | Depends on | Done when |
|----|---------|-----------|-----------|
| **WP-1** | **Settle the `IFileSystem` implementation seat.** One `HostFileSystem` in either `Arronix.Common/FileSystem/` (preferred, per extraction plan §3.2) or `Arronix.Host` (per spec §4.3) — not both. Add `IHostFileSystem` with the host-only members. | — | A governance test asserts exactly one `IFileSystem` implementation exists outside test doubles. |
| **WP-2** | `TransferPlanner` + `TransferVerifier` + `IBlockCloneProvider` (null impl). Pure, no I/O in the planner. | WP-1 | The §3.2 matrix is a parameterized test with 20 rows; verification levels round-trip on a corrupted-tail fixture. |
| **WP-3** | `FileTransferService` over the planner, host-internal in `Arronix.Common` (§12.1). Options-bound ignore list, intermediate suffix, residue limit. **Includes the contract deletions:** `IFileTransferService` + `FileTransferMode` out of `Arronix.Abstractions`, the two `IPluginContext` members, the `CapabilityMatrix` row, the `PluginContext` field. | WP-2 | Folder transfer, mirror and the case-rename dance pass against the surveyed behavior, with every magic number bound; a governance test asserts no transfer contract is reachable from `IPluginContext`. |
| **WP-4** | Permissions: `PosixFilePermissions` / `WindowsFilePermissions` platform packs, create-with-mode call sites, bind-time mask validation, **the umask experiment** (§5.2 OPEN). | WP-1 | A test asserts the resulting mode of a created file under a known umask; effective uid/gid/umask appear in system status. |
| **WP-5** | Root folders: entity, bindings, the eight registration checks, background capacity sampling, unmapped-folder endpoint. | WP-1 | Adding a root inside app-data, inside another root, or on a read-only mount each fails with its own diagnostic. |
| **WP-6** | Block-clone platform packs (Linux `FICLONE`, macOS `clonefile`, Windows ReFS) **plus the benchmark** that decides whether the Linux one survives (§3.4 decision 3). | WP-2 | Import throughput measured on btrfs with the pack enabled and disabled; the pack is kept or deleted on the number. |
| **WP-7** | `PlacementJournal` + `FilePlacementService` — the §4.3 ordering. Keyed per-target locking. | WP-3, WP-5 | A fault-injection test kills the process at each of the ten steps and asserts the §4.4 recovery outcome. |
| **WP-8** | `PlacementRecovery` — startup and scheduled. Orphan sweep. `Wedged` health contribution. | WP-7 | The ten-row recovery table is a parameterized test; `Wedged` names every affected path. |
| **WP-9** | Recycle bin: resolution order, date/operation layout, retention sweep, size cap, restore endpoint, `DeletionRecord` audit. | WP-7 | A displaced file is restorable to its original relative path; `RequireRecycleBin` blocks placement when no bin resolves. |
| **WP-10** | Upgrade replacement: displacement sets, the set-level rollback, the free-space guard conditioned on the resolved mode. | WP-7, WP-9 | The five §9.1 failure modes each leave at least one complete copy; the hardlink case does not consult free space. |
| **WP-11** | Scanning: `FileSystemEnumerable` pipeline, fingerprints, filters, host-tier supplementary detection, progress and cancellation. | WP-5 | A synthetic 250 k-file tree scans within budget and allocates below the ceiling; an unchanged subtree is skipped. |
| **WP-12** | Remote path mapping: entity, `PlatformPath` translation, save-time validation, the §6.4 diagnostic table. | WP-5 | The eleven-row table is a parameterized test; each row yields its diagnostic id and anchor. |
| **WP-13** | `MappingProposalService` + the `WorkbenchDescriptor` surface for reviewing and applying proposals. | WP-12 | The canonical `/downloads → /mnt/user/downloads` case yields a `Strong` proposal with its evidence sentence. |
| **WP-14** | The eight health contributors (§10) and the file-management section of system status. | WP-5, WP-8, WP-12 | Each contributor has a test that forces its condition. |
| **WP-15** | Filesystem watcher as a hint, off by default, with the four §8.2 failure detections. | WP-11 | Enabling on a network mount is refused with its reason; a buffer overflow escalates to a full scan. |

Build waves: WP-1 → {WP-2, WP-4, WP-5} → {WP-3, WP-6, WP-11, WP-12} → WP-7 → {WP-8, WP-9, WP-13} →
{WP-10, WP-14, WP-15}.

---

## 14. Deferred, with the trigger that un-defers it

| Deferred | Why now | Un-defer trigger |
|---|---|---|
| Directory `fsync` after rename | No managed API; recovery treats the gap conservatively as `Wedged`. | A reproducible report of a placed file vanishing across a power cut. |
| Setting the process umask | Needs a platform-pack P/Invoke; §5.2's create-with-mode may make it unnecessary. | WP-4's experiment shows `UnixCreateMode` is umask-masked *and* an operator cannot set the umask externally. |
| Following symlinks during a scan | Requires `(device, inode)` cycle detection, which needs a platform-pack stat probe. | A user with a symlink-based library asks; today the workaround is a root folder per link target. |
| Per-file checksum storage | The store's `MediaFileRecord` (spec §4.6) has no hash field, and adding one is additive. | Bit-rot detection or dedupe becomes a requested feature. |
| Cross-root move ("change this item's root folder") | It is §4.3 with a different target and a folder-level displacement set; the primitive exists, the workflow does not. | The library-management UI needs it — likely the milestone after this one. |
| `IDirectorySync`, `IVolumeIdentity` platform seams | One caller each; the promotion rule says hold. | A second caller appears. |
| Hard-link *farming* (placing one file into several libraries) | No shape in the model expresses one file satisfying units in two media kinds. | `UnitFileLink` (spec §4.6) is observed spanning kinds. |
| Quota-aware placement (write only where the *user* has quota, not where the volume has space) | `IStorageMount.AvailableFreeSpace` is already quota-aware for the common case. | An operator reports `ENOSPC` on a volume the platform believed had room. |
| A generic "verify the whole library" job | `Sampled`/`Full` verification exists per placement; a library-wide sweep is a scheduling wrapper. | Requested; it is a small job on top of WP-2. |

---

## 15. Summary of what changes relative to the surveyed applications

Stated bluntly, because these are the claims the implementation must be measured against:

1. **Upgrade never deletes before it places.** The single most damaging ordering in the surveyed code is
   inverted, and at no point does the operator hold zero copies.
2. **Every irreversible file operation is preceded by a durable statement of intent**, and every
   interrupted operation has a defined recovery with a defined outcome — including "we could not undo
   this, here is exactly what is affected".
3. **Filesystem type is never matched by name.** Capabilities are probed; `zfs`, `xfs`, `apfs`, `overlay`
   and whatever ships next year all work without a code change.
4. **Reflink is treated honestly**: no `net10.0` API, three unrelated syscalls behind a platform-pack
   seam, and never reported as anything other than a copy.
5. **Root folders are media-agnostic** and carry per-kind configuration in an opaque binding, so the
   Lidarr/Readarr defaults and the Readarr Calibre backend fit without the core learning what they are.
6. **Remote path mapping declares its grammar and proposes its own fix.** The most frequent support issue
   in this software category becomes an eleven-row table and a one-click proposal.
7. **Nothing is added to `Arronix.Abstractions`.** The 0.2.0 `FileSystem/` surface was already the right
   size; this design is the implementation it was waiting for.
