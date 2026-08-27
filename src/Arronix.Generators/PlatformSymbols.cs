using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;

namespace Arronix.Generators;

/// <summary>One type a generator recognizes a shape or an annotation by.</summary>
internal enum PlatformSymbol
{
    // Property annotations.
    Ignore,
    Identity,
    Title,
    Sortable,
    Filterable,
    Groupable,
    Searchable,
    Progress,
    Status,
    Timestamp,
    Size,
    Artwork,
    Disambiguation,
    Count,
    Ratio,
    Multiline,
    Editable,
    Derived,
    Display,
    Unit,
    Prominence,

    // Contracts and bases.
    MediaEntity,
    MediaEntityItem,
    MediaType,
    MediaItem,
    GroupDefinition,
    WorkbenchDefinition,

    // Platform values.
    ArtworkSet,
    ArtworkImage,
    ExternalIdSet,
    ExternalId,
    MediaItemId,
    PlatformPath,
    Language,
    QualityTier,
    OrdinalPath,

    // Framework shapes with no SpecialType.
    DateOnly,
    DateTimeOffset,
    TimeSpan,
    TimeOnly,
    Uri,
    Guid,
    Version,
    List,
    Dictionary,
    ReadOnlyDictionary,

    /// <summary>Not a symbol: how many there are, so a name missing from the tables leaves a gap.</summary>
    Total,
}

/// <summary>
/// The Arronix and framework types the generators reason about, resolved from the consumer compilation.
/// </summary>
/// <remarks>
/// Every Arronix symbol must come from the one assembly declaring <see cref="PlatformSymbol.MediaEntity"/>,
/// and every framework shape from the core library, so a package's own type spelled like either is an
/// ordinary type of its own. Nothing resolves unless everything does: a partly resolved set would answer
/// <see langword="false"/> to the questions it could not.
/// </remarks>
internal sealed class PlatformSymbols
{
    private const string Media = "Arronix.Abstractions.Media.";

    private const string MediaEntityName = Media + "IMediaEntity";

    private static readonly ConditionalWeakTable<Compilation, PlatformResolution> Resolved = new();

    private static readonly (PlatformSymbol Symbol, string MetadataName)[] Abstractions =
    [
        (PlatformSymbol.Ignore, Media + "IgnoreAttribute"),
        (PlatformSymbol.Identity, Media + "IdentityAttribute"),
        (PlatformSymbol.Title, Media + "TitleAttribute"),
        (PlatformSymbol.Sortable, Media + "SortableAttribute"),
        (PlatformSymbol.Filterable, Media + "FilterableAttribute"),
        (PlatformSymbol.Groupable, Media + "GroupableAttribute"),
        (PlatformSymbol.Searchable, Media + "SearchableAttribute"),
        (PlatformSymbol.Progress, Media + "ProgressAttribute"),
        (PlatformSymbol.Status, Media + "StatusAttribute"),
        (PlatformSymbol.Timestamp, Media + "TimestampAttribute"),
        (PlatformSymbol.Size, Media + "SizeAttribute"),
        (PlatformSymbol.Artwork, Media + "ArtworkAttribute"),
        (PlatformSymbol.Disambiguation, Media + "DisambiguationAttribute"),
        (PlatformSymbol.Count, Media + "CountAttribute"),
        (PlatformSymbol.Ratio, Media + "RatioAttribute"),
        (PlatformSymbol.Multiline, Media + "MultilineAttribute"),
        (PlatformSymbol.Editable, Media + "EditableAttribute"),
        (PlatformSymbol.Derived, Media + "DerivedAttribute"),
        (PlatformSymbol.Display, Media + "DisplayAttribute"),
        (PlatformSymbol.Unit, Media + "UnitAttribute"),
        (PlatformSymbol.Prominence, Media + "ProminenceAttribute"),
        (PlatformSymbol.MediaEntity, MediaEntityName),
        (PlatformSymbol.MediaEntityItem, Media + "IMediaItem"),
        (PlatformSymbol.MediaType, Media + "MediaType`4"),
        (PlatformSymbol.MediaItem, Media + "MediaItem`3"),
        (PlatformSymbol.GroupDefinition, Media + "GroupDefinition`2"),
        (PlatformSymbol.WorkbenchDefinition, Media + "WorkbenchDefinition`2"),
        (PlatformSymbol.ArtworkSet, Media + "ArtworkSet"),
        (PlatformSymbol.ArtworkImage, Media + "ArtworkImage"),
        (PlatformSymbol.ExternalIdSet, Media + "ExternalIdSet"),
        (PlatformSymbol.ExternalId, "Arronix.Abstractions.Shape.ExternalId"),
        (PlatformSymbol.MediaItemId, "Arronix.Abstractions.Identity.MediaItemId"),
        (PlatformSymbol.PlatformPath, "Arronix.Abstractions.FileSystem.PlatformPath"),
        (PlatformSymbol.Language, "Arronix.Abstractions.DTOs.Language"),
        (PlatformSymbol.QualityTier, "Arronix.Abstractions.DTOs.QualityTier"),
        (PlatformSymbol.OrdinalPath, "Arronix.Abstractions.Shape.OrdinalPath"),
    ];

