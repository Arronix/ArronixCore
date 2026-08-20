namespace Arronix.Host.Engines.Parsing;

/// <summary>
/// The metadata keys the declarative parse engine writes into
/// <see cref="Arronix.Abstractions.DTOs.ParsedRelease.AdditionalMetadata"/>, published so the quality
/// evaluator and downstream engines read the same spellings the parser wrote.
/// </summary>
internal static class DeclarativeParseFields
{
    /// <summary>The identifier of the title pattern that claimed the release, for corpus coverage.</summary>
    internal const string PatternId = "parse.patternId";

    /// <summary>The release-kind discriminator a pattern captured, when one did.</summary>
    internal const string ReleaseKind = "parse.releaseKind";

    /// <summary>Additional spellings of the title, joined with <c>", "</c>.</summary>
    internal const string AlternateTitles = "parse.alternateTitles";

    /// <summary>The first member of an expanded range, when the pattern declared one.</summary>
    internal const string RangeFrom = "parse.range.from";

    /// <summary>The last member of an expanded range, when the pattern declared one.</summary>
    internal const string RangeTo = "parse.range.to";

    /// <summary>Whether the range emits one span reading rather than one reading per member.</summary>
    internal const string RangeIsSpan = "parse.range.isSpan";

    /// <summary>Prefix for an external identifier a pattern captured; the scheme follows the dot.</summary>
    internal const string ExternalIdPrefix = "parse.externalId.";

    /// <summary>Prefix for a captured coordinate component: space identifier, dot, component identifier.</summary>
    internal const string CoordinatePrefix = "parse.coordinate.";

    /// <summary>Prefix for a tag written by a capture or a declared token-table row.</summary>
    internal const string TagPrefix = "parse.tag.";
}
