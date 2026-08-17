# The Media-Agnostic Acquisition Pipeline — Design (v0.1.0)

> **Status:** Design. Nothing here is implemented. This document is the direct input to the Acquisition
> milestone, which follows the Unified Host Runtime milestone.
>
> **Scope:** the path from *"something is wanted"* to *"a file is in the library"*, media-agnostically:
> want-detection → search → release candidates → decision engine → grab → download tracking →
> completed-download handling → import decision → file placement → post-import. Plus the three ledgers the
> pipeline reads and writes (history, queue, blocklist), the pending/delay machine, and the
> failed-download → re-search loop.
>
> **Source material (read, not guessed):** the shipped decision engines and import pipelines of all four
> apps — `ArronixCore/src/NzbDrone.Core/{DecisionEngine,Download,MediaFiles/EpisodeImport,IndexerSearch,Queue,History,Blocklisting}/`
> (Sonarr) and the same folders in `_reference/{Radarr,Lidarr,Readarr}/src/NzbDrone.Core/`. **172 concrete
> specification classes** were read and classified; every count below is derived from that census and is
> reproducible from the file lists in §3.
>
> **Governing documents:** `docs/design/unified-host-runtime.md` (authoritative for the shape model, the
> plugin model, providers and the scheduler — this document *consumes* it and never restates it),
> `ARCHITECTURE.md` §5/§6/§11/§12, `docs/arronix-common-extraction-plan.md` (three-tier promotion rule),
> `docs/contracts/stability.md` (experimental gate).
>
> **Target contract area:** a new `ARX0018` — `Arronix.Abstractions.Acquisition`, at **0.4.0**. Four small
> amendments are needed in **0.3.0** (§10.1) because they are unreachable additively later.

---

## 1. Resolution table — the contested calls

Format follows `docs/arronix-common-extraction-plan.md`. Where this document contradicts a settled decision
in `unified-host-runtime.md`, the row says so in bold and the reason is given in full in the body.

| # | Question | Positions | Resolution | One-line reason |
|---|----------|-----------|------------|-----------------|
| 1 | Is there a plugin seam for specifications at all? | `unified-host-runtime.md` §1 row 9: **no** — "the specification set is host-owned". This doc: a fifth seam. | **Fifth seam — `IAcquisitionPolicy`**, gated by the **existing** `matching` capability, no new capability. **Contradicts row 9.** | Row 9 is right about the *framework* and wrong about the *residue*: 11 of 67 shipped specification roles (§3.5) read data no host declaration contains — `SeriesType == Anime`, a XEM scene origin, a Levenshtein distance threshold — and today their only escape hatch is `MatchOutcome.RejectionReason`, a nullable string with no access to the profile, the queue or the history. |
| 2 | Rejection reasons: enum, string, or record? | Sonarr/Radarr: a flat enum (74 / 62 members, both with media nouns baked in). Lidarr/Readarr: raw `string` (`Lidarr/…/DecisionEngine/Decision.cs:18-30`). | **A closed `RejectionCode` enum plus a `RejectionReason` record with typed slots** (`Source`, `Comparison`, `Threshold`, `Actual`, `Units`, `PluginCode`). | Radarr's enum spends **24 of its 62 members** on one 7-valued question asked against three data sources (§4.2); a string cannot be filtered, counted, localized or linked; the record collapses the 24 to `7 × 3` and survives a fifth data source without an enum edit. |
| 3 | Where do "is this an upgrade?" checks live? | Four apps: three near-identical specifications (`History`, `Queue`, `UpgradeDisk`) each re-deriving the comparison. | **One `UpgradeEvaluator` × a `HoldingsSource` enum**, called three times. | `HistorySpecification.cs:66-121` and its Queue/Disk siblings are the *same* switch over `UpgradeableRejectReason` with a different noun in the message; the only real difference is where "current quality" came from. |
| 4 | Does the spec runner short-circuit or collect every rejection? | Radarr/Sonarr grab side: run a priority band, stop after the first band that rejects (`Radarr/…/DownloadDecisionMaker.cs:174-191`). Import side: run everything, collect all (`ImportDecisionMaker.cs:111-114`). | **Cost-banded collect-all**: evaluate every specification in a band, stop before the next band if the band rejected permanently. | It is Radarr's own shape with the enum bug fixed — `SpecificationPriority {Default=0, Parsing=0, Database=0, Disk=1}` gives three names to the value `0`, so the "priority" is really a two-valued cost. "Why wasn't this grabbed?" needs *all* the cheap reasons, not the first. |
| 5 | Is the import decision per-file or per-batch? | S/R/K: per-file (`ImportDecisionMaker.cs:70-107`). L: per-**batch**, because identification is a set-assignment problem (`Lidarr/…/TrackImport/ImportDecisionMaker.cs:142-186`, `Identification/Munkres.cs`, 504 lines). | **Per-batch.** `ImportPlan` over a whole candidate folder; per-file decisions are a projection of it. | A per-file API cannot express "these 12 files are collectively this album release, and that assignment is only optimal as a whole"; a per-batch API expresses the per-file case for free. |
| 6 | Can the 0.1.0 `IImportPipeline` / `ImportDecision` carry this? | They were written before this pipeline was designed. | **No — so they are deleted, not bridged.** `IImportPlanner` / `ImportPlan` / `ImportVerdict` become the **only** import seam (§9). | `ImportDecision` has `MediaItemId TargetItemId` — **one** unit — so it cannot express a multi-episode file (S), and `EvaluateImportAsync(string filePath, …)` is per-file so it cannot express Lidarr. A contract that cannot express the domain is replaced, not wrapped: a bridge whose acceptance criterion is "refuses to truncate" is a data-loss notice with extra steps. Zero implementers — the only references outside `Arronix.Abstractions` are registry plumbing — so deletion is free today and never again. |
| 7 | "Already have it" when the file-bearing unit ≠ the acquisition unit | S/R/K compare one `QualityModel`. L compares a **`List<QualityModel>`** and rejects if the new release is worse than **any** of them (`Lidarr/…/UpgradableSpecification.cs:32-66`). | **`HoldingsSnapshot` + a declared `HoldingsAggregation {WorstOf, BestOf, Exact}`**, defaulting to `WorstOf`. | Lidarr's "downgrade if worse than any current quality" is the only correct rule when N files satisfy one acquisition unit, and it is invisible in the other three apps only because there N = 1. |
| 8 | Is "is it out yet?" a plugin predicate or a declaration? | S: `AirDateUtc + Series.Runtime <= now` (`Tv/EpisodeRepository.cs:209-225`). R: `Movie.IsAvailable(MinimumAvailability, delay)`. L/K: `ReleaseDate`. | **A declared `AvailabilityPolicy` on `MediaShape`**: the plugin projects an `AvailableFrom` instant per item; the host applies a global offset. | Four apps, four expressions of one predicate that the core needs in **five** places (want-detection, `Availability`, `FullSeason`, `Discography`, `SeasonPackOnly`); a per-call plugin round-trip in the want-detection query is a non-starter, a projected field is a join. |
| 9 | Do "full season pack" and "discography" need two specs? | S has `FullSeasonSpecification`; L and K have `DiscographySpecification`. | **One core spec — `PackFullyAvailable`.** | Their bodies are the same sentence: *"if the release covers a scope wider than one unit, every unit it covers must already be available"* (`FullSeasonSpecification.cs:23-31` vs `Lidarr/…/DiscographySpecification.cs:24-33`). Two names, one rule. |
| 10 | Grab idempotency | None of the four apps has any. `DownloadService.DownloadReport` (`Download/DownloadService.cs:66-130`) sends and hopes. | **A `GrabIntent` with a deterministic idempotency key and a claim row**, written before the download-client call and reconciled after. | The surveyed failure — two RSS syncs racing, both grabbing the same release — is prevented today only by `IsEpisodeProcessed` *within one batch* (`ProcessDownloadDecisions.cs:56`); across batches or across a restart there is nothing. |
| 11 | Duplicate completion handling | Sonarr double-checks history and treats "already imported previously" as success (`CompletedDownloadService.cs:227-267`), with an explicit comment about the edge case. | **Make it an invariant, not a rescue**: import is a compare-and-swap on `UnitFileLink`, so a second completion is a no-op that still reports `Imported`. | Sonarr's own comment ("EDGE CASE: This process relies on EpisodeIds being consistent between executions…") is a bug report against the design; a CAS on the link removes the class. |
| 12 | Pending-release reasons | S/R/L/K: `Delay`, `DownloadClientUnavailable`, `Fallback` (`Download/Pending/PendingReleaseReason.cs:3-8`). | **Keep exactly those three.** | Byte-identical in four apps, four independent maintainer groups, a decade of use; inventing a fourth on zero evidence is the smell the three-tier rule exists to stop. |
| 13 | Blocklist identity | Title + size + published-date + indexer, or torrent info-hash (`Blocklisting/BlocklistService.cs:37-61,110-160`). | **A structured `BlocklistKey` tagged union** (`InfoHash` \| `TitleAndSize`), scoped to a `MediaItemRef` **library entry**, never global. | The two identities are genuinely different (a hash is exact; a title needs three tolerances) and the current code discovers that with `if (torrentInfo != null)` at the call site. |
| 14 | Failed → re-search scope | Sonarr escalates by counting episodes and comparing to the season (`RedownloadFailedDownloadService.cs:52-79`). | **Generalized over `AcquisitionScope`**: re-search at the *narrowest* scope that covers the failed unit set. | The rule is "one unit → unit search; the whole span → span search; otherwise unit search", which is `AcquisitionScope {Single, SequenceSpan, Ancestor}` written in TV. |
| 15 | Is interactive search a different pipeline? | All four: the same `DownloadDecisionMaker` with `SearchCriteria.InteractiveSearch = true`, then the UI shows rejections. | **Same pipeline, different terminal action.** `SearchOrigin.Interactive` runs every specification and **never auto-grabs**; the result is a `WorkbenchProposal` (`unified-host-runtime.md` §6.5, `WorkbenchSubject.ReleaseCandidates`). | The escape hatch the milestone already designed is exactly the right renderer for "here are 40 releases and why 37 were rejected"; a second code path would drift. |
| 16 | Custom formats: core or plugin? | S/R: a scoring engine over release title, size, language, quality, indexer flags, resolution, source — plus, in Sonarr only, `SeriesType` and `ReleaseType` matchers. | **Core**, over a declared field vocabulary; a plugin contributes matchable values through `FieldDescriptor`/`FieldValue`, never a matcher type. | Everything the engine matches on is already a `FieldValueKind`; Sonarr's two media-typed matchers become "match field `series-type` = `anime`", which is data. |
| 17 | Where does sample detection live? | Grab: title contains "sample" and size < 70 MB (`NotSampleSpecification.cs:21`). Import: media-info runtime vs expected runtime (`DetectSample.cs`). | **Core rule + a declared expected-duration field.** | The import-side rule needs "how long should this be?", which is `FieldValueKind.Duration` on the unit — the same value `AcceptableSizeSpecification` already needs (`AcceptableSizeSpecification.cs:41-75` sums `Episode.Runtime`; Lidarr sums `AlbumRelease.Duration/1000`). |
| 18 | Does the pipeline get its own scheduler? | — | **No.** Every stage is an `IScheduledJob` on the one host scheduler, using the schedule grammar and throttle keys already defined (`unified-host-runtime.md` §4.7). | A second scheduler is a second back-off domain, a second concurrency budget and a second place to look when nothing runs. |
| 19 | Retry/back-off for pipeline work | Four apps run two independent domains: scheduler back-off and `ProviderStatusBase` escalation. | **Keep both, unchanged**, and add **no third**. Pipeline-level retry is expressed as `NotBefore` on a queue entry. | The milestone already specifies the ladder (`0,1m,5m,15m,30m,1h,3h,6h,12h,24h` with full jitter) and `ProviderStatusStore`; the pipeline has no failure mode those two do not already cover. |
| 20 | Rejection persistence | S/R/L/K: `RejectionType {Permanent, Temporary}` (`DecisionEngine/RejectionType.cs`). | **Keep, renamed `RejectionPersistence`**, and make it the *only* thing that routes a release to Pending. | It is already the routing signal (`ProcessDownloadDecisions.cs:61-65` branches on `TemporarilyRejected`); naming it after what it decides makes that legible. |
| 21 | Does the core own release *prioritization*? | S: 9 comparators, 2 media-specific. R: 8, 0. L: 8, 1. K: 8, 1. | **Core owns the chain; the plugin contributes at most one comparator via `IAcquisitionPolicy.CompareAsync`.** | 7 comparators are identical in all four (`DownloadDecisionComparer.cs:30-41` × 4); the divergence is exactly `CompareEpisodeCount`/`CompareEpisodeNumber` (S), `CompareAlbumCount` (L), `CompareBookCount` (K) — one "prefer the release covering fewer/more units" rule that only the plugin can order. |
| 22 | Import "manual" path | All four ship a `Manual/` folder duplicating the decision maker. | **No second path.** Manual import is `IImportPlanner.PlanAsync` with `ImportTrigger.Manual`, surfaced through `WorkbenchDescriptor("manual-import")`. | The milestone already pays for the workbench; a duplicated decision maker is how the four apps got two subtly different definitions of "approved". |