    /// <summary>The framework shapes the language gives no <see cref="SpecialType"/>.</summary>
    private static readonly (PlatformSymbol Symbol, string MetadataName)[] Framework =
    [
        (PlatformSymbol.DateOnly, "System.DateOnly"),
        (PlatformSymbol.DateTimeOffset, "System.DateTimeOffset"),
        (PlatformSymbol.TimeSpan, "System.TimeSpan"),
        (PlatformSymbol.TimeOnly, "System.TimeOnly"),
        (PlatformSymbol.Uri, "System.Uri"),
        (PlatformSymbol.Guid, "System.Guid"),
        (PlatformSymbol.Version, "System.Version"),
        (PlatformSymbol.List, "System.Collections.Generic.List`1"),
        (PlatformSymbol.Dictionary, "System.Collections.Generic.IDictionary`2"),
        (PlatformSymbol.ReadOnlyDictionary, "System.Collections.Generic.IReadOnlyDictionary`2"),
    ];

    /// <summary>The bases that make a declaration the diagnostic's business, by metadata name.</summary>
    /// <remarks>Read from <see cref="Abstractions"/> so a rename cannot leave the two tables disagreeing.</remarks>
    private static readonly string[] AuthoringBaseNames = AuthoringBaseMetadataNames();

    private readonly INamedTypeSymbol?[] _symbols;

    private PlatformSymbols(INamedTypeSymbol?[] symbols) => _symbols = symbols;

    /// <summary>Resolves the types, or nothing when this compilation has no Arronix contract.</summary>
    /// <remarks>Cached per compilation: the same answer is asked for once per declaration examined.</remarks>
    internal static PlatformSymbols? Resolve(Compilation compilation) => Read(compilation).Symbols;

    /// <summary>Resolves the types and, when they do not resolve, why they did not.</summary>
    /// <remarks>Only the author-facing diagnostic needs the reason; generators just stay silent.</remarks>
    internal static PlatformResolution Read(Compilation compilation) =>
        Resolved.GetValue(compilation, static key => Create(key));

    internal INamedTypeSymbol Get(PlatformSymbol symbol) => _symbols[(int)symbol]!;

    /// <summary>Determines whether a type, or its constructed form, is exactly one of them.</summary>
    internal bool Is(ITypeSymbol? type, PlatformSymbol symbol) =>
        type is not null && SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, Get(symbol));

    /// <summary>Determines whether a type is, or implements, one of them.</summary>
    internal bool Implements(ITypeSymbol type, PlatformSymbol symbol)
    {
        if (Is(type, symbol))
        {
            return true;
        }

        foreach (var contract in type.AllInterfaces)
        {
            if (Is(contract, symbol))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Reads one annotation of a member.</summary>
    internal AttributeData? Attribute(ISymbol member, PlatformSymbol symbol)
    {
        foreach (var attribute in member.GetAttributes())
        {
            if (SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, Get(symbol)))
            {
                return attribute;
            }
        }

        return null;
    }

    /// <summary>Determines whether a member carries one annotation.</summary>
    internal bool Has(ISymbol member, PlatformSymbol symbol) => Attribute(member, symbol) is not null;

    /// <summary>Walks a type's bases for the one closed construction of one of them.</summary>
    internal INamedTypeSymbol? ClosedBase(INamedTypeSymbol type, PlatformSymbol symbol) =>
        CompilationSymbols.ClosedBase(type, Get(symbol));

    private static PlatformResolution Create(Compilation compilation)
    {
        var anchors = CompilationSymbols.ReferencedCandidates(compilation, MediaEntityName);

        if (anchors.Count == 0)
        {
            return PlatformResolution.Absent;
        }

        if (anchors.Count > 1)
        {
            return PlatformResolution.Incomplete(
                "this compilation references " + anchors.Count + " assemblies declaring '" + MediaEntityName
                + "' (" + Identities(anchors) + "), so none of them is the platform contract",
                AuthoringBases(compilation, anchors));
        }

        var contract = anchors[0].ContainingAssembly;
        var symbols = new INamedTypeSymbol?[(int)PlatformSymbol.Total];
        string? defect = null;

        foreach (var declared in Abstractions)
        {
            symbols[(int)declared.Symbol] = CompilationSymbols.DeclaredBy(
                compilation,
                declared.MetadataName,
                contract);

            defect ??= symbols[(int)declared.Symbol] is null
                ? Missing(compilation, declared.MetadataName, contract)
                : null;
        }

        var core = compilation.GetSpecialType(SpecialType.System_Object).ContainingAssembly;

        foreach (var declared in Framework)
        {
            // A reference set may split the framework across assemblies — System.Uri is not in the core
            // library at run time — so the one other referenced declaration is taken when it is not there.
            symbols[(int)declared.Symbol] =
                CompilationSymbols.DeclaredBy(compilation, declared.MetadataName, core)
                ?? CompilationSymbols.Referenced(compilation, declared.MetadataName);

            defect ??= symbols[(int)declared.Symbol] is null
                ? "the framework shape '" + declared.MetadataName + "' is not in this reference set"
                : null;
        }

        return defect is null
            ? PlatformResolution.Complete(new PlatformSymbols(symbols))
            : PlatformResolution.Incomplete(defect, AuthoringBases(compilation, anchors));
    }

    /// <summary>Says what happened to one contract type this reference set does not supply.</summary>
    /// <remarks>Declared elsewhere and declared nowhere are different defects with different remedies.</remarks>
    private static string Missing(Compilation compilation, string metadataName, IAssemblySymbol contract)
    {
        var elsewhere = CompilationSymbols.ReferencedCandidates(compilation, metadataName);

        return elsewhere.Count == 0
            ? "'" + metadataName + "' is not declared by the referenced contract "
                + contract.Identity.GetDisplayName()
            : "'" + metadataName + "' is declared by " + Identities(elsewhere)
                + " rather than by the referenced contract " + contract.Identity.GetDisplayName();
    }

    /// <summary>The bases a declaration must reach for a failed reading to be its problem.</summary>
    /// <remarks>
    /// Taken from every candidate contract, because the ambiguous case has no single one to take them
    /// from. Each entry is still compared by symbol identity.
    /// </remarks>
    private static ImmutableArray<INamedTypeSymbol> AuthoringBases(
        Compilation compilation,
        IReadOnlyList<INamedTypeSymbol> anchors)
    {
        var bases = ImmutableArray.CreateBuilder<INamedTypeSymbol>();

        foreach (var anchor in anchors)
        {
            foreach (var name in AuthoringBaseNames)
            {
                if (CompilationSymbols.DeclaredBy(compilation, name, anchor.ContainingAssembly) is { } declared)
                {
                    bases.Add(declared);
                }
            }
        }

        return bases.ToImmutable();
    }

    private static string[] AuthoringBaseMetadataNames()
    {
        var names = new List<string>();

        foreach (var declared in Abstractions)
        {
            if (declared.Symbol is PlatformSymbol.MediaType or PlatformSymbol.MediaItem)
            {
                names.Add(declared.MetadataName);
            }
        }

        return names.ToArray();
    }

    private static string Identities(IReadOnlyList<INamedTypeSymbol> declarations)
    {
        var names = new List<string>();

        foreach (var declaration in declarations)
        {
            names.Add(declaration.ContainingAssembly.Identity.GetDisplayName());
        }

        names.Sort(StringComparer.Ordinal);

        return string.Join(" and ", names);
    }
}

