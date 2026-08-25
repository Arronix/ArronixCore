# Typed media north star

This document defines the destination and governing authoring rules. The dependency-ordered route from the
current repository state is maintained in the [typed media execution roadmap](typed-media-roadmap.md).

## Outcome

A media extension is one ordinary object whose generic base closes the media-owned item, acquisition
target, interpreted release, and parser types. Its primary constructor supplies invariant identity, names,
non-empty format composition, minimum availability, and file cardinality; its overridable members return
optional or repeatable media-specific typed values. The extension does not
implement capability badges, expose a configuration callback, or replay its definition into a builder.

```csharp
public sealed partial class Movies() :
    MediaType<Movie, ReleaseTarget<Movie>, Release<Video>, MovieReleaseParser>(
        MediaKindId.FromString("movies"),
        "Movie",
        "Movies",
        formats: [new FormatUse<Video>(VideoFormat.Definition)],
        availability: new OrderedSelectionDefinition<Movie, MovieReleaseStage>(
            movie => movie.Status,
            "Minimum availability",
            MovieReleaseStage.Released))
{
    public override IdentityDefinition Identity => MovieIdentity;
    public override IReadOnlyList<IGroupDefinition<Movie>> Groups => [MovieCollections];
    public override IReadOnlyList<ISelectionDefinition<Movie>> AdditionalSelections => [AvailabilityDelay];
    public override IReadOnlyList<SearchDefinition> Searches => [ByIdentifier, ByTitle];
    public override MatchingDefinition<Movie> Matching => MovieMatching;

    public override QueryDefinition<Movie> Querying => MovieQueries;
    public override NamingDefinition<Movie> Naming => MovieNaming;
    public override SummaryDefinition<Movie> Summary => MovieSummary;
    public override IntentDefinition<Movie> Intent => MovieIntent;
    public override IReadOnlyList<IWorkbenchDefinition<Movie>> Workbenches => MovieWorkbenches;

    public override ReleasePolicy<Release<Video>> ReleasePolicy => MovieReleasePolicy;
}

public sealed class MovieReleaseParser : IReleaseParser<Release<Video>>
{
    public static ReleaseParseResult<Release<Video>> Parse(ReleaseParseContext context)
    {
        // Direct typed C# parsing; context.ExternalIds were recognized by catalogers.
    }
}
```

The exact types above are the contract. The host may erase them while producing its runtime and wire
projections, but erasure is a one-way internal compilation step and never the media author's vocabulary.

## Authoring promise

A third party should be able to take ownership of a complete media domain through a few ordinary CLR
types and the closed generic relationships between them. The author supplies the domain's item, target,
release, parser, format uses, and genuine policy or presentation differences. Arronix supplies and derives
the standard actions, orchestration, validation, registration, runtime dispatch, generic presentation, and
wire projection.

The authoring surface is small because the common abstractions are complete, not because the model is
flattened. An author does not replay a schema through builders, maintain parallel descriptors, implement
capability badges, repeat generic types across registration calls, or understand Host compilation and
type-erasure mechanics. Known relationships remain typed until Host deliberately compiles them into an
internal projection.

The obvious implementation should be the ownership-correct implementation. A media extension cannot
accidentally become the home of catalog-vendor identifiers, format vocabulary, language rules, source
protocol codes, or Client layout simply because those concepts appear while implementing that media kind.

The ordinary package reference is `Arronix.Sdk`. It contributes the semantic contract as the extension's
runtime dependency and the shape generator as an analyzer, but no SDK runtime assembly. The media definition
is `partial` so the generator can supply its non-authoring Host-binding projection; a missing modifier is reported
at the declaration by an Arronix diagnostic. Generated capture, visitors, erased registrations, expression
carriers, and `System.Type` bridges are not author-facing concepts.

## Rules

1. `MediaType` owns common slots. Required values are constructor arguments; an optional slot with zero
   values returns an empty collection and is not represented by an `IUses...` marker.
2. A relationship with varying closed types is a typed value using double dispatch. It is not encoded as an
   interface implemented by the media type and discovered by reflection.
3. Plugin registration names the partial media definition once: `registry.AddMediaType<Movies>()`. The author
   neither implements nor calls a generated shape or capture member.
4. The parser type is passed once as generic arity and returns the exact release shape through the static
   `IReleaseParser<TRelease>` contract. It is not rebuilt as a `ParseDeclaration` object graph.
5. `MediaItem` contains common item facts. Media-owned lifecycle and aggregate types remain ordinary public
   CLR types so paired catalogers and curators compile against the exact shape.
