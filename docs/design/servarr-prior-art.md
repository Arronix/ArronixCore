# Servarr as prior art: is there a shared *arr core?

> **Status:** investigation record, 2026-08-10.
> **Question:** Is `Servarr` a real shared code implementation that Sonarr / Radarr / Lidarr / Readarr / Prowlarr depend on, or a brand-and-wiki umbrella over five independent hard-forks?
> **Method:** dependency archaeology over local `--depth 1` clones of Radarr, Lidarr, Readarr and Prowlarr in `_reference/`, the Sonarr-derived tree in `ArronixCore/src`, the GitHub REST API for the `Servarr`, `Sonarr`, `Radarr`, `Lidarr`, `Readarr` and `Prowlarr` organizations, and `wiki.servarr.com`.

---

## Verdict

**Servarr is a wiki, a set of build-and-asset services, and a brand — not a shared application core.** The GitHub organization `Servarr` contains 28 public repositories and *not one of them is shared application code*: they are the consolidated wiki, two small backend services (an update server and an OAuth token broker), cross-compilation toolchains for exotic runtimes (FreeBSD, linux-x86, armel), CI helper images, and forks of third-party libraries (FFmpeg, FFMpegCore, FluentMigrator, System.Data.SQLite, Mono.Posix, coverlet) republished under a `Servarr.*` package prefix. Every `Servarr`-branded NuGet package consumed by the *arrs is a repackaged *third-party* dependency, never *arr code. There are no git submodules, no shared source projects, no shared class library, no plugin contract, and no `Servarr.Core` / `Servarr.Common` package anywhere in any of the five applications. The decisive datapoint: **upstream Sonarr — the ancestor of the entire family — has zero Servarr dependencies at all**; its `src/NuGet.Config` lists only `nuget.org`, and where Radarr takes `Servarr.FFMpegCore`, Sonarr takes `Openur.FFMpegCore` from an unrelated fork owner. Code moves between the *arrs by copy-paste hard-fork: all five carry a directory literally named `NzbDrone.Core` compiled into a per-app assembly (`Radarr.Core.csproj`, `Lidarr.Core.csproj`, …) over a shared `NzbDrone.*` namespace, and of the 723 source file paths that exist in all four reference clones only 287 (39.7%) are still byte-identical. The one genuine inter-app integration — Prowlarr's `Applications/` folder — is deliberately *not* shared code: it is ~5,150 lines of hand-written, per-app HTTP adapters that talk to each target over its public REST API, complete with duplicated DTOs and hardcoded minimum-version gates. **Nobody has unified the *arr code. Arronix is not duplicating existing prior art; it is filling a genuine and conspicuous gap.**

---

## 1. The `Servarr` GitHub organization

Organization metadata (`GET /orgs/Servarr`):

| field | value |
| --- | --- |
| created | 2019-10-27 |
| description | *"Common Packages and Services for -arr programs"* |
| public repos | 28 |
| public members | 1 (`Qstick`) |
| blog / homepage | none set |

The organization's own one-line description is, read carefully, entirely accurate and entirely damning: **packages and services**, not *code*. There is no repository whose contents are application logic shared between the *arrs.

### 1.1 Complete repository inventory, classified

Retrieved via `GET /orgs/Servarr/repos?per_page=100&sort=updated`. `fork` = a fork of a third-party upstream.

| repo | class | fork? | archived? | last push | note |
| --- | --- | --- | --- | --- | --- |
| `Wiki` | **docs** | no | no | 2026-08-10 | The consolidated wiki. 632 stars, 103 forks. Actively pushed daily. |
| `ServarrAPI.Update` | **service** | no | no | 2025-10-17 | "Update Server for -arr Programs (Lidarr, Radarr, Readarr)" |
| `ServarrAPI.Auth` | **service** | no | no | 2025-03-23 | "Auth Server for -arr programs (Spotify, Trakt, etc)" |
| `dotnet-bsd` | **build toolchain** | no | no | 2026-05-21 | Cross-build a .NET SDK for FreeBSD on Linux |
| `dotnet-linux-x86` | **build toolchain** | no | no | 2024-10-13 | 32-bit x86 .NET SDK builds |
| `dotnet-linux-armel` | **build toolchain** | no | no | 2022-04-28 | armv5/armv6 .NET |
| `dotnet-linux-arm-vfpv3d16` | **build toolchain** | no | no | 2022-05-16 | |
| `Servarr.NETCore.Platforms` | **build toolchain** | no | no | 2022-05-17 | RID graph patches for the above |
| `ffmpeg-build` | **asset build** | no | no | 2026-06-25 | Builds the FFmpeg/FFprobe binaries shipped with Radarr |
| `FFmpeg` | **3rd-party fork** | yes | no | 2026-06-24 | Mirror of git.ffmpeg.org |
| `FFMpegCore` | **3rd-party fork** | yes | no | 2023-01-14 | Source of `Servarr.FFMpegCore` |
| `fluentmigrator` | **3rd-party fork** | yes | no | 2024-04-13 | Source of `Servarr.FluentMigrator.*` |
| `SQLite.Build` | **asset build** | no | no | 2023-08-05 | Source of `System.Data.SQLite.Core.Servarr` |
| `Mono.Posix.NETStandard` | **3rd-party fork** | no | no | 2022-11-03 | Source of the `-servarrNN` Mono.Posix builds |
| `libMonoPosixHelper` | **3rd-party fork** | no | **yes** | 2020-07-28 | Superseded by the above |
| `coverlet` | **3rd-party fork** | yes | no | 2021-04-27 | Source of the `coverlet-nightly` feed Readarr uses |
| `diagnostics` | **3rd-party fork** | yes | no | 2021-07-07 | .NET diagnostic tooling |
| `azure-pipelines-agent` | **CI tooling** | yes | no | 2022-11-03 | FreeBSD builds of the Azure agent |
| `runner` | **CI tooling** | yes | no | 2021-09-13 | GitHub Actions runner fork |
| `TestImages` | **CI tooling** | no | no | 2022-04-13 | `ghcr.io/servarr/testimages` |
| `AzureDiscordNotify` | **CI tooling** | no | no | 2022-10-18 | Pipeline notification script |
| `ajv-cli` | **CI tooling** | yes | no | 2022-06-19 | JSON schema linting |
| `m1_segfault` | **CI tooling** | no | no | 2021-11-26 | Apple-Silicon repro case |
| `cf-workers-status-page` | **service** | yes | no | 2025-02-13 | status.servarr.com |
| `discord-bot-cogs` | **community** | no | no | 2025-10-22 | Discord bot |
| `spksrc` | **packaging** | no | no | 2024-02-20 | "Servarr synology packages" |
| `mediawiki-docker` | **docs infra** | no | **yes** | 2021-05-12 | Old wiki hosting |
| `aether-mediawiki` | **docs infra** | no | **yes** | 2020-08-31 | Old wiki theme |

