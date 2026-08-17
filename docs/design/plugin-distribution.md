# Arronix Plugin Packaging, Distribution and Lifecycle — Design

> **Status:** Design. No code has been written. This document is the direct input to the implementation
> phase for everything that surrounds the plugin loader specified in
> `docs/design/unified-host-runtime.md` §3–§4.
>
> **Scope:** the package format, dependency isolation in practice, signing and verification, the install /
> enable / upgrade / rollback / uninstall lifecycle and its treatment of plugin data and configuration,
> contract-range negotiation across a host upgrade, a static registry index, and an honest verdict on
> unload and hot-reload.
>
> **Relationship to the runtime spec.** `unified-host-runtime.md` is authoritative for the manifest
> (§3.5), the capability model (§3.2), contract ranges and the experimental gate (§3.6), the
> `AssemblyLoadContext` and the 16-step load pipeline (§4.1), and plugin lifecycle states (§4.8). This
> document designs *around* those and contradicts none of them. Where it adds to them — new
> `CoreErrorCode` values, new manifest fields, a new options section, one new `PluginStatusView` member —
> the addition is called out explicitly and attributed to the work package that owns the file.
>
> **Relationship to the threat model.** A plugin threat-model document is being written concurrently. This
> document states *what* is signed, *with what*, *when it is checked* and *what that buys*; it deliberately
> does not re-derive the attacker model, the in-process-execution consequences or the containment analysis.
> Where the two touch, the threat model wins. See §3.6 and §12.
>
> **Governing documents:** `ARCHITECTURE.md` §4, §8, §11, §15, §16;
> `docs/arronix-common-extraction-plan.md` (three-tier promotion rule);
> `docs/contracts/stability.md` (experimental gate, additive-only MINOR).
>
> **Empirical basis:** Lidarr is the only surveyed *arr with a real plugin system — a collectible
> `AssemblyLoadContext`, a GitHub-release-backed installer and an unload routine — and its behavior is
> cited throughout, mostly as the negative example it was designed to be
> (`lidarr-music.md` §"(iii) Lidarr has a real, shipping first-party plugin system", lines 450–476).
> Sonarr's self-updater (`src/NzbDrone.Core/Update/`, `src/NzbDrone.Update/`) and Prowlarr's provider
> factory supply the update and orphaned-configuration prior art.

---

## 0. The headline: unload and hot-reload

**Recommendation: require a host restart for install, upgrade, rollback and uninstall. Do not ship
hot-reload. Keep `isCollectible: true`, implement `Unload()` behind a diagnostic-only endpoint, and never
let any product behavior depend on collection succeeding.**

Three reasons, in order of weight:

1. **The only *arr that built this already gave up on it.** Lidarr has a genuine collectible-ALC plugin
   system with a written unload routine — `PluginLoader.UnloadPlugins` calls `Unload()`, then
   `GC.Collect()` + `GC.WaitForPendingFinalizers()` in a loop, giving up after ten attempts and returning
   `false` (`_reference/Lidarr/src/NzbDrone.Common/Composition/PluginLoader.cs:50-79`). That routine is not
   on the install path. Both `InstallPluginService.InstallPlugin` and `UninstallPlugin` end by logging
   *"Please restart Lidarr"* (`InstallPluginService.cs:78`, `:90`, `:94`). The code that tries to unload and
   the code that installs plugins do not meet, and the shipped product asks for a restart.

2. **Arronix's own design holds plugin objects in ~24 host singletons.** `IPluginRegistry`
   (`unified-host-runtime.md` §3.4) has twenty-four `Add*` methods, every one of which hands a
   plugin-constructed object to a long-lived host registry — media shape, item source, four matchers and
   planners, five provider families, jobs, health contributors, three telemetry seams, redaction rules,
   diacritic folding, an outbound HTTP interceptor, event handlers, an intent surface. Unload requires
   dropping *all* of them plus everything transitively reachable, and one missed reference fails silently
   with no diagnostic beyond "still alive". §7.2 enumerates nine distinct blocker classes; four of them
   (in-flight async continuations, `ThreadLocal` on pool threads, native libraries, and a debugger or
   profiler attached) are not fixable by discipline in the loader alone, and the shared
   `JsonSerializerOptions` reflection cache is fixable only by a real design change.

3. **The value is small and the failure mode is bad.** The thing an operator actually wants is "stop this
   plugin misbehaving *now*" and "install a plugin without losing my download queue". The first is
   delivered by **soft disable** (§7.5) — an immediate dispatch gate that stops every call into the plugin
   while its assemblies stay resident — which needs no unload at all. The second is worth exactly one
   process restart, which for a media manager is seconds. A hot-reload that works on the developer's
   machine and pins the ALC in production gives you a process that leaks an entire assembly closure per
   upgrade and a bug report nobody can reproduce.

What is *not* being given up: `isCollectible: true` stays (the spec already mandates it, §4.1), the
registration ledger stays a single droppable graph, and §7.4 lists the five design rules that keep true
unload reachable later. §7.6 states the exact completion condition that would un-defer it.

---

## Resolution table — the contested calls

Every non-obvious decision in this document, with the reason on one line. Format matches
`docs/arronix-common-extraction-plan.md`.

