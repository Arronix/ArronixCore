using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Arronix.Abstractions.Definition;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Intent;
using Arronix.Abstractions.Media;
using Arronix.Abstractions.Shape;


namespace Arronix.Host.Media;

/// <summary>
/// A media kind that has been checked once and can no longer disappoint an engine.
/// </summary>
/// <remarks>
/// <para>
/// The model-level stage of the parse-don't-validate gate: <see cref="ValidatedShape"/> resolves the derived
/// structure, then <see cref="DefinitionValidationRules"/> resolves every cross-reference the model's
/// sections make over it — captures to coordinate components, rung rows to ladder tiers, query tiers to
/// search kinds. After the gate an engine constructed from this object cannot encounter an unresolved
/// identifier, because none survived admission.
/// </para>
/// <para>
/// The model is held verbatim and its rule order is untouched. Ordered tables <i>are</i> the algorithm —
/// pre-release before broadcast, weak signals last — so nothing here sorts, canonicalizes or deduplicates a
/// row list; what the derivation produced is what every engine executes and what the wire publishes.
/// </para>
/// <para>
/// Structure and intent are <i>derived</i> from an item type rather than declared, and are checked here
/// anyway. Derivation producing an invalid model is a host defect rather than a plugin one, and a gate that
/// trusts its own side is not a gate.
/// </para>
/// </remarks>
public sealed class ValidatedDefinition
{
    private readonly Dictionary<string, GuardPattern> _guardsById;
    private readonly Dictionary<string, TitlePattern> _patternsById;

    private ValidatedDefinition(
        MediaKindId kind,
        MediaKindModel model,
        PluginIntentSurface? intent,
        ValidatedShape shape)
    {
        Kind = kind;
        Model = model;
        Intent = intent;
        Shape = shape;

        _guardsById = model.Parsing?.Guards.ToDictionary(guard => guard.GuardId, StringComparer.Ordinal)
            ?? new Dictionary<string, GuardPattern>(StringComparer.Ordinal);
        _patternsById = model.Parsing?.TitlePatterns.ToDictionary(
                pattern => pattern.PatternId,
                StringComparer.Ordinal)
            ?? new Dictionary<string, TitlePattern>(StringComparer.Ordinal);
    }

    /// <summary>
    /// Gets the media kind.
    /// </summary>
    public MediaKindId Kind { get; }

    /// <summary>
    /// Gets the derived runtime model the engines compile, saying exactly what was derived.
    /// </summary>
    /// <remarks>
    /// Internal because it is host machinery rather than a declaration. Its collections are copied into
    /// host-owned values before it is retained: the engines read this model outside any invocation lease,
    /// so an extension-supplied collection here would run extension code with no ticket held.
    /// <para>
    /// Two members are delegates by contract and cannot be copied, and they differ.
    /// <see cref="MediaKindModel.Respace"/> is the media kind's own; the one path that reaches it is the
    /// declarative release parser compiled from this model, which is an internal seam of
    /// <c>RegisteredMediaKind</c> and is obtainable only from a leased handle — so invoking it holds the
    /// contributing extension's ticket. Each template requirement's predicate is built by the host's
    /// definition compiler rather than supplied by the extension, and no production path invokes one at
    /// all today; when a consumer is written it has to sit behind the same media-kind lease. Being
    /// published is not the proof for either — the lease on the path that reaches them is.
    /// </para>
    /// </remarks>
    internal MediaKindModel Model { get; }

    /// <summary>
    /// Gets the derived intent surface, when the kind declared anything to work with.
    /// </summary>
    public PluginIntentSurface? Intent { get; }

    /// <summary>
    /// Gets the resolved structure the sections were checked against.
    /// </summary>
    public ValidatedShape Shape { get; }