**Tally: 1 wiki, 3 services, 5 cross-build toolchains, 8 third-party library forks/asset builds, 7 CI helpers, 1 packaging, 3 archived. Zero repositories of shared *arr application code.** Three repos are archived; the actively-pushed ones are `Wiki` (daily), `ffmpeg-build`, `FFmpeg`, `dotnet-bsd` and `ServarrAPI.Update`.

Note also what is *absent*: `Prowlarr-Indexers` is **not** in this org — the Cardigann definition set lives at `Prowlarr/Indexers`, owned by the Prowlarr organization, not Servarr (see §5). Neither is any of the actual applications: `Sonarr/Sonarr`, `Radarr/Radarr`, `Lidarr/Lidarr`, `Readarr/Readarr`, `Prowlarr/Prowlarr` each live in their own organization with their own repos, their own websites (`*.github.io`) and their own satellite services (`LidarrAPI.Metadata`, `RadarrAPI.TMDB`, …).

---

## 2. Do the *arrs take a code dependency on anything Servarr-owned?

This is the decisive question and it is answerable from the clones. The answer is: **yes, but only on forks of third-party libraries — never on *arr code.**

### 2.1 Every `Servarr`-branded package reference, exhaustively

Grep across all `*.csproj`, `Directory.Packages.props`, `packages.lock.json`, `NuGet.config`, `*.sln`, `*.targets` in the four reference clones:

```
_reference/Radarr/src/NzbDrone.Core/Radarr.Core.csproj:14     Servarr.FFMpegCore                4.7.0-26
_reference/Radarr/src/NzbDrone.Core/Radarr.Core.csproj:15     Servarr.FFprobe                   5.1.10.124
_reference/Radarr/src/NzbDrone.Mono/Radarr.Mono.csproj:11     Mono.Posix.NETStandard            5.20.1.34-servarr20
_reference/Radarr/src/NzbDrone.Mono.Test/Radarr.Mono.Test.csproj:10  Mono.Posix.NETStandard     5.20.1.34-servarr20

_reference/Lidarr/src/NzbDrone.Mono/Lidarr.Mono.csproj:9      Mono.Posix.NETStandard            5.20.1.34-servarr20
_reference/Lidarr/src/NzbDrone.Mono.Test/Lidarr.Mono.Test.csproj:6   Mono.Posix.NETStandard     5.20.1.34-servarr20

_reference/Prowlarr/src/NzbDrone.Mono/Prowlarr.Mono.csproj:7  Mono.Posix.NETStandard            5.20.1.34-servarr20
_reference/Prowlarr/src/NzbDrone.Mono.Test/Prowlarr.Mono.Test.csproj:6  Mono.Posix.NETStandard  5.20.1.34-servarr20

_reference/Readarr/src/Directory.Packages.props:14            Servarr.FluentMigrator.Runner            3.3.2.9
_reference/Readarr/src/Directory.Packages.props:15            Servarr.FluentMigrator.Runner.SQLite     3.3.2.9
_reference/Readarr/src/Directory.Packages.props:16            Servarr.FluentMigrator.Runner.Postgres   3.3.2.9
_reference/Readarr/src/Directory.Packages.props:32            Mono.Posix.NETStandard                   5.20.1.34-servarr22
_reference/Readarr/src/Directory.Packages.props:57            System.Data.SQLite.Core.Servarr          1.0.115.5-18
```

That is the **complete** list. Five distinct package identities:

| package | what it actually is |
| --- | --- |
| `Servarr.FFMpegCore` | fork of `rosenbjerg/FFMpegCore`, a .NET wrapper around the FFmpeg CLI |
| `Servarr.FFprobe` | the FFprobe **binary**, repackaged as a NuGet asset |
| `Servarr.FluentMigrator.Runner{,.SQLite,.Postgres}` | fork of `fluentmigrator/fluentmigrator`, a DB migration framework |
| `System.Data.SQLite.Core.Servarr` | a rebuild of the SQLite ADO.NET provider with custom native binaries |
| `Mono.Posix.NETStandard` `…-servarrNN` | a rebuild of Mono's POSIX interop shim for platforms Mono no longer ships |

Not one of these contains a line of Sonarr/Radarr/Lidarr/Readarr/Prowlarr logic. They exist because the *arrs target platforms (FreeBSD, musl, armv6, 32-bit x86, Synology) that the upstream maintainers of those libraries do not, and because Mono.Posix was effectively abandoned upstream. **This is shared *build and packaging* infrastructure, not shared implementation.**