---

## 2. The pipeline

### 2.1 Stage map

Eleven stages. Each row names the contract, who owns it, and what "failure" means there — because a stage
whose failure mode is undefined is a stage that will silently swallow work.

| # | Stage | Input → Output | Owner | Failure modes |
|---|---|---|---|---|
| 1 | **Want detection** | `WantQuery` → `WantedSet` | Host (`IWantDetector`) | Empty want-set (valid); shape has no `CompletenessUnit` (config defect, health check); availability field absent → unit treated as *not yet wanted*, never as wanted |
| 2 | **Query planning** | `AcquisitionRequest` → `ReleaseQueryPlan` | **Plugin** `IReleaseQueryPlanner` | Plugin throws → kind quarantined for this run, `RejectionCode.PlanFailed`; empty plan → stage 3 skipped, recorded as `SearchOutcome.NoQueries` |
| 3 | **Search dispatch** | `ReleaseQueryPlan` → `ReleaseCandidate[]` | Host (`IReleaseSearchDispatcher`) | Indexer ineligible (set intersection, §5.4 of the milestone) → not dispatched; indexer 429 → `ProviderStatusStore` back-off, partial result flagged; **all** indexers failed → `SearchOutcome.AllSourcesFailed`, no grab, retry via `NotBefore` |
| 4 | **Matching** | `ReleaseCandidate` → `MatchOutcome` | **Plugin** `IReleaseMatcher` | No units → `RejectionCode.NoMatch`; `MatchConfidence.Low` on a non-interactive origin → `RejectionCode.MatchTooWeak`; plugin throws → `RejectionCode.MatcherFailed` (release kept, marked, never grabbed) |
| 5 | **Decision** | `ReleaseSubject` → `GrabDecision` | Host (`IGrabDecisionEngine`) + plugin residue (`IAcquisitionPolicy`) | Any permanent rejection → not grabbed, reasons persisted; any temporary rejection with no permanent one → **Pending**; specification throws → `RejectionCode.EvaluationError` attributed to the specification id |
| 6 | **Prioritization** | `GrabDecision[]` → ordered | Host (`IGrabPriorityComparer`) | Total order is guaranteed (final tiebreak on `ReleaseId` ordinal) — no nondeterminism |
| 7 | **Grab** | `GrabIntent` → `GrabReceipt` | Host (`IGrabExecutor`) | Client unavailable → Pending(`DownloadClientUnavailable`) **and** the protocol is marked failed for the rest of the batch (`ProcessDownloadDecisions.cs:96-110`); client rejects as duplicate → treated as **success**, receipt reconciled; fetch 404 → blocklist-eligible, `RejectionCode.ReleaseUnavailable` |
| 8 | **Tracking** | client items → `TrackedAcquisition[]` | Host (`IAcquisitionTracker`) | Item disappeared before completion → `Failed` if grabbed by us, ignored otherwise; item present but untracked (grabbed elsewhere) → tracked read-only, never imported unless the category matches |
| 9 | **Completed-download handling** | `TrackedAcquisition` → `ImportRequest` | Host (`ICompletedAcquisitionService`) | Path not visible/not writable → `ImportBlocked` + health check; no grab history **and** no category → skipped (never guess); library entry unresolvable → `ImportBlocked`, manual interaction required |
| 10 | **Import planning + decision** | `ImportRequest` → `ImportPlan` → `ImportVerdict[]` | **Plugin** `IImportPlanner` (proposal) + host (`IImportDecisionEngine`) | Planner throws → `ImportRejectionCode.PlanFailed`, whole batch blocked (never partially imported); zero eligible files → `ImportOutcome.NothingEligible`; some units unimported → `ImportBlocked`, `ManualInteractionRequired` notification |
| 11 | **Placement + post-import** | `ImportVerdict[]` → `ImportResult[]` | Host (`IFilePlacementService`) | Destination exists → `DestinationExistsException`, verdict fails, **batch continues** (per-file); transfer fails mid-batch → already-placed files stay linked, the rest are re-planned next pass; post-import mutation fails → warning, never rolls back the placement |

**Two rules bind the whole map.**

1. **No stage mutates library state except stage 11.** Everything before it produces values. That is what
   makes stages 1–10 freely re-runnable, which is what makes the whole pipeline idempotent (§8).
2. **Every rejection is persisted with the subject that caused it.** The four apps discard grab rejections
   the moment the batch ends — which is precisely why *"why wasn't this grabbed?"* is the #1 support
   question. §4.4 says where they go instead.

### 2.2 The three ledgers

The pipeline reads and writes three append-mostly ledgers. All three are **Tier B, host-side** — zero plugin
implementers, and every one of them is a Storage-Layer concern under `ARCHITECTURE.md` §5.

```csharp
// src/Arronix.Host/Acquisition/IAcquisitionHistory.cs   (Tier B)
public interface IAcquisitionHistory
{
    ValueTask RecordAsync(AcquisitionEvent @event, CancellationToken ct);
    ValueTask<AcquisitionEvent?> MostRecentForUnitAsync(MediaItemRef unit, CancellationToken ct);
    ValueTask<IReadOnlyList<AcquisitionEvent>> ByDownloadIdAsync(string downloadId, CancellationToken ct);
    ValueTask<IReadOnlyDictionary<MediaItemRef, AcquisitionEvent>> MostRecentForUnitsAsync(
        IReadOnlyList<MediaItemRef> units, CancellationToken ct);
}

/// <remarks>
/// Generalizes <c>EpisodeHistory</c> (<c>History/EpisodeHistory.cs:24-46</c>) and its three siblings. The
/// per-app <c>SeriesFolderImported</c> / <c>DownloadFolderImported</c> split collapses: the *source* of an
/// import is <see cref="ImportTrigger"/>, not a separate event type. The six string consts Sonarr keeps on
/// the class (<c>DOWNLOAD_CLIENT</c>, <c>RELEASE_GROUP</c>, <c>SIZE</c>, …) become typed slots; the open
/// <see cref="Context"/> bag is plugin-owned and opaque to the host, exactly as
/// <c>NotificationMessage.Context</c> is.
/// </remarks>
public sealed record AcquisitionEvent
{
    public required Guid EventId { get; init; }
    public required DateTimeOffset OccurredAt { get; init; }
    public required AcquisitionEventKind Kind { get; init; }
    public required MediaKindId MediaKind { get; init; }
    public required IReadOnlyList<MediaItemRef> Units { get; init; }
    public required string CorrelationId { get; init; }
    public string? SourceTitle { get; init; }
    public ReleaseId? Release { get; init; }
    public QualityTier? Quality { get; init; }
    public IReadOnlyList<Language> Languages { get; init; } = [];
    public string? DownloadId { get; init; }
    public ProviderId? Indexer { get; init; }
    public ProviderId? DownloadClient { get; init; }
    public DownloadProtocol? Protocol { get; init; }
    public SearchOrigin? Origin { get; init; }
    public long? Size { get; init; }
    public int CustomFormatScore { get; init; }
    public MediaFileId? File { get; init; }
    public string? Message { get; init; }
    public IReadOnlyDictionary<string, string> Context { get; init; } = new Dictionary<string, string>();
}

public enum AcquisitionEventKind
{
    Grabbed = 0, GrabFailed = 1, Imported = 2, ImportFailed = 3,
    DownloadFailed = 4, DownloadIgnored = 5, FileDeleted = 6, FileRenamed = 7, Blocklisted = 8,
}
```

```csharp
// src/Arronix.Host/Acquisition/IAcquisitionQueue.cs   (Tier B)
/// <remarks>
/// The queue is NOT a table. It is a projection of <see cref="IAcquisitionTracker"/> joined to the grab
/// history — which is what all four apps already do (<c>Queue/QueueService.cs</c> builds rows from tracked
/// downloads on every read). Modeling it as a projection is what makes "the queue" and "what the download
/// client actually has" incapable of disagreeing.
/// </remarks>
public interface IAcquisitionQueue
{
    ValueTask<IReadOnlyList<QueueEntry>> SnapshotAsync(CancellationToken ct);
    ValueTask<IReadOnlyList<QueueEntry>> ForUnitsAsync(IReadOnlyList<MediaItemRef> units, CancellationToken ct);
}

public sealed record QueueEntry
{
    public required string DownloadId { get; init; }
    public required MediaKindId MediaKind { get; init; }
    public required IReadOnlyList<MediaItemRef> Units { get; init; }
    public required string Title { get; init; }
    public required DownloadItemStatus Status { get; init; }        // the milestone's stable-adjacent enum
    public required TrackedState State { get; init; }
    public QualityTier? Quality { get; init; }
    public IReadOnlyList<Language> Languages { get; init; } = [];
    public long TotalSize { get; init; }
    public long RemainingSize { get; init; }
    public TimeSpan? RemainingTime { get; init; }
    public DownloadProtocol Protocol { get; init; }
    public ProviderId? DownloadClient { get; init; }
    public ProviderId? Indexer { get; init; }
    public IReadOnlyList<StatusMessage> Messages { get; init; } = [];
    public string? OutputPath { get; init; }
}
```

