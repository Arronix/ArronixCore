# Arronix

Arronix is a clean-sheet, plugin-composed media automation platform for .NET 11. It extracts the orchestration common to the *arr ecosystem without making the common core own the semantics of Movies, Television, Music, Books, Video, Audio, Documents, or any particular external service.

The goal is replacement-grade behavioural coverage through a small, strongly typed third-party SDK. Media,
format, language, and provider authors state their owned differences; Arronix derives the common application
machinery. It does not obtain a small surface by flattening domain shape or exposing Host internals.

The project is pre-alpha. It builds and has a broad local test suite, but persistence, authentication, production provider packages, and an end-to-end typed acquisition flow are incomplete.

## Run it

```bash
bash eng/run-arronix.sh
```

That one command builds this repository's deliverables, installs them, and starts the installation it just
made. It prints the address to open, the packages it installed and where the state lives, then keeps running
until you interrupt it. It stops only the server it started, and it never signals a process it did not
launch. The script itself only resolves the pinned .NET 11 SDK; composing and running an installation is
owned by `src/Arronix.Installation`.

Everything it produces lives under one directory that git ignores:

```text
artifacts/installation/
  server/            the published Arronix.Api
  client/wwwroot/    the published Arronix.Client
  packages/          one folder per installed package
  package-state/     each package's own data, cache and scratch folders
  state/arronix.db   the durable catalog and library state
  installation.json  what this installation holds
```

The installation carries the real Movies extension, the Video format package it requires, the reference
languages, the production TMDb provider and a sample movie catalog. TMDb needs an API read access token
before it can answer; add one under Settings. The sample catalog is an ordinary separately packaged
`ICataloger<Movie>` with invented titles, configured for you on first run, so the catalog path can be
evaluated before any account exists anywhere. `--no-sample-catalog` leaves it out.

Once it is up: the dashboard lists the installed extensions and the platform's health; **Movies** is the
generic media workspace derived from the extension's own declaration; **Find in catalog** searches the
catalogs configured for that kind — search the sample catalog for `sample` to see everything it holds — and
each result can be added, opened, monitored and refreshed;
**Contracts** shows the shared contract assemblies the browser downloaded from this host and verified;
**Settings** configures providers. Everything you add survives a restart.

```bash
bash eng/run-arronix.sh --port 5300     # take that port, or refuse if something holds it
bash eng/run-arronix.sh --no-build      # start what is already installed
bash eng/run-arronix.sh --open          # open the address once it answers
bash eng/run-arronix.sh install         # compose the installation without running it
bash eng/run-arronix.sh reset           # empty its state, leaving the installed code
bash eng/run-arronix.sh reset --all     # remove the installation directory
bash eng/run-arronix.sh --help          # every argument
```

The installed server knows which installation it belongs to. `Arronix:Installation:Root` in
`artifacts/installation/server/appsettings.json` is what makes the packages folder, the per-package state
folder, the database and the client root one installation rather than four settings that have to be kept in
agreement; leave it unset and each is configured independently, as before. A fresh installation reports
health as *degraded* because no library root folder is configured, which is accurate — nothing above needs
one, and importing files is not yet implemented.

## The architectural center

A media extension closes an ordinary abstract definition over three C# domain types and one typed parser:

```csharp
public abstract class MediaType<TItem, TTarget, TRelease, TParser>(
    MediaKindId kind,
    string singularName,
    string pluralName,
    IReadOnlyList<IFormatUse> formats,
    ISelectionDefinition<TItem> availability,
    FileBindingDefinition files = FileBindingDefinition.OnePerItem)
    where TItem : class, IMediaItem
    where TTarget : class, IReleaseTarget
    where TRelease : class, IRelease
    where TParser : IReleaseParser<TRelease>
{
    public MediaKindId Kind { get; } = kind;
    public string SingularName { get; } = singularName;
    public string PluralName { get; } = pluralName;
    public FileBindingDefinition Files { get; } = files;
    public IReadOnlyList<IFormatUse> Formats { get; } = formats;
    public ISelectionDefinition<TItem> Availability { get; } = availability;
}
```

The primary constructor captures the required identity, display names, non-empty format composition,
minimum-availability rule, and file cardinality once; one-file-per-item is the ordinary default. The kind
identifier is explicit and does not depend on mutable display wording. That four-arity base is the current
typed authoring surface. Ordinary typed overrides declare identity, grouping, additional selections,
semantic searches, matching, release policy, querying, naming, summaries, intent exceptions, workbenches,
and derivations. Standard platform actions are derived from those facts rather than repeated in each media
type. There is no public whole-media replay builder and no reflection-discovered `IUses...` capability
vocabulary. `TParser` implements the static `IReleaseParser<TRelease>.Parse` contract directly in C#; the
parser type is the declaration.