### 2.2 The NuGet feeds

`NuGet.config` per app — the Servarr-hosted Azure Artifacts feeds:

```
_reference/Radarr/src/NuGet.config:6    dotnet-bsd-crossbuild      pkgs.dev.azure.com/Servarr/Servarr/_packaging/dotnet-bsd-crossbuild
_reference/Radarr/src/NuGet.config:7    Mono.Posix.NETStandard     pkgs.dev.azure.com/Servarr/Servarr/_packaging/Mono.Posix.NETStandard
_reference/Radarr/src/NuGet.config:8    FFMpegCore                 pkgs.dev.azure.com/Servarr/Servarr/_packaging/FFMpegCore
_reference/Lidarr/src/NuGet.config:7-8  (dotnet-bsd-crossbuild, Mono.Posix.NETStandard)
_reference/Prowlarr/src/NuGet.config:6-7 (dotnet-bsd-crossbuild, Mono.Posix.NETStandard)
_reference/Readarr/src/NuGet.config:6-10 (dotnet-bsd-crossbuild, Mono.Posix.NETStandard, SQLite, coverlet-nightly, FluentMigrator)
```

Readarr additionally declares `packageSourceMapping` (`NuGet.config:19-31`) that scopes each Servarr feed to exactly the fork it publishes — `Mono.Posix.NETStandard`, `System.Data.SQLite.Core.Servarr`, `coverlet.*`, `Servarr.FluentMigrator*`. **The mapping is itself proof of scope**: the Servarr feeds are only ever allowed to serve those third-party forks; everything else comes from `nuget.org`.

Lidarr also runs its *own* private feed for its *own* fork — `pkgs.dev.azure.com/Lidarr/Lidarr/_packaging/Taglib` (`_reference/Lidarr/src/NuGet.config:6`), publishing `TagLibSharp-Lidarr` 2.2.0.27. Even the fork-hosting habit isn't centralized.

### 2.3 The Sonarr control case — the decisive datapoint

Sonarr is the ancestor of every other *arr. If Servarr were a real shared core, Sonarr would be its most obvious consumer. It is not a consumer at all.

`Sonarr/Sonarr` `src/NuGet.Config` in full:

```xml
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
```

One feed. `nuget.org`. And `src/NzbDrone.Core/Sonarr.Core.csproj` uses **`Openur.FFMpegCore` 5.4.0.37** and **`Openur.FFprobeStatic` 9.0.0.408** — a completely different fork lineage from Radarr's `Servarr.FFMpegCore` — plus upstream `FluentMigrator.Runner.Core` 8.0.1 where Readarr uses `Servarr.FluentMigrator.Runner` 3.3.2.9. Sonarr's `Sonarr.Common.csproj` contains no Servarr package either.

So the family's origin project has **completely exited** the Servarr package orbit while its descendants remain in it. That is not the behavior of a shared platform; it is the behavior of a convenience feed that some downstreams happen to still use.

### 2.4 Submodules, vendored source, project references — none

* No `.gitmodules` in any of Radarr, Lidarr, Readarr, Prowlarr, or the ArronixCore tree.
* No `ProjectReference` crossing a repository boundary anywhere. Each app's `src/` is hermetic.
* `src/Libraries/` exists only in Sonarr/ArronixCore and holds no *arr-shared code.

### 2.5 Version drift confirms independence

If a shared core existed, the pinned versions of common dependencies would move together. They do not:

| package | Radarr | Lidarr | Readarr | Prowlarr | Sonarr (upstream) |
| --- | --- | --- | --- | --- | --- |
| `MonoTorrent` | 3.0.2 | 3.0.2 | (via CPM) | **2.0.7** | 3.0.2 |
| `FluentAssertions` | 6.12.1 | **5.10.3** | (CPM) | 6.12.0 | — |
| `Swashbuckle.AspNetCore.*` | 8.1.4 | **9.0.6** | (CPM) | — | — |
| `Selenium.Support` | 3.141.0 | 3.141.0 | 3.141.0 | **4.1.0** | — |
| `Moq` | 4.18.4 | 4.18.4 | (CPM) | **4.17.2** | — |
| `TargetFramework` | net8.0 | net8.0 | **net6.0** | net8.0 | net10.0 |
| FFmpeg wrapper | `Servarr.FFMpegCore` | — | — | — | **`Openur.FFMpegCore`** |
| central pkg mgmt | no | no | **yes** | no | yes |

Radarr, Lidarr and Prowlarr do not even use Central Package Management; only Readarr adopted it. Readarr is stranded on **net6.0** — long out of support — while its siblings are on net8.0 and Sonarr has reached net10.0. Four independently-drifting dependency graphs, not one.

### 2.6 CI: Servarr-hosted artifacts are build-time only

`azure-pipelines.yml` in all four (Radarr / Lidarr / Readarr / Prowlarr are near-identical copies of the same 1,200-line pipeline — itself an artifact of copy-paste, see §3):

```
azure-pipelines.yml:16-17     sentryOrg: 'servarr'   sentryUrl: 'https://sentry.servarr.com'
azure-pipelines.yml:~485,~898 containerImage: ghcr.io/servarr/testimages:alpine
Readarr:509,926               curl api.github.com/repos/Servarr/dotnet-linux-x86/releases  → SDK download
azure-pipelines.yml:~1108-1145 git config --global user.name "Servarr"     (bot commits)
azure-pipelines.yml:~1210     raw.githubusercontent.com/Servarr/AzureDiscordNotify/master/DiscordNotify.ps1
```

