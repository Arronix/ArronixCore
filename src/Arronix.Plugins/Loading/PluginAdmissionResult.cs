using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Health;
using Arronix.Abstractions.Identity;


namespace Arronix.Plugins.Loading;

/// <summary>
/// One media kind exactly as the host admitted it.
/// </summary>
/// <remarks>
/// <para>
/// The tokens here are the ones the admitted projection actually derived, not the ones a declaration file
/// claimed. That distinction is the whole reason this type exists: a typed media kind's naming vocabulary
/// is a consequence of its item type, its group relationships and its identity roles, so the only honest
/// answer to "which tokens does this kind own" comes from the kind the host admitted.
/// </para>
/// <para>
/// Tokens stay attached to their kind. Flattening them across an extension's kinds would make a two-kind
/// extension claim every token for every kind it supplies, which is a cross product rather than ownership.
/// </para>
/// </remarks>
public sealed class AdmittedMediaKind
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AdmittedMediaKind"/> class.
    /// </summary>
    /// <param name="kind">The admitted kind's identifier.</param>
    /// <param name="tokens">The naming tokens that kind's admitted projection derived.</param>
    /// <exception cref="ArgumentNullException"><paramref name="tokens"/> is <see langword="null"/>.</exception>
    public AdmittedMediaKind(MediaKindId kind, IReadOnlyList<NamingToken> tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);

        Kind = kind;
        Tokens = tokens;
    }

    /// <summary>
    /// Gets the admitted kind's identifier.
    /// </summary>
    public MediaKindId Kind { get; }

    /// <summary>
    /// Gets the naming tokens the admitted projection derived for this kind, in derivation order.
    /// </summary>
    public IReadOnlyList<NamingToken> Tokens { get; }
}

/// <summary>
/// What one extension actually contributed, as the host admitted it, keyed per media kind.
/// </summary>
/// <remarks>
/// <para>
/// The loader owns isolation, ordering and ownership; the host owns meaning. Before this type existed the
/// loader had to reconstruct meaning from the declaration file, so a typed extension whose kind the host
/// had already bound and published still failed a check that could only see the legacy shape seam. The
/// inventory closes that gap in the one direction the dependency allows: the host hands back what it
/// admitted, and the loader's remaining checks read that rather than the manifest.
/// </para>
/// <para>
/// An empty inventory is not an error. A loader driven without a host admission check, or an extension that
/// contributes no media kind at all, both produce one, and the loader keeps its transitional declaration
/// path for exactly those cases.
/// </para>
/// </remarks>
public sealed class AdmittedInventory
{
    private readonly Dictionary<MediaKindId, AdmittedMediaKind> _byKind;

    /// <summary>
    /// Initializes a new instance of the <see cref="AdmittedInventory"/> class.
    /// </summary>
    /// <param name="mediaKinds">The admitted kinds, each carrying its own derived tokens.</param>
    /// <exception cref="ArgumentNullException"><paramref name="mediaKinds"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Two entries claim one media kind.</exception>
    public AdmittedInventory(IReadOnlyList<AdmittedMediaKind> mediaKinds)
    {
        ArgumentNullException.ThrowIfNull(mediaKinds);

        _byKind = [];

        foreach (var kind in mediaKinds)
        {
            if (!_byKind.TryAdd(kind.Kind, kind))
            {
                throw new ArgumentException(
                    $"Media kind '{kind.Kind}' appears more than once in one admitted inventory.",
                    nameof(mediaKinds));
            }
        }

        MediaKinds = mediaKinds;
    }

    /// <summary>
    /// Gets the inventory of an extension that contributed no media kind.
    /// </summary>
    public static AdmittedInventory Empty { get; } = new([]);

    /// <summary>
    /// Gets the admitted kinds, in admission order.
    /// </summary>
    public IReadOnlyList<AdmittedMediaKind> MediaKinds { get; }

    /// <summary>
    /// Gets a value indicating whether the host admitted any media kind for this extension.
    /// </summary>
    public bool HasMediaKinds => MediaKinds.Count > 0;

    /// <summary>
    /// Gets the admitted kind identifiers, in admission order.
    /// </summary>
    public IReadOnlyList<MediaKindId> Kinds => [.. MediaKinds.Select(kind => kind.Kind)];

    /// <summary>
    /// Finds one admitted kind.
    /// </summary>
    /// <param name="kind">The kind identifier.</param>
    /// <param name="admitted">The admitted kind when it was admitted; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the kind was admitted.</returns>
    public bool TryGet(MediaKindId kind, [NotNullWhen(true)] out AdmittedMediaKind? admitted)
        => _byKind.TryGetValue(kind, out admitted);

    /// <summary>
    /// Gets every derived token name across every admitted kind.
    /// </summary>
    /// <remarks>
    /// For agreement only. Ownership is claimed per kind, because a token means something inside a media
    /// context rather than across an installation.
    /// </remarks>
    internal IReadOnlyList<string> TokenNames
        => [.. MediaKinds.SelectMany(kind => kind.Tokens).Select(token => token.Name).Distinct(StringComparer.Ordinal)];
}

/// <summary>
/// The host's verdict on what an extension registered, and what it admitted when the verdict was yes.
/// </summary>
/// <remarks>
/// A verdict and an inventory rather than a boolean and two out parameters, because the admitted inventory
/// is the thing the rest of the pipeline needs and a check that only says "yes" forces every later step to
/// go looking for the facts again — which is exactly how the loader ended up asking a declaration file what
/// a typed media kind contains.
/// </remarks>
public sealed class PluginAdmissionResult
{
    private PluginAdmissionResult(
        bool isAdmitted,
        AdmittedInventory inventory,
        CoreErrorCode errorCode,
        IReadOnlyList<string> defects)
    {
        IsAdmitted = isAdmitted;
        Inventory = inventory;
        ErrorCode = errorCode;
        Defects = defects;
    }

    /// <summary>
    /// Gets a value indicating whether what the extension registered may be committed.
    /// </summary>
    public bool IsAdmitted { get; }

    /// <summary>
    /// Gets what the host admitted. Empty when admission refused, and legitimately empty when the extension
    /// contributes no media kind.
    /// </summary>
    public AdmittedInventory Inventory { get; }

    /// <summary>
    /// Gets the failure class. Meaningful only when admission refused.
    /// </summary>
    public CoreErrorCode ErrorCode { get; }

    /// <summary>
    /// Gets everything wrong when admission refused, or an empty list when it did not.
    /// </summary>
    public IReadOnlyList<string> Defects { get; }

    /// <summary>
    /// Records an admission that succeeded.
    /// </summary>
    /// <param name="inventory">What the host admitted.</param>
    /// <returns>The result.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="inventory"/> is <see langword="null"/>.</exception>
    public static PluginAdmissionResult Admitted(AdmittedInventory inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);

        return new PluginAdmissionResult(isAdmitted: true, inventory, CoreErrorCode.Unknown, defects: []);
    }

    /// <summary>
    /// Records an admission that refused.
    /// </summary>
    /// <param name="errorCode">The failure class.</param>
    /// <param name="defects">Everything wrong.</param>
    /// <returns>The result.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="defects"/> is <see langword="null"/>.</exception>
    public static PluginAdmissionResult Refused(CoreErrorCode errorCode, IReadOnlyList<string> defects)
    {
        ArgumentNullException.ThrowIfNull(defects);

        return new PluginAdmissionResult(isAdmitted: false, AdmittedInventory.Empty, errorCode, defects);
    }
}