- `TItem` is the durable catalog/library entity.
- `TTarget` is the ephemeral acquisition intent.
- `TRelease` is a publication interpreted from an indexer's raw `ReleaseListing`.
- `TParser` is the media-owned parser that produces that exact release type.

Every item and grouping entity shares a compiled `IMediaEntity` floor. The concrete
`MediaItem<TReleaseTimeline,TReleaseStage>` and `MediaCollection<TItem>` classes provide the common shape;
the three-arity item form preserves an exact nominal item type in relationships. `Movie`, for example, is
an intentionally empty closure over that complete common shape. Release milestones and availability
behaviour stay together in the media-owned lifecycle. Typed definition values bind media types to formats,
groups, release policies, parsers, selections, searches, and matching. A build-time generator emits closed
field getters, and Host derives the kind-blind descriptor without reflecting over plugin properties.

The same direct-use rule applies to acquisition values. `ReleaseTarget<TItem>` is the ordinary one-item
target and `Release<TRepresentation>` carries the common title, year, edition, and representation. A media
extension adds its own target or release class only when it carries additional facts, such as Television's
set-shaped episode coverage.

Catalogers and curators are paired with the item shape they actually supply:

```csharp
ICataloger<TItem>
ICurator<TItem>
```

Each cataloger also recognizes identifiers from its own namespace when they appear in a release name.
Host composes those `ExternalIdReading` values into the typed parser context, so media definitions never
copy vendor marker names or regular expressions.

Format capabilities own representation facts. `Arronix.Format.Video`, for example, owns video lineage, streams, codecs, dynamic range, audio tracks, file extensions, title vocabulary, and video policy defaults. Movies and Television compose Video; Abstractions and Host do not contain video vocabulary.

## Projects

```text
Arronix.Abstractions      public media-neutral contracts, version 0.9.0
Arronix.Sdk               authoring metapackage: Abstractions plus generator/analyzer assets
Arronix.Common            shared host-side implementations
Arronix.Plugins           manifests, isolation, compatibility and capability admission
Arronix.Host              DI composition and generic runtime engines
Arronix.Api               REST and SignalR edge
Arronix.Client            Blazor WebAssembly client; Abstractions only

Arronix.Format.Video      independently owned video representation capability
Arronix.Language.Reference independently loadable English, German and French language rules
Arronix.Generators         compile-time media shape projection; analyzer only

Arronix.Plugin.Movies     typed reference media extension
Arronix.Plugin.Tv         television pressure test; production implementation migrating
Arronix.Plugin.Music      legacy media implementation awaiting typed conversion
Arronix.Plugin.Books      legacy media implementation awaiting typed conversion
```

## Release selection

Common release terms mix channel, carrier, acquisition, transformations, raster, codec, correction, and defects. Arronix does not compress those into one global quality ladder.

The typed model separates:

- raw provider output (`ReleaseListing`);
- media-owned interpretation (`TRelease`);
- target coverage (`TargetMatch<TTarget>`);
- format-owned representation facts (`IRepresentation` implementations);
- one compiled `ReleasePolicy<TRelease>` assembled from owner-scoped fragments.

Selection is deterministic: requirements, lexicographic core preferences, bounded facets, acquisition factors, and a stable release-id tie-break. Indexer result order cannot change the answer.

## Provider activation

Plugins register provider implementation types rather than constructed objects. Host admits the declaration first, then activates the implementation through DI with its capability-scoped `IPluginContext`. Vendor implementations remain outside universal contracts and media definitions.

Language-specific title comparison, query spelling, file-name spelling, and sorting are plugin capabilities too. A media
definition states a title's language; it does not embed English articles, French stop words, or German
transliteration rules.

## Start here

- [Current operational context](CONTEXT.md)
- [Architecture](ARCHITECTURE.md)
- [External interface contract](INTERFACE.md)
- [Decision history](HISTORY.md)
- [Contract stability](docs/contracts/stability.md)
- [Glossary](GLOSSARY.md)

Build and test:

```bash
bash eng/ci/run-tests.sh
```

Exact .NET 11 Preview 7 SDK `11.0.100-preview.7.26381.103` is pinned by `global.json`. The proof rail performs
locked restore and one Release warnings-as-errors build, retains that build's solution binlog as practical
evidence of the actual compiler inputs, requires a non-empty result from every discovered test project, and
binds each registered NUnit leaf to its executed assembly and CLR method. Its Portable PDB checks include exact
embedded bytes for the locked primary and support sources. An eight-column append-only registry protects three
durable proof sentinels, while the compatibility ledger enforces exactly 302 current skips; four immutable Movie
parser rows are isolated from the ordinary representative corpus. This is same-build provenance rather than a
cryptographic or hermetic-build attestation claim. First-party plugins currently declare the tested contract
range `">=0.9 <0.10"`.

## License

See [LICENSE](LICENSE) and [NOTICE](NOTICE).
