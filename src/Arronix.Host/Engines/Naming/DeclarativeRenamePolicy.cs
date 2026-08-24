using System.Linq;
using Arronix.Abstractions.Definition;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Naming;
using Arronix.Abstractions.Shape;
using Arronix.Common.Naming;
using Arronix.Host.Languages;

namespace Arronix.Host.Engines.Naming;

/// <summary>
/// Resolves the items a naming render binds against. The host wires this to its store; tests wire a
/// fixture.
/// </summary>
/// <remarks>
/// The rename seam hands the engine a bare <see cref="MediaItemId"/>, so something must answer for the
/// item and its ancestors. Kept as a one-method seam rather than a store dependency because the engine
/// needs exactly this much and nothing else about persistence.
/// </remarks>
internal interface INamingItemResolver
{
    /// <summary>
    /// Resolves an item.
    /// </summary>
    /// <param name="itemId">The item's identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The item, or null when it does not exist.</returns>
    Task<ItemView?> GetItemAsync(MediaItemId itemId, CancellationToken cancellationToken = default);
}

/// <summary>
/// The naming engine wearing the existing plugin seam: an <see cref="IRenamePolicy"/> built from a media
/// kind's derived naming section instead of hand-written per-kind code.
/// </summary>
/// <remarks>
/// <para>
/// This is how a declared kind and an imperative kind flow through one pipeline: the host constructs
/// one of these per validated definition and registers it exactly where a plugin's own policy would
/// register, so downstream rename callers cannot tell the difference
/// (<c>docs/design/declarative-media-kinds.md</c> §2 — the engines implement the seams verbatim).
/// </para>
/// <para>
/// <see cref="GenerateFileNameAsync"/> takes the file's facts — the review's A3 amendment — because most
/// of a rendered name is file properties: quality, release group, languages, technical facets.
/// </para>
/// </remarks>
internal sealed class DeclarativeRenamePolicy : IRenamePolicy
{
    private const int MaxAncestorDepth = 8;

    private readonly ShapeTokenDeriver _deriver;
    private readonly NamingEngine _engine;
    private readonly INamingItemResolver _resolver;
    private readonly IReadOnlySet<string> _derivableTokens;

    /// <summary>
    /// Initializes a new instance of the <see cref="DeclarativeRenamePolicy"/> class.
    /// </summary>
    /// <param name="mediaKind">The kind the policy serves.</param>
    /// <param name="shape">The kind's declared shape.</param>
    /// <param name="declaration">The kind's naming declaration.</param>
    /// <param name="resolver">The item resolver.</param>
    /// <param name="options">The render options.</param>
    /// <param name="languages">The installed language operations.</param>
    public DeclarativeRenamePolicy(
        MediaKindId mediaKind,
        MediaShape shape,
        NamingDeclaration declaration,
        INamingItemResolver resolver,
        RenderOptions? options = null,
        LanguageTextService? languages = null)
    {
        ArgumentNullException.ThrowIfNull(shape);
        ArgumentNullException.ThrowIfNull(declaration);
        ArgumentNullException.ThrowIfNull(resolver);

        MediaKind = mediaKind;
        _deriver = new ShapeTokenDeriver(shape);
        _engine = new NamingEngine(declaration, options, languages);
        _resolver = resolver;
        _derivableTokens = PotentialTokens(shape);
    }

    /// <inheritdoc />
    public MediaKindId MediaKind { get; }

    /// <inheritdoc />
    public async Task<string> GenerateFileNameAsync(
        MediaItemId itemId,
        MediaFileFacts? file,
        string namingTemplate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(namingTemplate);

        var bindings = _deriver.Bind(await ResolveChainAsync(itemId, cancellationToken).ConfigureAwait(false), file);

        return _engine.RenderTemplate(namingTemplate, bindings, file);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, string>> ResolveTokensAsync(
        MediaItemId itemId,
        CancellationToken cancellationToken = default)
    {
        var bindings = _deriver.Bind(await ResolveChainAsync(itemId, cancellationToken).ConfigureAwait(false), file: null);
        var tokens = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var binding in bindings.All)
        {
            if (binding.Values.Count > 0 && binding.Values[0].Length > 0)
            {
                tokens[$"{{{binding.DisplayName}}}"] = binding.Values[0];
            }
        }

        return tokens;
    }

    /// <inheritdoc />
    public bool ValidateTemplate(string namingTemplate)
    {
        if (string.IsNullOrWhiteSpace(namingTemplate))
        {
            return false;
        }

        var compiled = NamingTemplateParser.Parse(namingTemplate);

        // A token the shape cannot derive and no host global supplies is a typo, and a typo rendering
        // as empty text is the surveyed failure this engine refuses to carry (§2.3: Sonarr maps every
        // unknown token to string.Empty and silently shortens the name).
        return compiled.IsValid
            && compiled.ReferencedTokens.All(_derivableTokens.Contains);
    }

    private async Task<IReadOnlyList<ItemView>> ResolveChainAsync(MediaItemId itemId, CancellationToken cancellationToken)
    {
        var chain = new List<ItemView>();
        var item = await _resolver.GetItemAsync(itemId, cancellationToken).ConfigureAwait(false);

        while (item is not null && chain.Count < MaxAncestorDepth)
        {
            chain.Insert(0, item);

            if (item.Parent is not { } parent)
            {
                break;
            }

            item = await _resolver.GetItemAsync(parent.Id, cancellationToken).ConfigureAwait(false);
        }

        return chain;
    }

    private static IReadOnlySet<string> PotentialTokens(MediaShape shape)
    {
        var tokens = new HashSet<string>(StringComparer.Ordinal)
        {
            // The host-global vocabulary (naming design §2.3).
            "qualitytitle", "qualityfull", "qualityproper", "qualityreal",
            "releasegroup", "originaltitle", "originalfilename", "languages", "ext",
        };

        foreach (var level in shape.Levels)
        {
            tokens.Add(NamingTokenName.Canonicalize($"{level.Name} Title"));

            foreach (var field in level.Fields)
            {
                tokens.Add(NamingTokenName.Canonicalize($"{level.Name} {field.Name}"));
            }

            foreach (var scheme in level.Identity.ExternalIds)
            {
                tokens.Add(NamingTokenName.Canonicalize($"{level.Name} {scheme.Scheme} Id"));
            }

            foreach (var axis in level.SequenceAxes)
            {
                tokens.Add(NamingTokenName.Canonicalize(axis.Name));
                tokens.Add(NamingTokenName.Canonicalize($"{axis.Name} Name"));
            }
        }

        foreach (var space in shape.CoordinateSpaces)
        {
            tokens.Add(NamingTokenName.Canonicalize(space.Name));

            foreach (var component in space.Components)
            {
                tokens.Add(NamingTokenName.Canonicalize(component.Name));
            }
        }

        foreach (var declared in shape.Tokens)
        {
            // D16: contributed extras, declared verbatim.
            tokens.Add(NamingTokenName.Canonicalize(declared.Name));
        }

        return tokens;
    }
}
