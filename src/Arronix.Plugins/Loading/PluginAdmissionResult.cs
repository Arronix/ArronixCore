using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Health;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Plugins;


namespace Arronix.Plugins.Loading;

/// <summary>
/// One host admission attempt whose candidates have been built but not published.
/// </summary>
/// <remarks>
/// Preparation may activate extension types, derive media projections and validate every candidate. Commit
/// must only publish those already-built values. Implementations must make rollback attempt-scoped: it may
/// remove values published by this attempt, but never values merely sharing its extension identifier.
/// </remarks>
internal interface IPluginAdmissionAttempt
{
    /// <summary>Gets the extension this attempt belongs to.</summary>
    PluginId Plugin { get; }

    /// <summary>Gets the authoritative inventory derived from this attempt's prepared media candidates.</summary>
    AdmittedInventory Inventory { get; }

    /// <summary>Publishes every prepared contribution, or publishes none.</summary>
    /// <param name="errorCode">The failure class when publication was refused.</param>
    /// <param name="defects">Every publication defect, or an empty list on success.</param>
    /// <returns><see langword="true"/> when the attempt was committed.</returns>
    bool TryCommit(out CoreErrorCode errorCode, out IReadOnlyList<string> defects);

    /// <summary>
    /// Abandons an uncommitted attempt or withdraws exactly the values this attempt committed.
    /// </summary>
    void Rollback();

    /// <summary>
    /// Settles what this attempt committed provisionally, once nothing that could still fail remains.
    /// </summary>
    /// <remarks>
    /// Called after the last fallible publication step. Between <see cref="TryCommit"/> and this, a
    /// publication failure can still take the attempt back, so anything applied in between has to be
    /// reversible until here.
    /// </remarks>
    void Confirm();
}

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

        if (tokens.Any(static token => token is null))
        {
            throw new ArgumentException("An admitted token collection must not contain null entries.", nameof(tokens));
        }

        Kind = kind;
        Tokens = Array.AsReadOnly(tokens.ToArray());
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
/// An authoritative empty inventory is not an error: it means Host admission ran and the extension
/// contributed no media kind. That is distinct from the loader's pre-admission state, so an authoritative
/// empty result never falls back to a manifest or legacy declaration as though admission had not run.
/// </para>
/// </remarks>
public sealed class AdmittedInventory
{
    private readonly Dictionary<MediaKindId, AdmittedMediaKind> _byKind;

    private AdmittedInventory(IReadOnlyList<AdmittedMediaKind> mediaKinds, bool isAuthoritative)
    {
        ArgumentNullException.ThrowIfNull(mediaKinds);

        var snapshot = new List<AdmittedMediaKind>(mediaKinds.Count);
        _byKind = [];

        foreach (var kind in mediaKinds)
        {
            if (kind is null)
            {
                throw new ArgumentException(
                    "An admitted media-kind collection must not contain null entries.",
                    nameof(mediaKinds));
            }

            if (!_byKind.TryAdd(kind.Kind, kind))
            {
                throw new ArgumentException(
                    $"Media kind '{kind.Kind}' appears more than once in one admitted inventory.",
                    nameof(mediaKinds));
            }

            snapshot.Add(kind);
        }

        MediaKinds = snapshot.AsReadOnly();
        IsAuthoritative = isAuthoritative;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AdmittedInventory"/> class.
    /// </summary>
    /// <param name="mediaKinds">The admitted kinds, each carrying its own derived tokens.</param>
    /// <exception cref="ArgumentNullException"><paramref name="mediaKinds"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Two entries claim one media kind.</exception>
    public AdmittedInventory(IReadOnlyList<AdmittedMediaKind> mediaKinds)
        : this(mediaKinds, isAuthoritative: true)
    {
    }

    /// <summary>
    /// Gets the inventory of an extension that contributed no media kind.
    /// </summary>
    public static AdmittedInventory Empty { get; } = new([]);

    /// <summary>
    /// Gets the absence of a Host admission result, used only by pre-admission and quarantined states.
    /// </summary>
    internal static AdmittedInventory NotAdmitted { get; } = new([], isAuthoritative: false);

    /// <summary>
    /// Gets the admitted kinds, in admission order.
    /// </summary>
    public IReadOnlyList<AdmittedMediaKind> MediaKinds { get; }

    /// <summary>
    /// Gets a value indicating whether Host admission produced this inventory, including an authoritative
    /// result containing no media kinds.
    /// </summary>
    public bool IsAuthoritative { get; }

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
internal sealed class PluginAdmissionResult
{
    private PluginAdmissionResult(
        bool isAdmitted,
        AdmittedInventory inventory,
        CoreErrorCode errorCode,
        IReadOnlyList<string> defects,
        IPluginAdmissionAttempt? attempt)
    {
        IsAdmitted = isAdmitted;
        Inventory = inventory;
        ErrorCode = errorCode;
        Defects = Array.AsReadOnly(defects.ToArray());
        Attempt = attempt;
    }

    /// <summary>
    /// Gets a value indicating whether what the extension registered may be committed.
    /// </summary>
    public bool IsAdmitted { get; }

    /// <summary>
    /// Gets what the host admitted. Non-authoritative when admission refused, and authoritatively empty when
    /// the extension contributes no media kind.
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
    /// Gets the prepared Host attempt, or <see langword="null"/> when admission was refused.
    /// </summary>
    public IPluginAdmissionAttempt? Attempt { get; }

    /// <summary>Records a successful preparation whose values still require final publication.</summary>
    /// <param name="attempt">The attempt that owns the prepared values.</param>
    /// <returns>The prepared result.</returns>
    public static PluginAdmissionResult Prepared(IPluginAdmissionAttempt attempt)
    {
        ArgumentNullException.ThrowIfNull(attempt);

        return new PluginAdmissionResult(
            isAdmitted: true,
            attempt.Inventory,
            CoreErrorCode.Unknown,
            defects: [],
            attempt);
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

        return new PluginAdmissionResult(
            isAdmitted: false,
            AdmittedInventory.NotAdmitted,
            errorCode,
            defects,
            attempt: null);
    }
}
