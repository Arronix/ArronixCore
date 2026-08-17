# The Typed Media Model — contract specification

> **Status:** Design, pass 2. Written from a complete typed draft of Movies
> (`typed-movies-exhibit.cs`, produced alongside this document) that accounts for every line of
> `src/Arronix.Plugin.Movies/MoviesShape.cs`, `MoviesIntent.cs` and `Definition/*.cs`.
>
> **Binding direction:** `docs/open-decisions.md` **Part 6**. Part 5 is the review that motivates it.
> Entries marked DISSOLVED (D-2, D-4) are not actioned; both are shown below as *dissolved by construction*.
>
> **Scope:** Movies only. Tv, Music and Books are expected to stop compiling and to be parked out of
> `Arronix.sln` with a re-add trigger. Nothing here is designed for TV's five coordinate spaces or Music's
> acquisition≠file-unit split; where a choice is cheap to keep open, it is marked **[door]** in one line and
> not built.
>
> **The one-sentence claim.** A media kind becomes typed entities plus attributes plus a fluent
> configuration; the host *derives* the existing `MediaShape` / `MediaLevel` / `FieldDescriptor` /
> `FileBinding` / `CoordinateSpace` / `GroupingAxis` objects from them, so every engine, the binder, the
> intent surface and the client are unchanged. This is EF Core's split between entity types and the model,
> which is also the idiom chosen for persistence.

---

## 0. What the exhibit proved, in numbers

| | Declaration surface (pass 1) | Typed surface | Note |
|---|---:|---:|---:|
| `MoviesShape.cs` | 1,445 | ~190 | 40 field-id constants + 40 `FieldDescriptor` blocks → 24 properties |
| `MoviesIntent.cs` | 965 | ~120 | 11 browse axes, 17 sorts, 22 filters, 5 states **derived, not written** |
| `Definition/` (naming, notify, query, match, quality) | 523 | ~90 | ten P2-8 rows vanish; three P2-2 grammars become C# methods |
| `Definition/` (parse, corpus, catalog) | 1,121 | 1,121 | **carried unchanged** — P2-5 survives; catalog leaves next milestone |
| Vendor references across the plugin | **217** | **44** | all 44 in one call: `b.Catalog(…)`, which leaves next milestone |
| Vendor references in the *shape* | **93** | **0** | P2-1's leading item, closed |

The measure Part 5 set for pass 2 was *"what does the second kind cost?"*. The typed Movies file is ~400
lines, of which ~190 is the entity and ~120 is configuration. Books, which has no collection axis, no
ordered availability and one search kind, should land near 150.

---

## 1. The core contracts

Placement: `src/Arronix.Abstractions/Media/`. They cross the extension boundary and the client boundary, so
the placement rule in `Wire/MediaKindDescriptor.cs` puts them in the contract assembly.

### 1.1 `IMediaItem`

```csharp
/// <summary>
/// A catalog entity a media kind owns. Marker only: everything about it is read from its properties.
/// </summary>
/// <remarks>
/// Deliberately empty. A base class carrying <c>Id</c> would put a host-owned member on a
/// plugin-owned type and force every kind to inherit before it can declare; the identity is found by the
/// <see cref="IdentityAttribute"/> instead, exactly as EF Core finds a key. The interface exists so that
/// generic constraints can name "an item", not so that it can carry behaviour.
/// </remarks>
[Experimental(ExperimentalContracts.Media, UrlFormat = ExperimentalContracts.UrlFormat)]
public interface IMediaItem;
```

### 1.2 `IMediaGroup<TMember>`

```csharp
/// <summary>
/// A collection that cuts across a kind's items rather than containing them: monitorable, possibly
/// outliving its members, with metadata of its own.
/// </summary>
/// <typeparam name="TMember">The item type whose instances belong to a group.</typeparam>
/// <remarks>
/// This is what closes the defect <c>MoviesShape.cs</c> recorded against itself: <c>GroupingAxis</c>
/// declared that a group has its own metadata and had nowhere to say what that metadata is, so a front end
/// could render a movie generically and could not render a collection at all. A group is a type, so its
/// fields derive exactly as an item's do.
/// </remarks>
[Experimental(ExperimentalContracts.Media, UrlFormat = ExperimentalContracts.UrlFormat)]
public interface IMediaGroup<TMember>
    where TMember : IMediaItem;
```

### 1.3 `IMediaType<TItem>` — the authoring seam

```csharp
/// <summary>
/// The authoring seam a media-kind plugin implements: one type per media kind, declaring what the item's
/// attributes cannot.
/// </summary>
/// <typeparam name="TItem">The kind's item type.</typeparam>
/// <remarks>
/// Static-abstract, and it does <b>not</b> derive from the non-generic <see cref="IMediaType"/>. The two
/// are the same split EF Core has between <c>DbContext.OnModelCreating</c> and <c>IModel</c>: this one is
/// how a kind is written, the other is what the host holds afterwards. A plugin type that implemented both
/// would be authoring surface and runtime model at once, which is precisely the conflation pass 1 had.
/// </remarks>
[Experimental(ExperimentalContracts.Media, UrlFormat = ExperimentalContracts.UrlFormat)]
public interface IMediaType<TItem>
    where TItem : IMediaItem
{
    /// <summary>Gets the media kind identifier. Must equal the manifest's <c>mediaKinds</c> entry.</summary>
    static abstract MediaKindId Kind { get; }

    /// <summary>Declares what the item's attributes cannot say.</summary>
    /// <param name="builder">The builder.</param>
    static abstract void Configure(IMediaTypeBuilder<TItem> builder);
}
```

> **Naming risk, stated once.** `IMediaType<TItem>` and `IMediaType` differ only in arity and are unrelated
> by inheritance, which reads oddly at a glance. Part 6 named both, so both names are kept; if the collision
> proves confusing in review, rename the runtime one `IMediaTypeModel` — a mechanical change confined to the
> host and the client, since no plugin ever names it.

### 1.4 `IMediaType` — the non-generic handle the host and the client hold

This is the answer to *"what must the non-generic handle expose for a client that never names `Movie`?"*.
The client's reference discipline does not change: `Arronix.Client` references `Arronix.Abstractions` only,
resolves `IMediaType` from DI, and gets everything from it.

```csharp
/// <summary>
/// One media kind's runtime model: the derived descriptor, the item type, and the projections a consumer
/// that cannot name the item type needs.
/// </summary>
/// <remarks>
/// Built by the host from a <see cref="IMediaType{TItem}"/>; never implemented by a plugin. Every member
/// is either derived from the item type or carried verbatim from the builder — there is no second source
/// of truth and nothing here can disagree with the entity.
/// </remarks>
[Experimental(ExperimentalContracts.Media, UrlFormat = ExperimentalContracts.UrlFormat)]
public interface IMediaType
{
    /// <summary>Gets the media kind identifier.</summary>
    MediaKindId Kind { get; }

    /// <summary>Gets the item type, so <c>System.Text.Json</c> and EF Core have a target.</summary>
    Type ItemType { get; }

    /// <summary>Gets the group types this kind declares, if any.</summary>
    IReadOnlyList<Type> GroupTypes { get; }

    /// <summary>Gets the derived structure — the same <see cref="MediaShape"/> every engine already reads.</summary>
    MediaShape Shape { get; }

    /// <summary>Gets the derived intent surface.</summary>
    PluginIntentSurface Intent { get; }

    /// <summary>Gets the release, match, query, naming and summary models the engines compile.</summary>
    MediaKindModel Model { get; }

    /// <summary>Projects one typed item onto the descriptor-shaped view a non-.NET consumer reads.</summary>
    /// <param name="item">An instance of <see cref="ItemType"/>.</param>
    /// <returns>The view.</returns>
    /// <exception cref="ArgumentException"><paramref name="item"/> is not of <see cref="ItemType"/>.</exception>
    ItemView Project(object item);

    /// <summary>Reads a field off a typed item by its derived identifier, for a generic sort or filter.</summary>
    /// <param name="item">An instance of <see cref="ItemType"/>.</param>
    /// <param name="fieldId">A <see cref="FieldDescriptor.FieldId"/> from <see cref="Shape"/>.</param>
    /// <returns>The value, or an absent value when the item has none.</returns>
    FieldValue Read(object item, string fieldId);
}
```