Every Servarr touchpoint in CI is a **build container, an SDK download, a crash-reporting endpoint or a notification script**. None of it ships into the applications.

Docker images are not Servarr's either — `Radarr/README.md:5` badges **`linuxserver/radarr`** and `Prowlarr/README.md:5` badges **`hotio/prowlarr`**. The two dominant *arr container images are maintained by unaffiliated third parties (LinuxServer.io and hotio), which the projects merely link to on the wiki.

### 2.7 Runtime services on `servarr.com` — shared *infrastructure*, per-app *clients*

There is one place where a Servarr-owned thing is contacted at runtime: the `*.servarr.com` service endpoints.

```
_reference/Radarr/src/NzbDrone.Common/Cloud/RadarrCloudRequestBuilder.cs:16     https://radarr.servarr.com/v1/
_reference/Lidarr/src/NzbDrone.Common/Cloud/LidarrCloudRequestBuilder.cs:16     https://lidarr.servarr.com/v1/
_reference/Readarr/src/NzbDrone.Common/Cloud/ReadarrCloudRequestBuilder.cs:16   https://readarr.servarr.com/v1/
_reference/Prowlarr/src/NzbDrone.Common/Cloud/ProwlarrCloudRequestBuilder.cs:15 https://prowlarr.servarr.com/v1/
_reference/Prowlarr/src/NzbDrone.Common/Cloud/ProwlarrCloudRequestBuilder.cs:18 https://releases.servarr.com/v1/
_reference/Radarr/src/NzbDrone.Core/Notifications/Trakt/TraktProxy.cs:25-26     https://auth.servarr.com/v1/trakt/{auth,renew}
_reference/Radarr/src/NzbDrone.Core/ImportLists/Simkl/SimklImportBase.cs:21-22  https://auth.servarr.com/v1/simkl/{auth,renew}
_reference/Readarr/src/NzbDrone.Core/{ImportLists,Notifications}/Goodreads/GoodreadsSettingsBase.cs:27,26  https://auth.servarr.com/v1/goodreads/sign
```