```csharp
// src/Arronix.Host/Acquisition/IBlocklist.cs   (Tier B)
public interface IBlocklist
{
    ValueTask BlockAsync(BlocklistEntry entry, CancellationToken ct);
    ValueTask<bool> IsBlockedAsync(MediaItemRef libraryEntry, ReleaseIdentity identity, CancellationToken ct);
    ValueTask ClearAsync(MediaItemRef libraryEntry, CancellationToken ct);
}

/// <remarks>
/// A tagged union, per the milestone's rule #6 (no <c>[JsonPolymorphic]</c>). The two identities are not
/// interchangeable: <c>InfoHash</c> is exact; <c>TitleAndSize</c> needs a size tolerance and a published-date
/// tolerance, which is what <c>BlocklistService.SameNzb</c> (<c>Blocklisting/BlocklistService.cs:110-160</c>)
/// hand-rolls at the call site behind an <c>if (torrentInfo != null)</c>.
/// </remarks>
public readonly record struct ReleaseIdentity
{
    public ReleaseIdentityKind Kind { get; init; }
    public string? InfoHash { get; init; }
    public string? Title { get; init; }
    public long? Size { get; init; }
    public DateTimeOffset? PublishedAt { get; init; }
    public ProviderId? Indexer { get; init; }

    public static ReleaseIdentity OfInfoHash(string hash, ProviderId? indexer = null);
    public static ReleaseIdentity OfTitle(string title, long size, DateTimeOffset publishedAt, ProviderId? indexer);
}

public enum ReleaseIdentityKind { InfoHash = 0, TitleAndSize = 1 }
```

**Blocklisting is scoped to the library entry, never global.** All four apps already key on
`(seriesId | movieId | artistId | authorId, …)`; a unified host with one database makes a global blocklist
actively dangerous, because a title that is junk for one library entry is not junk for another.

---

## 3. The specification framework — and the full inventory

### 3.1 The generic shape

All four apps converged on the same two-line idea and then wrote it four times with four different result
types (`DownloadSpecDecision`, `ImportSpecDecision`, `Decision`, `ImportDecision<T>`). One shape:

```csharp
// src/Arronix.Host/Acquisition/Specifications/ISpecification.cs   (Tier B)
public interface ISpecification<in TSubject>
{
    /// <summary>Stable, kebab-case, host-globally unique. Appears in the rejection and in telemetry.</summary>
    string SpecificationId { get; }

    /// <summary>Determines when it runs. See <see cref="SpecificationCost"/>.</summary>
    SpecificationCost Cost { get; }

    /// <summary>Whether a rejection from this specification can become true later. Routes to Pending.</summary>
    RejectionPersistence Persistence { get; }

    ValueTask<SpecVerdict> EvaluateAsync(TSubject subject, CancellationToken ct);
}

/// <remarks>
/// Replaces <c>SpecificationPriority {Default=0, Parsing=0, Database=0, Disk=1}</c>
/// (<c>DecisionEngine/SpecificationPriority.cs</c>) — three names for the value 0 and one for 1, which is a
/// two-valued cost pretending to be a four-valued priority. Bands run in order; a band that produces a
/// PERMANENT rejection stops the next band from running at all. Within a band, everything runs and every
/// rejection is collected, because the user asked "why", not "which one first".
/// </remarks>
public enum SpecificationCost
{
    /// <summary>Pure function of the subject. Size, age, protocol, title terms.</summary>
    Local = 0,
    /// <summary>Reads host state: profile, monitoring, history, queue, blocklist, links.</summary>
    Stored = 1,
    /// <summary>Touches the filesystem. Free space, sample probe, unpacking check.</summary>
    Disk = 2,
    /// <summary>Touches the network or a plugin. Runs last, and only for otherwise-accepted subjects.</summary>
    Remote = 3,
}

public enum RejectionPersistence { Permanent = 0, Temporary = 1 }

public sealed record SpecVerdict
{
    public static SpecVerdict Accept { get; }
    public bool IsAccepted { get; }
    public RejectionReason? Rejection { get; }
    public static SpecVerdict Reject(RejectionReason reason);
}
```

Two subject types, both host-owned records:

```csharp
/// <summary>Everything a grab-time specification may read. Deliberately a closed record: a specification
/// that needs a service is a specification that cannot be unit-tested or replayed.</summary>
public sealed record ReleaseSubject
{
    public required MediaKindId MediaKind { get; init; }
    public required ReleaseCandidate Release { get; init; }         // STABLE 0.1.0 DTO
    public required ValidatedShape Shape { get; init; }
    public required MatchOutcome Match { get; init; }
    public required IReadOnlyList<MediaItemRef> Units { get; init; }
    public required MediaItemRef LibraryEntry { get; init; }
    public required AcquisitionProfile Profile { get; init; }       // quality ladder + custom formats + facets
    public required HoldingsSnapshot Holdings { get; init; }        // §5.2 — history + queue + disk, merged
    public required SearchContext Search { get; init; }             // origin, requested units, requested scope
    public required ProviderId? Indexer { get; init; }
    public required DownloadProtocol Protocol { get; init; }
    public QualityTier? Quality { get; init; }
    public int CustomFormatScore { get; init; }
    public IReadOnlyList<string> IndexerFlags { get; init; } = [];
    public TimeSpan? ExpectedDuration { get; init; }                // summed over Units, from the declared field
    public DateTimeOffset? UnitsAvailableFrom { get; init; }        // max over Units, from AvailabilityPolicy
}

/// <summary>Everything an import-time specification may read. One entry of an <see cref="ImportPlan"/>.</summary>
public sealed record ImportSubject
{
    public required MediaKindId MediaKind { get; init; }
    public required ImportPlan Plan { get; init; }                  // the whole batch — MoreTracks needs it
    public required ImportCandidate Candidate { get; init; }        // this file + its proposed links
    public required ValidatedShape Shape { get; init; }
    public required AcquisitionProfile Profile { get; init; }
    public required HoldingsSnapshot Holdings { get; init; }
    public required ImportTrigger Trigger { get; init; }
    public GrabReceipt? Grab { get; init; }                         // null for a folder scan
    public bool IsExistingLibraryFile { get; init; }
}

public enum ImportTrigger { CompletedDownload = 0, FolderScan = 1, Manual = 2, ReplacementRescan = 3 }
```

### 3.2 Grab-time specification inventory

**Census.** Concrete classes implementing `IDownloadDecisionEngineSpecification` / `IDecisionEngineSpecification`
across `DecisionEngine/Specifications/{,RssSync,Search}/`: **Sonarr 37, Radarr 29, Lidarr 32, Readarr 30 =
128 classes**. Collapsing pure media-noun renames (`MonitoredEpisode`/`MonitoredMovie`/`MonitoredAlbum`/
`MonitoredBook` are one role) yields **43 distinct roles**.

Legend — **Core**: the host owns it outright. **Core†**: the host owns it but needs a declaration that does
not exist in the 0.3.0 shape (§10.1 lists all four). **Plugin**: no host declaration can express it.

| # | Role | S | R | L | K | Verdict | How the core does it / why it cannot |
|---|---|:-:|:-:|:-:|:-:|---|---|
| 1 | Size within profile | ● | ● | ● | ● | **Core†** | per-tier MB-per-minute × `ExpectedDuration` summed over `Units`; needs the declared duration field (`AcceptableSizeSpecification.cs:41-108`; Lidarr sums `Duration/1000`) |
| 2 | Maximum size | ● | ● | ● | ● | Core | global byte cap |
| 3 | Minimum age | ● | ● | ● | ● | Core | usenet only; `Release.PublishDate` |
| 4 | Retention | ● | ● | ● | ● | Core | usenet only |
| 5 | Protocol allowed | ● | ● | ● | ● | Core | delay profile's enabled protocols |
| 6 | Quality allowed by profile | ● | ● | ● | ● | Core | `AcquisitionProfile.Ladder` membership |
| 7 | Custom-format minimum score | ● | ● | ● | ● | Core | `CustomFormatScore >= profile.MinScore` |
| 8 | Blocklisted | ● | ● | ● | ● | Core | `IBlocklist.IsBlockedAsync` |
| 9 | Blocked indexer | ● | ● | ● | ● | Core | `ProviderStatusStore` |
| 10 | Indexer tag match | ● | ● | ● | ● | Core | `ProviderDefinition.Tags` ∩ `LibraryFacet.Tags` |
| 11 | Required indexer flags | | ● | | | Core | `IndexerFlags` ⊇ required set |
| 12 | Torrent seeding requirement | ● | ● | ● | ● | Core | min seeders / seed time |
| 13 | Not a sample | ● | ● | ● | ● | Core | title contains "sample" ∧ size < threshold (`NotSampleSpecification.cs:21`) |
| 14 | Raw disk image | ● | ● | ● | ● | Core | extension deny-list |
| 15 | Release restrictions | ● | ● | ● | ● | Core | required/forbidden term lists |
| 16 | Repack group match | ● | ● | ● | ● | Core | repack must come from the same release group |
| 17 | Proper/repack preference | ● | ● | ● | ● | Core | revision comparison + global preference |
| 18 | Already imported | ● | ● | ● | ● | Core | history: same hash or same title already imported |
| 19 | Free space | ● | ● | ● | | Core | `IStorageMount` |
| 20 | Monitored | ● | ● | ● | ● | Core | `LibraryFacet.Monitor` over `MonitorDimension`s |
| 21 | Library-entry match | ● | ● | ● | ● | Core | `Match.Units[*].Parent == Search.LibraryEntry` (`SeriesSpecification`, `MovieSpecification`, `ArtistSpecification`, `AuthorSpecification` — one role, four nouns) |
| 22 | Requested unit intersects | ● | | ● | ● | Core | `Match.Units ∩ Search.RequestedUnits ≠ ∅` |
| 23 | Search-arity match | ● | | ● | ● | Core | a single-unit search must not accept a pack (`SingleEpisodeSearchMatch` ×3) |
| 24 | Search-scope coordinate match | ● | | | | Core | searched span component == release span component (`Search/SeasonMatchSpecification.cs:36`), pure `Coordinate` comparison |
| 25 | Upgrade vs history | ● | ● | ● | ● | Core | `UpgradeEvaluator(HoldingsSource.History)` (§5) |
| 26 | Upgrade vs queue | ● | ● | ● | ● | Core | `UpgradeEvaluator(HoldingsSource.Queue)` |
| 27 | Upgrade vs disk | ● | ● | ● | ● | Core | `UpgradeEvaluator(HoldingsSource.Disk)` |
| 28 | Upgrades allowed | ● | ● | ● | ● | Core | `profile.UpgradeAllowed` |
| 29 | Cutoff met | | | ● | ● | Core | L/K's separate `CutoffSpecification`; S/R fold it into the upgrade evaluator — same rule |
| 30 | Delay profile | ● | ● | ● | ● | Core | with all four bypasses (`RssSync/DelaySpecification.cs:36-110`) |
| 31 | Pending | ● | | | | Core | a better release is already pending for these units |
| 32 | Deleted-file grace | ● | | ● | ● | Core | do not re-grab a file the user just deleted |
| 33 | Same units as an existing file | ● | | ● | | Core | pure `UnitFileLink` set algebra (`SameEpisodesSpecification.cs:19-32` is `LinksForUnit`/`LinksForFile` written in TV) |
| 34 | Span-constraint violation | ● | | | | Core | `FileBinding.SpanConstraints` — the shape already declares `(aired, season, MustNotSpan)`; `MultiSeasonSpecification.cs:20-24` becomes a validator |
| 35 | Not yet available | | ● | | | **Core†** | `UnitsAvailableFrom > now - delay`; needs `AvailabilityPolicy` |
| 36 | Pack fully available | ● | | ● | ● | **Core†** | one rule, two names: `FullSeasonSpecification.cs:23-31` ≡ `DiscographySpecification.cs:24-33`; needs `AvailabilityPolicy` |
| 37 | Early release | | | ● | ● | Core | per-indexer earliest-grab window |
| 38 | Language wanted | | ● | | | Core | `Language` is a stable DTO |
| 39 | Hardcoded subtitles | | ● | | | Core | a release flag |
| 40 | Anime version upgrade | ● | | | | **Plugin** | branches on `Series.SeriesType == Anime` and on an anime-specific "v2/v3" release convention (`AnimeVersionUpgradeSpecification.cs:31-49`) |
| 41 | Scene-mapping origin | ● | | | | **Plugin** | reads a XEM `SceneOrigin` string with values `mixed`/`tvdb`/`unknown` and a per-mapping comment |
| 42 | Season-pack-only | ● | | | | **Plugin** | 80% core (availability of sibling units + an indexer setting), but gated on `SeriesType == Standard` |
| 43 | Split release | ● | | | | **Plugin** | "this release is half a unit" — the core has no sub-unit concept and correctly should not gain one |