Four members, four reasons:

| Member | Why the client cannot do without it |
|---|---|
| `ItemType` | Part 6 §2: a `System.Text.Json` target so the typed model can cross to a .NET client directly, and the EF Core entity type on the server. Without it there is nothing to deserialize into. |
| `Shape` | The generic browser renders columns, sorts and detail panes from it. It is *derived*, so it is not a second source of truth. |
| `Project` / `Read` | A Python CLI or a TUI cannot load the assembly (Part 6, "consequences"). These two are the projection to `ItemView`/`FieldValue` that keeps the descriptor honest for non-.NET consumers. `Read` exists separately because a generic table sorting on one column should not project a whole item. |
| `Model` | What the engines compile. Held here rather than beside the registry so that "everything about a kind" has one handle. |

**Not on it, deliberately:** any `Task<…>`. `IMediaType` is a model, not a service. Catalog projection stays
on `IMediaItemSource`, which Part 6 keeps and which closes over `TItem` inside the family.

---

## 2. The attribute vocabulary

Placement: `src/Arronix.Abstractions/Media/Attributes/`. All `[AttributeUsage(AttributeTargets.Property)]`,
all sealed, none inheritable.

### 2.1 The dividing rule

> **An attribute states a fact about one property in isolation. The builder states a fact that relates two
> or more things, or that is about the type as a whole.**

Applied: *"this property is the title"* is intrinsic to the property → attribute. *"files bind one-to-one to
items"* relates the item to the file model → builder. *"minimum availability is a threshold over `Status`"*
relates a property to a selection policy → builder. *"this string is a synopsis rather than a line"* is
intrinsic → attribute.

The rule has one consequence worth stating: **an attribute never takes an identifier string.** If a
declaration needs to name something else by id, it is relating two things and belongs in the builder, where
the reference can be a lambda instead.

### 2.2 The vocabulary

| Attribute | Arity | Derives | Notes |
|---|---|---|---|
| `[Identity]` | exactly 1 per entity | the item key | Must be `MediaItemId`. Analyzer-enforced. |
| `[Title]` | exactly 1 per entity | `FieldSemantics.Title`, and the level's display title | Analyzer-enforced. Drives the host's title transforms (clean, article-moved, first-character). |
| `[Searchable]` | 0..n | `FieldSemantics.Searchable` | |
| `[Sortable]` | 0..n | `FieldSemantics.Sortable` + a derived `SortOption` | |
| `[Filterable]` | 0..n | `FieldSemantics.Filterable` + a derived `FilterOption` | Operators derive from the CLR type (§4.4). |
| `[Groupable]` | 0..n | `FieldSemantics.Groupable` + a derived facet `BrowseAxis` | |
| `[Disambiguation]` | 0..n | `FieldSemantics.Disambiguation` | |
| `[Status]` | 0..1 | `FieldSemantics.Status` + one `StateDescriptor` per enum member | Property must be an enum. |
| `[Timestamp]` | 0..n | `FieldSemantics.Timestamp` + a sequence `BrowseAxis` | |
| `[Artwork]` | 0..n | `FieldSemantics.Artwork` | |
| `[Size]` | 0..n | `FieldSemantics.Size`, `FieldValueKind.ByteSize` | A bare `long` is otherwise `Integer`. |
| `[Progress]` | 0..n | `FieldSemantics.Progress` | Unused by Movies; kept because the semantic exists. |
| `[Count]` | 0..n | `FieldValueKind.Count` | Distinguishes "how many of a thing" from a plain integer. |
| `[Multiline]` | 0..n | `FieldValueKind.MultilineText` | |
| `[Editable]` | 0..n | `FieldDescriptor.Editable` | |
| `[Derived]` | 0..n | not user-editable, not cataloger-supplied, host-recomputed | §2.3. |
| `[Prominence(p)]` | 0..n | `FieldDescriptor.Prominence` | Default `Detail`; only `Primary`/`Secondary`/`Diagnostic` are written. |
| `[Unit(s)]` | 0..n | `FieldDescriptor.Unit` | Rarely needed — `TimeSpan` and `Certification` carry their unit in the type. |
| `[Display(...)]` | 0..n | `Name`, `Description`, and a naming token's example | The only source of prose (**C7**). |
| `[Ignore]` | 0..n | nothing; the property is not a field | For a helper an entity happens to expose. |

`FieldSemantics.Identity` and `.SortKey` derive rather than being written: `Identity` from `[Identity]` plus
any `ExternalIdSet` property, `SortKey` from the host's derived sort title. `[SortKey]` is therefore **not**
in the vocabulary, though Part 6's sketch showed it — a kind that authored its own sort key would recreate
the Movies/Tv `LeadingArticles` divergence P2-7 measured.

### 2.3 `[Derived]` and the LINQ constraint

The exhibit found this and it is the most portable of its findings.

`ReleaseDate` is *"the earliest home release, else the cinema date"* and `Status` is *"how far through the
release sequence"*. Both are computed. Both are also `[Sortable]` and `[Filterable]` — the library is sorted
by release date and filtered by availability. **A C# computed property is invisible to LINQ translation**, so
either would force client-side evaluation over the whole library.

So a derived field is a *stored* property the host recomputes on write, and `[Derived]` declares exactly
that: not editable by a user, not supplied by a cataloger, recomputed whenever an input changes. The
recomputation is a static method on the media type, bound in the builder.

This is the first place the typed media model and the EF Core persistence decision constrain each other, and
it recurs for every derived-and-queryable field on every kind. **[door]** the recomputation binding
(`b.Derives(m => m.Status, Movies.StatusOf)`) is deliberately one-input-one-output; a kind needing a
multi-field recomputation pass can have one later without changing the attribute.

---

## 3. `IMediaTypeBuilder<TItem>`

Only what Movies needs is specified. Where TV or Music will clearly want more, it is one **[door]** line.

