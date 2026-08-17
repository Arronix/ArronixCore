using Arronix.Abstractions.Naming;
using Arronix.Abstractions.Parsing;
using Arronix.Abstractions.Quality;
using Arronix.Abstractions.Shape;

// Shape and naming contracts are experimental; the catalog binds engines to their seams.
#pragma warning disable ARX0009
#pragma warning disable ARX0013

namespace Arronix.Host.Media;

/// <summary>
/// The host engines that can execute a validated definition, each registered as a factory for the seam it
/// implements.
/// </summary>
/// <remarks>
/// <para>
/// Engines are host-side, media-agnostic and written once; a factory here turns one
/// <see cref="ValidatedDefinition"/> into one seam instance for that kind. Because every engine implements
/// an <i>existing</i> seam — the declarative title parser behind <see cref="IReleaseParser"/>, the
/// declarative matcher behind <see cref="IReleaseMatcher"/>, the declarative planner behind
/// <see cref="IReleaseQueryPlanner"/> — the registry and everything downstream see no difference between a
/// declarative and an imperative kind: both arrive as the same <see cref="MediaKindContribution"/>.
/// </para>
/// <para>
/// A factory that is <see langword="null"/> means the engine has not landed in this build. The binder then
/// refuses the definition rather than admitting a kind that declares behavior nothing can execute —
/// admission is all of a media kind or none of it, and a definition's required sections are behavior, not
/// decoration. Composition fills the slots as the engines land; the seam contracts here are the freeze
/// line, not the engine types.
/// </para>
/// </remarks>
public sealed class DefinitionEngineCatalog
{
    /// <summary>
    /// Gets or sets the item-store engine: the definition's shape and fields projected as the catalog seam.
    /// The storage milestone's engine (design table row E9).
    /// </summary>
    public Func<ValidatedDefinition, IMediaItemSource>? ItemStore { get; set; }

    /// <summary>
    /// Gets or sets the parse engine behind the release-parsing seam (rows E2/E3).
    /// </summary>
    public Func<ValidatedDefinition, IReleaseParser>? Parser { get; set; }

    /// <summary>
    /// Gets or sets the match engine behind the release-matching seam (row E5).
    /// </summary>
    public Func<ValidatedDefinition, IReleaseMatcher>? Matcher { get; set; }

    /// <summary>
    /// Gets or sets the query templater behind the query-planning seam (row E6).
    /// </summary>
    public Func<ValidatedDefinition, IReleaseQueryPlanner>? QueryPlanner { get; set; }

    /// <summary>
    /// Gets or sets the quality evaluator, surfaced through the quality seam while imperative kinds still
    /// hold it open (row E4). Optional: a definition whose quality section is the ladder-derived default is
    /// fully served by the ladder itself.
    /// </summary>
    public Func<ValidatedDefinition, IQualityModel>? Quality { get; set; }

    /// <summary>
    /// Gets or sets the naming renderer, surfaced through the rename-policy seam while imperative kinds
    /// still hold it open (row E7). Optional for the same reason: the default naming section is the bare
    /// folder spine.
    /// </summary>
    public Func<ValidatedDefinition, IRenamePolicy>? Naming { get; set; }
}
