#pragma warning disable ARX0013 // Shape contracts are experimental; these tests exercise the declaration.
#pragma warning disable ARX0016 // Intent contracts are experimental; these tests exercise the declaration.
#pragma warning disable ARX0019 // Definition contracts are experimental; these tests exercise the declaration.
#pragma warning disable ARX0020 // Media contracts are experimental; these tests exercise the typed surface.

using System.Linq;
using Arronix.Abstractions.Definition;
using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Intent;
using Arronix.Abstractions.Media;
using Arronix.Abstractions.Shape;
using Arronix.Host.Media.Typed;

namespace Arronix.Plugin.Movies.Tests.Support;

/// <summary>
/// The runtime model the host derives from the typed movie kind, plus the lookups every fixture needs.
/// </summary>
/// <remarks>
/// <para>
/// The subject of almost every fixture here is what comes out of the derivation: a field descriptor, a
/// browse axis, a rung-table row, a declared expression, a naming template, a request map. Deriving it once
/// keeps every fixture reading the same object the host would hold.
/// </para>
/// <para>
/// <b>What these fixtures can and cannot prove.</b> They prove that the item type and its configuration
/// derive into the structure and intent the engines read, that every declared expression compiles, and that
/// every cross-reference inside the derived model resolves. They cannot run the release corpus end to end
/// from the model alone, which is what <see cref="MoviesEngines"/> is for.
/// </para>
/// </remarks>
internal static class MoviesDeclaration
{
    /// <summary>The kind's runtime model, derived once from <see cref="Movie"/> and <see cref="Movies"/>.</summary>
    internal static IMediaType Model { get; } = MediaTypeModelFactory.Build<Movie, Movies>();

    /// <summary>The derived structure.</summary>
    internal static MediaShape Shape => Model.Shape;

    /// <summary>The derived intent surface.</summary>
    internal static PluginIntentSurface Intent => Model.Intent;

    /// <summary>The carried per-kind engine inputs.</summary>
    internal static MediaKindModel Carried => Model.Model;

    /// <summary>The parse section.</summary>
    internal static ParseDeclaration Parsing => Carried.Parsing;

    /// <summary>The single video format family.</summary>
    internal static FormatFamily Video => Shape.FormatFamilies[0];

    /// <summary>The derived runtime model, exactly as the host derives it.</summary>
    /// <remarks>
    /// <para>
    /// Nothing is composed on top any more. Three things used to be: the external-identifier schemes, one
    /// unit-resolution row and the match-confidence table. The host now supplies all three itself — the
    /// schemes because the gate stopped cross-checking a catalog map against a shape that deliberately does
    /// not enumerate them, the unit row because the derivation emits the one its structure implies, and the
    /// confidence table because how far to trust a basis is host policy and
    /// <c>MatchConfidencePolicy</c> owns it.
    /// </para>
    /// <para>
    /// So a fixture reading this is reading the derivation's own output, with nothing standing in.
    /// </para>
    /// </remarks>
    internal static IMediaType Derived => Model;

    /// <summary>The item level of the derived shape.</summary>
    internal static MediaLevel Level => Shape.Levels[0];

    /// <summary>Every derived field of the item level, by its derived identifier.</summary>
    internal static IReadOnlyDictionary<string, FieldDescriptor> Fields { get; } =
        Level.Fields.ToDictionary(static field => field.FieldId, StringComparer.Ordinal);

    /// <summary>Every declared rung by name, the unknown rung included.</summary>
    internal static IReadOnlyDictionary<string, QualityTier> Tiers { get; } = BuildTiers();

    /// <summary>Every corpus case that pins a rung, by input text.</summary>
    internal static IReadOnlyDictionary<string, string> ExpectedTiers { get; } =
        Carried.Corpus
            .Where(static row => row.ExpectedTierId is not null)
            .ToDictionary(static row => row.Input, static row => row.ExpectedTierId!, StringComparer.Ordinal);

    /// <summary>Every corpus case that pins a title, by input text.</summary>
    internal static IReadOnlyDictionary<string, string> ExpectedTitles { get; } =
        Carried.Corpus
            .Where(static row => row.ExpectedTitle is not null)
            .ToDictionary(static row => row.Input, static row => row.ExpectedTitle!, StringComparer.Ordinal);

    /// <summary>Looks a rung up by the name the ladder publishes it under.</summary>
    /// <param name="name">The rung name.</param>
    /// <returns>The tier.</returns>
    internal static QualityTier Tier(string name) => Tiers[name];

    /// <summary>Finds a declared guard by identifier.</summary>
    /// <param name="guardId">The identifier.</param>
    /// <returns>The guard.</returns>
    internal static GuardPattern Guard(string guardId) =>
        Parsing.Guards.Single(guard => string.Equals(guard.GuardId, guardId, StringComparison.Ordinal));

    /// <summary>Finds a declared title pattern by identifier.</summary>
    /// <param name="patternId">The identifier.</param>
    /// <returns>The pattern.</returns>
    internal static TitlePattern Pattern(string patternId) =>
        Parsing.TitlePatterns.Single(
            pattern => string.Equals(pattern.PatternId, patternId, StringComparison.Ordinal));

    /// <summary>Every rung rule that resolves to one tier.</summary>
    /// <param name="tierId">The tier name.</param>
    /// <returns>The rules, in declared order.</returns>
    internal static IReadOnlyList<RungRule> RulesFor(string tierId) =>
        [.. Parsing.RungResolution.Rules.Where(
            rule => string.Equals(rule.TierId, tierId, StringComparison.Ordinal))];

    private static Dictionary<string, QualityTier> BuildTiers()
    {
        var tiers = new Dictionary<string, QualityTier>(StringComparer.Ordinal);

        foreach (var family in Shape.FormatFamilies)
        {
            foreach (var tier in family.Ladder)
            {
                tiers[tier.Name] = tier;
            }

            tiers[family.Unknown.Name] = family.Unknown;
        }

        return tiers;
    }
}