```csharp
[Experimental(ExperimentalContracts.Media, UrlFormat = ExperimentalContracts.UrlFormat)]
public interface IMediaTypeBuilder<TItem>
    where TItem : IMediaItem
{
    /// <summary>Names the kind for one item and for several.</summary>
    IMediaTypeBuilder<TItem> Named(string singular, string plural);

    /// <summary>Declares how files bind to items.</summary>
    IFileBindingBuilder<TItem> Files { get; }

    /// <summary>Declares a format family and its ladder.</summary>
    IFormatFamilyBuilder<TItem> Format(string familyId, string name);

    /// <summary>Declares the external-identity roles the kind requires and admits.</summary>
    IIdentityBuilder<TItem> Identity(Expression<Func<TItem, ExternalIdSet>> property);

    /// <summary>Declares a cross-cutting group.</summary>
    IGroupBuilder<TItem, TGroup> Group<TGroup>(Expression<Func<TItem, TGroup?>> property)
        where TGroup : class, IMediaGroup<TItem>;

    /// <summary>Declares a selection policy over an ordered enum property.</summary>
    IOrderedSelectionBuilder<TItem, TEnum> Selection<TEnum>(Expression<Func<TItem, TEnum>> property)
        where TEnum : struct, Enum;

    /// <summary>Declares a selection policy that has no backing property.</summary>
    IThresholdSelectionBuilder Selection(string facetId, string name);

    /// <summary>Declares a way the kind can be searched for.</summary>
    ISearchBuilder<TItem> Search(string searchKindId, string name);

    /// <summary>Declares how parsed readings resolve to items.</summary>
    IMatchBuilder<TItem> Matching { get; }

    /// <summary>Declares the query tiers and alias spellings.</summary>
    IQueryBuilder<TItem> Querying { get; }

    /// <summary>Declares the default templates, the spine and the template rules.</summary>
    INamingBuilder<TItem> Naming { get; }

    /// <summary>Declares how an item is summarized.</summary>
    ISummaryBuilder<TItem> Summary { get; }

    /// <summary>Declares the parts of the intent surface derivation cannot produce.</summary>
    IIntentBuilder<TItem> Intent { get; }

    /// <summary>Declares the kind's actions.</summary>
    IActionBuilder<TItem> Actions { get; }

    /// <summary>Declares a working surface over a typed row.</summary>
    IWorkbenchBuilder<TItem, TRow> Workbench<TRow>(string workbenchId, string name);

    /// <summary>Declares quality evaluation beyond the ladder.</summary>
    IQualityBuilder<TItem> Quality { get; }

    /// <summary>Binds the recomputation of a <c>[Derived]</c> property.</summary>
    IMediaTypeBuilder<TItem> Derives<TValue>(
        Expression<Func<TItem, TValue>> property,
        Func<TItem, TValue> recompute);

    /// <summary>Carries the release models, which stay regex and stay data.</summary>
    IParsingBuilder<TItem> Parsing(ParseDeclaration parsing);

    /// <summary>Carries the catalog mapping, until catalogers become plugins of their own.</summary>
    IMediaTypeBuilder<TItem> Catalog(CatalogDeclaration catalog);

    /// <summary>Carries the parity corpus.</summary>
    IMediaTypeBuilder<TItem> Corpus(IReadOnlyList<CorpusCase> cases);
}
```

The sub-builders Movies exercises, in the shapes the exhibit used:

```csharp
public interface IFileBindingBuilder<TItem> where TItem : IMediaItem
{
    /// <summary>The degenerate 1:1 corner: the item is its own acquisition unit and its own file bearer.</summary>
    IMediaTypeBuilder<TItem> OnePerItem();
}
// [door] TV and Music need Anchor<TAnchor>().Unit<TUnit>() with span constraints and a meaningful
// ordinal. OnePerItem() is sugar over that general form; it is the only form built this iteration.

public interface IFormatFamilyBuilder<TItem> where TItem : IMediaItem
{
    IFormatFamilyBuilder<TItem> Extensions(params string[] extensions);
    IFormatFamilyBuilder<TItem> Ladder(IReadOnlyList<QualityTier> tiers, QualityTier unknown);
    IFormatFamilyBuilder<TItem> Facet(
        string facetId, string name, TechnicalFacetCase titleCase,
        IReadOnlyList<string>? exceptions = null, bool ordinalSuffixesLowerCase = false);
    IFormatFamilyBuilder<TItem> SupportsEmbeddedMetadata();
    IFormatFamilyBuilder<TItem> CoexistsWithOtherFamilies();
}

public interface IIdentityBuilder<TItem> where TItem : IMediaItem
{
    /// <summary>The kind cannot function without an identifier in this role.</summary>
    IIdentityBuilder<TItem> Requires(IdentifierRole role);

    /// <summary>The kind will carry an identifier in this role when a cataloger supplies one.</summary>
    IIdentityBuilder<TItem> Admits(IdentifierRole role);

    /// <summary>The assigning catalog may retire an identifier and redirect it.</summary>
    IIdentityBuilder<TItem> SupportsRedirects();
}

public interface IGroupBuilder<TItem, TGroup>
    where TItem : IMediaItem where TGroup : class, IMediaGroup<TItem>
{
    IGroupBuilder<TItem, TGroup> Named(string singular, string plural);
    IGroupBuilder<TItem, TGroup> Monitorable();
    IGroupBuilder<TItem, TGroup> DiscoverySource();
    IGroupBuilder<TItem, TGroup> Independent();      // default is RefCounted
}
// [door] Books' many-to-many series with a labelled position ("2.5", "1-3") and a designated primary
// member needs Group<TGroup>(Expression<Func<TItem, IReadOnlyList<TGroup>>>) plus a link type carrying
// the position. The single-valued overload is the only one built; arity, position and primary-member all
// DERIVE from the property being a single reference.

public interface IOrderedSelectionBuilder<TItem, TEnum>
    where TItem : IMediaItem where TEnum : struct, Enum
{
    IOrderedSelectionBuilder<TItem, TEnum> Named(string name);
    IOrderedSelectionBuilder<TItem, TEnum> AtLeast(TEnum defaultFloor);
    IOrderedSelectionBuilder<TItem, TEnum> Offering(params TEnum[] choices);
}
```

`Matching`, `Querying`, `Naming`, `Summary`, `Intent`, `Actions` and `Workbench<TRow>` are given in the
exhibit at call-site fidelity; their interface declarations are mechanical from those calls and are left to
the implementing work package rather than transcribed here.

**The one thing to hold the builder to:** every reference into the item is an `Expression<Func<TItem, …>>`,
never a string. That is what turns P2-2's field-id grammar (`"{title}|{originalTitle}"`,
`"fields.collection"`, `"tags.SourceGroup"` on the naming side) into something the compiler checks and a
rename refactors. Where the exhibit could not do it — `b.Selection("availabilityDelay", …)` — it is recorded
as **C2**.

---

## 4. The derivation

Input: the item type, its attributes, the group types, and the builder's recorded calls.
Output: the objects the engines, the binder, the intent surface and the client already consume.

### 4.1 `MediaShape`

| Member | Derived from |
|---|---|
| `Kind` | `IMediaType<TItem>.Kind` |
| `Name` / `PluralName` | `Named(…)`; falls back to `typeof(TItem).Name` and a pluralizer |
| `Levels` | exactly one, from `TItem` (§4.2). **[door]** a hierarchy becomes one level per item type in a declared containment chain |
| `FileBinding` | `Files.OnePerItem()` → anchor = unit = the level, both uniqueness flags true, ordinal meaningless, no span constraints |
| `CoordinateSpaces` | one `CoordinateKind.Singleton`, canonical and dense, when no coordinate is declared. **Zero authoring for Movies.** |
| `GroupingAxes` | one per `Group<TGroup>(…)` call (§4.5) |
| `FormatFamilies` | one per `Format(…)` call |
| `SelectionFacets` | one per `Selection(…)` call (§4.6) |
| `SearchKinds` | one per `Search(…)` call |
| `Tokens` | derived (§4.7) |