| # | Question | Options | Resolution | One-line reason |
|---|---|---|---|---|
| D1 | Container format | zip / tar.gz / a bespoke framed format / OCI artifact | **zip (`.arxpkg`)** | Only zip has a central directory, so the manifest and signature can be read and verified *without* decompressing or extracting the payload — which is the whole of the static-inspection-first ordering. |
| D2 | Does the installer use `Arronix.Common`'s `IArchiveService`? | yes / no | **No — a dedicated `PluginPackageReader`** | `IArchiveService.CreateZipAsync` flattens directories so it cannot even *produce* a package, and package-specific entry policy (caps, roots, ratio limits, deterministic ordering) does not belong in a general archive service. **Recommend amending its XML doc**, which currently claims it handles "signed extension packages it installs" (`src/Arronix.Common/Archives/IArchiveService.cs:5-6`). |
| D3 | One manifest or two? | extend `plugin.json` / a second distribution manifest | **Two: `plugin.json` (runtime, spec-owned, verbatim) + `package.json` (distribution)** | The runtime manifest is read by the loader on every start and must stay small and stable; publisher, license, per-file digests, platform variants and index metadata are the *installer's* problem and are consumed and discarded at install. `package.json` never lands in the install directory, which also disposes of the npm-name-collision objection. |
| D4 | Where do bundled non-code assets live? | `assets/` in the package / embedded resources | **Embedded resources only** | The ALC loads **from stream** (spec §4.1), so `Assembly.Location` is `""` and `AppContext.BaseDirectory` is the *host's* — a plugin physically cannot find a file it shipped without a new `IPluginPaths.InstallFolder` contract member. Adding a contract member to reach data that could be embedded is the wrong trade. |
| D5 | Payload layout inside the package | flat / `lib/` subtree | **`lib/` subtree, extracted to the install-directory root** | `AssemblyDependencyResolver` needs the standard `dotnet publish` layout (`<entry>.deps.json` adjacent, `runtimes/<rid>/native/`), and the spec requires `plugin.json` *beside the plugin assembly* with discovery at `{root}/*/plugin.json`; `lib/` exists inside the package only to separate installable payload from installer-only metadata. |
| D6 | Multi-platform packaging | one fat package / one package per RID | **Both, index-declared; fat is the default** | A managed-only plugin (all four first-party ones) is RID-agnostic and a single artifact is simpler; a plugin with native assets may publish per-RID artifacts and the index selects on `RuntimeInformation.RuntimeIdentifier`. |
| D7 | May a plugin ship an assembly that is in the shared framework? | yes, newest wins / no | **No — reject at package validation** | The host hands the plugin `IJsonSerializer` and DTOs; a second `System.Text.Json` makes `JsonSerializerOptions` a *different* `Type` and every cross-boundary call fails with a cast error nobody can read. |
| D8 | May a plugin ship `Arronix.Abstractions.dll`? | yes / no | **No — reject at package validation, and the ALC returns `null` for it regardless** | Belt and braces on the spec's §4.1 step 2: a second Abstractions makes the plugin's `IPluginModule` a different `Type` and no cast ever succeeds — the most common ALC bug, and it is silent. |
| D9 | What happens when a plugin's private dependency is also in the *host's* (non-framework, non-blocked) closure? | unify on the host's / plugin wins | **Plugin wins; both copies coexist** | Nothing in `Arronix.Abstractions` names a third-party type — the zero-package rule is precisely what makes per-plugin dependency isolation work rather than merely look like it works. |
| D10 | Signature algorithm | Authenticode / CMS-PKCS#7 / X.509 chain + PKI / GPG / detached ECDSA over SPKI keys | **Detached ECDSA P-256 / SHA-256, raw SPKI public keys** | Verified: `System.Security.Cryptography.Pkcs` is **not** in the `net10.0` ref pack, so CMS costs a package; Authenticode is Windows-only; a PKI needs a CA and revocation infrastructure this project will not run. `ECDsa` + `ImportSubjectPublicKeyInfo` are in-box and cross-platform. |
| D11 | What does the signature cover? | the archive bytes / a digest ladder | **A digest ladder: sign `sha256(canonical package.json)`; `package.json` carries `sha256` of every payload entry** | Signing the whole archive forbids any re-packing and makes partial (code-only) re-verification at load impossible; the ladder gives per-file verification for one signature. It loses nothing: `package.json` enumerates every installable entry and is itself the signing input, so one signature still covers every byte — while remaining checkable against the *installed directory*, which is where files actually get swapped. (`threat-model.md` §4.4's whole-archive requirement is reconciled to this.) |
| D12 | Are unsigned plugins allowed? | yes / no / configurable | **Configurable, floor defaults to `SelfSigned`, unsigned raises a permanent health warning** | Refusing unsigned outright kills community plugins and forces everyone to `AllowUnsigned=true` on day one, which is worse than an honest, visible trust tier. |
| D13 | Self-signed trust semantics | reject / trust-on-first-use with key pinning | **TOFU with pinning; an upgrade signed by a different key is refused until the operator re-approves** | Self-signing buys **continuity**, not identity; pinning is the only thing that converts that into a real property, and it costs one field in `state.json`. |
| D14 | Where does plugin *data* live? | inside the install directory / a separate root | **A separate root, `{AppData}/plugin-data/{pluginId}/`** | `IPluginPaths.DataFolder` documents "data that must survive a restart **and an upgrade**" (`src/Arronix.Abstractions/Hosting/IPluginPaths.cs:28-31`); the only way to honor that is to never place it where an upgrade or uninstall deletes a tree. Lidarr does the opposite and `DeleteFolder(recursive)`s the whole plugin folder on uninstall (`InstallPluginService.cs:81-96`). |
| D15 | Does uninstall delete plugin configuration? | yes / no | **No. Never.** Uninstall retains data *and* database rows | The product decision, and the direct inversion of Prowlarr's `RemoveMissingImplementations`, which `Delete`s every stored definition whose implementation vanished (`_reference/Prowlarr/src/NzbDrone.Core/ThingiProvider/ProviderFactory.cs:194-203`) — under a plugin model that means uninstalling destroys the user's setup. |
| D16 | How is destructive removal expressed then? | a flag on uninstall / a separate operation | **A separate `purge` operation requiring the plugin id typed as confirmation** | If "never silently destroy" is the rule, destruction needs its own verb and its own friction; a boolean on a DELETE is exactly the shape that gets passed by accident. |
| D17 | Rollback of plugin *data* | best-effort reverse migration / refuse | **Refuse: a plugin whose stored `dataSchemaVersion` exceeds its own is quarantined** | Reverse data migration is unwritable in the general case, and a downgraded plugin reading forward-migrated rows is silent corruption — the one failure class worse than a refusal. |
| D18 | `current`-version selection on disk | symlink / junction / pointer file | **A one-line JSON pointer file** | Symlink creation on Windows needs Developer Mode or admin, junctions are NTFS-and-directory-only, and the APIs differ per platform — for a value the loader reads once per start. |
| D19 | Host-upgrade compatibility | warn afterwards / preflight before | **Preflight before, with a blocking operator decision and a co-ordinated two-phase upgrade** | Discovering that three of five plugins went dark *after* restarting is the failure this whole section exists to prevent. |
| D20 | Does a media kind vanish when its plugin is quarantined? | yes (silently) / no | **No — the host synthesizes an "unavailable" entry from the last successful activation** | Silent disappearance of TV from the UI is indistinguishable from data loss; the operator must see *why*. Requires one additive member on `PluginStatusView` (WP-6). |
| D21 | Registry shape | a service with a search API / a static JSON index | **Static JSON on object storage, signed with the same scheme** | There are **no inter-plugin dependencies** (`ARCHITECTURE.md` §4 forbids cross-plugin calls), so the index needs no solver, no server and no database — it is a file, mirrorable and air-gappable by copy. |
| D22 | How does a plugin advertise its host compatibility to the index? | scrape it from release notes / declare it | **Declared in `package.json`, copied into the index, re-checked against the downloaded package** | Lidarr parses "Minimum Lidarr Version" out of a GitHub release **body** with a regex (`PluginService.cs:229-238`, `:249`). A version constraint read out of prose is not a constraint. |
| D23 | Where does install work run? | inline in the API request / the job queue | **The job queue, as `plugin-install` / `plugin-registry-refresh` jobs** | `ARCHITECTURE.md` §16: one scheduler, one queue. Downloads are long, cancellable and want progress reporting, all of which the queue already does. |
| D24 | Unload / hot-reload | ship it / restart-required | **Restart-required; unload is a diagnostic only** | §0 and §7. |
| D25 | Does disable require a restart? | yes / no | **No — disable is an immediate dispatch gate; assemblies stay resident until restart** | This is the operator need that hot-reload is usually asked for, and it is achievable honestly without unload. |

---

## 1. The package format

### 1.1 What a distributable plugin is

A **plugin package** is a zip archive with the extension `.arxpkg`, named
`{pluginId}-{version}[-{rid}].arxpkg` (`tv-0.4.1.arxpkg`, `music-1.2.0-linux-x64.arxpkg`).

```text
tv-0.4.1.arxpkg                    (zip)
├── plugin.json                    runtime manifest — unified-host-runtime.md §3.5, verbatim
├── package.json                   distribution manifest + per-file digests (INSTALLER ONLY)
├── signature.json                 detached signature block (absent ⇒ unsigned)
├── LICENSE                        required; surfaced pre-download by the index
├── README.md                      optional; first paragraph becomes the index summary
├── CHANGELOG.md                   optional
└── lib/                           the `dotnet publish` output, verbatim
    ├── Arronix.Plugin.Tv.dll      the entry assembly named by plugin.json
    ├── Arronix.Plugin.Tv.deps.json
    ├── Arronix.Plugin.Tv.pdb      optional; portable PDBs only
    ├── AngleSharp.dll             the plugin's private third-party closure, flat
    ├── AngleSharp.Xml.dll
    ├── de/Arronix.Plugin.Tv.resources.dll     satellite assemblies, if any
    └── runtimes/
        ├── linux-x64/native/libfoo.so
        └── osx-arm64/native/libfoo.dylib
```

**Only four roots are legal:** `plugin.json`, `package.json`, `signature.json`, `lib/**`, plus the three
optional documentation files. Anything else is a validation failure. A closed root set is what makes the
extraction policy in §1.6 checkable rather than aspirational.

**Install mapping.** `lib/**` is extracted to the install-directory root, and `plugin.json` is written
beside it:

```text
{AppData}/plugins/tv/versions/0.4.1/
├── plugin.json
├── Arronix.Plugin.Tv.dll
├── Arronix.Plugin.Tv.deps.json
├── AngleSharp.dll
└── runtimes/linux-x64/native/libfoo.so
```

This is exactly the layout the spec's loader expects: `plugin.json` shipped *"beside the plugin assembly"*
(§3.5), discovery by enumerating `{root}/*/plugin.json` (§4.1 step 1), and
`AssemblyDependencyResolver(entryAssemblyPath)` finding `Arronix.Plugin.Tv.deps.json` adjacent.
`package.json` and `signature.json` are **consumed by the installer and never written to the install
directory** — their contents are folded into `state.json` (§4.1).

### 1.2 Why zip, and why not `IArchiveService`

Three formats were considered.

| | zip | tar.gz | bespoke framed format |
|---|---|---|---|
| Read one entry without reading the rest | **yes** (central directory) | no — gzip is a single stream | yes, if you write the code |
| In-box in `net10.0` | `System.IO.Compression` | `System.Formats.Tar` (verified present) | — |
| Tooling every publisher already has | yes | yes | no |
| Deterministic output achievable | yes, with fixed order/timestamps | yes | yes |

The decisive property is **random access**. The verification ordering demanded by the runtime spec is
static-inspection-first: manifest, then contract range, then reference graph, *then* an ALC. Applied to a
package, that means reading `plugin.json` and `signature.json` and deciding whether to trust the thing
**before** unpacking a single payload byte. With tar.gz that requires streaming and buffering the entire
archive first; with zip it is two `ZipArchive.GetEntry` calls against the central directory. That alone
settles it.

**The installer does not use `Arronix.Common.Archives.IArchiveService`, and that is a deliberate
recommendation against the obvious reuse.** Three reasons:

1. `CreateZipAsync` stores each file *"under its own file name, with no directory part"*
   (`src/Arronix.Common/Archives/IArchiveService.cs:56-59`). It cannot produce a package with a `lib/`
   subtree, so the packing half is unusable regardless.
2. `ExtractAsync` chooses the format by extension and *"An unrecognized extension is refused rather than
   guessed at"* (`:44-48`). `.arxpkg` is not in its list, and widening that list to admit a
   plugin-specific extension pushes plugin knowledge into a general archive service.
3. The package extraction policy (§1.6) — entry-count and size caps, compression-ratio limits, a closed
   root set, per-entry digest checking interleaved with extraction — is package-specific. `IArchiveService`
   promises zip-slip rejection (`:38-41`) and nothing else, correctly.

Instead: `src/Arronix.Plugins/Packaging/PluginPackageReader.cs`, over `System.IO.Compression.ZipArchive`
directly. No new package reference; `System.IO.Compression` is in the shared framework.

> **Recommended amendment (WP-owner: whoever owns `src/Arronix.Common/Archives/`).** The
> `IArchiveService` summary currently reads *"backups it writes, update packages it downloads and signed
> extension packages it installs"* (`IArchiveService.cs:5-6`). The third clause is no longer true. It
> should read *"backups it writes and update packages it downloads"*, with a `<remarks>` line pointing at
> `Arronix.Plugins.Packaging.PluginPackageReader` for plugin packages and saying why.

### 1.3 `plugin.json` — unchanged, plus one requested field

The runtime manifest is `unified-host-runtime.md` §3.5 verbatim. This document requests **one additive
field**, owned by WP-7 (`Arronix.Plugins/Manifest/PluginManifest.cs`):

```jsonc
{
  "schemaVersion": 0,
  "id": "tv",
  "name": "TV",
  "version": "0.4.1",
  "contracts": { "arronix": ">=0.3 <0.4" },
  "entryAssembly": "Arronix.Plugin.Tv.dll",

  // NEW. The version of the plugin's own persisted data shape. Absent ⇒ 0 ⇒ the plugin stores nothing.
  // The host records the highest value ever activated and refuses to activate a plugin whose value is
  // LOWER than the recorded one (§4.6). This is the whole of rollback safety, and it costs one integer.
  "dataSchemaVersion": 2,

  "mediaKinds": ["tv"],
  "identifiers": ["tvdb", "tmdb", "imdb"],
  "capabilities": ["media-kind", "parsing", "matching", "indexing", "quality", "renaming", "import"],
  "tokens": [ { "name": "{Series Title}", "description": "…", "exampleValue": "…", "isRequired": true } ],
  "policies": { "parsing": [...], "matching": [...], "quality": [...], "import": [...], "naming": [...] }
}
```

Everything else a distributor wants to say — author, license, homepage, support URL, platform variants,
digests — is **not** here. The loader reads `plugin.json` on every start; it should carry only what the
loader needs, and none of that list is.

### 1.4 `package.json` — the distribution manifest

```jsonc
{
  "packageSchemaVersion": 1,

  // Identity. MUST match plugin.json exactly; a mismatch is PluginPackageMismatch.
  "id": "tv",
  "version": "0.4.1",

  // Compatibility, declared — never scraped (D22).
  "contracts": { "arronix": ">=0.3 <0.4" },     // copied from plugin.json, cross-checked. The ONLY version gate.
  "targetFramework": "net10.0",
  "platforms": ["any"],                          // or ["linux-x64", "osx-arm64", "win-x64"]

  // What the operator is consenting to, visible before download (§6.4).
  "capabilities": ["media-kind","parsing","matching","indexing","quality","renaming","import"],
  "mediaKinds": ["tv"],
  "dataSchemaVersion": 2,

  // Provenance.
  "publisher": { "name": "Arronix", "url": "https://github.com/…", "keyId": "arx-official-1" },
  "license": "GPL-3.0-or-later",
  "homepage": "https://github.com/…",
  "releaseNotesUrl": "https://github.com/…/releases/tag/v0.4.1",
  "publishedAt": "2026-08-04T09:12:00Z",

  // The digest ladder (D11). Every installable entry, ordinal by path. Excludes package.json and
  // signature.json, which cannot appear in a list they are the integrity root of.
  "files": [
    { "path": "plugin.json",                   "size": 1183,    "sha256": "b1e2…" },
    { "path": "lib/Arronix.Plugin.Tv.dll",     "size": 184320,  "sha256": "9fa0…" },
    { "path": "lib/Arronix.Plugin.Tv.deps.json","size": 2841,   "sha256": "44c7…" },
    { "path": "lib/AngleSharp.dll",            "size": 1093632, "sha256": "0d31…" },
    { "path": "LICENSE",                       "size": 35149,   "sha256": "8ceb…" }
  ]
}
```

**There is one version constraint in the package, and it is `contracts.arronix`.** It is checked against
the host's `Arronix.Abstractions` version, exactly as the spec's §4.1 step 5 already does.

An earlier draft also carried `"hostVersions": ">=0.3.0"`, marked *DISPLAY METADATA ONLY*, retained on the
grounds that "naming which one is decorative is cheaper than deleting it". It is deleted. A second version
range sitting in the manifest and in every registry index entry, next to the real one, differing from it,
and enforced by nobody is precisely the field some future implementer wires into a check — at which point
two constraints disagree and the package that satisfies the real gate is refused by the decorative one. The
cost of deleting it now is one field in a format with no published packages; the cost of deleting it later
is a format change plus a re-index.

The UI question it existed to answer — *"which Arronix is this for?"* — is answered without a stored field:
`GET /api/v1/system/status` returns the host's application version and its contract version (§5.1), and the
client renders the compatible host range by resolving `contracts.arronix` against the contract versions the
host knows. That is a derived string, so it cannot drift from the gate, because it *is* the gate.

### 1.5 A plugin's private third-party assemblies

The plugin's own dependency closure ships **flat inside `lib/`, exactly as `dotnet publish` emits it**,
together with its `.deps.json`. That file is not decoration: `AssemblyDependencyResolver` — which the
spec's `PluginLoadContext` already uses (§4.1) — parses `<entryAssemblyName>.deps.json` from the directory
next to the entry assembly and is the sole reason RID-specific native assets and satellite resource
assemblies resolve at all. A package without it loads the entry assembly and then fails on the first
dependency, at an arbitrary point during `Configure`.

Publishing rules for a plugin author, stated as a checklist because every one of them has a failure mode:

| Rule | Why |
|---|---|
| `dotnet publish -c Release` (not `build`) | `build` does not emit the transitive closure or `runtimes/`. |
| `<EnableDynamicLoading>true</EnableDynamicLoading>` | Emits a `.deps.json` for a library project and copies references locally. Without it there is no deps file. |
| Do **not** self-contain | A self-contained publish drags the entire framework in; every one of those assemblies is rejected by D7. |
| `<SatelliteResourceLanguages>` trimmed to what you ship | Otherwise `lib/` grows a directory per locale of every dependency. |
| Portable PDBs only, optional | Embedded PDBs bloat the assembly; Windows PDBs are not portable. |
| Nothing named `Arronix.*` in the closure except the entry assembly | D8, and the spec's static reference-graph check (§4.1 step 6) rejects it anyway — better to fail at pack time. |

The reference plugins (`Arronix.Plugin.{Tv,Movies,Music,Books}`, spec WP-13..16) take **zero** package
references, so their `lib/` is two files. That is the intended shape for a first-party plugin, and the
machinery above exists for the community ones.

### 1.6 Extraction policy, and reproducibility

`PluginPackageReader` enforces, before writing anything:

| Check | Limit | Failure |
|---|---|---|
| Entry path is relative, no `..` segment, no rooted path, no backslash, no drive letter | — | `PluginPackageCorrupt` |
| Entry path begins with one of the four legal roots | closed set (§1.1) | `PluginPackageCorrupt` |
| Duplicate entry names (case-insensitive, after normalization) | 0 allowed | `PluginPackageCorrupt` |
| Entry count | `MaxEntryCount`, default 4096 | `PluginPackageCorrupt` |
| Single entry uncompressed size | 512 MB | `PluginPackageCorrupt` |
| Total uncompressed size | `MaxPackageBytes`, default 256 MB | `PluginPackageCorrupt` |
| Per-entry compression ratio | `MaxCompressionRatio`, default 200:1 | `PluginPackageCorrupt` |
| Every entry in `package.json.files[]` present, and vice versa | exact set equality | `PluginPackageMismatch` |
| Per-entry SHA-256 recomputed **while streaming to disk** | must match `files[]` | `PluginPackageCorrupt` |

Digests are checked *during* extraction rather than in a separate pass, so the file that lands on disk is
the file that was hashed — a two-pass check over a temporary directory is a TOCTOU window for no benefit.
Extraction goes to `{root}/.staging/{pluginId}/{version}.tmp/` and is `Directory.Move`d into place only
after the last digest matches; a partially-extracted install is never visible to the loader.

**Reproducibility.** The packer writes a deterministic zip: entries ordered ordinal by path, timestamps
fixed to `1980-01-01T00:00:00Z`, no directory entries, forward slashes only, a fixed compression level, no
extra fields, and UTF-8 names. `sha256(package)` is therefore a function of the content alone, which is
what lets the registry index publish a digest that two independent builds can both produce and lets a
mirror be verified without trusting the mirror.

---

## 2. Dependency isolation in practice

This is where plugin systems break, so this section is a specification, not a sketch.

### 2.1 The probing order

The runtime calls `PluginLoadContext.Load(AssemblyName)` **only on a miss** — assemblies already loaded in
this ALC are returned before the override runs, and that implicit step 0 is why a plugin never gets two
copies of its own dependency.

```csharp
// src/Arronix.Plugins/Loading/PluginLoadContext.cs   (extends unified-host-runtime.md §4.1)
protected override Assembly? Load(AssemblyName name)
{
    var simpleName = name.Name!;

    // 1. HARD DENY. Throw, never return null. Returning null falls back to the Default context and
    //    SUCCEEDS, silently granting the plugin Arronix.Common. Spec §4.1, resolution #21.
    if (BlockedPrefixes.Any(p => simpleName.StartsWith(p, StringComparison.Ordinal)))
        throw new PluginIsolationException(simpleName);          // PluginIsolationViolation 2008

    // 2. THE ONE SHARED CONTRACT. Return null so the Default context wins and the plugin's
    //    IPluginModule is the SAME Type as the host's. Version is not negotiated here: the contract
    //    range was already checked at pipeline step 5, and by D8 the package cannot even contain a copy.
    if (simpleName is "Arronix.Abstractions") return null;

    // 3. SHARED FRAMEWORK. Return null. A plugin never gets its own System.Text.Json (D7).
    if (_sharedFramework.Contains(simpleName)) return null;

    // 4. THE PLUGIN'S OWN CLOSURE, via its .deps.json. Load FROM STREAM so no file is locked and the
    //    context stays collectible (Lidarr's proven lesson, PluginLoader.cs:37-48).
    var path = _resolver.ResolveAssemblyToPath(name);
    if (path is not null)
        return LoadFromStream(new MemoryStream(File.ReadAllBytes(path)));

    // 5. UNRESOLVED. Return null and let Default try. This is safe ONLY because step 1 already threw for
    //    every Arronix implementation assembly; what remains reachable here is framework-adjacent and
    //    almost always ends in a FileNotFoundException naming the assembly, which is the right message.
    return null;
}
```

**Step 3's set is built once, at host startup, from the runtime's own view of itself — not from a
hard-coded list:**

```csharp
// src/Arronix.Plugins/Loading/SharedFrameworkAssemblies.cs
internal static FrozenSet<string> Build()
{
    var frameworkDirs = new[]
    {
        RuntimeEnvironment.GetRuntimeDirectory(),                       // Microsoft.NETCore.App
        Path.GetDirectoryName(typeof(WebApplication).Assembly.Location) // Microsoft.AspNetCore.App
    };

    // TRUSTED_PLATFORM_ASSEMBLIES lists framework AND application assemblies. Filtering to the shared
    // framework directories is what keeps Arronix.Common out of the set — without the filter this would
    // hand every plugin the host's implementation assemblies through step 3, which is the exact hole
    // step 1 exists to close.
    return ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
        .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
        .Where(p => frameworkDirs.Contains(Path.GetDirectoryName(p), StringComparer.Ordinal))
        .Select(Path.GetFileNameWithoutExtension)
        .ToFrozenSet(StringComparer.OrdinalIgnoreCase)!;
}
```

The filter is load-bearing and gets its own test: `SharedFrameworkAssembliesTests` asserts the set
contains `System.Text.Json` and `System.Private.CoreLib` and does **not** contain `Arronix.Common`,
`Arronix.Host`, `Microsoft.Extensions.Options` or `NzbDrone.Common`.

### 2.2 Contextual reflection — the step everyone forgets

`Load` is only consulted for the *static* reference graph. A plugin calling `Assembly.Load("AngleSharp")`,
`Type.GetType("AngleSharp.HtmlParser, AngleSharp")` or anything reflection-based resolves against
`AssemblyLoadContext.CurrentContextualReflectionScope`, which defaults to **Default** — so the call misses
the plugin's own ALC entirely and fails, or worse, silently binds to a host-side copy.

Every entry into plugin code is therefore wrapped:

```csharp
// src/Arronix.Plugins/Loading/PluginLoader.cs
using (_context.EnterContextualReflection())
{
    module.Configure(pluginContext);
}
```

The same wrap goes around every host→plugin dispatch (`IScheduledJob.ExecuteAsync`, provider invocations,
`IMediaItemSource.QueryAsync`, health contributors), implemented once in the scoping decorators of spec
§4.2 rather than at 24 call sites.

### 2.3 Two plugins, two versions of one package — worked

Plugin `music` ships `AngleSharp 1.1.2`; plugin `books` ships `AngleSharp 0.17.1`.

| | `music` | `books` |
|---|---|---|
| ALC | `arronix-plugin:music` | `arronix-plugin:books` |
| Resolution | step 4 → `{…}/music/versions/1.2.0/AngleSharp.dll` | step 4 → `{…}/books/versions/0.9.0/AngleSharp.dll` |
| Resulting `Type` | `AngleSharp.Html.Dom.IHtmlDocument` in ALC `music` | a **different** `Type` with the same full name, in ALC `books` |
| Do they ever meet? | **No.** | |

They cannot meet, and the reason is structural rather than lucky:

- `ARCHITECTURE.md` §4: *"No direct cross-plugin calls; interaction flows through Core contracts /
  events."* There is no call path from one plugin to another.
- `Arronix.Abstractions` has **zero package references** and mentions no third-party type, so no value of a
  plugin-private type can be expressed in any signature that crosses the boundary. **The zero-package rule
  in Abstractions is not hygiene — it is the mechanism that makes per-plugin dependency isolation actually
  work rather than merely appear to.** This is worth stating loudly because it is the single design
  decision that most plugin systems get wrong and then patch forever.
- The one remaining channel is events. `IEventPublisher`/`IEventHandler<TEvent>` are constrained to
  `IDomainEvent`, which lives in Abstractions; a plugin publishing an event carrying a plugin-private type
  would be publishing a type the subscriber's ALC cannot load. `FilteredEventPublisher` (spec §4.2) already
  restricts a plugin to platform events plus its own namespace, so the cross-plugin case does not arise —
  but the **serialization** case does, and §7.2 blocker 9 covers it.

Cost: both copies are resident. That is the price of isolation and it is the right price. It is made
visible rather than hidden — `GET /api/v1/plugins/{id}/assemblies` lists each ALC's loaded assemblies with
name, version and size, so "why is the process 400 MB" has an answer an operator can read.

### 2.4 Conflict with a host assembly

Four distinct cases, with four different answers. Getting these confused is the usual source of "it works
in dev, it explodes in the container".

| The plugin ships… | Example | Result | Where enforced |
|---|---|---|---|
| An **Arronix implementation** assembly | `Arronix.Common.dll` | Rejected at **pack** time; rejected at **discovery** by the static reference-graph inspector (spec §4.1 step 6); and the ALC **throws** if it is ever asked (step 1) | three layers, deliberately |
| **`Arronix.Abstractions`** | `Arronix.Abstractions.dll` | Rejected at package validation (D8); the ALC returns `null` regardless so Default always wins | §1.6 + step 2 |
| A **shared-framework** assembly | `System.Text.Json.dll` | Rejected at package validation (D7); the ALC returns `null` regardless so the in-box one always wins | §1.6 + step 3 |
| A **host-referenced but non-framework, non-Arronix** assembly | `Microsoft.Extensions.Options.dll`, `Polly.dll` | **Allowed. The plugin's copy wins inside its ALC. Two copies coexist.** | step 4, by design (D9) |

The fourth row is the interesting one and it is safe *by construction*: `IPluginContext` (spec §3.3)
exposes not one `Microsoft.Extensions.*` or third-party type. Its entire surface is `PluginId`, three
strings, `CapabilitySet`, `IPluginRegistry`, and eight Abstractions interfaces plus `TimeProvider` — and
`TimeProvider` is BCL, hence shared-framework, hence unified by step 3. There is no member through which a
divergent `IOptions<T>` could travel.

The failure this prevents, spelled out because it is the one people actually hit: if `Arronix.Abstractions`
had taken a single `PackageReference` — say `Microsoft.Extensions.Logging.Abstractions` for an
`ILogger`-shaped member — then a plugin shipping a different version of that package would produce an
`ILogger` whose `Type` differs from the host's, and every `Configure` call would fail with
`InvalidCastException: Unable to cast object of type 'ILogger' to type 'ILogger'`. That is precisely why
the spec refuses an `IPluginLogger` and routes diagnostics through the package-free `ITelemetryEmitter`
(§3.7). The isolation design and the zero-package rule are the same decision viewed from two angles.

### 2.5 Native libraries

```csharp
protected override nint LoadUnmanagedDll(string unmanagedDllName)
{
    // 1. The plugin's own runtimes/<rid>/native/, via its .deps.json.
    var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
    if (path is not null) return LoadUnmanagedDllFromPath(path);

    // 2. Registered INativeLibraryResolver instances, in registration order, per the contract's own
    //    documented semantics (src/Arronix.Abstractions/Hosting/INativeLibraryResolver.cs:20-29).
    foreach (var resolver in _nativeResolvers)
        if (resolver.Resolve(unmanagedDllName) is { } resolved)
            return NativeLibrary.Load(resolved);

    // 3. Default probing.
    return nint.Zero;
}
```

Three honest hazards, none of which the managed isolation model fixes:

1. **The OS loader unifies by SONAME, not by ALC.** Two plugins each shipping `libfoo.so.1` get *one*
   mapping on Linux with the first-loaded winning; on Windows `LoadLibrary` unifies by module name for
   already-loaded modules. Managed isolation does not extend to native code, ever. The mitigation is a
   packaging rule, not a runtime one: a plugin shipping natives should ship uniquely-named or statically
   linked ones. This is documented in the publisher guide and validated only advisorily — the packer warns
   when two installed plugins ship natives with a colliding file name.
2. **Native libraries never unload.** `ALC.Unload()` does not `dlclose`. See §7.2 blocker 6.
3. **Windows file locks.** A loaded native DLL cannot be replaced on disk, so upgrading a plugin with
   native assets cannot reuse the install directory. This is already handled: §4.1 gives every version its
   own directory and never writes into a live one.

### 2.6 What still breaks — the honest list

| Hazard | Mitigation | Residual |
|---|---|---|
| Plugin's `.deps.json` missing or stale | Package validation requires `lib/{entryAssembly}.deps.json` to exist and to name every `lib/*.dll` | A hand-edited deps file can still lie; the failure is a clean `FileNotFoundException` at load |
| Diamond within one plugin (two deps needing different versions of a third) | NuGet already flattened it at publish; the loader sees one file | If the flattened version is wrong, that is the plugin's bug and it fails inside its own ALC |
| Native SONAME collision across plugins | packaging guidance + a packer warning | Real. Not solvable in-process. |
| A plugin P/Invoking into the host process | none | Real, and out of scope here — the threat model owns it (§3.6) |
| Reflection into host internals via `AppDomain.CurrentDomain.GetAssemblies()` | none at the ALC level | Real. `EnterContextualReflection` scopes *resolution*, not *enumeration*. Threat model owns it. |
| Memory cost of duplicated closures | visibility via `/plugins/{id}/assemblies` | Accepted |

---

## 3. Signing and verification

### 3.1 What is signed

The digest ladder (D11), bottom-up:

```text
every installable file  ──sha256──▶  package.json.files[]
package.json (canonical bytes)  ──sha256──▶  the signing input
                                 ──ECDSA-P256/SHA-256──▶  signature.json.signatures[].signature
```

One signature therefore covers every byte of the package, while still permitting **partial
re-verification**: at load time the host can re-check the digests of `plugin.json` and `lib/*.dll` alone
without re-hashing a 200 MB payload, because each of those has its own entry in a list that is itself
covered by the signature.

"Canonical bytes" means literally the bytes of the `package.json` entry as stored in the zip. No JSON
canonicalization scheme, no re-serialization, no key ordering rules. Canonicalizing JSON is a well-known
source of signature-bypass bugs; hashing the octets that were actually stored has none of that surface and
costs nothing, because the packer writes the file once.

### 3.2 `signature.json`

```jsonc
{
  "signatureSchemaVersion": 1,
  "signed": { "file": "package.json", "sha256": "3b7c…" },
  "signatures": [
    {
      "keyId": "arx-official-1",
      "alg": "ES256",
      // base64 SubjectPublicKeyInfo. Present even for a trusted key: it lets an air-gapped verifier work
      // from the package alone, and the trust decision still comes from matching it against the store.
      "publicKey": "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE…",
      // base64, DER SEQUENCE per RFC 3279.
      "signature": "MEUCIQDf…",
      "signedAt": "2026-08-04T09:12:04Z"
    }
  ]
}
```

`signatures` is a list so a package can carry a publisher signature **and** a registry counter-signature;
verification succeeds when *at least one* signature satisfies the configured trust floor, and the
resulting trust level is the highest any single signature achieves.

**Algorithm selection, and what was rejected.**

| Option | Verdict | Reason |
|---|---|---|
| **ECDSA P-256 / SHA-256, raw SPKI keys** | **adopted** | `ECDsa.ImportSubjectPublicKeyInfo` + `VerifyData(…, DSASignatureFormat.Rfc3279DerSequence)` are in `System.Security.Cryptography`, **verified present** in the `net10.0` `Microsoft.NETCore.App` ref pack. Zero packages, cross-platform, 64-byte keys, no chain machinery. |
| CMS / PKCS#7 detached | rejected | `System.Security.Cryptography.Pkcs.dll` is **not** in the `net10.0` ref pack (verified against the installed 10.0.9 ref pack). It costs a package registration for a format whose only advantage is tooling we do not use. |
| Authenticode | rejected | Windows-only signing and verification. A Linux-hosted media manager cannot verify it. |
| X.509 chains + a real PKI | rejected | Requires a CA, an issuance process, revocation distribution and expiry handling. This project will run none of those, and a PKI nobody operates degrades into a self-signed cert with extra steps. |
| GPG / `gpg --verify` | rejected | An external binary dependency and a keyring outside the process, in a product that ships as a self-contained service and a container. |
| **Ed25519 / minisign** | rejected — **and this settles a conflict with `threat-model.md`** | `threat-model.md` §4.4 previously required a detached Ed25519/minisign signature over the whole archive. There is no in-box Ed25519: the `net10.0` ref pack's `System.Security.Cryptography` exposes `ECDsa`, `MLDsa` and `SlhDsa`, and `Ed25519` occurs in it only inside the composite ML-DSA hybrid names (`MLDsa65WithEd25519`) — no standalone signature type. Mandating it means a BouncyCastle or NSec dependency in the load path of the one component that must work before any plugin code runs. minisign additionally reintroduces the external binary and out-of-process keyring rejected for GPG one row above. `threat-model.md` §4.4 has been reconciled to this row and to D11. |
| Sigstore / cosign | rejected | Verification requires a network call to a transparency log, so it fails closed in exactly the air-gapped installs that most want reproducible verification. |
| ML-DSA-65 (post-quantum) | **reserved, not required** | `System.Security.Cryptography.MLDsa` exists in .NET 10, but `MLDsa.IsSupported` depends on OpenSSL 3.5+ / recent Windows CNG, so it is not portable enough to mandate. `"alg": "ML-DSA-65"` is a reserved value; the verifier's allow-list is a closed set of one today. |

`alg` is validated against a closed allow-list before any parsing — an unknown algorithm is a verification
failure, never a skip. The classic signature-bypass bug is `alg: "none"`, and the way not to have it is a
closed set rather than a lookup with a default.

### 3.3 When verification happens

**At install** — full verification. Signature, then every file's digest as it is extracted (§1.6). The
resulting trust level and the pinned `keyId` are written into `state.json`.

**At every load** — re-verification, because the install directory is a normal directory that anything on
the machine can edit between yesterday's install and today's restart. Slotted into the spec's §4.1
pipeline as **step 5a, between the contract-range check (step 5) and the static reference-graph check
(step 6)** — which puts it before ALC creation (step 7), satisfying the static-inspection-first ordering:

| # | Step | Failure code |
|---|---|---|
| 5 | Contract range + experimental gate | `PluginContractMismatch` 2001 |
| **5a** | **Package integrity: recompute digests per `Arronix:Plugins:VerifyOnLoad`; if the recorded trust level is not `Unsigned`, re-verify the signature against the pinned key** | **`PluginPackageCorrupt` 2012 / `PluginSignatureInvalid` 2011** |
| 6 | Static reference-graph check | `PluginIsolationViolation` 2008 |
| 7 | Create the ALC | `PluginLoadFailure` 2000 |

New `CoreErrorCode` values, continuing the spec's own 2003–2010 block. Values are appended rather than
interleaved because a stable numbering makes a code greppable across issues, logs and support bundles —
not because renumbering would be forbidden; below 1.0.0 it would not be (`docs/contracts/stability.md`),
and no `CoreErrorCode` value is persisted anywhere:

```
PluginSignatureInvalid = 2011      PluginPackageCorrupt = 2012
PluginPackageMismatch  = 2013      PluginTrustFloorNotMet = 2014
PluginDataSchemaTooNew = 2015      PluginRevoked = 2016
```

`VerifyOnLoad` has three settings because full re-hashing has a real cost:

| Setting | Hashes | Cost on a 200 MB plugin | Default |
|---|---|---|---|
| `Full` | every file in the install record | seconds | — |
| `Code` | `plugin.json` + every `*.dll` + every native asset | milliseconds to tens of ms | **yes** |
| `None` | nothing; trust the install record | 0 | — |

`Code` is the default because the ALC only ever executes managed and native binaries; a tampered
`README.md` is not an escalation, and a tampered `.dll` is the entire threat. Documentation files are
covered by the signature and re-checked at install, not at every start.

### 3.4 The trust model

Three levels, and the honest statement of each:

| Level | Established by | What it actually asserts |
|---|---|---|
| **`Verified`** | A signature by a key present in `Arronix:Plugins:TrustedKeys` (shipped first-party keys, plus keys an operator added) | These bytes were produced by a holder of a key this installation has decided to trust. Nothing about behavior. |
| **`SelfSigned`** | A signature that verifies against the key embedded in `signature.json`, **pinned** on first install; a later version signed by a different key is refused until the operator explicitly re-approves (`PluginSignatureInvalid`, message naming both key ids) | **Continuity, not identity.** It says "the same party that gave you version 1 gave you version 2". That is a real and useful property — it defeats a compromised mirror substituting a different publisher's build — and it is emphatically not authentication. |
| **`Unsigned`** | No `signature.json`, or a signature over a `package.json` whose digest does not match | Nothing. Integrity of the *install* only, from the digests recorded at install time. |

`Arronix:Plugins:MinimumTrust` sets the floor and defaults to **`SelfSigned`**. Unsigned packages are
refused (`PluginTrustFloorNotMet`) unless the floor is lowered, which raises a permanent
`HealthSeverity`-carrying `HealthCheck` naming every unsigned plugin. This is deliberately not "reject
unsigned outright": that policy has one observable outcome, which is that every community plugin author
tells users to set `AllowUnsigned=true`, and the setting stops meaning anything. An honest, visible tier
beats a bypassed absolute.

**Development exemption.** Plugins discovered under `Arronix:Plugins:DevelopmentRoots` skip trust
evaluation entirely and are recorded as `Development`. This is required for the first-party plugins to run
from `_temp/bin/$(Configuration)/Arronix.Plugin.Tv/` during the milestone. The exemption itself emits a
`HealthSeverity` warning listing the roots, so a production install that inherited a dev config says so out
loud.

**Revocation.** The registry index carries `revocations[]` (§6.2). A revoked package that is *installed* is
refused at load with `PluginRevoked`, its configuration retained per §4.4, and a health check naming the
reason. No OCSP, no CRL, no online check at load — the revocation list arrives with the daily index
refresh and is cached on disk, so an offline host uses the last list it saw. That is a real limitation and
it is the correct trade for a self-hosted product.

### 3.5 Trust is per-installation, and the store is boring

```jsonc
"Arronix": { "Plugins": {
  "MinimumTrust": "SelfSigned",
  "TrustedKeys": [
    { "keyId": "arx-official-1", "alg": "ES256", "publicKey": "MFkwEwYH…", "addedAt": "2026-01-01T00:00:00Z" }
  ]
}}
```

Registered with `AddValidatedOptions<PluginTrustOptions>` per the house convention (spec §7.4). Operators
add keys through `POST /api/v1/plugins/trust/keys`, which writes to the configuration store and returns the
key's SHA-256 fingerprint for out-of-band comparison. Removing a key does not uninstall anything; it
demotes affected plugins to `SelfSigned` (if their embedded key still verifies) or `Unsigned` at the next
load, which may then breach the floor. That transition is surfaced as a health check, not a silent
failure to start.

### 3.6 What verification buys, given plugins run in-process

Stated plainly, because over-claiming here is how security theater starts:

**Signing tells you where the bytes came from. It tells you nothing about what they do.** A `Verified`
plugin runs in the host process with the host's full ambient authority. It can P/Invoke, it can
`System.IO.File.Delete` any path the process user can reach — bypassing `ScopedFileSystem` entirely,
because that decorator constrains the *contract*, not the runtime — it can open sockets without
`IHttpGateway`, and it can reflect over host internals. The capability model
(`ARCHITECTURE.md` §11), the closed `IPluginRegistry`, the static reference-graph check and the scoping
decorators are a **defense against mistakes and against scope creep by well-intentioned plugins**. They
are not a sandbox and must never be described as one.

So what signing is actually for, precisely:

- **Supply-chain integrity in transit and at rest.** A compromised CDN, a MITM'd download, a mirror
  serving a modified artifact, or a tampered install directory are all detected.
- **Publisher continuity across versions** (the `SelfSigned` tier's real value).
- **A revocation handle.** Without a stable publisher identity there is nothing to revoke.
- **Attribution after an incident.** "Which build was this, and who produced it" is answerable.

Everything about what a plugin can *do* once loaded — the attacker model, the consequences of in-process
execution, whether WASM or process isolation is worth the cost, and what the capability model does and
does not contain — belongs to the concurrent plugin threat-model document and is deliberately not
re-derived here. **Dependency: if that document concludes that a stronger isolation boundary is required
before third-party plugins are permitted at all, §6's public-registry work should be gated behind it.**
This document's position is that packaging and distribution are worth designing now regardless, because
first-party plugins need install/upgrade/rollback on day one.

---

## 4. Installation lifecycle

### 4.1 The on-disk layout

```text
{AppData}/
├── plugins/                                  Arronix:Plugins:Root
│   ├── .staging/                             downloads and extraction scratch; emptied at startup
│   ├── .trash/{pluginId}/{version}/          uninstalled installs, retained TrashRetention
│   ├── .index/                               cached registry index + ETags
│   └── tv/
│       ├── state.json                        the install record (below)
│       ├── current.json                      {"version":"0.4.1"} — a pointer file, not a symlink (D18)
│       └── versions/
│           ├── 0.4.1/                        plugin.json + publish output
│           └── 0.4.0/                        retained for rollback (KeepPreviousVersions)
├── plugin-data/tv/                           IPluginPaths.DataFolder      ← SEPARATE TREE (D14)
├── plugin-cache/tv/                          IPluginPaths.CacheFolder
└── plugin-temp/tv/                           IPluginPaths.TempFolder      ← emptied at startup
```

The separation in the last three lines is the single most important decision in this section.
`IPluginPaths.DataFolder` is documented as *"data that must survive a restart and an upgrade"*
(`src/Arronix.Abstractions/Hosting/IPluginPaths.cs:28-31`). If it lived under `versions/{v}/`, an upgrade
would orphan it and an uninstall would delete it. Lidarr places plugins at
`{appdata}/plugins/{owner}/{name}` and uninstalls with `DeleteFolder(pluginFolder, true)`
(`InstallPluginService.cs:84-86`), so anything a Lidarr plugin persisted beside itself is destroyed by
uninstall. Arronix's uninstall physically cannot do that, because it operates on a tree that contains no
plugin data.

`state.json`:

```jsonc
{
  "stateSchemaVersion": 1,
  "pluginId": "tv",
  "installedVersions": ["0.4.0", "0.4.1"],
  "current": "0.4.1",
  "previous": "0.4.0",
  "enabled": true,
  "source": { "kind": "registry", "registry": "official", "url": "https://…/tv-0.4.1.arxpkg" },
  "trust": { "level": "Verified", "keyId": "arx-official-1",
             "keyFingerprint": "sha256:9c1f…", "verifiedAt": "2026-08-05T11:02:00Z" },
  "packageSha256": "c40b…",
  "files": [ { "path": "plugin.json", "sha256": "b1e2…" }, … ],   // folded in from package.json
  "highestDataSchemaVersion": 2,                                    // §4.6 — monotonic, never lowered
  "lastActiveMediaKinds": ["tv"],                                   // §5.4 — for the unavailable-kind view
  "lastActivatedAt": "2026-08-05T11:03:12Z",
  "pendingRestart": false
}
```

`highestDataSchemaVersion` and `lastActiveMediaKinds` are the only two fields that outlive the code they
describe, and both exist to make a *degraded* install explainable rather than silent.

### 4.2 The operation matrix

| Operation | Files | Plugin data (`plugin-data/`) | Configuration (database) | Restart | Reversible by |
|---|---|---|---|---|---|
| **Install** | download → `.staging` → verify → extract → `versions/{v}` → write `state.json`, `current.json` | created empty on first activation | none created | required to activate | Uninstall |
| **Enable** | `state.enabled = true` | untouched | untouched | required to activate | Disable |
| **Disable** | `state.enabled = false` | **retained** | **retained** | **not required** (§7.5) | Enable |
| **Upgrade** | new version extracted alongside; `current.json` flips only after full verification | **retained**; plugin may migrate forward on first activation | **retained**; `highestDataSchemaVersion` raised | required | Rollback |
| **Rollback** | `current.json` flips back to `previous` | **retained**, and possibly forward-migrated → see §4.6 | **retained** | required | Upgrade |
| **Uninstall** | `versions/*` → `.trash/{id}/{v}/`; `state.state = Uninstalled` | **retained** | **retained, marked orphaned** (§4.4) | required | Reinstall same id |
| **Purge** | `.trash` entry deleted, `plugin-data/{id}` deleted, `state.json` deleted | **destroyed** | **destroyed** | — | **nothing** |

Every one of install / upgrade / rollback / uninstall / purge runs as a **`plugin-install` job on the one
queue** (D23), so it gets progress reporting, cancellation and the existing failure/backoff handling for
free, and two concurrent installs of the same plugin id are serialized by the queue's concurrency governor
rather than by a lock somebody has to remember to take.

### 4.3 Nothing ever mutates a running install

Every write goes to `.staging` and lands with a single `Directory.Move` (§1.6). The live
`versions/{current}/` directory is never opened for write, which:

- makes a crash mid-install a no-op rather than a corrupt install;
- sidesteps Windows file locks on loaded assemblies and native libraries entirely (§2.5 hazard 3);
- makes rollback a one-line pointer rewrite rather than a re-download.

`.staging` is emptied at startup, so an interrupted install leaves nothing but disk usage that is reclaimed
at next boot — the same discipline Sonarr's updater applies to its sandbox
(`src/NzbDrone.Core/Update/InstallUpdateService.cs:116-121`, which deletes the update sandbox before
extracting into it).

### 4.4 Orphaned configuration — the inversion, made concrete

When a plugin is uninstalled or fails to load, every database row it owned stays. Concretely, for each
affected record type:

| Record | On plugin absence |
|---|---|
| `ProviderDefinition` rows whose `implementation` belongs to the plugin | marked `Orphaned = true`; hidden from pickers; excluded from dispatch; **never deleted** |
| `LibraryFacet` rows for the plugin's media kinds | retained; the kind is not browsable while the plugin is absent |
| `UnitFileLink`, `GroupMembership`, `MediaFileRecord` | retained; the join survives the plugin |
| Tags, root folders, exclusions | host-owned; unaffected |
| Scheduled-job registrations | dropped (they are runtime registrations, not stored config); job **history** retained |

Surfaced by a dedicated health contributor:

> *"4 indexer definitions and 1 notification belong to plugin `music`, which is not installed. They are
> retained and inactive. Reinstall `music` to restore them, or purge the plugin to delete them."*

This is exactly the inversion of Prowlarr's `RemoveMissingImplementations`, which walks stored definitions
and calls `_providerRepository.Delete(invalidDefinition)` for every one whose implementation type no longer
resolves (`_reference/Prowlarr/src/NzbDrone.Core/ThingiProvider/ProviderFactory.cs:194-203`). That is a
defensible design in a monolith where a missing implementation means a removed feature. Under a plugin
model it means *"uninstalling a plugin for ten minutes deletes your indexer settings"*, which is the
behavior the spec's resolution #42 already rules out and which this section makes operational.

### 4.5 Reinstall restores everything

Because nothing was deleted, reinstalling a plugin with the same `PluginId` reactivates it and clears the
`Orphaned` flags on its definitions. Two guards:

- The id must match. `PluginId` is the identity (§3.1), not the package file name or the download URL.
- If `state.json` recorded a pinned key and the reinstalled package is signed by a different one, the
  install is refused pending explicit operator re-approval (D13).

### 4.6 Rollback, and the data problem stated honestly

Rolling back **code** is a pointer rewrite. Rolling back **data** is generally impossible, and pretending
otherwise is how plugin systems produce unrecoverable installs.

The rule (D17):

```csharp
// src/Arronix.Host/Runtime/PluginBootstrapper.cs — evaluated at activation, before Configure
if (manifest.DataSchemaVersion < state.HighestDataSchemaVersion)
    return Quarantine(CoreErrorCode.PluginDataSchemaTooNew,
        $"Plugin '{manifest.Id}' {manifest.Version} declares dataSchemaVersion {manifest.DataSchemaVersion}, " +
        $"but its stored data was last written at version {state.HighestDataSchemaVersion}. " +
        "Reinstall a version declaring at least that, or purge the plugin's data.");
```

`highestDataSchemaVersion` is monotonic and is never lowered by any operation except purge. So:

- Rolling back 0.4.1 → 0.4.0 where both declare `dataSchemaVersion: 2` — **works**, immediately.
- Rolling back where 0.4.1 declared 3 and 0.4.0 declares 2 — **refused, with a message that names both
  numbers and the two ways out.** The plugin is quarantined; the host still starts; nothing is deleted.

This is a refusal rather than an attempt, and the reason is that the alternative — a plugin reading rows
written by a schema it does not understand — corrupts silently and is discovered weeks later. A blocked
rollback is an inconvenience an operator can act on; a silent one is a data-loss incident.

Consequence to accept: a plugin author who bumps `dataSchemaVersion` casually makes their own release
non-rollbackable. That should be documented in the publisher guide as the meaning of the field, and it is
the correct pressure — the field is a promise about persisted state, not a build number.

### 4.7 Retention

- `KeepPreviousVersions` (default **1**) — versions beyond that are moved to `.trash` at upgrade.
- `TrashRetention` (default **14 days**) — a startup job deletes older entries.
- `plugin-data/{id}` is **never** subject to retention. Only purge deletes it.
- Purge requires `DELETE /api/v1/plugins/{id}?purge=true&confirm={pluginId}`; the `confirm` value must
  equal the id. A client that fills that in automatically has defeated the design, so the API documentation
  says so explicitly and the client (WP-12) requires typing.

---

## 5. Contract-range negotiation at upgrade time

### 5.1 The two versions, and which one decides

| Version | Where it lives | What it gates |
|---|---|---|
| Host **application** version | `IHostRuntimeInfo` | **nothing.** Read from the running host for display; never declared by a package and never compared against one. |
| Host **contract** version (`Arronix.Abstractions`) | `IPluginContext.HostContractVersion` | **everything.** `contracts.arronix` is checked against it (spec §4.1 step 5) plus the experimental gate (§3.6). |

`GET /api/v1/system/status` exposes both, so a registry client, a CI check and a human all read the same
two numbers from one place. One decisive constraint; the second exists only because "which Arronix is this
for?" is a question a UI must be able to answer.

### 5.2 Preflight — the decision happens *before* the upgrade

Discovering after a restart that three of five plugins are dark is the failure this section exists to
prevent. So compatibility is computed *before* the host update is applied:

```csharp
// src/Arronix.Host/Update/IHostUpgradePreflight.cs
public sealed record PluginUpgradeVerdict(
    PluginId Id,
    string InstalledVersion,
    string DeclaredRange,
    PluginUpgradeOutcome Outcome,
    string? AvailableCompatibleVersion,   // from the registry index, null when none exists
    string? RequiredRange);               // the range a compatible build would have to declare

public enum PluginUpgradeOutcome
{
    Compatible = 0,               // the declared range covers the target contract version
    UpgradeAvailable = 1,         // incompatible, but the index has a version that is compatible
    NoCompatibleVersion = 2,      // incompatible and nothing in any configured registry helps
    Unknown = 3                   // no registry configured, or the index could not be refreshed
}

public sealed record HostUpgradeCompatibility(
    string TargetHostVersion,
    string TargetContractVersion,
    IReadOnlyList<PluginUpgradeVerdict> Plugins)
{
    public bool RequiresOperatorDecision => Plugins.Any(p => p.Outcome is not PluginUpgradeOutcome.Compatible);
}
```

This has exactly one requirement of whatever host update mechanism Arronix eventually ships: **the update
feed must advertise the `abstractionsVersion` of the host build it is offering.** Without it, preflight has
nothing to evaluate `contracts.arronix` against and the whole section degrades to "upgrade and find out".

That is the entire dependency. The feed itself is a new Arronix artifact whose metadata shape is decided on
its own merits when it is built — this document neither specifies it nor inherits one. An earlier draft here
described the field as going "the same place the existing `UpdatePackage` carries `Version`, `Hash` and
`Url`" and framed the work as "whoever restores that feed", which quietly assumed
`src/NzbDrone.Core/Update/` would be reactivated in place and shaped `IHostUpgradePreflight` around a legacy
DTO. It will not be: the legacy tree does not survive into Arronix, and a new feed shaped by a 2010-era
record is baggage carried for no reason at all.

### 5.3 The two-phase upgrade

When every incompatible plugin has an `UpgradeAvailable`:

1. Download and fully verify the compatible plugin packages into `.staging` — **before** touching the host.
2. Apply the host update (existing mechanism).
3. On the new host's first boot, the plugin bootstrapper finds staged versions whose declared ranges cover
   the *new* contract version, promotes them (`current.json` flip) and activates.
4. If step 2 fails and the host is rolled back, the staged plugin versions are still staged and are simply
   not promoted — the old `current` pointers were never touched.

Ordering matters and is not arbitrary: plugin packages are downloaded *first* because a failed download
after the host has already been replaced leaves an install with no compatible plugin and no network
guarantee. Staging first makes the whole thing a pointer flip at the end.

### 5.4 When a plugin has no compatible version

The host upgrade proceeds only if the operator confirms, and then:

- The plugin is **quarantined** at load with `PluginContractMismatch` (spec §4.1 step 5), which is already
  the specified behavior. Nothing new is invented.
- Its configuration is **retained and orphaned** (§4.4). Nothing is deleted.
- `/api/v1/plugins` reports it with the declared range, the host's contract version, and the exact range a
  compatible build must declare (`">=0.4 <0.5"`) — the message the spec's resolution #17 already requires.
- **Its media kinds do not silently vanish.** This is the part that needs a design decision (D20).

`/api/v1/kinds` is built from *activated* plugins, so a quarantined TV plugin means TV simply is not in the
list — indistinguishable, in a client, from "the user never had TV". That is unacceptable for a media
manager where the alternative reading is "my library is gone".

Resolution: `state.json` records `lastActiveMediaKinds[]` from the last successful activation, and the host
surfaces those through `PluginStatusView` rather than by faking a `MediaKindDescriptor`:

> **Request to WP-6** (owner of `src/Arronix.Abstractions/Wire/PluginStatusView.cs`): add
> `IReadOnlyList<string> DeclaredMediaKinds` and `IReadOnlyList<string> LastActiveMediaKinds`. Additive,
> experimental (`ARX0017`), no behavior in a contract. The client (WP-12) renders an *unavailable* entry
> in the kind switcher — "TV — unavailable, the TV plugin needs an update" — driven entirely by data, with
> no compile-time knowledge of any media kind, preserving §6.2's governing principle.

A synthetic `MediaKindDescriptor` was considered and rejected: it would require the host to fabricate a
shape it does not have, and every client code path that assumes a descriptor is browsable would need a
null-shape branch. A separate, explicitly-unavailable list is honest data.

### 5.5 "Partially incompatible" — what it is and is not

A **plugin** never partially activates. Spec §4.1: *"A plugin never partially activates"*, and step 16
commits registrations only after every check passes. So there is no such thing as a half-loaded plugin.

An **installation** absolutely can be partially degraded — three of five plugins active — and that is a
first-class state, not an incidental one:

- A `SystemCompatibility` health contributor returns `Unhealthy` (not `Degraded`) while any plugin is
  quarantined for contract mismatch, with one check item per plugin.
- `/health` therefore returns 503, so a container orchestrator notices. This is deliberate: a media manager
  missing two of its four libraries is not healthy, and reporting 200 because the process is up is exactly
  the silent degradation being designed against.
- `GET /api/v1/plugins` returns a top-level `degraded: true` alongside the array, so the client can render
  a persistent banner rather than a dismissible toast.
- The SignalR `system` group receives a `PluginStateChanged` envelope, so an open UI updates without a
  refresh.

---

## 6. Discovery

### 6.1 Shape: two static files, no service

```text
https://plugins.arronix.org/index.json            root index — small, cacheable, signed
https://plugins.arronix.org/index.sig.json        detached signature over index.json
https://plugins.arronix.org/p/{pluginId}.json     per-plugin detail, all versions
https://plugins.arronix.org/a/{pluginId}/{v}/…    the .arxpkg artifacts
```

Object storage behind any CDN. No database, no search service, no accounts, no API keys.

The reason this is *sufficient* rather than merely cheap: **there are no inter-plugin dependencies.**
`ARCHITECTURE.md` §4 forbids direct cross-plugin calls, and the spec's §4.8 states plugins have no
inter-plugin dependencies, so activation order is plain ordinal. A registry whose packages never depend on
one another needs **no dependency solver**, which is the single feature that forces every other package
registry to become a service. Removing that requirement removes the service.

### 6.2 `index.json`

```jsonc
{
  "indexSchemaVersion": 1,
  "generatedAt": "2026-08-09T00:00:00Z",
  "plugins": [
    {
      "id": "tv",
      "name": "TV",
      "summary": "Television series management.",
      "publisher": { "name": "Arronix", "keyId": "arx-official-1" },
      "license": "GPL-3.0-or-later",
      "homepage": "https://…",
      "mediaKinds": ["tv"],
      "capabilities": ["media-kind","parsing","matching","indexing","quality","renaming","import"],
      "latest": { "version": "0.4.1", "contracts": { "arronix": ">=0.3 <0.4" } },
      "detail": "p/tv.json"
    }
  ],
  "revocations": [
    { "id": "badplugin", "version": "1.0.3", "packageSha256": "aa17…",
      "reason": "Published with a leaked API key.", "revokedAt": "2026-07-02T00:00:00Z" }
  ],
  "keys": [
    { "keyId": "arx-official-1", "alg": "ES256", "publicKey": "MFkwEwYH…" }
  ]
}
```

`p/tv.json`:

```jsonc
{
  "id": "tv",
  "versions": [
    {
      "version": "0.4.1",
      "contracts": { "arronix": ">=0.3 <0.4" },
      "dataSchemaVersion": 2,
      "capabilities": ["media-kind","parsing","matching","indexing","quality","renaming","import"],
      "publishedAt": "2026-08-04T09:12:00Z",
      "releaseNotesUrl": "https://…",
      "artifacts": [
        { "platform": "any", "url": "a/tv/0.4.1/tv-0.4.1.arxpkg", "size": 214_118, "sha256": "c40b…" }
      ]
    },
    { "version": "0.4.0", "contracts": { "arronix": ">=0.3 <0.4" }, "dataSchemaVersion": 2, "…": "…" },
    { "version": "0.2.9", "contracts": { "arronix": ">=0.2 <0.3" }, "dataSchemaVersion": 1, "…": "…" }
  ]
}
```

**Full version history is retained, not just `latest`.** That is what makes §5.2's preflight able to answer
"is there a version of this plugin that works with the *new* host?" and what makes it able to answer the
mirror-image question after a host *downgrade*. An index carrying only the newest version cannot support
either.

`index.json` is signed with the same scheme as a package (§3.2), verified against the registry's
`trustedKeyIds` from configuration. An unsigned or unverifiable index is refused and the previous cached
copy is kept — a registry that starts serving an unsigned index is a compromise signal, not a reason to
proceed.

### 6.3 Version selection

Given a plugin id and no explicit version, the installer picks the **highest version whose
`contracts.arronix` is satisfied by the running host's contract version and passes the experimental gate**
(spec §3.6). Ordering is SemVer 2.0 precedence via `Arronix.Plugins.Versioning.SemanticVersion` — the same
parser the loader uses, not a second one. Prereleases are excluded unless the operator asked for a specific
version.

The reason this is trivial and Lidarr's equivalent is 85 lines of GitHub-release filtering with four
regexes (`PluginService.cs:40-125`, `:240-256`) is entirely down to §6.1: the index states the constraint
as data, so selection is a filter and a max rather than a scrape.

### 6.4 Capabilities before download — an install-time consent surface

Because `capabilities[]` is in the index, the UI shows what a plugin will be granted **before** anything is
downloaded:

> *This plugin will be granted: **network**, **storage**, **import**, and will register a media kind.*

Then, after download and before install, the installer **re-checks that the downloaded package's
`plugin.json` capabilities match what the index advertised** and refuses on mismatch
(`PluginPackageMismatch`). Without that check the index becomes an advisory sticker and a divergent package
is an escalation path: consent to `network` at the index, receive `storage` in the package.

### 6.5 Multiple registries, pinning, direct install, offline

- `Arronix:Plugins:Registries` is an ordered list of `{name, url, trustedKeyIds[]}`. First match by plugin
  id wins.
- A plugin id present in two registries is a **warning**, and installing it requires naming the registry.
  Silent shadowing of a plugin id by a second registry is the classic dependency-confusion attack, and an
  ordered list with a warning is the cheapest correct answer.
- Direct install without a registry: `POST /api/v1/plugins/install/url {url, sha256}`. The `sha256` is
  **required** — a URL install with no expected digest is an unauthenticated fetch, and the trust floor
  still applies to the signature inside the package.
- Local file install: multipart upload of an `.arxpkg`, same verification path.
- Offline: a registry `url` may be a `file://` path or a local directory, so the whole index and all
  artifacts can be copied onto an air-gapped host by `cp -r` and configured as a registry. This falls out
  of the format for free and is worth stating because it is the usual reason people want a plain-files
  registry.
- Refresh: `plugin-registry-refresh`, scheduled `daily 03:17` in the spec's schedule grammar
  (`daily HH:mm`, §4.7), with `If-None-Match`/ETag and an on-disk cache in `plugins/.index/`.

### 6.6 Deliberately not built

Search API, download counts, ratings, comments, categories beyond `capabilities`/`mediaKinds`, a
publishing API, accounts, a dependency solver, semver range *resolution* across packages, and
delta/patch updates. Each would turn a file into a service. Publishing is `git push` on a repository that
regenerates the index in CI and uploads it — which is also what makes the whole thing forkable by anyone
who wants their own registry.

---

## 7. Unload and hot-reload

### 7.1 What .NET 10 actually guarantees

A collectible `AssemblyLoadContext` is collected when — and only when — **every** reference to it, to any
assembly it loaded, to any `Type` from those assemblies, and to any object of those types, has become
unreachable, and a GC has run. `Unload()` does not free anything; it marks the context and unregisters it
from further loads. There is no forced teardown, no "abort everything in this ALC", and no diagnostic that
says what is holding it.

That last point deserves emphasis, because it determines whether this is a maintainable feature: **when
unload fails you get a boolean.** Finding the culprit requires a process dump and `!gcroot` in WinDbg or
`dotnet-dump`. A feature whose failure mode is "collect a dump from the user's NAS" is not a feature a
self-hosted product can support.

### 7.2 The blocker inventory

Each with the concrete Arronix instance, because a generic list is not actionable.

| # | Blocker | The Arronix instance | Fixable by discipline? |
|---|---|---|---|
| 1 | **Host singletons holding plugin objects** | All 24 `IPluginRegistry.Add*` methods (spec §3.4) deposit plugin-constructed objects into `MediaKindRegistry`, `ProviderRegistry`, `IntentRegistry`, `BackgroundTaskRegistry`, health, telemetry and event registries | **Yes**, via the single `PluginRegistrationLedger` — but it must be *complete*, and completeness is unverifiable by review |
| 2 | **Delegates and subscriptions** | `IEventPublisher` subscriptions, `TimeProvider` timer callbacks, `CancellationTokenRegistration`s taken against the host's long-lived shutdown CTS | Mostly, with a per-plugin `CancellationTokenSource` and disposal via the ledger |
| 3 | **Running jobs** | The scheduler may be inside a plugin `IScheduledJob.ExecuteAsync`. A job that ignores its token cannot be stopped, and `Thread.Abort` does not exist. **`IScheduledJob` grows the shutdown contract now** — see below; that converts an unbounded wait into a bounded one, and makes an over-running job an attributable fault rather than an anonymous hang | **Partly.** A deadline the contract states, plus "give up on unload" past it |
| 4 | **In-flight async continuations** | A plugin awaiting `IHttpGateway.ExecuteAsync` holds the ALC through its state machine; the remote decides when that ends | **No.** Unbounded |
| 5 | **Static and thread-local state** | A plugin's `static readonly` cache is fine (it dies with the ALC), but a `ThreadStatic`/`ThreadLocal<T>` holding a plugin type on a **thread-pool thread** outlives the ALC indefinitely, because pool threads are never retired | **No.** Not observable from the host |
| 6 | **Native libraries** | `LoadUnmanagedDllFromPath` handles are not released by `Unload()`; there is no `dlclose`. Any plugin shipping `runtimes/*/native/` can never be fully reclaimed, and on Windows the file stays locked | **No.** Architectural |
| 7 | **Open handles** | `LoadFromStream` avoids locking *assembly* files (Lidarr's lesson, `PluginLoader.cs:37-48`) but a plugin holding a `FileStream` on its own data blocks a Windows directory swap | Partly — §4.3 makes it moot for install, not for unload |
| 8 | **Debugger or profiler attached** | Prevents collection outright | No |
| 9 | **Reflection caches keyed on plugin types** | The host's **shared** `IJsonSerializer` singleton wraps a `JsonSerializerOptions`, which caches a `JsonTypeInfo` per `Type` it has ever serialized. The moment a plugin-supplied DTO crosses the wire, the host's long-lived options object pins that plugin's ALC — permanently, invisibly, and for every plugin | **Yes, but only by giving each plugin its own `JsonSerializerOptions`**, which is a real cost and a real design change |

Blockers 4, 5, 6 and 8 are not closable by any amount of care in the loader. Blocker 9 is the one most
likely to be missed and is worth its own note: it is a single shared object in `Arronix.Common` that
silently defeats unload for every plugin, and nothing about the symptom points at it.

**Blocker 3 was previously in that list, and it was there for the wrong reason.** The earlier text read
*"`IScheduledJob` is stable (0.1.0) and cannot grow a cooperative-shutdown guarantee before 1.0"* — a
self-imposed freeze on a contract that no plugin implements, in a repository whose four reference plugins
(WP-13…WP-16) are unwritten. Below 1.0.0 the contract may change in a MINOR (`docs/contracts/stability.md`),
so the only real question is what the right shape is. It is this:

```csharp
// src/Arronix.Abstractions/Scheduling/IScheduledJob.cs
/// <summary>
/// How long this job needs, after cancellation is requested, to stop cleanly.
/// The scheduler waits at most this long; a job still running when it elapses is reported as hung,
/// its plugin stops being scheduled, and its slot is never returned. Clamped by the host to
/// <c>JobScheduler.MaxShutdownDeadline</c>.
/// </summary>
TimeSpan ShutdownDeadline { get; }
```

What this does and does not buy is worth being exact about, because the surrounding analysis depends on it.
It does **not** make a runaway job killable: cooperative cancellation is all .NET offers and the threat
model's T-07c conclusion is unchanged — *a job that ignores its token cannot be reclaimed, and the only
recovery is a process restart*. What it does buy is that the wait is **bounded and declared**: the scheduler
knows how long to wait rather than guessing, an author who needs 90 seconds to flush says so instead of
being cut off at a host default, and an author who says 5 seconds and takes 300 has broken a contract term
that can be named in a health item and in the `/plugins` view. An unbounded, undeclared wait is the thing
that makes "can this plugin be stopped?" unanswerable, and §7.4 rule 4 needs it answerable.

Correspondingly, blocker 3 is **not** a reason unload is unreachable. Blockers 4, 5, 6 and 8 are enough on
their own, and the recommendation in §7.3 does not change — but it should rest on the blockers that are
genuinely architectural, not on one that was a version-policy artifact.

### 7.3 The recommendation

**Restart-required for install, upgrade, rollback and uninstall.**

- Every mutating plugin endpoint returns `202 Accepted` with `restartRequired: true`.
- `state.json.pendingRestart` is set, and a health contributor reports *"2 plugin changes are waiting for a
  restart"* at `HealthSeverity` warning until it happens.
- The client offers a restart action. The host requests it through the existing
  `IServiceLifecycleController` (`src/Arronix.Abstractions/Hosting/IServiceLifecycleController.cs`), whose
  contract is deliberately limited to querying state and requesting a restart, with `IsSupported` reporting
  whether a service manager is present at all. Where it is not (a bare `dotnet run`, some containers), the
  UI says "restart Arronix to apply" instead of offering a button that does nothing.
- `Unload()` is still called on shutdown for tidiness, and its success or failure is logged at `Debug`. No
  code path branches on the result.

**A diagnostic endpoint, dev builds only:** `POST /api/v1/plugins/{id}/unload-probe` drops the ledger,
calls `Unload()`, and runs the drain loop with a bounded attempt count and a `WeakReference` check —
structurally Lidarr's `AwaitPluginUnload` (`PluginLoader.cs:61-79`), which is a reasonable implementation
of an unreasonable operation. It reports `collected: true|false` and **nothing else**, because nothing else
is obtainable in-process. Its purpose is to stop regressions in the design rules below from going unnoticed
for a year, not to be an operator tool.

### 7.4 The five rules that keep unload reachable

Adopting restart-required now must not foreclose unload later. These are cheap today and expensive to
retrofit:

1. **One droppable graph.** Every plugin contribution — registrations, scoping decorators, `IPluginContext`
   itself, the per-plugin `CancellationTokenSource`, the ALC — is reachable only from a single
   `PluginRegistrationLedger` instance. No host registry may hold a plugin object that is not also in the
   ledger. Enforced by a test that reflects over every `IPluginRegistry.Add*` method and asserts a
   corresponding ledger slot, extending the spec's existing `CapabilityMatrixTests` pattern.
2. **Per-plugin `JsonSerializerOptions`.** Plugin-supplied DTOs are never serialized through the shared
   `IJsonSerializer` singleton. Blocker 9 disappears; the cost is one options object per plugin and a
   slightly less convenient wire path.
3. **Per-plugin cancellation.** Every host→plugin call carries a token derived from a per-plugin CTS that
   the ledger owns and disposes. No plugin callback is ever registered on a host-lifetime token.
4. **Tracked in-flight work.** The scheduler counts in-flight jobs per plugin and exposes the count, so
   "can this plugin be stopped" is answerable and so a future unload has a quiescence signal to wait on.
5. **`EnterContextualReflection` around every entry** (§2.2), so a plugin never accidentally binds a type
   into the Default context — which would pin its own ALC through the host's reflection caches.

None of these costs anything measurable. All five are needed before unload could even be attempted
seriously.

### 7.5 What *is* delivered without a restart: soft disable

Disable is the operator need that hot-reload usually stands in for — "this plugin is hammering an indexer,
stop it now" — and it is achievable honestly:

```csharp
// src/Arronix.Host/Runtime/PluginDispatchGate.cs
// Disable flips one flag, checked by the scoping decorators before every host→plugin call. The plugin's
// assemblies stay resident; nothing is unloaded; nothing is called.
public bool IsDispatchable(PluginId id) => _states[id] is { Enabled: true, State: PluginState.Active };
```

On disable, atomically: the dispatch gate closes; the plugin's scheduled jobs are removed from the
scheduler and in-flight ones are canceled; its providers are marked inactive and excluded from indexer and
notification dispatch; its media kinds are removed from `/api/v1/kinds` and reported as
`DisabledByOperator` via `PluginStatusView`; its event subscriptions stop receiving. Its configuration and
data are untouched (§4.2). Re-enabling reverses the flag — the object graph never left memory, so there is
nothing to reload.

This is stated as a deliberate limitation rather than sold as hot-reload: **the code is still in the
process.** It is not a containment mechanism, and if the concern is a hostile plugin rather than a
misbehaving one, the answer is a restart with the plugin disabled at load. Say that in the UI copy.

### 7.6 The trigger that un-defers unload

Not "when someone has time". A concrete completion condition:

1. All five §7.4 rules implemented, each with a test.
2. A leak test asserting that each of the four reference plugins' ALCs is collected within 10 GCs after
   the ledger is dropped — run in CI, on every platform, non-flaky. If it is flaky, unload does not work.
3. `IScheduledJob.ShutdownDeadline` shipped (§7.2), the scheduler honoring and clamping it, and a documented
   statement that a job exceeding it blocks unload. This condition is **met by construction** once the
   contract member lands — it is listed here because the unload analysis depends on the guarantee, not
   because it is still open.
4. A rule that a plugin shipping native assets is **never** unloadable (blocker 6), surfaced in
   `PluginStatusView` so the UI can say which plugins require a restart and which do not.

Only when all four hold does hot-upgrade become a design question rather than a wish. Until then,
`isCollectible: true` costs nothing and forecloses nothing, exactly as the spec says (§4.1).

---

## 8. Surface area added

### 8.1 API (additive to `unified-host-runtime.md` §6.6)

```text
GET    /api/v1/plugins                                   → { degraded: bool, plugins: PluginStatusView[] }
GET    /api/v1/plugins/{id}/assemblies                   → LoadedAssemblyView[]      (isolation diagnostics)
POST   /api/v1/plugins/{id}/enable | /disable            → ActionResult  (disable is immediate, §7.5)
POST   /api/v1/plugins/{id}/upgrade  { version? }        → 202  restartRequired: true
POST   /api/v1/plugins/{id}/rollback                     → 202
DELETE /api/v1/plugins/{id}                              → 202  (uninstall; data + config retained)
DELETE /api/v1/plugins/{id}?purge=true&confirm={id}      → 202  (destructive; id must be typed)
POST   /api/v1/plugins/install      { pluginId, version?, registry? }   → 202
POST   /api/v1/plugins/install/url  { url, sha256 }                     → 202
POST   /api/v1/plugins/install/file (multipart .arxpkg)                 → 202
GET    /api/v1/plugins/available                         → RegistryEntryView[]   ?q=&capability=&kind=
GET    /api/v1/plugins/available/{pluginId}              → RegistryPluginView    (all versions)
POST   /api/v1/plugins/registry/refresh                  → 202
GET    /api/v1/plugins/trust/keys                        → TrustedKeyView[]
POST   /api/v1/plugins/trust/keys   { keyId, alg, publicKey }           → TrustedKeyView (returns fingerprint)
DELETE /api/v1/plugins/trust/keys/{keyId}
POST   /api/v1/system/update/preflight                   → HostUpgradeCompatibility
POST   /api/v1/plugins/{id}/unload-probe                 → { collected: bool }   DEV BUILDS ONLY
```

All `202` responses enqueue a job; progress arrives on the SignalR `system` group.

### 8.2 Configuration

```jsonc
"Arronix": {
  "Plugins": {
    "Root": "{AppData}/plugins",
    "DataRoot": "{AppData}/plugin-data",
    "DevelopmentRoots": [],
    "MinimumTrust": "SelfSigned",              // Verified | SelfSigned | Unsigned
    "VerifyOnLoad": "Code",                    // Full | Code | None
    "KeepPreviousVersions": 1,
    "TrashRetention": "14.00:00:00",
    "MaxPackageBytes": 268435456,
    "MaxEntryCount": 4096,
    "MaxCompressionRatio": 200,
    "TrustedKeys": [ { "keyId": "arx-official-1", "alg": "ES256", "publicKey": "MFkwEwYH…" } ],
    "Registries": [ { "name": "official", "url": "https://plugins.arronix.org/", "trustedKeyIds": ["arx-official-1"] } ]
  }
}
```

One `SectionName` const per options type, registered through `AddValidatedOptions<T>`, per the house
conventions (spec §7.4). No new `PackageReference` anywhere in this document — every primitive used
(`System.IO.Compression`, `System.Security.Cryptography`, `System.Reflection.Metadata`,
`System.Formats.Asn1`) is verified present in the `net10.0` shared framework.

### 8.3 New files, by project

```text
src/Arronix.Plugins/
├── Packaging/{PluginPackageReader,PluginPackageWriter,PackageManifest,PackageFileEntry,
│              PackageEntryPolicy,PluginPackageException}.cs
├── Signing/{SignatureBlock,SignatureEntry,PackageSigner,PackageVerifier,SignatureAlgorithm,
│            TrustLevel,TrustEvaluator}.cs
├── Loading/SharedFrameworkAssemblies.cs                (§2.1)
└── Configuration/{PluginTrustOptions,PluginRegistryOptions}.cs

src/Arronix.Host/
├── Plugins/{PluginInstaller,PluginInstallJob,PluginInstallState,InstallRecord,InstallRecordStore,
│            PluginTrashKeeper,PluginDispatchGate,OrphanedConfigurationContributor}.cs
├── Plugins/Registry/{RegistryIndex,RegistryPluginDetail,RegistryClient,RegistryCache,
│                     RegistryRefreshJob,VersionSelector}.cs
└── Update/{IHostUpgradePreflight,HostUpgradePreflight,HostUpgradeCompatibility,PluginUpgradeVerdict}.cs

src/Arronix.Api/Endpoints/PluginManagementEndpoints.cs
src/Arronix.Client/Plugins/*                            (browse, install, trust, restart prompt)
tools/arxpack/                                           the CLI that produces and signs .arxpkg
```

---

## 9. Work packages

Ordered. Depends on the runtime spec's WP-7 (`Arronix.Plugins`) and WP-8 (`Arronix.Host`) existing. Each
owns a disjoint file set; none touches a file owned by the runtime spec's WPs except where a request is
recorded in §11.

| WP | Owns | Depends on | Notes |
|---|---|---|---|
| **WP-D1** | `Arronix.Plugins/Packaging/` — reader, writer, entry policy, `package.json` model | spec WP-7 | The deterministic writer and the entry-policy caps are the whole of this WP. No signing yet. |
| **WP-D2** | `Arronix.Plugins/Signing/` — `PackageSigner`, `PackageVerifier`, `TrustEvaluator`, `PluginTrustOptions` | WP-D1 | ECDSA P-256 only; closed `alg` allow-list; TOFU pinning logic. |
| **WP-D3** | `Arronix.Plugins/Loading/SharedFrameworkAssemblies.cs` + the `Load` steps 3/5 and `EnterContextualReflection` wrapping | spec WP-7 | Small, but it is the isolation core. Ships with the filter test in §2.1. |
| **WP-D4** | `Arronix.Host/Plugins/` — install state machine, `InstallRecordStore`, staging/promotion, trash keeper | spec WP-8, WP-D2 | Install / upgrade / rollback / uninstall / purge, all as `plugin-install` jobs. |
| **WP-D5** | `Arronix.Host/Plugins/PluginDispatchGate.cs` + gate checks in the spec's scoping decorators | WP-D4 | Soft disable (§7.5). Touches spec-owned decorator files — coordinate with WP-7's owner. |
| **WP-D6** | `Arronix.Host/Plugins/OrphanedConfigurationContributor.cs` + the `Orphaned` flag on provider definitions | WP-D4, spec WP-10 | The §4.4 inversion. Health check plus dispatch exclusion. |
| **WP-D7** | `Arronix.Host/Plugins/Registry/` — index model, client, cache, refresh job, version selector | WP-D2 | Index signature verification reuses WP-D2. |
| **WP-D8** | `Arronix.Host/Update/` — `HostUpgradePreflight` and the two-phase upgrade orchestration | WP-D7 | Blocked on nothing else; the update *feed* itself is out of scope. |
| **WP-D9** | `Arronix.Api/Endpoints/PluginManagementEndpoints.cs` | WP-D4..D8, spec WP-11 | §8.1 verbatim. |
| **WP-D10** | `Arronix.Client/Plugins/` — browse, capability-consent screen, trust keys, restart prompt, unavailable-kind banner | WP-D9, spec WP-12 | Requires the WP-6 `PluginStatusView` addition (§11). |
| **WP-D11** | `tools/arxpack/` — pack, sign, verify, index-generate CLI | WP-D1, WP-D2 | Also the CI entry point for publishing. |
| **WP-D12** | `Arronix.Plugins.Tests/{Packaging,Signing}/` + `Arronix.Host.Tests/Plugins/` | all | Test list below. |

**Tests that must exist** (added to the spec's §7.5 table):

| Test | Asserts |
|---|---|
| `PackageEntryPolicyTests` | zip-slip, absolute paths, duplicate entries, entry-count / size / ratio caps each rejected with the right code |
| `DeterministicPackTests` | packing the same input twice produces byte-identical archives |
| `PackageVerifierTests` | a good signature verifies; a flipped payload byte fails; an unknown `alg` fails **closed**; a key swap on upgrade is refused |
| `TrustEvaluatorTests` | the floor is enforced; the dev exemption applies only under `DevelopmentRoots`; removing a trusted key demotes rather than deletes |
| `SharedFrameworkAssembliesTests` | the set contains `System.Text.Json`, excludes `Arronix.Common`, `Arronix.Host`, `Microsoft.Extensions.Options` |
| `PluginLoadContextConflictTests` | a plugin shipping `System.Text.Json` is rejected at pack; a plugin shipping `AngleSharp` v1 and another shipping v0.17 both load and get distinct `Type`s |
| `InstallLifecycleTests` | uninstall retains `plugin-data` and definitions; purge deletes both; reinstall clears `Orphaned` |
| `DataSchemaGuardTests` | rollback across a `dataSchemaVersion` decrease quarantines with `PluginDataSchemaTooNew` and deletes nothing |
| `HostUpgradePreflightTests` | the four `PluginUpgradeOutcome` values are produced from a fixture index |
| `UnavailableKindTests` | a quarantined plugin's `lastActiveMediaKinds` still reaches `PluginStatusView` |

---

## 10. Deferred, with the trigger that un-defers it

| Deferred | Trigger |
|---|---|
| **Plugin unload / hot-reload** | The four conditions in §7.6, all holding, non-flaky, on every platform. Until then `isCollectible: true` costs nothing. |
| **ML-DSA (post-quantum) signatures** | `MLDsa.IsSupported` true on every platform Arronix ships to. The `alg` field and its closed allow-list already accommodate it; adding a value is one line. |
| **Delta / patch updates** | A plugin large enough that full re-download is a real cost. Nothing in this design is close. |
| **A publishing API** | Never, if avoidable. Publishing is a CI job that regenerates a static index. |
| **A dependency solver in the registry** | A decision to permit inter-plugin dependencies, which `ARCHITECTURE.md` §4 currently forbids. That is an architecture change, not a registry feature. |
| **Per-plugin resource limits (CPU, memory, handles)** | Process or WASM isolation. Unenforceable in-process; the threat model owns the question. |
| **WASM / out-of-process plugin hosting** | `ARCHITECTURE.md` §15, and the threat model's verdict. This document's package format survives it — the package would carry a `.wasm` payload under `lib/` and everything from §4 onward is unchanged. |
| **A plugin SDK / project template** | The second external plugin author. `dotnet new arronixplugin` is trivial once the publish rules in §1.5 stop moving. |
| **Signed `plugin.json` alone (without a package)** | Never. A manifest signature that does not cover the code is worse than no signature, because it looks like one. |
| **Automatic plugin upgrades** | An explicit operator opt-in, and a track record of upgrades not breaking. Auto-upgrading in-process code on a media server with no rollback-of-data story (§4.6) is not defensible yet. |
| **An Arronix host update feed** | Out of scope here, and a new artifact rather than a restored one — the legacy `src/NzbDrone.Core/Update/` mechanism is not carried forward. §5.2 makes exactly one demand of whatever is built: it advertises `abstractionsVersion`. |

---

## 11. Requests to other work packages, and open dependencies

**Requests** (each additive, each attributed to the file's owner):

| Request | File | Owner | Why |
|---|---|---|---|
| Add `"dataSchemaVersion": int` to the manifest | `Arronix.Plugins/Manifest/PluginManifest.cs` + `plugin.schema.json` | spec WP-7 | §4.6 rollback safety. One integer; absent ⇒ 0. |
| Add `TimeSpan ShutdownDeadline { get; }` to `IScheduledJob`, and `JobScheduler.MaxShutdownDeadline` to clamp it | `Arronix.Abstractions/Scheduling/IScheduledJob.cs`, `Arronix.Host/Scheduling/` | spec WP-4 | §7.2 blocker 3 and §7.6 condition 3. One interface member, added **now**, while zero plugins implement the interface — the whole cost is four reference-plugin stubs that do not exist yet. |
| Add `DeclaredMediaKinds` and `LastActiveMediaKinds` to `PluginStatusView` | `Arronix.Abstractions/Wire/PluginStatusView.cs` | spec WP-6 | §5.4 — a quarantined plugin's kinds must not silently vanish. |
| Add six `CoreErrorCode` values (2011–2016) | `Arronix.Abstractions/Health/CoreErrorCode.cs` | spec WP-1 | §3.3. Six appended members, continuing the existing block. |
| Amend the `IArchiveService` summary to drop "signed extension packages it installs" | `Arronix.Common/Archives/IArchiveService.cs:5-6` | Arronix.Common owner | D2 — the installer does not use it, and the doc should not promise it. |
| Gate checks in the scoping decorators for soft disable | `Arronix.Plugins/Scoping/*` | spec WP-7 | §7.5. One `IsDispatchable` check per decorator. |
| Per-plugin `JsonSerializerOptions` rather than the shared `IJsonSerializer` singleton for plugin DTOs | `Arronix.Plugins/Scoping/` | spec WP-7 | §7.4 rule 2 — blocker 9, the one that silently defeats unload forever. Worth doing even under restart-required, because retrofitting it later means auditing every serialization site. |

**Open dependencies, stated rather than guessed:**

1. **The threat model.** §3.6 defers the in-process-execution analysis entirely. If it concludes that
   third-party plugins require a stronger boundary before being permitted, §6 (public registry) should be
   gated behind that conclusion; §1–§5 and §7 stand regardless, because first-party plugins need packaging
   and lifecycle on day one.
2. **The Storage-Layer milestone.** §4.4's `Orphaned` flag is described here as a column on provider
   definitions; `IMediaStore` is in-memory in the current milestone (spec §4.6), so the flag lives in the
   in-memory store first and moves with everything else when persistence lands. No shape changes.
3. **The host update feed.** §5.2's preflight needs the update package to advertise `abstractionsVersion`.
   No Arronix feed exists yet, and none is inherited. Stated here as a requirement on that future artifact,
   not as a shape for it.
4. **The registry's actual hostname and the first-party signing key.** Both are operational decisions, not
   design ones. `plugins.arronix.org` and `arx-official-1` are placeholders throughout.