**Grab-time totals: 39 core (36 outright, 3 needing a declaration) / 4 plugin — 90.7% core.**

### 3.3 Import-time specification inventory

**Census.** `MediaFiles/{EpisodeImport,MovieImport,TrackImport,BookImport}/Specifications/`: **Sonarr 14,
Radarr 10, Lidarr 12, Readarr 8 = 44 classes → 24 distinct roles.**

| # | Role | S | R | L | K | Verdict | How the core does it / why it cannot |
|---|---|:-:|:-:|:-:|:-:|---|---|
| 1 | Already imported | ● | ● | ● | ● | Core | history by download id |
| 2 | Free space | ● | ● | ● | ● | Core | `IStorageMount` |
| 3 | Not unpacking | ● | ● | ● | ● | Core | `_rar`/`_unpack_` folder + mtime window |
| 4 | Upgrade over existing | ● | ● | ● | ● | Core | `UpgradeEvaluator(HoldingsSource.Disk)` |
| 5 | Not a sample | ● | ● | | | **Core†** | measured duration ≪ `ExpectedDuration`; needs the declared duration field |
| 6 | Matches folder | ● | ● | | | Core | file-level match must not contradict folder-level match |
| 7 | Matches grab | ● | ● | | | Core | imported units ⊆ grabbed units |
| 8 | Has an audio track | ● | ● | | | **Plugin** | a container probe meaningful only for one format family |
| 9 | Grabbed-release quality | | ● | | | Core | file quality must not contradict the grab |
| 10 | Not multi-part | | ● | | | Core | `FileBinding.OrdinalIsMeaningful == false` ⇒ two files claiming one unit is a defect. Readarr *wants* multi-part and declares `true`; one declaration, opposite behaviors, no branch |
| 11 | Path inside a root folder | | | ● | ● | Core | `LibraryFacet.RootFolderPath` containment |
| 12 | Variant/aggregate upgrade | | | ● | ● | Core | `HoldingsAggregation.WorstOf` over the acquisition unit (§5.3) |
| 13 | Close enough to the catalog record | | | ● | ● | **Plugin** | a distance threshold over a plugin-defined metric (`Identification/Distance.cs`, 186 lines) |
| 14 | Close enough per sub-unit | | | ● | | **Plugin** | same, per track |
| 15 | Covers at least as many units | | | ● | | Core | do not switch the selected variant to one covering fewer units (`MoreTracksSpecification.cs:20-27`) — `VariantSelection` + `UnitFileLink` counting |
| 16 | No missing or unmatched units | | | ● | | Core | the plan must cover the variant's units with nothing left over |
| 17 | Variant is wanted | | | ● | | Core | `LibraryFacet.SelectedVariant` / `AutoSwitchVariant` |
| 18 | Same units as existing file | ● | | ● | | Core | `UnitFileLink` set algebra |
| 19 | Same file already linked | | | | ● | Core | path + size identity |
| 20 | Required coordinate present | ● | | | | **Plugin** | "anime series require an absolute number" — a per-item coordinate requirement |
| 21 | Title present and not TBA | ● | | | | **Plugin** | a naming-token precondition specific to episode titles |
| 22 | File parsed as a whole span | ● | | | | **Plugin** | a parser-flag artifact (`FileEpisodeInfo.FullSeason`) |
| 23 | Split file | ● | | | | **Plugin** | as row 43 above |
| 24 | Unverified coordinate mapping | ● | | | | Core | `CoordinateConfidence.Unverified` on any reading in the canonical space — the shape already declares `MayBeUnverified` |

**Import-time totals: 17 core / 7 plugin — 70.8% core.**

### 3.4 The headline split

| Measure | Media-agnostic (core) | Plugin-supplied | Core share |
|---|---:|---:|---:|
| **Distinct roles, grab** | 39 | 4 | **90.7%** |
| **Distinct roles, import** | 17 | 7 | **70.8%** |
| **Distinct roles, total** | **56** | **11** | **83.6%** |
| **Concrete classes shipped across the four apps** | **159** | **13** | **92.4%** |
| Classes by app (S / R / L / K) | 42 / 38 / 42 / 37 | 9 / 1 / 2 / 1 | — |

Arithmetic, so it can be checked: 128 grab classes + 44 import classes = 172; per app 51 / 39 / 44 / 38.

The two numbers say different things and both matter. **83.6%** is how much of the *design* the core owns.
**92.4%** is how much of the *shipped code* it owns — higher, because the plugin-specific rules cluster in
one app. **Sonarr alone accounts for 8 of the 11 plugin roles and participates in 9**; the remaining two are
Lidarr's and Readarr's fuzzy-matching distance thresholds.

**Read the plugin column carefully: it is small, it is real, and it is not shrinkable.** Four of the eleven
are anime and XEM; two are fuzzy-matching distance; the rest are parser artifacts. No amount of declaration
design removes them, because each reads a value only the plugin knows how to compute. That is the whole
justification for resolution row 1.

### 3.5 The residue seam

```csharp
// src/Arronix.Abstractions/Acquisition/IAcquisitionPolicy.cs   (ARX0018, 0.4.0)
/// <remarks>
/// The FIFTH behavior seam, and the only one this document adds. It exists for exactly the 11 rows marked
/// Plugin in §3.2/§3.3. It is gated by the EXISTING <c>Capability.Matching</c> — the same capability that
/// gates <see cref="IReleaseMatcher"/> — because a plugin that may decide *what a release is* is already
/// trusted to decide *whether that release is acceptable to its own media semantics*. No new capability,
/// no widening of the matrix.
/// <para><b>What it may not do.</b> It cannot accept a release the core rejected: the host runs it and
/// unions its rejections with its own. A policy that returns Accept for everything is exactly equivalent to
/// not registering one. This is a veto, never a grant — which is what keeps §11's least-privilege story
/// intact and what makes "the core owns the decision engine" still true.</para>
/// </remarks>
public interface IAcquisitionPolicy
{
    MediaKindId MediaKind { get; }

    /// <summary>Additional grab-time vetoes. Called once per candidate, in the <c>Remote</c> cost band.</summary>
    Task<PolicyOutcome> EvaluateReleaseAsync(PolicyReleaseSubject subject, CancellationToken ct = default);

    /// <summary>Additional import-time vetoes. Called once per candidate file within a plan.</summary>
    Task<PolicyOutcome> EvaluateImportAsync(PolicyImportSubject subject, CancellationToken ct = default);

    /// <summary>The single per-kind tiebreak the four apps each hand-rolled: <c>CompareEpisodeCount</c> /
    /// <c>CompareAlbumCount</c> / <c>CompareBookCount</c>. Applied AFTER every core comparator, so it can
    /// never override quality, custom-format score, protocol or indexer priority. Return 0 to abstain.</summary>
    int Compare(PolicyReleaseSubject left, PolicyReleaseSubject right);
}

public sealed record PolicyOutcome
{
    public static PolicyOutcome Accept { get; }
    public required IReadOnlyList<PolicyRejection> Rejections { get; init; }
}

/// <summary>A plugin's rejection. <see cref="Code"/> is a plugin-owned, kebab-case, stable identifier that
/// the host carries, groups by and reports — and never interprets. <see cref="Message"/> is the user-facing
/// sentence, already in the host's configured locale.</summary>
public sealed record PolicyRejection
{
    public required string Code { get; init; }                  // "anime-version-not-upgrade"
    public required string Message { get; init; }
    public RejectionPersistence Persistence { get; init; } = RejectionPersistence.Permanent;
    public IReadOnlyDictionary<string, string> Detail { get; init; } = new Dictionary<string, string>();
}
```

`PolicyReleaseSubject` / `PolicyImportSubject` are **narrowed projections** of the host's own subjects: they
carry the release, the matched units, the candidate path, the shape and the plugin's own `Context` bag — and
deliberately **not** the profile, the holdings snapshot or the queue. A plugin that wants to re-implement
the upgrade rules should not be able to.

**The cheaper alternative, and why it loses.** `MatchOutcome.RejectionReason` already exists (a nullable
`string` on the stable-adjacent `IReleaseMatcher` result). It could carry all 11 rules. It loses on three
counts: it fires at match time, so it cannot see the profile or the queue; it is one string, so a release
rejected for two reasons reports one; and it is unstructured, so `RejectionCode` filtering (§4) would have a
permanent hole in it. Recorded as considered and rejected, not overlooked.

---

## 4. Rejection reasons as first-class data

### 4.1 The type

```csharp
// src/Arronix.Abstractions/Acquisition/RejectionReason.cs   (ARX0018 — it crosses to the client)
public sealed record RejectionReason
{
    /// <summary>Closed vocabulary. The client switches on it to choose an icon, a grouping and a help link.</summary>
    public required RejectionCode Code { get; init; }

    /// <summary>The specification that produced it. Stable, kebab-case, host-globally unique.</summary>
    public required string SpecificationId { get; init; }

    public required RejectionPersistence Persistence { get; init; }

    /// <summary>Which body of evidence the comparison was made against. Absent for non-comparative codes.</summary>
    public HoldingsSource? Source { get; init; }

    /// <summary>The upgrade sub-reason, when <see cref="Code"/> is <c>NotAnUpgrade</c>. This plus
    /// <see cref="Source"/> is the pair that collapses 24 of Radarr's 62 enum members.</summary>
    public UpgradeVerdict? Upgrade { get; init; }

    /// <summary>Typed operands, so the client can format "1.2 GB" and "45 min" itself and a CLI can align
    /// columns. Populated for every threshold code.</summary>
    public FieldValue? Threshold { get; init; }
    public FieldValue? Actual { get; init; }

    /// <summary>Units the rejection is about, when it is about some and not all of them.</summary>
    public IReadOnlyList<MediaItemRef> Units { get; init; } = [];

    /// <summary>Set iff <see cref="Code"/> is <c>PluginPolicy</c>. Plugin-owned, opaque to the host.</summary>
    public string? PluginCode { get; init; }

    /// <summary>A rendered sentence in the host's locale. NEVER the primary representation — a fallback for
    /// logs and for a client that does not recognize a newer <see cref="Code"/>.</summary>
    public required string Message { get; init; }
}
```