### 4.2 `MediaLevel`

| Member | Derived from |
|---|---|
| `Id` | `typeof(TItem).Name` camel-cased → `"movie"` |
| `Name` / `PluralName` | `Named(…)` |
| `Parent` | `null` — one level. **[door]** a declared containment chain |
| `Roles` | with one level and `OnePerItem()`: `LibraryEntry \| AcquisitionUnit \| CompletenessUnit \| FileBearing`. **Zero authoring.** `VariantAxis` only when a variant is declared |
| `Identity` | §4.3 |
| `CoordinateSpaceIds` | the singleton space |
| `SequenceAxes` | `[]` |
| `Fields` | one `FieldDescriptor` per non-`[Ignore]` public property (§4.4) |
| `MonitorDimensions` | **host default.** Every surveyed kind has exactly one toggle called "wanted"; P2-7's class. Overridable in the builder, not overridden by Movies |
| `FormatFamilyIds` | the declared families |
| `Variant` | `null` |

### 4.3 `LevelIdentity` — reshaped, not carried

This is a descriptor member that existed to serve the string surface, so per the brief it is reshaped rather
than derived into.

* `HasCatalogRecord` / `HasLibraryRecord` — **deleted.** Both are `true` for every surveyed kind, and both
  are structurally true of anything implementing `IMediaItem` in a host that keeps the catalog/library
  split. Asking an author to restate them was P2-8.
* `SupportsIdentifierRedirects` — kept, from `Identity(…).SupportsRedirects()`, and **recorded as
  misplaced**: "TMDb merges duplicate entries and redirects" is a fact about a catalog. It moves to the
  cataloger's declaration at the cataloger milestone.
* `ExternalIds` — **reshaped from schemes to roles.** The kind declares
  `Requires(IdentifierRole.PrimaryWork)` and `Admits(IdentifierRole.SecondaryWork)`; the *host* composes
  the concrete `ExternalIdScheme` list from the installed catalogers at registration and refreshes it when
  the installed set changes.

```csharp
public sealed record LevelIdentity
{
    /// <summary>Gets whether the assigning catalog may retire an identifier and redirect it.</summary>
    public bool SupportsIdentifierRedirects { get; init; }

    /// <summary>Gets the identifier roles this level requires to function.</summary>
    public required IReadOnlyList<IdentifierRole> RequiredRoles { get; init; }

    /// <summary>Gets the identifier roles this level will carry when a cataloger supplies one.</summary>
    public IReadOnlyList<IdentifierRole> AdmittedRoles { get; init; } = [];

    /// <summary>
    /// Gets the concrete schemes bound to those roles by the installed catalogers. Host-composed; empty
    /// until a cataloger for this kind is installed.
    /// </summary>
    public IReadOnlyList<BoundIdentifierScheme> ExternalIds { get; init; } = [];
}

/// <summary>An abstract identifier role a media kind can declare without naming a catalog.</summary>
public enum IdentifierRole
{
    /// <summary>The catalog the kind's own records are keyed by.</summary>
    PrimaryWork = 0,

    /// <summary>A second catalog the same work is known to.</summary>
    SecondaryWork = 1,

    /// <summary>The catalog a group on a declared axis is keyed by.</summary>
    PrimaryGroup = 2,
}

/// <summary>A concrete scheme an installed cataloger bound to a role.</summary>
public sealed record BoundIdentifierScheme(IdentifierRole Role, string Scheme, string Name, PluginId Cataloger);
```

**Three consequences, all wanted, all already predicted by Part 5 §P2-1:**

1. `IdentifierOrder = [imdb, tmdb]` — provider-reliability knowledge — leaves the plugin and becomes host
   configuration over installed catalogers.
2. `RequiredFields = ["tmdbId"]` on the identifier query tier becomes "requires the primary identifier", so
   identifier search works with *any* cataloger installed rather than only with TMDb.
3. `{Movie TmdbId}` in the folder template becomes `{Movie Id}`, which renders the primary identifier as
   `scheme-value`. A TMDb-catalogued library still writes `{tmdb-335984}`; a differently-catalogued one
   writes its own stamp.

**Risk to state:** a kind with no cataloger installed has an empty `ExternalIds` and therefore no identifier
search and no identity stamp in its folder name. That is honest — it is what "no cataloger" means — but it
must produce a health-check warning, not a silent degradation.

### 4.4 `FieldDescriptor` — from a property

| Derived member | Rule |
|---|---|
| `FieldId` | camel-cased property name |
| `Name` | `[Display(Name)]`, else the property name split on case |
| `Description` | `[Display(Description)]`, else `null` (**C7**) |
| `Multivalued` | the property type is `IReadOnlyList<T>`, `ArtworkSet` or `ExternalIdSet` |
| `Editable` | `[Editable]` and not `[Derived]` |
| `Prominence` | `[Prominence]`, else `Detail` |
| `Unit` | `[Unit]`, else `null` |
| `Semantics` | the union of the semantic attributes; plus `Identity` for `[Identity]` and `ExternalIdSet` |
| `Choices` | for an enum, one `FacetValue` per member (`Value` = camel-cased name, `Name` = split name or `[Display]`) |
| `Components` | for a record or class that is not an entity: one nested `FieldDescriptor` per property, **one level deep** |

`ValueKind`, from the CLR type (after unwrapping `Nullable<>` and `IReadOnlyList<>`):

| CLR type | `FieldValueKind` |
|---|---|
| `string` | `Text`, or `MultilineText` with `[Multiline]` |
| `int` `long` | `Integer`; `ByteSize` with `[Size]`; `Count` with `[Count]` |
| `double` `decimal` | `Decimal`; `Ratio` with `[Ratio]` |
| `bool` | `Boolean` |
| `DateOnly` | `Date` |
| `DateTimeOffset` | `Instant` |
| `TimeSpan` | `Duration` |
| `Uri` | `Link` |
| `Language` | `Language` |
| `QualityTier` | `Quality` |
| `OrdinalPath` | `Ordinal` |
| `ArtworkSet` | `Artwork` (multivalued) |
| `ExternalIdSet` | `ExternalIdentifier` (multivalued) |
| an `enum` | `Enumerated` |
| an `IMediaItem` or `IMediaGroup<>` | `Reference` |
| any other record/class | `Composite`, with `Components` |
| a path type | `FilePath` |

**`FieldValueKind.Composite` and `FieldDescriptor.Components` already exist on the contract and Movies never
used them.** They were added for exactly this — the type's own remarks say *"a repeated tuple … is one
multivalued composite field, never several parallel lists correlated by index"* — and the string surface had
no way to produce one, so Movies split translations into three correlated lists and wrote the loss into its
remarks. Derivation produces them for free from `Rating`, `AlternateTitle` and `Overview`. **No contract
change; a defect closed by using what was already there.**

`FilterOperators`, derived (this is what deletes 22 hand-written `FilterOption` rows):

