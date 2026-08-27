using System.ComponentModel;

namespace Arronix.Abstractions.Client;

/// <summary>How large a graph a client contract may describe.</summary>
/// <remarks>
/// One set of bounds for every walk over a shape a contract produced — its serialization graph, its
/// projection schema, the projected values it hands a consumer — so that walks cannot drift into
/// disagreeing about what is describable. Real shapes are orders of magnitude smaller; these are the point
/// past which a shape is refused rather than followed.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class ClientContractLimits
{
    /// <summary>The deepest nesting a contract may describe.</summary>
    public const int MaxDepth = 32;

    /// <summary>The most values a contract may describe, across the whole graph.</summary>
    public const int MaxNodes = 4096;

    /// <summary>The longest free text a projected value may carry.</summary>
    /// <remarks>An overview is prose; a field that carries a document is not one this platform renders.</remarks>
    public const int MaxTextLength = 65_536;

    /// <summary>The longest address a projected value may carry, including an inline image payload.</summary>
    public const int MaxAddressLength = 8_192;

    /// <summary>
    /// The longest identifier a contract may declare or a projected value may name — a field identifier, a
    /// unit, a stored choice, an artwork role, a language code.
    /// </summary>
    public const int MaxIdentifierLength = 256;

    /// <summary>The most bytes a serialized entity may be read as.</summary>
    /// <remarks>A payload is one item, not a catalog; a response past this is refused rather than buffered.</remarks>
    public const int MaxPayloadBytes = 4_194_304;

    /// <summary>The most text one rendering may carry: a contract's schema plus one projection's values.</summary>
    /// <remarks>
    /// Each value is bounded on its own; without a total, a graph of them multiplies out well past what a
    /// browser should hold from a payload that is itself capped. One total, not one per walk: a schema is
    /// read once when its contract is admitted and rendered again by every projection of it.
    /// </remarks>
    public const int MaxProjectionCharacters = 1_048_576;

    /// <summary>The largest pixel measurement an image may state.</summary>
    /// <remarks>A measurement exists so a consumer can choose without fetching; an absurd one is a defect.</remarks>
    public const int MaxImageEdge = 100_000;
}