These back `Servarr/ServarrAPI.Update` (update-channel manifests) and `Servarr/ServarrAPI.Auth` (an OAuth broker holding client secrets the open-source clients can't embed). This is real, genuinely shared, genuinely useful infrastructure — and it is a **hosted service reached over HTTP**, not a library. Note that even here the *client* code is not shared: there are four separate `<App>CloudRequestBuilder.cs` files, each a near-copy of the others differing only in hostname. And Sonarr again opts out entirely, using its own `https://services.sonarr.tv/v1/` and `https://skyhook.sonarr.tv/v1/` (`ArronixCore/src/NzbDrone.Common/Cloud/SonarrCloudRequestBuilder.cs:15,18`).

---

## 3. How code actually moves between the *arrs: hard-fork and copy-paste

### 3.1 The structural tell

Every one of the five applications contains directories literally named `NzbDrone.Common` and `NzbDrone.Core`, containing files in the `NzbDrone.*` namespace, compiled into per-app assemblies:

| app | project file | namespace in `Disk/DiskTransferService.cs` |
| --- | --- | --- |
| Radarr | `src/NzbDrone.Common/Radarr.Common.csproj` | `NzbDrone.Common.Disk` |
| Lidarr | `src/NzbDrone.Common/Lidarr.Common.csproj` | `NzbDrone.Common.Disk` |
| Readarr | `src/NzbDrone.Common/Readarr.Common.csproj` | `NzbDrone.Common.Disk` |
| Prowlarr | `src/NzbDrone.Common/Prowlarr.Common.csproj` | `NzbDrone.Common.Disk` |
| Sonarr | `src/NzbDrone.Common/Sonarr.Common.csproj` | `NzbDrone.Common.Disk` |

Five separately-versioned assemblies over one namespace inherited from a project (NzbDrone) that was renamed to Sonarr in 2014. That is the fossil record of five `git clone`s, not five consumers of a package.

### 3.2 Quantified drift

MD5 over every `.cs` file under `NzbDrone.Common` + `NzbDrone.Core`, matched by relative path:

| app | files |
| --- | --- |
| Radarr | 1,717 |
| Lidarr | 1,569 |
| Readarr | 1,468 |
| Prowlarr | 1,111 |
| Sonarr tree (ArronixCore) | 1,788 |

Pairwise — *shared paths / byte-identical / identical %*:

| pair | shared | identical | % |
| --- | ---: | ---: | ---: |
| Radarr ↔ Lidarr | 1,125 | 501 | 44.5% |
| Radarr ↔ Readarr | 1,051 | 443 | 42.2% |
| Radarr ↔ Prowlarr | 765 | 413 | 54.0% |
| Lidarr ↔ Readarr | 1,109 | 647 | 58.3% |
| Lidarr ↔ Prowlarr | 751 | 396 | 52.7% |
| Readarr ↔ Prowlarr | 731 | 367 | 50.2% |

**Across all four: 723 file paths exist in every clone; 287 (39.7%) are still byte-identical in all four.** Roughly 60% of the notionally-common surface has diverged.

Comparisons against the ArronixCore tree (35–39% identical) are reported for completeness but should be discounted: that tree has already been deliberately modernized by this project, so it understates its similarity to upstream Sonarr.

### 3.3 Spot checks on the files that "should" be identical

| file | Sonarr(AC) | Radarr | Lidarr | Readarr | Prowlarr |
| --- | ---: | ---: | ---: | ---: | ---: |
| `NzbDrone.Common/Disk/DiskTransferService.cs` | 541 | 548 | 539 | 538 | 536 |
| `NzbDrone.Core/Datastore/BasicRepository.cs` | 508 | 512 | 512 | 508 | 512 |
| `NzbDrone.Core/ThingiProvider/ProviderFactory.cs` | 216 | 216 | 216 | 216 | 211 |
| `NzbDrone.Core/Datastore/TableMapping.cs` | 250 | 258 | 286 | 286 | 168 |
| `NzbDrone.Common/Http/HttpClient.cs` | 376 | 388 | 388 | 388 | 448 |
| `NzbDrone.Core/Download/DownloadService.cs` | 145 | 144 | 145 | 145 | 207 |
| `NzbDrone.Core/Indexers/IndexerBase.cs` | 133 | 121 | 115 | 114 | 229 |

Five distinct MD5s for `DiskTransferService.cs`. Five for `BasicRepository.cs`. Same file, same name, same namespace, same purpose, five separately-maintained copies drifting by a handful of lines each.

`ProviderFactory.cs` is the interesting counter-example: **byte-identical across Sonarr, Radarr, Lidarr and Readarr** (`md5=1eb68fe4…`), and differing in Prowlarr only. A file that has stayed *perfectly* identical across four independent repositories for a decade is not evidence of sharing — it is evidence that nobody has needed to touch it, which is precisely the file you would have extracted into a package if a package existed.

### 3.4 The `ThingiProvider` abstraction, in detail

`ThingiProvider` is the *arrs' core extensibility abstraction — the base contract behind indexers, download clients, notifications, import lists, metadata providers and (in Prowlarr) applications. If there were any shared core, it would be this. Comparing the folder across the four reference clones: **18 common paths, 10 byte-identical, 8 differing.**

* Identical in all four: `IProviderConfig.cs`, `NullConfig.cs`, `ProviderMessage.cs`, `ConfigContractNotFoundException.cs`, `Events/Provider{Deleted,Updated,StatusChanged}Event.cs`, `Status/EscalationBackOff.cs`, `Status/ProviderStatusBase.cs`, `Status/ProviderStatusServiceBase.cs`.
* Differing: `IProvider.cs`, `IProviderFactory.cs`, `IProviderRepository.cs`, `ProviderDefinition.cs`, `ProviderFactory.cs`, `ProviderRepository.cs`, `Events/ProviderAddedEvent.cs`, `Status/ProviderStatusRepository.cs`.

The *nature* of the drift is revealing. `IProvider.cs` is identical between Radarr and Lidarr; Prowlarr's copy differs **only by a UTF-8 BOM and one blank line**. Whereas `ProviderDefinition.cs` has substantively forked — Radarr adopted `Equ` memberwise equality with `[MemberwiseEqualityIgnore]` attributes on `Name`, `Settings` and others; Lidarr never did, and still carries the older expanded property-getter form.

That is the whole story in one file: cosmetic divergence from independent formatting passes, plus real divergence from features landing in one fork and never being back-ported. No merges, no cherry-picks — just parallel evolution.

### 3.5 A caveat on history

All four reference clones are `--depth 1` (`git rev-list --count HEAD` = 1 in each; `git rev-parse --is-shallow-repository` = `true`). **Deep history is unavailable, so this report makes no claim about the presence or absence of cherry-picks in the commit graph.** All drift conclusions above are drawn from *content comparison of the current trees*, which is sufficient to establish that no build-time or run-time sharing mechanism exists. (Incidentally, Readarr's HEAD commit is `0b79d30 Retirement announcement`, and `Readarr/Readarr` is now **archived** on GitHub — the family is four, not five.)

---

## 4. Is there any shared runtime abstraction at all?

**No.** Explicitly checked and not found:

* **No shared package.** Exhaustively established in §2.1 — the only `Servarr.*` packages are third-party forks.
* **No shared host.** Each app has its own `NzbDrone.Host`, its own `NzbDrone.Console`, its own service/daemon wrapper, its own DryIoc container wiring, its own SQLite database file, its own config file, its own port.
* **No shared plugin API.** Every app has a `NzbDrone.Common/Composition/AssemblyLoader.cs`, but only **Lidarr** has an actual plugin system: `NzbDrone.Common/Composition/PluginLoadContext.cs` (a 42-line `AssemblyLoadContext` with `isCollectible: true` and `AssemblyDependencyResolver`), plus `NzbDrone.Core/Plugins/{Plugin,PluginService,PluginVersion,InstallPluginService}.cs` and an API surface at `Lidarr.Api.V1/System/Plugins`. Its contract is minimal and entirely Lidarr-private:

  ```csharp
  public interface IPlugin
  {
      string Name { get; }
      string Owner { get; }
      string GithubUrl { get; }
      PluginVersion InstalledVersion { get; }
      PluginVersion AvailableVersion { get; set; }
  }
  ```

  Radarr, Readarr, Prowlarr and Sonarr have nothing equivalent. Upstream Sonarr instead ships `src/Sonarr.RuntimePatches` — a Harmony-based runtime-patching project, a fundamentally different (and adversarial) extension strategy. So even the *idea* of plugins is unshared: one fork built a loader, another built a monkey-patcher, three built neither.

**The only thing genuinely shared is a common ancestor plus copy-paste.** NzbDrone → Sonarr; Sonarr → Radarr (2014); Sonarr/Radarr → Lidarr; Lidarr → Readarr; Radarr/Jackett → Prowlarr. Each divergence point is a fork, and thereafter improvements propagate — when they propagate at all — by a human reading one repo's diff and hand-applying it to another.

---

## 5. Prowlarr's `Applications/` folder: integration at the API level

`_reference/Prowlarr/src/NzbDrone.Core/Applications/` is the closest thing the ecosystem has to inter-app integration, and characterizing it precisely matters.

**Structure:** 59 `.cs` files, ~5,150 lines. A small generic spine — `IApplication.cs`, `ApplicationBase.cs`, `ApplicationFactory.cs`, `ApplicationService.cs`, `AppIndexerMap{,Repository,Service}.cs`, `ApplicationStatus*.cs`, `ApplicationSyncLevel.cs` — plus **seven per-target adapter folders**: `Sonarr/`, `Radarr/`, `Lidarr/`, `Readarr/`, `Whisparr/`, `Mylar/`, `LazyLibrarian/`.

**The contract** (`Applications/IApplication.cs`, complete):

```csharp
public interface IApplication : IProvider
{
    void AddIndexer(IndexerDefinition indexer);
    void UpdateIndexer(IndexerDefinition indexer, bool forceSync = false);
    void RemoveIndexer(int indexerId);
    List<AppIndexerMap> GetIndexerMappings();
}
```

Note that `IApplication` derives from `IProvider` — Prowlarr's *own* `ThingiProvider`. A target *arr is modeled exactly like an indexer or a notification: a configurable provider with a settings contract and a health status. There is no shared type between Prowlarr and Sonarr here; `IndexerDefinition` is Prowlarr's own entity.

**It is unambiguously HTTP-over-REST, per-app, hand-written.** From `Applications/Sonarr/SonarrV3Proxy.cs:27-31`:

```csharp
private static Version MinimumApplicationVersion => new(3, 0, 5, 0);
private const string AppApiRoute = "/api/v3";
private const string AppIndexerApiRoute = $"{AppApiRoute}/indexer";
```

versus `Applications/Lidarr/LidarrV1Proxy.cs:27` → `new(1, 0, 2, 0)` and `/api/v1`. Each proxy hardcodes its target's API version and a minimum-version gate, and fails validation with a per-app message. Prowlarr must *know*, out of band, which REST dialect each target speaks.

**The duplication is total, and instructive.** `diff Sonarr/SonarrIndexer.cs Radarr/RadarrIndexer.cs` yields only: the namespace, the class name, the field type (`List<SonarrField>` vs `List<RadarrField>`), and TV-specific extras Radarr lacks (`SeasonSearchMaximumSingleEpisodeAge`, `animeCategories`, `animeStandardFormatSearch`, `seedCriteria.seasonPackSeedTime`). Seventy-eight lines versus sixty-eight lines of otherwise-identical DTO and `Equals` logic, written out seven times. Prowlarr cannot share a DTO with Sonarr because it has no way to *reference* Sonarr's types — so it retypes them, and re-retypes them for each sibling.

**Characterization for the record:** Prowlarr's `Applications/` is **integration at the API boundary via loose, per-app, independently-versioned adapters** — the same pattern one would write against a third-party SaaS. It is emphatically *not* shared code, and its shape is the strongest available evidence that shared code is unavailable: the ecosystem's own integration point had to be built as if the other *arrs were foreign systems, because to Prowlarr they are.

**Secondary point — the indexer definitions.** Prowlarr's Cardigann YAML definitions are **not bundled** (0 `.yml` definition files in the clone) and are **not Servarr-owned**. `NzbDrone.Core/IndexerVersions/IndexerDefinitionUpdateService.cs` fetches them at runtime:

```csharp
private const string DEFINITION_BRANCH = "master";
private const int DEFINITION_VERSION = 11;
...
var request = new HttpRequest($"https://indexers.prowlarr.com/{DEFINITION_BRANCH}/{DEFINITION_VERSION}");
```

with fallback to `{AppData}/Definitions` on disk and a `{AppData}/Definitions/Custom` overlay, plus an 18-entry `_definitionBlocklist` for definitions that have been reimplemented in C#. The source repository is **`Prowlarr/Indexers`** (185 stars, no declared license, pushed daily) — Prowlarr org, not Servarr. Its top contributors (`garfield69`, `kaso17`, `ngosang`, `zone117x`) are substantially the Jackett contributor set, reflecting the definitions' Jackett/Cardigann heritage. **This is a genuine runtime asset feed, consumed by exactly one application.**

---

## 6. Governance

**Servarr is a loose brand with shared community infrastructure, not a formal organization.**

* The `Servarr` GitHub org has **one public member** (`Qstick`, a Radarr/Prowlarr/Lidarr maintainer). There is no published governance document, no steering group, no foundation, no legal entity, no published funding structure. Contributions are handled per-application, with each app carrying its own `CLA.md`, `CODE_OF_CONDUCT.md` and `CONTRIBUTING.md` — five separate CLAs, not one.
* **The wiki** (`Servarr/Wiki`, created 2020-09-03, 632 stars, 103 forks, **no declared license**) describes itself as "the consolidated wiki for Lidarr, Prowlarr, Radarr, Readarr, Sonarr, and Whisparr" ([wiki.servarr.com](https://wiki.servarr.com/)). Its top contributor is `bakerboy448` with 2,697 commits — more than four times the next human contributor — followed by a `ServarrAdmin` bot account (592 commits, currently emitting daily `docs: update supported indexers` commits). The wiki is effectively one dedicated maintainer plus automation, with community PRs. Its structure is per-app (`radarr/`, `lidarr/`, `prowlarr/`, `readarr/`, `sonarr/` directories) with only a thin genuinely-shared layer: `docker-guide.md`, `permissions-and-networking.md`, `install-script.md`, `Definitions.md` (which is literally a disambiguation redirect page), and `servarr/servarr-install-script.sh`. Even the "shared" wiki is mostly five wikis in a trenchcoat.
* **The domain** `servarr.com` hosts the wiki (`wiki.`), the legacy MediaWiki (`wikiold.`), Sentry (`sentry.`), the update server (`releases.`, `<app>.`), the OAuth broker (`auth.`) and a status page. Ownership is not publicly documented; operationally it is run by the same small maintainer circle.
* **Docker images are third-party.** `linuxserver/radarr`, `hotio/prowlarr` etc. are built and maintained by LinuxServer.io and hotio respectively — organizations with no formal relationship to Servarr. Servarr's only container artifacts are CI test images (`ghcr.io/servarr/testimages`).
* **Whisparr** appears in Prowlarr's adapters and on the wiki but has no repository in any of these orgs — it is an out-of-tree Radarr fork that the brand tolerates. **Readarr** is archived and its final commit is a retirement announcement. Membership in "Servarr" is by convention and mutual recognition, not by any formal mechanism.

---

## 7. The three-layer distinction, stated plainly

Conflating these is the error this investigation exists to prevent:

| layer | does Servarr provide it? | evidence |
| --- | --- | --- |
| **Shared branding & documentation** | **Yes, substantially.** The wiki, the `*arr` naming convention, the Discord, the shared troubleshooting and Docker guides, mutual cross-linking. | `Servarr/Wiki`, 632 stars, daily pushes; `wiki.servarr.com/<app>/…` linked from every app's issue templates (`.github/ISSUE_TEMPLATE/bug_report.yml`, `.github/label-actions.yml`) |
| **Shared build & asset infrastructure** | **Yes, genuinely.** Cross-build toolchains for FreeBSD/x86/armel, forked builds of Mono.Posix / SQLite / FluentMigrator / FFMpegCore, CI test images, an update server and an OAuth broker. | §2.1, §2.2, §2.6, §2.7 |
| **Shared application code** | **No. None. Zero.** | §2.1 (no `Servarr.*` application package exists), §2.4 (no submodules, no cross-repo project references), §2.5 (independently drifting dependency graphs), §3 (39.7% byte-identity across notionally-common files), §4 (no shared host, no shared plugin API), §5 (the ecosystem's own integration point is written as REST-against-a-foreign-system) |

If someone tells you "Servarr is the shared core of the *arrs", the counter is a single command:

```
$ grep -rn 'Servarr' */src --include='*.csproj' --include='*.props'
```

Every hit is FFmpeg, SQLite, FluentMigrator or Mono.Posix. Not one is *arr code.

---

## 8. What this means for Arronix

### 8.1 Arronix is filling a genuine gap, not duplicating prior art

The unified-host thesis — one runtime, one database, one auth surface, one plugin contract, with Sonarr/Radarr/Lidarr/Readarr/Prowlarr as plugins — **has no existing implementation, partial or otherwise.** The strongest existing counter-candidates fail on inspection:

* **Servarr the org**: build tooling and hosted services only (§1, §2).
* **Lidarr's plugin system**: real but Lidarr-private, with a five-property `IPlugin` contract carrying no domain abstraction (§4). It loads *Lidarr* extensions into *Lidarr*; it does not host other *arrs.
* **Prowlarr's `Applications/`**: loose HTTP adapters, one per target, with duplicated DTOs and hardcoded API-version gates (§5). It is the shape you build when you *cannot* share code.
* **Third-party unifiers** (dashboards, `pyarr`, Homarr and similar): all aggregate the *arrs over their public REST APIs from outside. Same API-boundary pattern, further out.

The gap Arronix targets — a shared *implementation* seam, not a shared *protocol* seam — is unoccupied. This should be recorded as a positive finding and cited in `ARCHITECTURE.md`'s motivation if it is not already.

### 8.2 …but the gap is unoccupied for reasons worth respecting

Nothing here shows that unification was tried and failed; it shows it was never seriously attempted. Still, the evidence carries warnings:

* **Divergence is load-bearing, not accidental.** `IndexerBase.cs` runs 114 lines in Readarr and 229 in Prowlarr; `TableMapping.cs` runs 168 in Prowlarr and 286 in Lidarr. Prowlarr is not a media manager at all — it has no library, no import pipeline, no file organization — yet it still carries `NzbDrone.Core/Download/`. A unified core must let a plugin decline whole subsystems, not merely configure them.
* **60% drift over ~10 years is what "shared by copy-paste" costs.** It also means there is no single correct version of any given file to extract from. Every extraction into `Arronix.Common` is a *decision*, not a merge, and the Sonarr lineage in `ArronixCore/src` should stay the reference point rather than any attempt to reconcile all four.
* **The domain models genuinely differ.** Music (releases, tracks, artists, MusicBrainz) and books (editions, authors, Goodreads) do not reduce to episodes-and-movies. The plugin seam has to sit *below* the domain model, in acquisition, storage, disk, HTTP, scheduling, notifications and provider registration — which is what `ThingiProvider` already almost is, and which the §3.4 evidence shows stays stable across forks when nobody perturbs it. **`ThingiProvider` is the single best-validated candidate for the plugin seam**: it survived four independent decade-long forks with 10 of 18 files still byte-identical, and it is the abstraction all five apps already use for indexers, download clients, notifications and import lists. Arronix's plugin contract should be recognizably its descendant.

### 8.3 Reuse rather than rebuild

Concrete assets worth consuming rather than reimplementing:

| asset | owner | verdict |
| --- | --- | --- |
| **`Prowlarr/Indexers`** Cardigann YAML definitions + `indexers.prowlarr.com` feed | Prowlarr org | **Reuse the format; treat the feed with care.** The Cardigann schema plus a compatible parser gets Arronix hundreds of indexers for the cost of one YAML interpreter. But the repo has **no declared license** — resolve that before vendoring definitions, and prefer implementing the *parser* against the schema over redistributing the *definitions*. The runtime feed is Prowlarr-operated and versioned (`DEFINITION_VERSION = 11`); depending on it is depending on another project's uptime and goodwill. Mirror, or make the endpoint configurable, or both. |
| **`wiki.servarr.com`** schema/settings/API documentation | Servarr org (no license declared) | **Reuse as reference, not as content.** Its per-app settings, quality-definition, custom-format and naming-token pages are the best existing description of *arr semantics and are directly useful when specifying Arronix's equivalents. It carries no license, so do not copy prose; treat it as a spec to be re-expressed. It is also the de-facto standard users will arrive with — matching its terminology reduces migration friction. `GLOSSARY.md` should stay aligned with it. |
| **`torznab.xsd`** | present verbatim in all four `schemas/` dirs | **Reuse directly.** Torznab/Newznab is the real interoperability standard in this ecosystem, and it is a published XML schema, not anyone's private code. |
| **The *arr public REST APIs** (`/api/v3`, `/api/v1`) | per-app | **Reuse as a free specification of *semantics*. They are not a wire target — Arronix presents no *arr-shaped endpoints.** These APIs, and Prowlarr's `Applications/` proxies (§5), are the best available written-down answer to *what operations does a media manager actually need to expose* — queue, history, blocklist, manual search, root folders, provider settings — and reading them saves real design time. Presenting them is a different matter and is settled against: a per-plugin Sonarr-v3/Radarr-v3/Lidarr-v1 surface would be a second, media-noun-bearing representation of data that already has one in `/api/v1`, and it re-imports the per-kind DTO duplication that §5 identifies as Prowlarr's central pathology (`ARCHITECTURE.md` §8: the legacy compatibility surface "was not built… a compatibility shim would have been a shim over nothing"; `unified-host-runtime.md` #46 and its §8 deferred list say the same, and `frontend-decommissioning.md` holds the invariant that no TV concept enters `Abstractions/Api/Client`). Third-party clients (Overseerr/Jellyseerr, Ombi, `pyarr`, mobile clients, dashboards) are a real audience and are served by `/api/v1` being good and documented, not by Arronix impersonating four other products. |
| **`Servarr.*` NuGet forks** (FFMpegCore, FFprobe, FluentMigrator, System.Data.SQLite, Mono.Posix) | Servarr org | **Do not adopt.** `ArronixCore/Directory.Packages.props` already takes upstream `FluentMigrator.Runner` 8.0.1, upstream `Mono.Posix.NETStandard` 5.20.1-preview, `SourceGear.sqlite3` and `System.Data.SQLite`. Sonarr upstream reached the same conclusion independently (§2.3). These forks exist to support platforms (FreeBSD, armv5, 32-bit x86, Synology) that a net10.0 greenfield need not carry. Adopting them would import a private feed dependency, a stale fork lineage and an unowned security surface for nothing. |
| **`*.servarr.com` runtime services** (update, auth broker) | Servarr org | **Do not depend on.** These are another project's hosted infrastructure, gated on secrets Arronix does not hold. Where Arronix needs an OAuth broker for third-party services (Trakt, Spotify, Goodreads-style integrations), that is a first-party service to design — note it as a dependency the plugin-distribution and threat-model documents must account for. |
| **Servarr Docker images** | n/a | **Nothing to reuse.** The *arr images users actually run are LinuxServer.io's and hotio's, not Servarr's. Arronix ships its own; the relevant prior art is those images' conventions (PUID/PGID, `/config`, `/downloads`), not Servarr's. |

### 8.4 One-line framing

> Servarr is a wiki and a build farm with a brand attached. The five *arrs are five copies of one 2010-era codebase drifting apart in parallel, integrating with each other over HTTP as though they were strangers. Arronix is not competing with a shared core, because there has never been one.

---

## Appendix: reproducing this investigation

```bash
# §2.1 — every Servarr package reference in the ecosystem
cd /Users/adam/Code/GitHub/Arronix/_reference
grep -rniI --include='*.csproj' --include='*.props' --include='*.config' \
           --include='packages.lock.json' --include='*.targets' --include='*.sln' 'servarr' .

# §2.3 — the Sonarr control case
gh api repos/Sonarr/Sonarr/contents/src/NuGet.Config --jq '.content' | base64 -d
gh api repos/Sonarr/Sonarr/contents/src/NzbDrone.Core/Sonarr.Core.csproj --jq '.content' | base64 -d | grep PackageReference

# §2.4 — submodules
for r in Radarr Lidarr Readarr Prowlarr; do cat $r/.gitmodules 2>/dev/null || echo "$r: none"; done

# §1 — org inventory
gh api 'orgs/Servarr/repos?per_page=100&sort=updated' \
  --jq '.[] | [.name,(.archived|tostring),(.fork|tostring),(.pushed_at|.[0:10]),(.description//"-")] | @tsv'

# §3.2 — drift measurement: md5 every .cs under NzbDrone.Common+Core, match by relative path,
#         then count byte-identical files across the intersection of paths.

# §5 — the Prowlarr integration surface
wc -l Prowlarr/src/NzbDrone.Core/Applications/*/*.cs
diff Prowlarr/src/NzbDrone.Core/Applications/{Sonarr/SonarrIndexer.cs,Radarr/RadarrIndexer.cs}
```

**Sources consulted:** the four local `--depth 1` clones; the GitHub REST API for the `Servarr`, `Sonarr`, `Radarr`, `Lidarr`, `Readarr` and `Prowlarr` organizations; [wiki.servarr.com](https://wiki.servarr.com/); [github.com/Servarr/Wiki](https://github.com/Servarr/Wiki); [github.com/Servarr](https://github.com/Servarr).