| Property type | Operators |
|---|---|
| `string` | `Contains \| Equals` |
| numeric | `Equals \| In \| GreaterThan \| LessThan \| Between` |
| date / instant / duration | `GreaterThan \| LessThan \| Between` |
| `enum` | `Equals \| NotEquals \| In` |
| `IReadOnlyList<T>` | `In \| Contains` |
| any nullable | the above, plus `IsNull` |

Checked row by row against `MoviesIntent.BuildFilters()`: **all 22 follow the rule exactly.**

### 4.5 `GroupingAxis`

| Member | Derived from |
|---|---|
| `AxisId` | camel-cased `typeof(TGroup).Name` |
| `Name` / `PluralName` | `Named(…)` on the group builder |
| `MemberLevelId` | the item level |
| `Arity` | `ManyToOne` — the property is a single reference. **[door]** a list property derives `ManyToMany` |
| `Position` | `None` — no link type declared |
| `HasPrimaryMember` | `false` — meaningless at `ManyToOne`, so not authorable |
| `IsMonitorable` / `IsDiscoverySource` / `Lifetime` / `HasOwnMetadata` | the four builder calls; `HasOwnMetadata` is `true` whenever `TGroup` has fields, which is always |
| `Fields` | `TGroup`'s properties, by §4.4 — **the defect closed** |
| `ExternalIds` | host-composed from `TGroup`'s `ExternalIdSet` and the `PrimaryGroup` role |

### 4.6 `SelectionFacet`

From `Selection(m => m.Status)`:

* `FacetId` — the property name; `AppliesToLevelId` — the item level; `Name` — `Named(…)`
* `Kind = Enumerated`, **`ValuesAreOrdered = true`** — from the source being an enum. This member already
  exists on `SelectionFacet` and `MoviesShape` never set it; the ordering that "cannot be read off the
  declaration" was in fact declarable and was not declared.
* `Values` — `Offering(…)`, in enum order; `DefaultAllowed` — `AtLeast(…)`
* `Application` — **needs a new member.** See below.

From `Selection("availabilityDelay", …)`: `Kind = Threshold`, `ThresholdDirection`, `DefaultNumber`, `Unit`.
Declared by id and name because there is no property (**C2**).

`FacetApplication` grows one member — a descriptor reshape the brief authorizes and Movies has needed since
pass 1:

```csharp
public enum FacetApplication
{
    /// <summary>Items outside the selection are not created at all.</summary>
    Materialization = 0,

    /// <summary>Items outside the selection exist and are hidden.</summary>
    Visibility = 1,

    /// <summary>
    /// Items outside the selection exist, are visible, and are refused acquisition. The movie case: an
    /// unavailable film's row exists, the user sees it, and only a grab is refused.
    /// </summary>
    Acquisition = 2,
}
```

`MoviesShape.cs` lines 884–886 recorded picking "the less destructive of the two wrong answers". This is the
right one.

### 4.7 `MediaShape.Tokens` — derived, and a hard prerequisite falls out

The naming design's derivation rules D1, D9, D12 and D15 are implemented directly against the typed model,
so the 140-line `BuildTokens()` and the 120-line `MoviesTokens` class both go to zero:

| Rule | Source | Movies tokens produced |
|---|---|---|
| D1 | nameable `[Title]`/level fields | `{Movie Title}`, `{Movie OriginalTitle}`, `{Movie Certification}`, `{Movie Year}` |
| D1+transform | host title transforms over any `[Title]` | `{Movie CleanTitle}`, `{Movie TitleThe}`, `{Movie CleanTitleThe}`, `{Movie TitleFirstCharacter}`, `{Movie CleanOriginalTitle}` |
| D9 | the group's `[Title]` | `{Collection Title}`, `{Collection TitleThe}`, `{Collection CleanTitleThe}` |
| D12 | `LevelIdentity` roles | `{Movie Id}` — the primary identifier as `scheme-value` |
| D15 | `FormatFamily.TechnicalFacets` | `{Edition Tags}` |

`MoviesShape.cs` already partitioned its own token list and named the consequence: 24 of the 39 are
`HostGlobalTokenNames`, *"declared here only because no host-global token registry exists yet; the moment one
does, this list is what gets deleted from the shape"*, and `FolderIllegalTokenNames` is a host rule the token
contract has no slot for.

> **This is a prerequisite, not a cleanup.** Template validation checks that every token in a user's template
> is declared. If tokens are derived and the host registry does not yet exist, the derived set is missing all
> 24 host tokens and the default file template `"{Movie Title} ({Movie Year}) {Quality Full}"` fails
> validation on the first load. **WP-6 must land before WP-9.**

### 4.8 The intent surface

| `PluginIntentSurface` member | Derived | Written |
|---|---|---|
| `BrowseAxes` | one flat default; one facet axis per `[Groupable]`; one grouping axis per `Group<>`; one sequence axis per `[Timestamp]` | the default axis's name; suppressions |
| `Sorts` | one per `[Sortable]`; direction descending for numbers/dates, ascending for text | direction overrides |
| `Filters` | one per `[Filterable]`, operators by §4.4 | suppressions |
| `States` | one per member of the `[Status]` enum | tone per member |
| `Actions` | — | all of them; the host executes them and the plugin has no code path in |
| `ExternalSurfaces` | — | **none.** All six of Movies' were vendor URL grammar (P2-1 §5); a catalog surface belongs to whoever owns the identifier |
| `Workbenches` | columns from the row type | subject, inputs, commit |

Checked against `MoviesIntent.cs`: 11 browse axes, 17 sorts, 22 filters and 5 states — **55 declarations,
every one produced by the rules above.** Movies writes three exceptions.

### 4.9 What derivation cannot produce

| | What | Consequence |
|---|---|---|
| **C7** | `FieldDescriptor.Description`, `NamingToken.ExampleValue` | `[Display]` carries them. A source generator reading XML doc comments would remove the attribute — noted, not built |
| **C8** | "exactly one `[Title]`", "`[Identity]` must be `MediaItemId`", "`[Status]` must be an enum" | an analyzer, and it must ship *before* the first typed kind or the failure moves from compile time back to load time |
| **C12** | a sort or filter over a *multivalued composite* — "sort by rating" needs to know which source and which voice | new gap, opened by this exhibit. The old shape sidestepped it with four vendor-named scalar sorts, which is the coupling P2-1 removes. Needs a sort key naming a projection, or a `[Primary]` element convention |
| **C1** | a per-file, kind-owned property (`{Edition Tags}`) | rides as an untyped `TechnicalFacet` row, unchanged |
| **C2** | a selection axis with no backing property | declared by id and name |
| **C3** | `FacetApplication` had no "gates acquisition" | **fixed** — new enum member (§4.6) |
| **C4** | `WorkbenchSubject` has no member for a catalog candidate | needs `WorkbenchSubject.CatalogCandidates` — a one-member change |
| **C5** | a per-row option source | unchanged by typing; needs a resolve-options seam on the workbench broker |
| **C6** | release-title patterns | P2-5 survives Part 6 explicitly; regex stays |
| **C9** | the JSONPath response map | stays until the cataloger is its own plugin, at which point the mapping is ordinary C# |
| **C10** | certification-region and image-role derivations | same |
| **C11** | newznab category knowledge on the search kinds | protocol coupling in a media kind; out of scope, recorded |

---

## 5. What of `MediaKindDefinition` survives