    /// <summary>
    /// Checks a derived media type and, when it is sound, produces the resolved form.
    /// </summary>
    /// <param name="type">The derived runtime model.</param>
    /// <param name="validated">The resolved form when it is sound; otherwise <see langword="null"/>.</param>
    /// <param name="defects">Every fault found. Empty exactly when it is sound.</param>
    /// <returns><see langword="true"/> when it is sound.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="type"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Shape defects short-circuit the section checks, because a section cannot be checked against a
    /// structure that did not resolve; section defects never short-circuit each other, so a refused kind is
    /// refused with everything wrong with it.
    /// </remarks>
    [SuppressMessage(
        "Design",
        "CA1021:Avoid out parameters",
        Justification = "The try-and-out form is the parse-don't-validate idiom this type exists to provide, and it returns two results: the parsed model and the complete defect list.")]
    public static bool TryValidate(
        IMediaTypeRuntime type,
        out ValidatedDefinition? validated,
        out IReadOnlyList<ShapeDefect> defects)
    {
        ArgumentNullException.ThrowIfNull(type);

        return TryValidate(type.Kind, type.Shape, type.Intent, type.Model, out validated, out defects);
    }

    /// <summary>
    /// Checks the pieces of a media kind directly, for a caller that assembled them itself.
    /// </summary>
    /// <param name="kind">The media kind.</param>
    /// <param name="shape">The structure.</param>
    /// <param name="intent">The intent surface, when there is one.</param>
    /// <param name="model">The per-kind engine inputs.</param>
    /// <param name="validated">The resolved form when it is sound; otherwise <see langword="null"/>.</param>
    /// <param name="defects">Every fault found. Empty exactly when it is sound.</param>
    /// <returns><see langword="true"/> when it is sound.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="shape"/> or <paramref name="model"/> is <see langword="null"/>.
    /// </exception>
    [SuppressMessage(
        "Design",
        "CA1021:Avoid out parameters",
        Justification = "The try-and-out form is the parse-don't-validate idiom this type exists to provide, and it returns two results: the parsed model and the complete defect list.")]
    public static bool TryValidate(
        MediaKindId kind,
        MediaShape shape,
        PluginIntentSurface? intent,
        MediaKindModel model,
        out ValidatedDefinition? validated,
        out IReadOnlyList<ShapeDefect> defects)
    {
        ArgumentNullException.ThrowIfNull(shape);
        ArgumentNullException.ThrowIfNull(model);

        // Copied before anything reads it, and before it is retained. The engines compiled from this model
        // read it outside any invocation lease, so an extension-supplied collection here would run
        // extension code with no ticket held and would pin its context until the kind is withdrawn.
        model = ModelBoundary.Snapshot(model);
        validated = null;

        if (!ValidatedShape.TryValidate(shape, out var validatedShape, out var shapeDefects))
        {
            defects = shapeDefects;
            return false;
        }

        var found = new List<ShapeDefect>();
        DefinitionValidationRules.Check(model, validatedShape!, found);

        if (found.Count > 0)
        {
            defects = found;
            return false;
        }

        validated = new ValidatedDefinition(
            kind,
            model,
            intent is null ? null : DeclarationBoundary.Snapshot(intent),
            validatedShape!);
        defects = [];
        return true;
    }

    /// <summary>
    /// Gets a declared guard by its identifier.
    /// </summary>
    /// <param name="guardId">The identifier, which came from this model.</param>
    /// <returns>The guard.</returns>
    /// <exception cref="KeyNotFoundException">
    /// The identifier is not from this model. Reaching this is a defect in the caller: every guard reference
    /// the host holds was resolved by the gate.
    /// </exception>
    public GuardPattern GuardOf(string guardId) => _guardsById[guardId];

    /// <summary>
    /// Gets a declared title pattern by its identifier.
    /// </summary>
    /// <param name="patternId">The identifier, which came from this model.</param>
    /// <returns>The pattern.</returns>
    /// <exception cref="KeyNotFoundException">
    /// The identifier is not from this model. Reaching this is a defect in the caller: every pattern
    /// reference the host holds was resolved by the gate.
    /// </exception>
    public TitlePattern PatternOf(string patternId) => _patternsById[patternId];
}
