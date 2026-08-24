# Arronix.Abstractions

`Arronix.Abstractions` is the package-free contract assembly shared by Host, plugins, the API, and the
Blazor client. It owns universal semantics only. Movie, television, video, audio, document, codec, and
vendor vocabularies belong to the extensions that define them.

## Contract line

The current version is `0.8.0`. First-party manifests declare `>=0.8 <0.9`.

Arronix is pre-1.0. Any public contract may change or be deleted in a minor release. There is no separate
per-type experimental tier; versioning and plugin manifest ranges express compatibility. See
`docs/contracts/stability.md` for the complete policy.

## Typed media boundary

A media extension derives from:

```csharp
MediaType<TItem, TTarget, TRelease, TParser>
```

The four-arity base is the complete authoring surface. Typed override values declare identity,
groups, additional selections, semantic searches, matching, release policy, querying,
naming, summaries, intent exceptions, workbenches, and derivations. Standard platform actions are derived
from those facts rather than declared per media type. Optional and repeatable relationships are
empty or populated collections, not capability interfaces.

Its primary constructor requires the stable `MediaKindId`, singular/plural display names, a non-empty
format composition, and the typed minimum-availability selection. File binding is also constructor-owned
and defaults to `OnePerItem`; kinds with a different file relationship state it explicitly. The stable kind
remains independent of display wording and is never inferred by lower-casing the plural name.

- `TItem : IMediaItem` is the durable catalog and library entity.
- `TTarget : IReleaseTarget` is ephemeral acquisition intent.
- `TRelease : IRelease` is an interpreted publication.
- `TParser : IReleaseParser<TRelease>` is the media-owned parser for that publication shape.

`IMediaItem` and `IMediaGroup<TItem>` inherit the minimum `IMediaEntity` contract. The concrete
`MediaItem<TLifecycle>` and `MediaCollection<TItem>` classes carry the common, fully visible schema and can
be used directly. Release milestones and availability behaviour remain in the media-owned lifecycle;
a media-specific subclass is needed only when it adds real facts. `ItemInfo` is the reusable
title/overview value for `Localized<ItemInfo>`.

`ReleaseTarget<TItem>` is the directly usable one-item acquisition target. `Release<TRepresentation>`
directly carries title, year, edition, and the format-owned representation. A media-owned target or release
type is justified only by extra coverage or release facts.

The definition uses constructor values for invariant identity, naming, format composition, minimum
availability, and file cardinality, plus ordinary virtual instance members for optional or repeatable
media-specific declarations. Parsing is the deliberate static generic
seam: `TParser` implements `IReleaseParser<TRelease>.Parse` directly in C# instead of returning a
parse-declaration object graph.
There is no public non-generic shadow media model. `IMediaTypeRegistration` carries the captured definition
and closed type tuple through a kind-blind loader by double dispatch; Host creates its private runtime
projection after admission. Wire descriptors are derived discovery and generic-presentation data, never a
substitute for the typed schema.

Relationships are declared through closed generic values such as `FormatUse<TRepresentation>`,
`GroupDefinition<TItem,TGroup>`, typed selections, searches, matching rules, and release policy. Host
compiles them into its private runtime projection at the kind-blind boundary. The media namespace exports
no builder API and no `IUses...` capability badge surface.

Raw indexer output is `ReleaseListing`. `TargetMatch<TTarget>` retains typed covered and missing portions.
`ReleaseOption<TTarget,TRelease>` joins the listing, interpretation, and coverage judgment.
`ReleasePolicy<TRelease>` declares hard requirements, lexicographic preferences, and bounded facets over
the media-owned release type.

## Providers

Provider families are platform-owned dispatch seams:

- `IIndexer`
- `IDownloader`
- `INotifier`
- `ICataloger<TItem>`
- `ICurator<TItem>`

Catalogers and curators return the media type's own item class; they do not return a universal field bag.
Plugin registration contributes an implementation type. Host activates admitted providers through an exact
public `(IPluginContext)` constructor, or a public parameterless constructor when no context is needed; it
never resolves plugin implementations from Host DI. The non-generic `ICataloger` floor recognizes identifiers from its
own namespace in release text; Host validates and supplies those readings to the typed media parser.

## Format capabilities

`IRepresentation` and `FormatFamilyDefinition<TRepresentation>` are the universal binding seam. A format
package owns its representation types, file extensions, vocabulary, recognizers, and policy defaults.

The first implementation is `Arronix.Format.Video`. It is intentionally not referenced by Abstractions,
Host, or Client. Movies and Television reference it because those media types compose Video.

## Provenance and descriptors

Interpretation provenance is a sidecar, `InterpretationTrace<TSubject>`. Ordinary typed properties and
nullability represent facts and absence. There is no universal `Evidence<T>` wrapper or global ordering of
evidence sources.

Wire and presentation descriptors may contain serializable data only. They must not carry `System.Type`,
delegates, executable policies, or format implementations.

## Temporary compatibility surface

Television, Books, and Music still use the legacy imperative media seams while they migrate. The remaining
`ParsedRelease`, `QualityTier`, `IQualityModel`, ladder properties, and related interfaces are removal-only
compatibility scaffolding. Their video-specific fields have been removed, and new code must use typed
releases and release policy.

## Dependency rule

This project has no package references and no project references. Keep it that way.

Current architecture, migration state, and external boundary guidance are in `CONTEXT.md`,
`ARCHITECTURE.md`, and `INTERFACE.md` at the repository root.