/// <summary>One compilation's reading of the platform types, and why an incomplete one failed.</summary>
/// <remarks>
/// Three answers, not two: no Arronix contract is never an author's problem, an incomplete or duplicated
/// one always is.
/// </remarks>
internal sealed class PlatformResolution
{
    private PlatformResolution(
        PlatformSymbols? symbols,
        string? defect,
        ImmutableArray<INamedTypeSymbol> authoringBases)
    {
        Symbols = symbols;
        Defect = defect;
        AuthoringBases = authoringBases;
    }

    /// <summary>Gets the answer for a compilation that references no Arronix contract at all.</summary>
    internal static PlatformResolution Absent { get; } =
        new(null, null, ImmutableArray<INamedTypeSymbol>.Empty);

    /// <summary>Gets the resolved types, or <see langword="null"/> when they did not all resolve.</summary>
    internal PlatformSymbols? Symbols { get; }

    /// <summary>
    /// Gets why the types did not resolve, or <see langword="null"/> when they did, or when there is no
    /// Arronix contract to resolve them from.
    /// </summary>
    internal string? Defect { get; }

    /// <summary>Gets the media bases a declaration must reach for an incomplete reading to concern it.</summary>
    internal ImmutableArray<INamedTypeSymbol> AuthoringBases { get; }

    /// <summary>Records a complete reading.</summary>
    /// <param name="symbols">The resolved types.</param>
    /// <returns>The reading.</returns>
    internal static PlatformResolution Complete(PlatformSymbols symbols) =>
        new(symbols, null, ImmutableArray<INamedTypeSymbol>.Empty);

    /// <summary>Records a reading that failed, and why.</summary>
    /// <param name="defect">The first thing the reference set does not supply.</param>
    /// <param name="authoringBases">The media bases found in the candidate contracts.</param>
    /// <returns>The reading.</returns>
    internal static PlatformResolution Incomplete(
        string defect,
        ImmutableArray<INamedTypeSymbol> authoringBases) =>
        new(null, defect, authoringBases);

    /// <summary>Determines whether a declaration derives from one of the platform's media bases.</summary>
    /// <param name="type">The declared type.</param>
    /// <returns><see langword="true"/> when an incomplete reading is this declaration's problem.</returns>
    internal bool Authors(INamedTypeSymbol type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            foreach (var declared in AuthoringBases)
            {
                if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, declared))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