6. Grouping is a zero-or-more collection of durable typed relationships. An item may belong to zero, one,
   or several group instances and a media type may declare several group relationship types.
7. File binding is an invariant base-constructor value. It defaults to one file per item; a media type with
   another item/unit/file cardinality states it explicitly.
8. Format composition and minimum availability are required constructor values. A media definition cannot
   exist without at least one format family or without the selection that drives availability policy.
9. Searches are semantic requests a media type can make. Provider-protocol categories and codes are mapped
   by provider or protocol plugins, not embedded in a media definition.
10. Release dates and availability behaviour live together in a media-owned lifecycle object. A computed
   availability date is not stored as a pretend source milestone, and time-dependent status is evaluated
   against an explicit date or clock.
11. Catalog presence, monitoring state, and release stage are separate concepts. An upstream-deleted item is
   not a release stage below `Tba`.
12. User-authored template text may remain textual grammar. Known entity references, policies, rows,
    relationships, and provider pairings remain typed.
13. External identifier marker syntax belongs to the cataloger that owns the namespace. Host supplies
    validated `ExternalIdReading` values to the parser; Movies does not know TMDB or IMDb spellings.
14. The host projects the definition exactly once. No public builder API and no second declarative schema
    remain when the typed migration is complete.
15. Regression examples are test data. A media definition ships its parser and runtime behavior, not the
    cases used to verify them.
16. Standard operations are derived by Host. A media definition supplies availability, identity, groups,
    searches, naming and policy facts; it does not restate the platform action catalogue or wire keys.

## Vertical completion test

An abstraction or feature is complete only when one meaning survives the whole path:

```text
typed authoring
    -> generic capture and plugin admission
    -> DI activation and Host execution
    -> persistence where applicable
    -> API/wire projection
    -> Client consumption
    -> compatibility and architecture tests
```

A type, descriptor, generator output, unit test, or registration seam proves only its own layer. Any legacy
adapter, parallel schema, unexecuted policy, private Client convention, or runtime bypass remains explicit
migration work. Feature coverage is checked against the source *arr applications and the observable
Scene/PTP ecosystem; intentional differences are named and tested rather than silently removed.

Movies is the first authoring example, not evidence of universality. Television tests set-shaped coverage
and nested units; Music and Books test whether a supposed common primitive is actually video-family
vocabulary. Repeated local adapters indicate a missing common abstraction or incorrect owner boundary and
must not become the SDK pattern.

## Migration acceptance

- The `Movies` definition has no capability interfaces or `Configure` method; its plugin module performs
  only the required `AddMediaType<Movies>()` registration.
- `MovieCapabilities.cs` and the public media builder interfaces are gone.
- Host media compilation does not reflect over `IUses...` interfaces.
- The Movies module calls only `AddMediaType<Movies>()`.
- Movies supplies kind identity, display names, format composition, minimum availability, and file binding
  through the `MediaType` constructor rather than overriding required properties.
- Movies binds `MovieReleaseParser` as generic arity and exposes no `ParseDeclaration`.
- A public `Movie` type is available to paired provider assemblies; it is not a source-only alias.
- Movie release milestones and stage evaluation are one object-oriented model.
- Movies can declare plural collection membership and the host projects it as a many-to-many grouping axis.
- Production media contracts and runtime models contain no parser test corpus.
- Movies contains no per-kind action transcript; every standard action is derived from the compiled media definition.
- Standard workbenches, browse defaults, and ordering are derived unless they encode a demonstrated media
  difference; Movies does not repeat them merely because the runtime needs descriptors.
- Format defaults and media policy fragments compose without requiring a media author to drive a public
  policy-builder callback.
- Provider registration does not require an author to restate an item/media relationship already closed by
  the provider implementation.
- Generated projections and erased registration visitors are hidden binding SPI, not author obligations or
  concepts required to understand the SDK.
- An ordinary external project restores `Arronix.Sdk` plus its chosen domain packages and receives the
  generator and author-site diagnostics without a repository reference or direct generator dependency.
- A manifest does not manually repeat derivable media shape, token, policy, or action declarations.
- The solution builds with no warnings, all enabled tests pass, and architecture tests enforce the new
  authoring boundary.
- A feature is not reported complete until its production path, wire projection, and consumer path use the
  typed abstraction being tested.
- At least one release traverses the production path from listing and catalog-owned identifier recognition,
  through media parsing plus format recognizers, typed matching and policy, deterministic selection, and an
  executable acquisition action.
- An independently authored media extension can express its owned differences without depending on Host,
  Client, runtime-erasure, or generated-projection implementation details.
