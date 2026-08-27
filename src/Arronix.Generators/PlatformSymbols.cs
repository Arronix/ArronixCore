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

    private static readonly ConditionalWeakTable<Compilation, Resolution> Resolved = new();

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

    private readonly INamedTypeSymbol?[] _symbols;

    private PlatformSymbols(INamedTypeSymbol?[] symbols) => _symbols = symbols;

    /// <summary>Resolves the types, or nothing when this compilation has no Arronix contract.</summary>
    /// <remarks>Cached per compilation: the same answer is asked for once per declaration examined.</remarks>
    internal static PlatformSymbols? Resolve(Compilation compilation) =>
        Resolved.GetValue(compilation, static key => new Resolution(Create(key))).Symbols;

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

    private static PlatformSymbols? Create(Compilation compilation)
    {
        if (CompilationSymbols.Referenced(compilation, MediaEntityName) is not { } anchor)
        {
            return null;
        }

        var symbols = new INamedTypeSymbol?[(int)PlatformSymbol.Total];
        var contract = anchor.ContainingAssembly;

        foreach (var declared in Abstractions)
        {
            symbols[(int)declared.Symbol] = CompilationSymbols.DeclaredBy(
                compilation,
                declared.MetadataName,
                contract);
        }

        var core = compilation.GetSpecialType(SpecialType.System_Object).ContainingAssembly;

        foreach (var declared in Framework)
        {
            // A reference set may split the framework across assemblies — System.Uri is not in the core
            // library at run time — so the one other referenced declaration is taken when it is not there.
            symbols[(int)declared.Symbol] =
                CompilationSymbols.DeclaredBy(compilation, declared.MetadataName, core)
                ?? CompilationSymbols.Referenced(compilation, declared.MetadataName);
        }

        foreach (var symbol in symbols)
        {
            if (symbol is null)
            {
                return null;
            }
        }

        return new PlatformSymbols(symbols);
    }

    /// <summary>One compilation's answer, including the answer that it has no Arronix contract.</summary>
    private sealed class Resolution
    {
        internal Resolution(PlatformSymbols? symbols) => Symbols = symbols;

        internal PlatformSymbols? Symbols { get; }
    }
}