```csharp
public enum RejectionCode
{
    Unknown = 0,

    // --- matching -------------------------------------------------------------
    NoMatch = 100, MatchTooWeak = 101, WrongLibraryEntry = 102, WrongUnits = 103,
    WrongScope = 104, MatcherFailed = 105, SpanConstraintViolated = 106, UnverifiedCoordinate = 107,

    // --- profile --------------------------------------------------------------
    QualityNotAllowed = 200, CustomFormatScoreTooLow = 201, NotAnUpgrade = 202,
    UpgradesDisabled = 203, CutoffAlreadyMet = 204, LanguageNotWanted = 205,

    // --- release attributes ---------------------------------------------------
    SizeBelowMinimum = 300, SizeAboveMaximum = 301, AgeBelowMinimum = 302, AgeAboveRetention = 303,
    Sample = 304, RawDiskImage = 305, RequiredTermMissing = 306, ForbiddenTermPresent = 307,
    RequiredFlagMissing = 308, HardcodedSubtitles = 309, InsufficientSeeders = 310,

    // --- source ---------------------------------------------------------------
    ProtocolDisabled = 400, IndexerDisabled = 401, IndexerTagMismatch = 402, Blocklisted = 403,

    // --- library state --------------------------------------------------------
    NotMonitored = 500, NotYetAvailable = 501, PackNotFullyAvailable = 502,
    AlreadyGrabbed = 503, AlreadyQueued = 504, AlreadyImported = 505, RecentlyDeleted = 506,
    BetterReleasePending = 507, WaitingForDelay = 508, EarlyReleaseWindow = 509,

    // --- import-only ----------------------------------------------------------
    FileLocked = 600, UnsupportedExtension = 601, DangerousFile = 602, StillUnpacking = 603,
    OutsideRootFolder = 604, DestinationExists = 605, InsufficientFreeSpace = 606,
    CoversFewerUnits = 607, PlanIncomplete = 608, MultiPartNotSupported = 609,
    ContradictsGrab = 610, ContradictsFolder = 611,

    // --- terminal -------------------------------------------------------------
    PluginPolicy = 900, EvaluationError = 901, PlanFailed = 902,
}
```

### 4.2 The collapse this buys

Radarr's `DownloadRejectionReason` has **62** members (`Radarr/…/DecisionEngine/DownloadRejectionReason.cs:3-67`);
Sonarr's has **74**. **24 of Radarr's 62** express one 7-valued verdict against three data sources:

```
UpgradeVerdict   {BetterQualityHeld, BetterRevisionHeld, QualityCutoffMet, CustomFormatScoreNotImproved,
                  CustomFormatCutoffMet, CustomFormatIncrementNotMet, UpgradesNotAllowed}    ×
HoldingsSource   {History, Queue, Disk}
```

— 9 `History*` + 8 `Queue*` + 7 `Disk*`: the 21 cross-product members plus three source-specific variants
(`HistoryRecentCutoffMet`, `HistoryCdhDisabledCutoffMet`, `QueuePropersDisabled`). In this design all 24 are
**one** `RejectionCode.NotAnUpgrade` with `Source` and `Upgrade` populated. Adding a fourth holdings source
(an import list, a sibling library) costs zero members here and would cost seven in Radarr.

Two further collapses fall out: the media-noun families (`UnknownMovie`/`UnknownSeries`,
`WrongMovie`/`WrongEpisode`/`WrongSeason`/`WrongSeries`, `MovieNotMonitored`/`SeriesNotMonitored`/
`EpisodeNotMonitored`) become `NoMatch` / `WrongLibraryEntry` + `WrongUnits` + `WrongScope` / `NotMonitored`
with `Units` populated; and every threshold reason (`BelowMinimumSize`, `AboveMaximumSize`, `MinimumAge`,
`MaximumAge`, `MinimumSeeders`, `MinimumFreeSpace`) gains typed `Threshold`/`Actual` operands instead of
`string.Format` into a prose message.

**The bottom line: Sonarr's 74 grab + 35 import members and Radarr's 62 grab + 24 import members — 195 enum
members across the two apps that bothered to enumerate at all, plus two apps that never did (Lidarr and
Readarr still pass `string`) — become 55 codes, not one of which names a medium.**

### 4.3 Rejection persistence is the routing signal, not a comment

`RejectionPersistence.Temporary` is the *only* thing that sends a release to Pending. That is already true
in the four apps (`ProcessDownloadDecisions.cs:61-65` branches on `TemporarilyRejected`), but it is expressed
as a property on the specification *class*, so a specification that is sometimes-temporary cannot say so.
Moving it onto the `RejectionReason` fixes that: `WaitingForDelay` is temporary, `EarlyReleaseWindow` is
temporary, `NotYetAvailable` is temporary, `PackNotFullyAvailable` is temporary — and `SizeAboveMaximum` from
the *same* size specification is permanent.

### 4.4 Where rejections live

**Grab-time rejections are retained for the life of the release candidate cache, and no longer.** A
`ReleaseCandidateCache` keyed on `(MediaKindId, ReleaseIdentity)` holds the last decision with its full
rejection list, with a configurable TTL (default 24 h, aligned to the RSS window). Three consumers:

- `GET /api/v1/kinds/{kind}/releases?unit={ref}` — the interactive-search grid, which is a
  `WorkbenchProposal` over `WorkbenchSubject.ReleaseCandidates` with a `rejections` column.
- `GET /api/v1/activity/rejections?unit={ref}` — the direct answer to "why wasn't this grabbed?", grouped
  by `RejectionCode` with counts.
- The telemetry pipeline: one `TelemetryEvent` per *distinct* `(SpecificationId, Code)` per run, not per
  release, so a 500-release RSS sync emits ~10 events rather than 500.

**Import-time rejections are retained on the `TrackedAcquisition` until the item leaves the queue**, which is
what makes the queue's "manual interaction required" row explainable without re-running the pipeline. That is
already how the four apps behave (`TrackedDownload.StatusMessages`, `TrackedDownload.cs:15`); the difference
is that the messages are structured.