**It survives as the derivation's *output* type, and stops being an authoring surface.** That is what keeps
the blast radius small: the derivation produces one, and everything from `ValidatedDefinition` downwards —
`DefinitionValidationRules`, `MediaKindDefinitionBinder`, all six engines, `MediaKindRegistry`,
`MediaKindDescriptor` — is untouched.

The parse, match, query, naming and notification declarations are **not replaced by types this iteration**.
They are carried, but every *reference into the item* inside them becomes a property reference.

| Section | Survives | What improves now | What is deleted |
|---|---|---|---|
| `Shape` | as derived output | everything above | — |
| `Intent` | as derived output | §4.8 | `ExternalSurfaces` |
| `Parsing` | **verbatim** (P2-5) | — | `EscapeIds` (a code escape is a method now); `PreRewrites` (empty); `NormalizationOptions.LeadingArticles`, `.Transliterations`, `.QueryRewrites` (host-owned, P2-7) |
| `Matching` | carried | `KeyTemplate` strings → `Expression<Func<TItem,…>>`; `AgreementRule.Subject`/`AgreesWith` id strings → lambdas | `IdentifierOrder` (→ host config); `Units` (restates `OnePerItem`); `Variant` (null) |
| `Querying` | carried | `RequiredFields`/`FreeTextTemplate`/`AliasTemplate` id strings → lambdas; `RequiredFields = ["tmdbId"]` → an identifier *role* | `Limits` (host policy, P2-7); `Substitutions` (empty); `Grammar` (default) |
| `Quality` | carried | — | `CrossFamily` (unreachable with one family — P2-8's own confessed example) |
| `Naming` | carried | `TemplateSelectionRule.When` predicate atoms → a C# predicate; **D-4's disjunction becomes a predicate** | `MultiUnitStyles` (empty) |
| `Notifications` | carried | `HeadlineTemplate`/`Fields` templates → lambdas | `Occasions` (P2-7, host-owned); `DeepLinkTemplate` (**P2-9**); `ArtworkRoleOrder` (P2-7); `LinkTemplates` (vendor URLs); the four host-state summary rows |
| `Catalog` | carried, and leaving | field-id targets → property targets | — (moves wholesale at the cataloger milestone) |
| `Strategies` | **deleted** | D-2 dissolved: a strategy is a method | `StrategyBinding`, `StrategyRequirement`, the `HostVocabulary` strategy inventory and its validation rule |
| `RequiredVocabulary` | **deleted** | P2-4: `EnumOrdinalMaxima` was hand-maintained coupling a compiler should do. With a typed model there is no per-kind enum usage to declare | the whole record |
| `Corpus` | **verbatim** | — | — |

The renamed carrier is `MediaKindModel` (the `IMediaType.Model` member in §1.4) — the same record minus
`Shape`, `Intent`, `Strategies` and `RequiredVocabulary`, which have all moved or gone. `MediaKindDefinition`
becomes the host-internal name for `Shape + Intent + MediaKindModel` that the existing binder takes.

**P2-6 is explicitly out of scope and noted for the next iteration.** One event vocabulary means touching
`Providers/NotificationEvent`, `Wire/EventKind`, `EventEnvelope.State`/`.Message`, the notifier dispatch and
every notifier — none of which falls out of Movies going typed. P2-7 *is* done here, because every row it
names (`Occasions`, `OriginLimit`, `Transliterations`, `QueryRewrites`, `ArtworkRoleOrder`,
`LeadingArticles`) is deleted from Movies rather than edited in Tv/Music/Books.

### 5.1 What this costs — state it, do not bury it

`MoviesPluginModule`'s remarks turned "ships no executable media logic" into two architectural promises:

> *"the plugin assembly is eligible for unload once the definition is captured and validated"*
> *"the network and storage privileges are structurally unreachable — there is no code here that could hold either"*

**A typed media kind has code. Both promises are void.** The assembly must stay loaded because the types
*are* the model, and the plugin now has an instruction pointer that could, in principle, open a socket.

This is not an argument against the direction — Part 6 chose it knowing the trade — but it must be recorded
where the promises were made:

* Capability enforcement goes back to the manifest and the loader. It was already there; what is lost is the
  *belt* of "there is no code", leaving only the braces.
* Assembly unload is gone. Part 6 already noted the client consequence ("WASM has a single ALC and no
  unloading, so installing or removing a kind requires a client reload"); the same is now true server-side.
* Trimming becomes a build-time rule rather than an annotation, exactly as Part 6 predicted — and it is the
  likeliest thing to be discovered late.

---

## 6. Coexistence, and the deletion trigger

Both paths already meet at one place — `MediaKindDefinitionBinder.TryRegister` — and they keep meeting
there. Downstream of the binder there is no typed kind and no declared kind, only a `RegisteredMediaKind`.

**Registry.** One new method beside the existing one:

```csharp
/// <summary>
/// Registers a media kind from its typed model. Requires <see cref="Capability.MediaKind"/>.
/// </summary>
/// <typeparam name="TItem">The kind's item type.</typeparam>
/// <typeparam name="TType">The type declaring it.</typeparam>
/// <returns>This registry, for chaining.</returns>
IPluginRegistry AddMediaType<TItem, TType>()
    where TItem : IMediaItem
    where TType : IMediaType<TItem>;
```

**Flow.**

```
plugin: AddMediaType<Movie, Movies>()
   │
   ├─ PluginContext captures (typeof(Movie), typeof(Movies))  → TypedContribution
   │
host:  MediaTypeModelFactory.Build<Movie, Movies>()
   │      reflect Movie + attributes  ──►  MediaShape, PluginIntentSurface
   │      replay Movies.Configure(b)  ──►  MediaKindModel
   │      wrap                        ──►  MediaKindDefinition
   │
   └─► MediaKindDefinitionBinder.TryRegister(…)          ← UNCHANGED
          ValidatedDefinition.TryValidate                ← unchanged
          six engine factories                           ← unchanged
          MediaKindRegistry.TryRegister                  ← unchanged
```

The one addition below the binder is that `RegisteredMediaKind` gains an `IMediaType? TypedModel` property —
`null` for a declared kind — so the API and the client can serve the typed model where one exists.

**Two paths, one gate.** `ValidatedDefinition` keeps validating everything it validates today. Derivation
producing a structurally invalid definition is a *host* bug, and it must be refused by the same gate rather
than trusted — F-5 in Part 2 already showed what "the gate passed" is worth when a check is missing.

**Deletion trigger, recorded so it is not forgotten.** When the last kind converts — Books, on current
ordering — delete in one commit:

* `IPluginRegistry.AddMediaKind(MediaKindDefinition)`
* `DefinitionContribution` and its capture path in `PluginContext`
* `DefinitionCapabilityRules`' definition-section→capability mapping
* the `[Experimental(ExperimentalContracts.Definition)]` public surface of `MediaKindDefinition`, which
  becomes host-internal

Re-add triggers for the parked plugins are the inverse: `Arronix.Plugin.Tv` and `Arronix.Plugin.Tv.Tests`
return to `Arronix.sln` in the iteration that converts Tv, and so on. Each parked project keeps its last
green commit; nothing is deleted.

---

## 7. Work packages

Partitioned so no two implementers write the same file.

| # | Package | Files (all new unless marked) | Depends on | Notes |
|---|---|---|---|---|
| **WP-1** | Core interfaces | `Abstractions/Media/IMediaItem.cs`, `IMediaGroup.cs`, `IMediaType.cs`, `IMediaTypeOfT.cs`, `MediaKindModel.cs` | — | §1 verbatim |
| **WP-2** | Attribute vocabulary | `Abstractions/Media/Attributes/*.cs` (19 files, one per attribute) | — | §2.2 |
| **WP-3** | Item value types | `Abstractions/Media/ExternalIdSet.cs`, `ArtworkSet.cs`, `IdentifierRole.cs`, `BoundIdentifierScheme.cs` | — | Kind-agnostic only. `Rating`, `AlternateTitle`, `Certification` are **Movies'**, and live in WP-9 |
| **WP-4** | Builder interfaces | `Abstractions/Media/Builders/*.cs` | WP-1..3 | §3. Only what Movies needs |
| **WP-5** | Descriptor surgery | *edit* `Abstractions/Shape/LevelIdentity.cs`, `SelectionFacet.cs`, `Definition/MediaKindDefinition.cs`; *delete* `Definition/StrategyBinding.cs`, `StrategyRequirement.cs`, `RequiredVocabulary.cs`; *edit* `Definition/{Parse,Query,Naming,Notification,Quality}Declaration.cs`; *edit* `Intent/WorkbenchDescriptor.cs` | — | §4.3, §4.6, §5. **Breaks Tv/Music/Books — expected.** Also edits `Host/Media/HostVocabulary.cs` + `DefinitionValidationRules.cs` to drop the strategy/vocabulary rules |
| **WP-6** | Host token registry | `Host/Naming/HostTokenRegistry.cs`, `HostTitleTransforms.cs`; *edit* `Host/Engines/Naming/DeclarativeRenamePolicy.cs` | WP-5 | **Prerequisite for WP-9** (§4.7). Owns the 24 host-global tokens, the folder-illegality rule and the article/diacritic normalization P2-7 found divergent |
| **WP-7** | Shape derivation | `Host/Media/Typed/MediaTypeModelFactory.cs`, `ItemTypeReader.cs`, `FieldDescriptorFactory.cs`, `GroupAxisFactory.cs`, `TokenDerivation.cs` | WP-1..4, WP-6 | §4.1–4.7 |
| **WP-8** | Intent derivation | `Host/Media/Typed/IntentDerivation.cs` | WP-7 | §4.8. Separate file, separate implementer |
| **WP-9** | Builder implementation | `Host/Media/Typed/Builders/*.cs` | WP-4, WP-7 | The recording half of WP-4's interfaces |
| **WP-10** | Registry wiring | *edit* `Abstractions/Plugins/IPluginRegistry.cs`, `Plugins/Registration/PluginRegistry.cs`, `PluginContext.cs`, `PluginRegistrationLedger.cs`, `CapabilityMatrix.cs`; *edit* `Host/Media/RegisteredMediaKind.cs`, `Composition/MediaRegistration.cs`; new `Host/Media/Typed/TypedContribution.cs` | WP-7..9 | §6 |
| **WP-11** | Movies rewrite | *replace* `Plugin.Movies/MoviesShape.cs`, `MoviesIntent.cs`, `Definition/{MoviesDefinition,MoviesQuerying,MoviesMatching,MoviesNamingDeclaration,MoviesNotifications,MoviesQuality}.cs`; new `Movie.cs`, `Movies.cs`, `MovieCollection.cs`, `MovieValues.cs`, `WorkbenchRows.cs`; *edit* `MoviesPluginModule.cs`, `plugin.json` | WP-1..10 | The exhibit, made real. `Definition/MoviesParsing.cs`, `MoviesLadder.cs`, `MoviesCorpus.cs`, `MoviesCatalogDeclaration.cs` are **untouched** |
| **WP-12** | Movies tests | *edit* `Plugin.Movies.Tests/{Shape,Declaration,Naming,Planning,Matching}/*` and `Support/MoviesDeclaration.cs` | WP-11 | Re-point at the derived shape. `Parsing/*` and `Quality/*` should not need to change — evidence that the parse and ladder residue really is untouched |
| **WP-13** | Solution surgery | *edit* `Arronix.sln`; new `docs/parked-plugins.md` | WP-5 | **Verify agent, not an implementer.** Parks Tv, Music, Books and their test projects with per-project re-add triggers |
| **WP-14** | Analyzer | `Arronix.Analyzers/TypedMediaModelAnalyzer.cs` + diagnostics | WP-1..2 | **C8.** One `[Title]`, `[Identity]` is `MediaItemId`, `[Status]` is an enum, attribute legality by type, no `[Editable]` with `[Derived]`. Ships *before* WP-11 or the guarantees are load-time again |
| **WP-15** | Docs | *edit* `ARCHITECTURE.md`, `GLOSSARY.md`, `docs/design/declarative-media-kinds.md` (status → superseded), `docs/open-decisions.md` (Part 5 statuses) | WP-11 | Also records §5.1's void promises |

**Critical path:** WP-1/2/3 → WP-4 → WP-5 → WP-6 → WP-7 → WP-9 → WP-10 → WP-11 → WP-12.
WP-8, WP-13 and WP-14 are parallel. WP-6 is the one non-obvious ordering constraint and the one most likely
to be missed.

---

## 8. Open questions for the owner

1. **C12** — sorting and filtering over a multivalued composite. `Ratings` is the first case; TV's ratings
   and Music's per-track anything will all hit it. A `[Primary]` convention on a list element is cheap; a
   projection-valued sort key is general. Neither is built.
2. **§4.3** — a kind with no cataloger installed loses identifier search and its folder identity stamp.
   Health-check warning, or refuse to register the kind at all?
3. **§5.1** — the two void promises. Worth a line in `ARCHITECTURE.md` and in the threat model, since T-01
   reasoned partly from "a definition holds no instruction pointer".
4. **§1.3** — `IMediaType<TItem>` / `IMediaType` arity collision: keep Part 6's names, or rename the runtime
   one `IMediaTypeModel`?

---

# 9. What landed, and what did not (verified 2026-08-17)

Written after the fact and checked against the working tree, not against the plan above. Where the plan and
the tree disagree, the tree is what is recorded.

**State: `dotnet build Arronix.sln` 0 errors / 0 warnings; `dotnet test Arronix.sln` 2,480 passing,
336 skipped, 0 failing. `src/Sonarr.sln` builds. No project was removed from the solution.**

## 9.1 The parking clause never fired

The iteration plan assumed the typed surface would break `Arronix.Plugin.Tv`, `.Music` and `.Books`, and
provided for parking them out of `Arronix.sln` with re-add triggers. **It did not, and no project is
parked.** The reason is worth recording because it changes the shape of the remaining work:

> Tv, Music and Books were never on the string-declaration surface. They are **imperative** extensions —
> `AddMediaShape` plus one instance per seam — and `MediaKindDefinition` had exactly one producer in the
> whole repository, which was Movies, which had already converted.

So the superseded authoring surface had **zero production producers** at the moment it was deleted. What
looked like a migration was a dead-code removal. The three unconverted kinds keep compiling, keep passing
(Tv 84, Music 66, Books via the shared suites), and will convert one at a time against the imperative path
that still exists for them.

## 9.2 Landed

| Package | State | Notes |
|---|---|---|
| WP-1..4, WP-7..9 | **Landed previously** | Core seams, attributes, builders, derivation |
| **WP-10** registry wiring | **Landed here** | `IPluginRegistry.AddMediaType<TItem, TType>()`; `IMediaTypeRegistration` + `IMediaTypeBinder<T>` double-dispatch; `MediaTypeBinder`; `TypedContribution`; `PluginBootstrapper` admits typed kinds |
| **WP-5** descriptor surgery | **Landed here** | `MediaKindDefinition`, `StrategyBinding`, `StrategyRequirement`, `RequiredVocabulary`, `HostVocabulary` and `IPluginRegistry.AddMediaKind` deleted outright |
| **P2-6 / P2-7 / P2-9** | **Landed here** | `NotificationDeclaration.DeepLinkTemplate`, `.Occasions`, `.LinkTemplates`, `.ArtworkRoleOrder` and `GroupSummaryRule.ArtworkRoleOrder` deleted; `LinkTemplate` and `OccasionPhrase` deleted |
| Governance | **Landed here** | `TypedMediaKindGovernanceTests` — no vendor name and no route in a typed kind's derived structure, intent or engine inputs |

### The registration seam

`AddMediaType<TItem, TType>()` passes nothing: the two type arguments are the declaration. The pair crosses
the kind-blind loader as an `IMediaTypeRegistration` and the host reopens it through
`IMediaTypeBinder<TResult>` — double dispatch rather than reflecting a generic method back open, so the
fact stays a compile-time one and the trimmer can still see it.

### Where the capability check went

The bidirectional capability check is unchanged in strength and moved in location. A typed kind's demands
are only legible after its configuration call has been replayed, and that replay is host machinery — so
`Arronix.Plugins` reads them through `IMediaTypeCapabilityReader`, which the host implements by deriving the
model. A registry built **without** a reader refuses a typed kind outright rather than pricing it at the
media-kind capability alone: a check that silently narrows is worse than one that is absent.

`DefinitionCapabilityRules` now reads `MediaKindModel` instead of `MediaKindDefinition`. Every rule survived
the move; the shape section is absent from the table because structure is derived and carries no capability
of its own.

### Three gate refusals resolved

The derivation used to be refused by the host's own gate for three reasons. Each was resolved on its merits
rather than by fixture:

1. **`matching.units`** — the *derivation* moved. A kind with exactly one coordinate space has exactly one
   way to reach a unit, and the file binding already said one per entry, so `MediaTypeModelFactory` derives
   the rule. A kind with several spaces has a real choice, derives nothing, and is still refused until it
   declares one.
2. **`matching.confidence`** — the *gate* moved. How far to trust an identifier against a bare title is the
   same question for every media kind, so it is host policy: `MatchConfidencePolicy` owns the table and a
   kind that declares its own overrides it entire. This deleted a table every kind would have copied.
3. **external-identifier schemes** — the *gate* moved. The shape deliberately no longer enumerates schemes
   (which catalog issues which identifier is a fact about installed catalogers), so cross-checking a catalog
   response map against it could only ever refuse every typed kind. The check is removed with its successor
   named: it returns when a cataloger is its own plugin and both halves are in scope.

### Strategy bindings became derivation

`DeclarativeMatcher` no longer resolves strategies by name. Entry resolution had one implementation, so
choosing it by string was a choice with one option. Unit assignment is needed exactly when a release can
span more than one unit — which the match declaration's own unit rules already say — so it is derived from
`SpanExpansion`. This is what made `HostVocabulary`, `StrategyBinding` and `RequiredVocabulary` deletable.

## 9.3 Not landed, and why

| Item | State |
|---|---|
| **WP-6** host token registry | **Still the blocking prerequisite.** The 24 host-global tokens do not exist. The default file template still says `{Quality Full}`, which no registry resolves. Nothing regressed; nothing improved. |
| **WP-14** analyzer (C8) | **Not built.** "Exactly one `[Title]`", "`[Identity]` must be `MediaItemId`", "`[Status]` must be an enum" are still load-time failures rather than compile-time ones. Movies is correct by inspection, not by construction. |
| **C4** `WorkbenchSubject.CatalogCandidates` | Not added. `Arronix.Client/Rendering/IntentResolver.cs` switches exhaustively on the enum. |
| **C1** per-file kind-owned property | `Facet("edition", …)` is still an identifier-keyed row — the only untyped declaration left on the structural surface. Deferred to the iteration TV forces its shape. |
| **C2** non-property selection axis | `Selection("availabilityDelay", …)` is still declared by id and name. |
| **C12/C13** sort over a multivalued composite | Open. Owner question. |
| **P2-5** regexes | Out of scope by direction, and confirmed still true. |
| Tv / Music / Books conversion | Out of scope by direction. All three still compile and pass on the imperative path. |

## 9.4 Remnants examined and deliberately kept

The pollution rule — *nothing exists solely to serve the superseded surface* — was checked mechanically by
walking every public type under `Abstractions/Definition` and counting references outside its own file.
Four members had **zero consumers anywhere** and were deleted (§9.2). Four more are populated by nothing on
the typed path but **do** have live host consumers, so they stay, each for a stated reason:

| Member | Consumer | Why it stays |
|---|---|---|
| `EntryResolution.IdentifierOrder` | `DeclarativeMatcher`, the gate | P2-1's item 3. Deleting it removes the capability with nothing to replace it; the replacement is host configuration over installed catalogers, which is the cataloger milestone. |
| `QueryDeclaration.Limits` | `DeclarativeQueryPlanner` | Host search policy the derivation leaves empty, but a live engine input a later kind may need. |
| `QueryDeclaration.Substitutions` | `QueryTemplateRenderer` | Same. |
| `NamingDeclaration.MultiUnitStyles` | `MultiUnitStyleRenderer`, the gate | TV needs it; Movies has one unit per entry so derives none. |

**One naming point not addressed:** `ValidatedDefinition`, `DefinitionValidationRules`,
`DefinitionEngineCatalog` and `DefinitionCapabilityRules` keep "Definition" in their names although the
thing they validate is now a derived model rather than a written declaration. The names are stale; renaming
them is churn across eight files and was not done.

## 9.5 The two governance rules

`Arronix.Architecture.Tests/Capabilities/TypedMediaKindGovernanceTests.cs`. Both read the **derived
artifact** — the shape, the intent surface and the engine inputs the host actually builds — rather than
source text, because a source rule can be satisfied by moving a string and a derivation rule cannot.

Both discover typed kinds by reflection, so a kind is governed the day it converts rather than the day
someone remembers to list it. Today that is Movies alone, and that is visible in the test-case count rather
than hidden in an exclusion list.

- **P2-1** — no catalog vendor named in structure, intent or engine inputs. Two exemptions, both "somebody
  else's wire format rather than the kind's own vocabulary": the catalog declaration, which speaks to the
  vendor it names; and release-title **token patterns**, because `tmdb-335984` appears in release names
  because a stranger typed it there. Everything else under parsing stays governed.
- **P2-9** — no route or address. Absolute URLs and rooted paths carrying a placeholder.

**The vendor rule found a real defect on its first run:** the derived `{Movie Id}` token published
`exampleValue = "tmdb-335984"`, in code whose own comment explains that a kind spelling a catalog's name is
the leak the token exists to avoid. Fixed to `catalog-335984` in `TokenDerivation` and in `plugin.json`.
