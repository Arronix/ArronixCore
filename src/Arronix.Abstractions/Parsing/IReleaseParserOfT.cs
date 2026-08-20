using Arronix.Abstractions.Media;
using Arronix.Abstractions.Providers;
using Arronix.Abstractions.Releases;
using Arronix.Abstractions.Shape;

namespace Arronix.Abstractions.Parsing;

/// <summary>The inputs available while interpreting one release, file, or folder name.</summary>
public sealed record ReleaseParseContext
{
    /// <summary>Gets the text to interpret.</summary>
    public required string Text { get; init; }

    /// <summary>Gets where the text came from.</summary>
    public required MatchSource Source { get; init; }

    /// <summary>Gets external identities recognized by the installed catalogers.</summary>
    public IReadOnlyList<ExternalIdReading> ExternalIds { get; init; } = [];
}

/// <summary>The result of interpreting text as one typed release.</summary>
/// <typeparam name="TRelease">The media type's release shape.</typeparam>
public sealed record ReleaseParseResult<TRelease>
    where TRelease : class, IRelease
{
    /// <summary>Gets the interpreted release, or null when the parser declined the text.</summary>
    public TRelease? Release { get; init; }

    /// <summary>Gets the catalog-owned identities observed in the text.</summary>
    public IReadOnlyList<ExternalIdReading> ExternalIds { get; init; } = [];

    /// <summary>Gets the explanation of the interpretation.</summary>
    public InterpretationTrace<TRelease> Trace { get; init; } = InterpretationTrace<TRelease>.Empty;

    /// <summary>Gets the rejection reason when the parser deliberately declined the text.</summary>
    public string? Rejection { get; init; }

    /// <summary>Creates an accepted result.</summary>
    public static ReleaseParseResult<TRelease> Accepted(
        TRelease release,
        IReadOnlyList<ExternalIdReading>? externalIds = null,
        InterpretationTrace<TRelease>? trace = null)
    {
        ArgumentNullException.ThrowIfNull(release);
        return new ReleaseParseResult<TRelease>
        {
            Release = release,
            ExternalIds = externalIds ?? [],
            Trace = trace ?? InterpretationTrace<TRelease>.Empty
        };
    }

    /// <summary>Creates a declined result.</summary>
    public static ReleaseParseResult<TRelease> Rejected(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new ReleaseParseResult<TRelease> { Rejection = reason };
    }
}

/// <summary>A statically dispatched parser for one media-owned release shape.</summary>
/// <typeparam name="TRelease">The exact typed release it produces.</typeparam>
/// <remarks>
/// A parser is executable behavior, not a declaration graph. The media type carries its parser as a type
/// argument and the host invokes this member through a generic constraint; no parser instance, builder, or
/// reflected rule model exists on the typed path.
/// </remarks>
public interface IReleaseParser<TRelease>
    where TRelease : class, IRelease
{
    /// <summary>Interprets one input as the media type's exact release shape.</summary>
    static abstract ReleaseParseResult<TRelease> Parse(ReleaseParseContext context);
}
