using Arronix.Abstractions.DTOs;

namespace Arronix.Host.Engines.Naming;

/// <summary>
/// How a token behaves when a rendered name is over its byte budget.
/// </summary>
/// <remarks>
/// Replaces the surveyed single hard-coded elastic token (Sonarr shortens the episode title and nothing
/// else — <c>src/NzbDrone.Core/Organizer/FileNameBuilder.cs:1142</c>). <see cref="Droppable"/> is what
/// makes a quality tag vanish rather than truncate when a name is over budget.
/// </remarks>
internal enum TokenElasticity
{
    /// <summary>Never shortened. Coordinates and identifiers.</summary>
    Rigid = 0,

    /// <summary>Shortened to fit, deepest level first. Titles.</summary>
    Elastic = 1,

    /// <summary>Dropped entirely, with its affixes, before anything elastic is shortened.</summary>
    Droppable = 2,
}

/// <summary>
/// One resolved token: its per-unit values and its truncation behavior.
/// </summary>
internal sealed record TokenBinding
{
    /// <summary>Gets the canonical token name.</summary>
    public required string CanonicalName { get; init; }

    /// <summary>Gets the token's display spelling, e.g. <c>Quality Full</c>.</summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// Gets the value per bound unit. A single-unit render carries one element; tokens outside a span
    /// group read the first element.
    /// </summary>
    public required IReadOnlyList<string> Values { get; init; }

    /// <summary>Gets how the token yields when a name is over budget.</summary>
    public TokenElasticity Elasticity { get; init; } = TokenElasticity.Rigid;

    /// <summary>Gets the level depth the token derives from. Deeper text is cut first. Root is zero.</summary>
    public int Depth { get; init; }

    /// <summary>
    /// Gets the year bound beside the token's level, feeding the <c>year</c> modifier.
    /// </summary>
    public int? Year { get; init; }

    /// <summary>
    /// Gets the language of the bound text when the owning typed value stated one. An absent language
    /// never means English.
    /// </summary>
    public Language? Language { get; init; }

    /// <summary>
    /// Creates a single-valued binding.
    /// </summary>
    /// <param name="name">The token name in its display spelling. Canonicalized for lookup.</param>
    /// <param name="value">The value.</param>
    /// <param name="elasticity">The truncation behavior.</param>
    /// <param name="depth">The level depth.</param>
    /// <returns>The binding.</returns>
    public static TokenBinding Of(
        string name,
        string value,
        TokenElasticity elasticity = TokenElasticity.Rigid,
        int depth = 0) =>
        new()
        {
            CanonicalName = NamingTemplateParser.Canonicalize(name),
            DisplayName = name,
            Values = [value],
            Elasticity = elasticity,
            Depth = depth,
        };
}

/// <summary>
/// The full set of bindings one render works from, keyed canonically.
/// </summary>
internal sealed class NamingTokenBindings
{
    private readonly Dictionary<string, TokenBinding> _bindings = new(StringComparer.Ordinal);

    /// <summary>
    /// Gets the number of units bound: the span-group iteration count. One for a single-unit file.
    /// </summary>
    public int UnitCount { get; private set; } = 1;

    /// <summary>
    /// Gets every binding, for callers that enumerate the resolved vocabulary.
    /// </summary>
    public IReadOnlyCollection<TokenBinding> All => _bindings.Values;

    /// <summary>
    /// Adds or replaces a binding. The later write wins, which is what lets declared fallbacks and
    /// contributed tokens layer over derived ones.
    /// </summary>
    /// <param name="binding">The binding.</param>
    /// <returns>This instance.</returns>
    public NamingTokenBindings Set(TokenBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        _bindings[binding.CanonicalName] = binding;

        if (binding.Values.Count > UnitCount)
        {
            UnitCount = binding.Values.Count;
        }

        return this;
    }

    /// <summary>
    /// Adds a single-valued binding under a template spelling.
    /// </summary>
    /// <param name="tokenName">The token name in any spelling.</param>
    /// <param name="value">The value.</param>
    /// <param name="elasticity">The truncation behavior.</param>
    /// <param name="depth">The level depth.</param>
    /// <returns>This instance.</returns>
    public NamingTokenBindings Set(
        string tokenName,
        string value,
        TokenElasticity elasticity = TokenElasticity.Rigid,
        int depth = 0) =>
        Set(TokenBinding.Of(tokenName, value, elasticity, depth));

    /// <summary>
    /// Looks a binding up by canonical name.
    /// </summary>
    /// <param name="canonicalName">The canonical name.</param>
    /// <param name="binding">The binding, when bound.</param>
    /// <returns>Whether the token is bound.</returns>
    public bool TryGet(string canonicalName, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out TokenBinding? binding) =>
        _bindings.TryGetValue(canonicalName, out binding);
}
