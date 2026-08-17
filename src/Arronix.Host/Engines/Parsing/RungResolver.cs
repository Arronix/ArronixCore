// Consumes the experimental definition contracts (ARX0019).
#pragma warning disable ARX0019

namespace Arronix.Host.Engines.Parsing;

/// <summary>
/// Executes a kind's declared rung-resolution decision table over one release's tag evidence.
/// </summary>
/// <remarks>
/// <para>
/// The algorithm is exactly the declared order: default rows first (what evidence is assumed when the
/// release stated none), then the rung rows — first row whose predicate holds wins — then the container
/// fallbacks, consulted only when every row was silent, then the declared unknown tier. This replaces
/// the branch cascade of Radarr's <c>QualityParser.ParseQualityName</c>
/// (<c>src/NzbDrone.Core/Parser/QualityParser.cs:112-667</c>): the branches become rows, the guard
/// probes become declared guards, and the engine keeps only the sequencing.
/// </para>
/// <para>
/// There is deliberately no rule-selection mode here: last-occurrence semantics belong to the token scan
/// that produced the tags, because the rightmost token in a release name varies per release while this
/// table is fixed at authoring time.
/// </para>
/// </remarks>
internal sealed class RungResolver
{
    private readonly CompiledParseDeclaration _declaration;

    internal RungResolver(CompiledParseDeclaration declaration) => _declaration = declaration;

    /// <summary>Resolves one release's evidence to a ladder rung.</summary>
    /// <param name="context">The release's predicate context. Default rows may adjust its effective
    /// source group and stated resolution; the adjustments are visible to the rung rows.</param>
    /// <param name="rawTitle">The raw title, for the container fallback's extension probe.</param>
    /// <returns>The resolved rung.</returns>
    internal RungOutcome Resolve(ParsePredicateContext context, string rawTitle)
    {
        ApplyDefaults(context);

        foreach (var rule in _declaration.RungResolution.Rules)
        {
            if (TagPredicateEvaluator.Holds(rule.When, context))
            {
                return new RungOutcome(
                    rule.TierId,
                    rule.CarryStatedResolution ? context.StatedResolution : 0);
            }
        }

        if (ResolveExtension(rawTitle) is { } tierId)
        {
            return new RungOutcome(tierId, 0);
        }

        return new RungOutcome(_declaration.RungResolution.UnknownTierId, 0);
    }

    private void ApplyDefaults(ParsePredicateContext context)
    {
        foreach (var row in _declaration.Defaults)
        {
            if (!TagPredicateEvaluator.Holds(row.When, context))
            {
                continue;
            }

            // The pre-release sources have no resolution axis at all; a stated one is ignored outright.
            if (row.IgnoreStatedResolution)
            {
                context.StatedResolution = 0;
            }

            // Assumptions fill absence only: a row never overrides what the release actually stated.
            if (row.Resolution is { } resolution && context.StatedResolution == 0)
            {
                context.StatedResolution = resolution;
            }

            if (row.SourceGroup is { } source && context.SourceGroup is null)
            {
                context.SourceGroup = source;
            }
        }
    }

    private string? ResolveExtension(string rawTitle)
    {
        var fallbacks = _declaration.RungResolution.ContainerFallbacks;

        if (fallbacks.Count == 0)
        {
            return null;
        }

        var match = ReleaseTokenVocabulary.TrailingExtension().Match(rawTitle);

        if (!match.Success)
        {
            return null;
        }

        var extension = match.Value;
        string? wildcard = null;

        foreach (var fallback in fallbacks)
        {
            if (string.Equals(fallback.Extension, extension, StringComparison.OrdinalIgnoreCase))
            {
                return fallback.TierId;
            }

            if (string.Equals(fallback.Extension, "*", StringComparison.Ordinal))
            {
                wildcard = fallback.TierId;
            }
        }

        // The wildcard row means "every other recognized media container", never "every dot-suffix":
        // an unrecognized suffix is somebody's release group and implies nothing.
        return wildcard is not null
            && ReleaseTokenVocabulary.RecognizedContainerExtensions.Contains(extension)
            ? wildcard
            : null;
    }
}

/// <summary>The outcome of rung resolution.</summary>
/// <param name="TierId">The resolved tier, by name.</param>
/// <param name="CarriedResolution">
/// The stated resolution the winning row carried onto the tier, or zero. Exists because a surveyed rung
/// keeps its identity while adopting whatever resolution the release stated.
/// </param>
internal readonly record struct RungOutcome(string TierId, int CarriedResolution);