**What is deliberately not built: a rejection history table.** Persisting every rejection of every release
forever is a table that grows without bound and answers a question nobody asks ("why was this release, which
I have now forgotten, rejected three months ago?"). Stated as a decision so it is not re-litigated as an
oversight.

---

## 5. Upgrade and cutoff semantics

### 5.1 What "already have it" has to mean

The shape model makes three things vary independently, and the upgrade rule has to survive all three:

| Variation | Kind | Consequence for "already have it" |
|---|---|---|
| One file satisfies **N** units | TV (`AtMostOneUnitPerFile = false`) | The holdings for a unit set may be **one** file seen N times; deduplicate by `MediaFileId` before comparing |
| **N** files satisfy the acquisition unit | Music (`AnchorLevelId = album`, `UnitLevelId = track`) | Holdings are a **set** of qualities; Lidarr's `List<QualityModel>` (`Lidarr/…/UpgradableSpecification.cs:32-66`) |
| **N** files satisfy **one** unit | Books (`AtMostOneFilePerUnit = false`, `OrdinalIsMeaningful`) | A multi-part audiobook is one holding, not N; group by `(unit, ordinal)` and treat the group as the holding |
| Two ladders coexist | Books (`ebook` + `audiobook` families) | Comparison is **within a family only**; a holding in another family is not competition and never blocks |
| Completeness is variant-relative | Music, Books | Holdings are restricted to `LibraryFacet.SelectedVariant` when `VariantSelection.CompletenessIsVariantRelative` |

Readarr's own bug is the negative proof of the fourth row: it needed an ebook/audiobook dimension, did not
have one, and simulated it with quality-id bands (0–4 text, 10–13 audio) in **one** ordered ladder with two
`Unknown` sentinels — so any audiobook ranks as an upgrade over any EPUB. `FormatFamily` with one ladder each
makes that unrepresentable, and the upgrade evaluator is where that pays.

### 5.2 The snapshot

```csharp
// src/Arronix.Host/Acquisition/HoldingsSnapshot.cs   (Tier B)
public enum HoldingsSource { Disk = 0, Queue = 1, History = 2 }

/// <summary>What we already have, or are about to have, for a unit set — from all three sources at once.
/// Built ONCE per decision batch and passed to every specification, which is what removes the three
/// separate repository round-trips the four apps make per release.</summary>
public sealed record HoldingsSnapshot
{
    public required MediaItemRef AcquisitionUnit { get; init; }
    public required IReadOnlyList<Holding> Holdings { get; init; }
    public IReadOnlyList<Holding> From(HoldingsSource source);
    public IReadOnlyList<Holding> InFamily(string formatFamilyId);
}

public sealed record Holding
{
    public required HoldingsSource Source { get; init; }
    public required IReadOnlyList<MediaItemRef> Units { get; init; }
    public required string FormatFamilyId { get; init; }
    public QualityTier? Quality { get; init; }
    public int CustomFormatScore { get; init; }
    public IReadOnlyList<Language> Languages { get; init; } = [];
    public MediaFileId? File { get; init; }          // Disk
    public string? DownloadId { get; init; }         // Queue
    public DateTimeOffset? At { get; init; }         // History — drives the 12-hour recency window
    public int? Ordinal { get; init; }               // multi-part grouping
}
```

### 5.3 The evaluator

```csharp
// src/Arronix.Host/Acquisition/IUpgradeEvaluator.cs   (Tier B)
public interface IUpgradeEvaluator
{
    /// <summary>The one method that replaces UpgradableSpecification × 4 apps × 3 call sites.</summary>
    UpgradeAssessment Assess(UpgradeRequest request);
}

public sealed record UpgradeRequest
{
    public required AcquisitionProfile Profile { get; init; }
    public required IReadOnlyList<Holding> Current { get; init; }   // already filtered to source + family
    public required QualityTier Candidate { get; init; }
    public required int CandidateCustomFormatScore { get; init; }
    public required string CandidateFormatFamilyId { get; init; }
    public required HoldingsAggregation Aggregation { get; init; }
}

/// <remarks>
/// <c>WorstOf</c> is Lidarr's rule and the correct default: a release that improves on track 3 but is worse
/// than track 7 is not an upgrade for the album. <c>BestOf</c> exists because a variant switch compares
/// against the best the *other* variant offers. <c>Exact</c> is the degenerate N = 1 case that S/R/K have,
/// and is what their code silently assumes.
/// </remarks>
public enum HoldingsAggregation { WorstOf = 0, BestOf = 1, Exact = 2 }

public sealed record UpgradeAssessment
{
    public required bool IsUpgrade { get; init; }
    public required UpgradeVerdict Verdict { get; init; }
    public required bool CutoffMet { get; init; }
    /// <summary>The specific holding that blocked it, for the rejection's <c>Actual</c> slot.</summary>
    public Holding? Blocking { get; init; }
}

/// <summary>Reuses Radarr's <c>UpgradeableRejectReason</c> vocabulary verbatim — seven values, four
/// maintainer groups, one meaning each. Nothing here is invented.</summary>
public enum UpgradeVerdict
{
    Upgrade = 0, BetterQualityHeld = 1, BetterRevisionHeld = 2, QualityCutoffMet = 3,
    CustomFormatScoreNotImproved = 4, CustomFormatCutoffMet = 5,
    CustomFormatIncrementNotMet = 6, UpgradesNotAllowed = 7,
}
```

**The evaluation order is Sonarr's, verbatim** (`UpgradableSpecification.cs:31-122`), because it is the most
recently maintained of the four and because its ordering is load-bearing: quality-better-and-cutoff-unmet →
accept; quality-worse → reject; revision-better → accept; upgrades-disabled → reject; revision-worse →
reject; quality-better-but-cutoff-met → reject; then the three custom-format tests. Reordering any pair
changes user-visible behavior, so the order is part of the design and gets a test per pair.

### 5.4 Cutoff and the want-set

Cutoff is not an upgrade concept; it is a **want** concept, and putting it in the wrong place is why all four
apps have a separate `CutoffUnmet…SearchCommand` that re-derives it. Precisely:

```
wanted(unit) ⟺ monitored(unit)
              ∧ available(unit)
              ∧ ( holdings(unit) = ∅                                 -- "missing"
                ∨ ¬cutoffMet(profile, aggregate(holdings(unit))) )   -- "cutoff unmet"
              ∧ unit ∉ queuedUnits
```

Every clause is computable from declarations: `monitored` from `LibraryFacet.Monitor` over the level's
`MonitorDimension`s; `available` from the `AvailabilityPolicy` projection; `holdings` from `UnitFileLink`;
`aggregate` from `HoldingsAggregation`; `cutoffMet` from the family's `Ladder` and the stable `CutoffPolicy`;
`queuedUnits` from `IAcquisitionQueue`. Two exclusions apply and both are declared:
`SequenceException.ExcludedFromCompleteness` (Sonarr's specials), and, when
`VariantSelection.CompletenessIsVariantRelative`, restriction to the selected variant.

Sonarr computes exactly this in SQL — `EpisodeFileId == 0`, `SeasonNumber >= startingSeasonNumber`, and
`AirDateUtc + Series.Runtime <= now` (`Tv/EpisodeRepository.cs:209-225`) — minus the queue, in memory
(`IndexerSearch/EpisodeSearchService.cs:157-159`). The generalization is one query, not four.

```csharp
// src/Arronix.Host/Acquisition/IWantDetector.cs   (Tier B)
public interface IWantDetector
{
    IAsyncEnumerable<WantedGroup> DetectAsync(WantQuery query, CancellationToken ct);
}

public sealed record WantQuery
{
    public required MediaKindId MediaKind { get; init; }
    public MediaItemRef? Scope { get; init; }                  // a library entry, or null for everything
    public required WantKind Kind { get; init; }
    public bool MonitoredOnly { get; init; } = true;
    public bool IncludeQueued { get; init; }
}

public enum WantKind { Missing = 0, CutoffUnmet = 1, Both = 2 }

/// <summary>Units are grouped by the scope a search would target, so the caller never re-derives packs.
/// One <c>WantedGroup</c> becomes exactly one <see cref="AcquisitionRequest"/>.</summary>
public sealed record WantedGroup
{
    public required MediaItemRef LibraryEntry { get; init; }
    public required string SearchKindId { get; init; }
    public required AcquisitionScope Scope { get; init; }
    public required IReadOnlyList<MediaItemRef> Units { get; init; }
    public Coordinate? SpanCoordinate { get; init; }           // set iff Scope.Kind == SequenceSpan
}
```

**Grouping into packs is core, not plugin.** `SearchKind.Scope` already declares which scopes exist; the
grouping rule is "if a `SequenceSpan` scope exists and ≥ N units in one span coordinate are wanted, emit one
span group instead of N unit groups", with N configurable per kind. Sonarr's `EpisodeSearchGroup` is this
rule with `season` hard-coded.

---

## 6. Pending, delay, retries, blocklisting, and the failed-download loop

### 6.1 Pending

```csharp
// src/Arronix.Host/Acquisition/IPendingReleaseService.cs   (Tier B)
public interface IPendingReleaseService
{
    ValueTask AddAsync(PendingRelease pending, CancellationToken ct);
    ValueTask<IReadOnlyList<PendingRelease>> DueAsync(DateTimeOffset now, CancellationToken ct);
    ValueTask<PendingRelease?> OldestForUnitsAsync(IReadOnlyList<MediaItemRef> units, CancellationToken ct);
    ValueTask RemoveForUnitsAsync(IReadOnlyList<MediaItemRef> units, CancellationToken ct);
}

public sealed record PendingRelease
{
    public required Guid PendingId { get; init; }
    public required MediaKindId MediaKind { get; init; }
    public required MediaItemRef LibraryEntry { get; init; }
    public required IReadOnlyList<MediaItemRef> Units { get; init; }
    public required ReleaseCandidate Release { get; init; }
    public required PendingReason Reason { get; init; }
    public required DateTimeOffset AddedAt { get; init; }
    public required DateTimeOffset DueAt { get; init; }
    public required IReadOnlyList<RejectionReason> Rejections { get; init; }
}

/// <summary>Byte-identical to <c>Download/Pending/PendingReleaseReason.cs:3-8</c> in all four apps.</summary>
public enum PendingReason { Delay = 0, DownloadClientUnavailable = 1, Fallback = 2 }
```

The **delay profile** generalizes with no change of meaning: a per-protocol wait, a preferred protocol, and
three bypasses — better-revision-of-what-we-have, highest-quality-in-profile, and above-a-custom-format-score
(`RssSync/DelaySpecification.cs:47-92`). All three read only profile and holdings, so all three are core. The
one media-shaped line — `_pendingReleaseService.OldestPendingRelease(seriesId, episodeIds)` — becomes
`OldestForUnitsAsync(units)`.

**A pending release is re-decided, never replayed.** When `DueAt` arrives the release goes back through
stages 4–6 from the top. Replaying a stale decision is how a release grabbed after a 12-hour delay ends up
grabbed after the user has already imported a better one.

### 6.2 Retries and back-off

No new mechanism. Three existing ones, kept separate because they fail differently:

| Failure | Mechanism | Owner |
|---|---|---|
| An indexer or download client is unhealthy | `ProviderStatusStore` escalation (`0,1m,5m,15m,30m,1h,3h,6h,12h,24h`, initial-failure grace, 15-minute startup grace) | Providers (milestone §5.3) |
| A pipeline job threw | Scheduler back-off with full jitter + `FailureClass` from the result bag | Scheduler (milestone §4.7) |
| A release is not grabbable *yet* | `PendingRelease.DueAt` | This document |

The third is not a retry, and conflating it with one is a category error the four apps avoid and that a
naive redesign would introduce.

### 6.3 Blocklisting

Blocklisting is an **effect of failure, never a decision**. Three writers, all explicit:

1. The user, from the queue ("remove and blocklist").
2. `FailedDownloadService` equivalent, when a grabbed download fails and `AutoBlocklistFailed` is on.
3. `IGrabExecutor`, when the indexer returns a 404/410 for the download URL of a release it just offered —
   the release does not exist, so re-grabbing it next hour is guaranteed waste.

Reads happen in exactly one place: specification role 8 (§3.2).

### 6.4 Failed download → re-search

```
DownloadFailed(units, releaseSource)
   ├─ skipRedownload requested by the user?               → stop
   ├─ AutoRedownloadFailed disabled?                      → stop
   ├─ releaseSource == Interactive
   │     ∧ AutoRedownloadFromInteractive disabled?        → stop
   └─ re-search at the NARROWEST scope covering `units`:
         units.Count == 1                                       → SearchKind with Scope.Single
         units == all wanted units in one span coordinate       → SearchKind with Scope.SequenceSpan
         otherwise                                              → Scope.Single, one request carrying all units
```

That is `RedownloadFailedDownloadService.cs:31-79` with `SeasonNumber` replaced by "the span coordinate the
units share" and `GetEpisodesBySeason` replaced by "the wanted units in that span". The three-way branch is
preserved because its middle case is load-bearing: re-searching a failed season pack unit-by-unit produces 24
searches where one will do.

**The loop is bounded.** A `RedownloadAttempt` counter rides the correlation id; after
`AcquisitionOptions.MaxRedownloadAttempts` (default 3) for the same unit set within
`MaxRedownloadWindow` (default 24 h), the pipeline stops and raises
`NotificationEvent.ManualInteractionRequired`. None of the four apps bounds this loop, and an indexer serving
one permanently-broken release will happily grab-fail-regrab it forever.

---

## 7. Driving the pipeline from the scheduler

Every stage is an `IScheduledJob` on the one host scheduler. No pipeline-owned timers, no `Task.Run`.

| Job id | Default schedule | Throttle keys | What it does |
|---|---|---|---|
| `acquisition.rss-sync` | `every PT15M` | `cap:indexing`, `kind:{k}` | stages 2–7 with `SearchOrigin.Rss`, one pass per enabled indexer |
| `acquisition.search.missing` | `manual` | `cap:indexing`, `kind:{k}` | stage 1 (`WantKind.Missing`) then 2–7 |
| `acquisition.search.cutoff-unmet` | `manual` | `cap:indexing`, `kind:{k}` | stage 1 (`WantKind.CutoffUnmet`) then 2–7 |
| `acquisition.pending.release` | `every PT1M` | — | re-decides due `PendingRelease`s (stages 4–7) |
| `acquisition.downloads.refresh` | `every PT1M` | `cap:download` | stage 8 |
| `acquisition.downloads.process` | `every PT1M` | `cap:import` | stages 9–11 for completed items |
| `acquisition.import.scan` | `manual` | `cap:import` | stages 10–11 for a folder, `ImportTrigger.FolderScan` |

Notes that are decisions, not observations:

- **`rss-sync` is `every PT15M`, not `startup`.** The milestone's grammar has `startup`; using it here would
  make every host restart issue a full RSS sweep across every indexer, which is exactly the behavior that
  gets users banned from private trackers.
- **The two search jobs default to `manual`.** All four apps ship them as periodic and all four have a
  documented "my indexer banned me" support path as a result.
- **Throttle keys come from the milestone's synthesis rule**, unchanged: `cap:indexing` (limit 4),
  `cap:import` (2), `cap:download` (8). The pipeline adds `kind:{k}` so one plugin's slow indexer cannot
  starve another's.
- **Correlation ids flow through `JobExecutionContext.CorrelationId`.** One id spans want-detection →
  grab → import → notification for a given unit set, which is what makes the activity feed a story rather
  than a log. It is a typed member of the context (milestone resolution 24, revised in 0.4.0), not a
  published key in the `Parameters` bag; no parallel mechanism.
- **Progress uses `JobExecutionContext.Progress`.** Stage 3 reports per-indexer, stage 5 per-100
  releases (`_logger.ProgressTrace("Processing release {0}/{1}")` is the surveyed behavior, at a sane rate).

### 7.1 Where the plugin seams attach

| Seam | Stage | Called | Capability |
|---|---|---|---|
| `IReleaseQueryPlanner.PlanAsync` | 2 | once per `WantedGroup` | `indexing` |
| `IReleaseMatcher.MatchAsync` | 4 | once per candidate, `MatchSource.ReleaseName` | `matching` |
| `IAcquisitionPolicy.EvaluateReleaseAsync` | 5, `Remote` band | once per surviving candidate | `matching` |
| `IAcquisitionPolicy.Compare` | 6 | after every core comparator | `matching` |
| `IReleaseMatcher.MatchAsync` | 10 | once per file, `MatchSource.FileName`/`FolderName` | `matching` |
| `IImportPlanner.PlanAsync` | 10 | once per batch | `import` |
| `IAcquisitionPolicy.EvaluateImportAsync` | 10 | once per candidate | `matching` |
| `IImportPlanner.CommitAsync` | 11 | once per accepted verdict, post-placement | `import` |

**Every seam call is wrapped**: a per-call timeout (`AcquisitionOptions.PluginCallTimeout`, default 30 s), full
exception isolation, and a `TelemetryEvent` on failure attributed to the plugin id. A plugin that throws
degrades one media kind's pipeline, never the host's — the same quarantine posture the milestone takes at
load time.

---

## 8. Concurrency and idempotency

### 8.1 Two searches race

Two jobs can target overlapping unit sets: a scheduled missing-search and a user-triggered one, or an RSS
sync and a re-search after a failure. Three layers, cheapest first:

1. **Scheduler dedupe.** `IScheduledJob.MaxConcurrency = 1` per search job id, plus the `kind:{k}` throttle.
   This alone removes the common case.
2. **Unit-scoped leases.** Before stage 5 the engine takes a short lease on the unit set
   (`IAcquisitionLease.TryAcquireAsync(units, ttl)`, default 5 minutes, renewed per batch). A second job that
   cannot take the lease **skips those units and logs**, rather than blocking — a blocked search job holds an
   indexer throttle slot for nothing.
3. **Grab idempotency (below)**, which is the backstop for everything the first two miss.

The leases are advisory and in-memory in the first implementation, because the host is a single process. The
interface is defined now so the eventual multi-process story is a swap and not a redesign.

### 8.2 Grab idempotency

```csharp
public sealed record GrabIntent
{
    public required MediaKindId MediaKind { get; init; }
    public required MediaItemRef LibraryEntry { get; init; }
    public required IReadOnlyList<MediaItemRef> Units { get; init; }
    public required ReleaseIdentity Release { get; init; }
    public required ProviderId DownloadClient { get; init; }
    public required string CorrelationId { get; init; }

    /// <summary>
    /// Deterministic. SHA-256 over (mediaKind, downloadClientId, canonical release identity), where the
    /// canonical identity is the lowercase info hash when present and otherwise
    /// <c>{normalizedTitle}|{size}|{indexerId}</c>. Units are deliberately EXCLUDED: two searches that match
    /// the same release to slightly different unit sets must still collide, because the download client will
    /// deduplicate them anyway and we want to know that before we call it.
    /// </summary>
    public string IdempotencyKey { get; }
}
```

The executor performs: **insert the claim → call the client → record the receipt**. If the claim insert
conflicts, the executor loads the existing claim and returns its receipt instead of calling the client; if
the claim exists but has no receipt and is older than `GrabClaimTimeout` (default 2 minutes), it is
reclaimed. `DownloadClientRejectedReleaseException` — which all four apps already treat as "possible
duplicate" (`DownloadService.cs:47-51`) — is mapped to **success**, and the receipt is reconciled from the
client's item list on the next tracking pass.

This is the one place the design adds machinery the four apps do not have, and it is justified by their own
code: `ProcessDownloadDecisions.IsEpisodeProcessed` (`:56`) is an in-memory, single-batch, unit-level
approximation of exactly this, and it protects nothing across a restart.

### 8.3 A download completes twice

Sonarr handles this by re-reading history and treating "everything was already imported" as success, with a
comment in the source describing the edge case it cannot handle
(`CompletedDownloadService.cs:227-267` — *"relies on EpisodeIds being consistent between executions"*).
The structural fix is to make import a **compare-and-swap on the link**, not a re-check:

- `IMediaStore.LinkAsync(UnitFileLink)` is idempotent on `(Unit, File, Ordinal)`.
- `IFilePlacementService` computes the destination path, and if a file with the same content identity
  (size + mtime, or a hash when configured) is already linked to the same unit, it reports
  `ImportOutcome.AlreadyImported` **and does not touch the disk**.
- Completion is therefore a function of the link table, not of the history table, and a second completion
  callback produces the same `ImportResult` as the first.

Two consequences worth stating: the "all units imported?" test becomes
`units.All(u => links(u).Any())` restricted to the selected variant — no history join — and Sonarr's edge
case (an id changing between runs) becomes harmless, because a changed id means a genuinely different unit
that is genuinely still missing.

### 8.4 Placement is per-file, and partial success is a real outcome

A batch of 12 files where file 7 fails to move leaves 6 files imported and linked. That is correct and must
be reported as such: `ImportOutcome.PartiallyImported`, the acquisition stays `ImportBlocked`, and the next
pass re-plans only the 6 remaining files (they are still unlinked, so want-detection still wants them).
**No rollback.** Rolling back six successful moves to preserve an all-or-nothing illusion risks the user's
files to preserve a property nobody asked for.

---

## 9. The 0.1.0 import contracts are deleted, not bridged

Three 0.1.0 types are structurally inadequate for this pipeline, and a fourth member is misnamed for what it
now does. The earlier draft of this section proposed adding a sibling contract beside each of them and
bridging between the two. **That plan is withdrawn.** The
bridge was never written, and this is the last moment at which withdrawing it costs nothing.

| Deleted type | Limit | Consequence | Replacement |
|---|---|---|---|
| `IImportPipeline.EvaluateImportAsync(string filePath, ParsedRelease, …)` | Per **file**. | Cannot express Lidarr's batch identification, where the assignment of 12 files to 12 tracks is only optimal as a whole (`Identification/Munkres.cs`). | `IImportPlanner.PlanAsync(ImportRequest) → ImportPlan`. The one-file case is the degenerate batch; a per-batch API expresses the per-file case for free, which is why there is nothing to keep. |
| `ImportDecision.TargetItemId` (`MediaItemId`, singular) | One unit per file. | Cannot express a multi-episode file (Sonarr, `AtMostOneUnitPerFile = false`) or a multi-part audiobook (Readarr, `OrdinalIsMeaningful`). | `ImportVerdict` carrying `IReadOnlyList<UnitFileLink>`. |
| `MatchDecision.MatchedItemId` (`MediaItemId?`, singular) + `RejectionReason` (`string?`) | One unit, one unstructured reason. | Superseded by `MatchOutcome` in the milestone; the string reason is the hole §3.5 fills. | `MatchOutcome` + the rejection vocabulary (§3.2). **`MatchConfidence` is not deleted with it** — the enum is declared in `DTOs/MatchDecision.cs` but is live on `IReleaseMatcher.MatchCandidate.Confidence` and `Intent/WorkbenchProposal`, and `parsing-and-test-corpus.md` resolution 9 reuses it deliberately. Move it to `DTOs/MatchConfidence.cs` in the same change; delete only the `MatchDecision` record. |
| `IImportPipeline.ImportAsync` (the post-placement commit hook) | Not a limit — a naming and placement problem. | The method's meaning was already inverted to "the host has placed the file; commit it" (`file-management.md` §0 #2), which `ImportAsync` does not say. | `IImportPlanner.CommitAsync(ImportCommit, CancellationToken)` — the same hook, on the seam that already owns import, under a name that means what it does. |

**Why deletion and not a bridge.** Arronix is a clean-sheet rewrite. Nothing has ever shipped against
`IImportPipeline`, `ImportDecision` or `MatchDecision`; there is no plugin compiled against them and there
cannot be one, because no Arronix release exists for a plugin to have been compiled against. The structural losses in the table above are not losses to be *mediated* — a bridge whose acceptance test is
"refuses to truncate a multi-unit verdict" is an admission that the older shape destroys data, and shipping
it would put that admission in the hot path of every import forever. Two import seams also means two
definitions of "approved", which is the exact defect resolution 22 refuses on the manual-import path.

`docs/contracts/stability.md` no longer says otherwise: below 1.0.0 the assembly is unstable in the SemVer
sense and a contract with no implementer outside this repository is deleted rather than deprecated. The
earlier version of this section cited the milestone's `IIndexerProvider` decision (resolution #31) as
precedent for bridging; **that decision has itself been reversed** — the legacy provider trio and
`StableProviderBridge` are deleted outright — so the precedent now runs the other way.

**Sequencing.** `IImportPlanner` does not exist yet. The deletion of `IImportPipeline`, `ImportDecision`
and `MatchDecision` is therefore attached to **WP-A14**, the work package that introduces the replacement,
and happens in the same change. The codebase never contains both.

**Reconciliation owed elsewhere** (not this document's files): `file-management.md` §0 #2 records
`IImportPipeline.ImportAsync`'s inverted semantics as an XML-doc clarification; under this section it
becomes `IImportPlanner.CommitAsync` instead. `unified-host-runtime.md` §7.2's contract inventory and
`src/Arronix.Abstractions/README.md` both list the deleted types.

---

## 10. Dependencies, and where this design is thin

### 10.1 Four amendments needed in 0.3.0

These are additive to `Arronix.Abstractions.Shape` and should land **in the milestone that is currently being
written** — not because a later change would be "breaking" (below 1.0.0 nothing here is frozen; see
`docs/contracts/stability.md`), but because each is information the pipeline cannot *derive*, so every
work package built before they land is built against a shape that cannot answer the question.
**I do not own that file; these are requests.**

| # | Amendment | Where | Why it cannot wait |
|---|---|---|---|
| A1 | `MediaShape.Availability : AvailabilityPolicy?` — `{ AnchorFieldId, DefaultOffset }`, with `ItemView.Fields[AnchorFieldId]` carrying a `FieldValueKind.Instant` | `MediaShape`, new `AvailabilityPolicy` record | Five core specifications and want-detection need "is it out yet?" (§1 row 8). Without it, six roles move to Plugin and the core share drops from 83.6% to 74.6%. |
| A2 | `FieldSemantics.ExpectedDuration` (a new flag) | `FieldSemantics` | Size-per-minute (grab role 1) and sample detection (import role 5) both need the unit's expected duration. A `[Flags]` addition is additive; discovering it later is not. |
| A3 | `FileBinding.HoldingsAggregation : HoldingsAggregation` (default `WorstOf`) | `FileBinding` | §5.3. Defaulting is safe; leaving it out means the host guesses, and the guess is wrong for exactly one of the four surveyed kinds. |
| A4 | `SearchKind.MinimumUnitsForPack : int?` | `SearchKind` | The pack-grouping rule in §5.4. Absent it, either every wanted unit becomes its own search (Lidarr's behavior, and slow) or the threshold is hard-coded. |

If A1–A4 cannot land in 0.3.0, the fallback is to carry all four on `IAcquisitionPolicy` as plugin methods.
That is strictly worse — it moves declared data into behavior, it makes want-detection a per-unit plugin
call, and it makes the availability predicate unavailable to a SQL query — and it is recorded here so the
trade is visible rather than discovered.

### 10.2 What the milestone spec is silent on, that this design assumes

- **`AcquisitionProfile`.** The milestone defines `FormatFamily.Ladder`, `SelectionFacet` and reuses the
  stable `CutoffPolicy`, but never assembles them into the per-library-entry profile the pipeline reads. This
  document assumes a host-side `AcquisitionProfile { QualityLadder, Cutoff, MinScore, MinUpgradeScore,
  CutoffScore, UpgradeAllowed, Languages, CustomFormats, DelayProfile }` per `(kind, format family)`. **Named
  as a dependency, not designed here** — it belongs with the profiles/custom-formats design.
- **The custom-format engine.** Resolution row 16 says it is core over a declared field vocabulary. The
  matcher tree, scoring and negation semantics are a separate design; this document only requires that a
  `ReleaseSubject` arrives with `CustomFormatScore` already computed.
- **`ValidatedShape`'s defect list** does not yet include the four rules this pipeline needs
  (an `AvailabilityPolicy` anchor must resolve to an `Instant` field; a `HoldingsAggregation` of `Exact`
  requires `AtMostOneFilePerUnit`; a `SearchKind` with `Scope.SequenceSpan` requires a `SequenceAxis`;
  a `FormatFamily` ladder must be strictly ordered). Requested as validator additions.
- **`ReleaseCandidate` carries no seeders, no indexer flags, no info hash.** Six core specifications need
  them. They are reachable through `AdditionalData` (an open `IReadOnlyDictionary<string,string>`), which
  works and is ugly. The clean fix is a typed `ReleaseAttributes` record on `ReleaseCandidate`, and nothing
  postpones it: below 1.0.0 the record may be reshaped in any release. Recorded as a wart, and the wart has
  no version-boundary excuse.

### 10.3 Honest weaknesses

1. **The `Remote` cost band is a latency cliff.** `IAcquisitionPolicy.EvaluateReleaseAsync` runs once per
   surviving candidate; an RSS sync with 400 survivors and a plugin doing 20 ms of work is 8 seconds inside
   one job. Mitigation is a bounded-parallelism fan-out (default 8) and the per-call timeout, but a plugin
   that does network I/O in that method will hurt, and nothing structurally prevents it.
2. **`HoldingsSnapshot` is built per acquisition unit, per batch.** For a discography search over a
   500-album artist that is 500 snapshots. Batched loading (`FindLibraryManyAsync`, `MostRecentForUnitsAsync`)
   makes it 3 queries, not 1500 — but the object graph is still large, and no *arr has ever been profiled at
   that scale because no *arr has all four media kinds in one process.
3. **Interactive search reuses the workbench, which is a grid.** A release grid with 12 columns and 400 rows
   is a real screen the milestone's `WorkbenchDescriptor` can describe but has never been rendered. If it
   proves inadequate, the fallback is a dedicated wire DTO, and that would be a genuine gap in the
   descriptor-driven UI thesis rather than a small fix.
4. **Cross-level acquisition is still unvalidated.** The milestone records this (§2.13.3); the pipeline
   inherits it. A release whose `MatchOutcome.Units` spans two levels will be grabbed and will then confuse
   `HoldingsSnapshot`, whose `AcquisitionUnit` is singular. Guarded by rejecting such matches with
   `RejectionCode.WrongScope` until the shape validates them.
5. **No design for "the user changed the profile, re-evaluate everything".** All four apps handle it by
   waiting for the next scheduled search. This document does the same. It is the right call for now and it is
   not a good user experience.

---

## 11. Work packages

Ordered. Each is independently reviewable; each has a test gate named.

| WP | Contents | Depends on | Test gate |
|---|---|---|---|
| **WP-A0** | Amendments A1–A4 to `Arronix.Abstractions.Shape`, plus the four `ValidatedShape` rules in §10.2 | Unified-host-runtime WP-2 | Shape validator rejects each of the four defects; the four plugin shapes still validate |
| **WP-A1** | `ARX0018` registration: `docs/contracts/stability.md` table row, `ExperimentalContracts.Acquisition`, namespace `Arronix.Abstractions.Acquisition` | WP-A0 | The governance test that asserts every experimental type carries a matching area attribute |
| **WP-A2** | Rejection vocabulary: `RejectionCode`, `RejectionReason`, `RejectionPersistence`, `UpgradeVerdict`, `HoldingsSource` (Abstractions — they cross to the client) | WP-A1 | Every `RejectionCode` member is produced by exactly one specification id; no member is unreachable |
| **WP-A3** | The three ledgers: `IAcquisitionHistory`, `IAcquisitionQueue`, `IBlocklist` + in-memory implementations (Tier B) | Milestone §4.6 store | Blocklist matches an info hash exactly and a title within both tolerances; queue is a pure projection |
| **WP-A4** | `HoldingsSnapshot`, `IUpgradeEvaluator`, `HoldingsAggregation` | WP-A2, WP-A3 | Sonarr's 9-step ordering reproduced pair-by-pair; Lidarr's worst-of case; Readarr's two-ladder case |
| **WP-A5** | `IWantDetector` + `WantedGroup` grouping | WP-A0, WP-A4 | The four surveyed kinds produce the surveyed want-sets, including specials exclusion and variant-relative counting |
| **WP-A6** | Specification framework: `ISpecification<T>`, `SpecificationCost`, `SpecVerdict`, the banded runner, `ReleaseSubject`/`ImportSubject` | WP-A2 | A permanent rejection in band *n* prevents band *n+1*; all rejections within a band are collected; a throwing specification yields `EvaluationError` and does not abort the run |
| **WP-A7** | The 39 core grab specifications | WP-A4, WP-A5, WP-A6 | One test class per role, each asserting the exact `RejectionCode` and populated slots |
| **WP-A8** | `IAcquisitionPolicy` (Abstractions) + registration + capability wiring to `matching` | WP-A1, WP-A6 | The forward capability check; a policy cannot accept what the core rejected |
| **WP-A9** | Decision engine + prioritization: `IGrabDecisionEngine`, `IGrabPriorityComparer` (7 core comparators + policy tiebreak + `ReleaseId` final order) | WP-A7, WP-A8 | Total order proven by a shuffle-and-sort property test |
| **WP-A10** | Grab: `GrabIntent`, `IGrabExecutor`, the claim table, `GrabReceipt`, protocol-failure short-circuit | WP-A9 | Concurrent identical intents produce one client call; duplicate rejection maps to success |
| **WP-A11** | Pending: `IPendingReleaseService`, the three reasons, the delay specification's four bypasses, re-decide-on-due | WP-A9 | A due pending release is re-decided, not replayed |
| **WP-A12** | Tracking: `IAcquisitionTracker`, `TrackedAcquisition`, `TrackedState`, status messages | Milestone §5.6 `IDownloadClient` | State machine transitions exhaustively tested; untracked items never import |
| **WP-A13** | Completed-download handling: `ICompletedAcquisitionService`, path validation, library-entry resolution, `ImportBlocked` | WP-A12, WP-A3 | Every failure mode in §2.1 stage 9 produces a distinct, explainable state |
| **WP-A14** | Import planning: `IImportPlanner` (`PlanAsync` + `CommitAsync`), `ImportPlan`, `ImportCandidate`, `ImportVerdict`, `ImportCommit`. **In the same change:** delete `Abstractions/Import/IImportPipeline.cs`, `Abstractions/DTOs/ImportDecision.cs` and the `MatchDecision` record (moving `MatchConfidence` to `DTOs/MatchConfidence.cs`), plus their `IPluginRegistry` / `CapabilityMatrix` / `PluginBootstrapper` call sites (§9) | WP-A6 | A multi-unit verdict round-trips its whole `UnitFileLink` list; the solution builds with **no** reference to `IImportPipeline`, `ImportDecision` or `MatchDecision` anywhere outside the deletion diff |
| **WP-A15** | The 17 core import specifications | WP-A14, WP-A4 | One test class per role |
| **WP-A16** | Placement + post-import: `IFilePlacementService`, `FileTransferMode`, CAS on `UnitFileLink`, partial-success reporting, notification dispatch | WP-A15, milestone §5.5 | Double completion is a no-op; a mid-batch failure leaves earlier files imported |
| **WP-A17** | Failed-download loop: failure classification, blocklist-on-failure, scope-escalating re-search, the attempt bound | WP-A10, WP-A3 | The three-way scope branch; the loop terminates at `MaxRedownloadAttempts` |
| **WP-A18** | Scheduler wiring: the seven jobs, schedules, throttle keys, correlation and progress | Milestone §4.7 | No job defaults to a schedule that hammers an indexer at startup |
| **WP-A19** | Surfaces: rejection endpoints, the interactive-search and manual-import workbenches, queue and activity wire DTOs | WP-A9, WP-A14, milestone §6 | A rejected release renders with a code, a threshold and an actual value, with no media noun in the client |

---

## 12. Deferred, with the trigger that un-defers it

| Deferred | Trigger |
|---|---|
| **The custom-format matcher tree and scoring engine** | The Profiles milestone. This document only consumes `CustomFormatScore`; designing the matcher here would fork it. |
| **`AcquisitionProfile` assembly (ladder + cutoff + formats + delay + languages)** | The Profiles milestone. Named as a dependency in §10.2 so it cannot be forgotten. |
| **Multi-process / multi-host leases** | A second host process. `IAcquisitionLease` is defined now precisely so the in-memory implementation is a swap. |
| **Content-hash file identity** | A user report of duplicate imports that size + mtime missed. The interface takes an identity; only the implementation changes. |
| **Re-evaluate-on-profile-change** | A user requirement. Today: the next scheduled search. Recorded as a known poor experience (§10.3.5). |
| **A rejection history table** | Never, on current evidence (§4.4). Un-defers only if "why was this rejected last month?" becomes a real support pattern. |
| **Promotion of `IAcquisitionHistory` / `IAcquisitionQueue` / `IBlocklist` to Abstractions** | A plugin that must read or write them. Zero implementers; ARCHITECTURE §5 names them Storage-Layer. |
| **`ReleaseAttributes` replacing `ReleaseCandidate.AdditionalData`** | Not deferred on version-boundary grounds — `stability.md` permits reshaping any contract in a MINOR below 1.0.0. The only remaining question is which fields belong on the record, and it should be settled in this milestone rather than parked. Recorded as a wart, not a preference. |
| **Cross-level acquisition** | `ValidatedShape` learning to validate it. Until then `RejectionCode.WrongScope`. |
| **Import lists as a fourth holdings source** | An import list that should suppress a grab. The `HoldingsSource` enum takes a member and nothing else moves — which is the whole point of §4.2. |
| **Seed-time / seed-ratio management after import** | A torrent-specific milestone. `IDownloadClient.MarkImportedAsync` already exists and is the hook. |
