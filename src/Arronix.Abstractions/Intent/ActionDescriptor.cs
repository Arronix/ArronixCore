using System.Diagnostics.CodeAnalysis;
using Arronix.Abstractions.Shape;

namespace Arronix.Abstractions.Intent;

/// <summary>
/// Declares one thing a user may ask the platform to do.
/// </summary>
/// <remarks>
/// <para>
/// An extension contributes declarations, never code, to any interface. What is declared here is
/// <i>intent</i>: what the action is, what it acts on, what it costs, and how sure the user should be
/// before it happens. Nothing here says how the action is offered.
/// </para>
/// <para>
/// The test applied to every member of this contract area is whether a command line, a text interface, a
/// hands-free assistant and a graphical client each have an obvious, non-arbitrary way to honor it. If
/// only one of them does, the member is describing a presentation rather than an intent and has been
/// re-cast until it is not.
/// </para>
/// </remarks>
[Experimental(ExperimentalContracts.Intent, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed record ActionDescriptor
{
    /// <summary>
    /// Gets the identifier the action is invoked by.
    /// </summary>
    public required string ActionId { get; init; }

    /// <summary>
    /// Gets the action's display name, phrased as an instruction.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets a sentence explaining what the action does.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Gets what the action operates on.
    /// </summary>
    public required ActionScope Scope { get; init; }

    /// <summary>
    /// Gets the level the action applies at, when it applies at one.
    /// </summary>
    public MediaLevelId? TargetLevelId { get; init; }

    /// <summary>
    /// Gets the <see cref="GroupingAxis.AxisId"/> the action applies to. Set when and only when
    /// <see cref="Scope"/> is <see cref="ActionScope.Group"/>.
    /// </summary>
    public string? TargetGroupAxisId { get; init; }

    /// <summary>
    /// Gets how much the action costs and how far it can be undone.
    /// </summary>
    public required Consequence Consequence { get; init; }

    /// <summary>
    /// Gets how sure the user must be before the action happens.
    /// </summary>
    public required ConfirmationRequirement Confirmation { get; init; }

    /// <summary>
    /// Gets a plain-language statement of what the action will do, presented or spoken before anything
    /// irreversible happens. A sentence, so that any consumer can put it in front of a person.
    /// </summary>
    public string? ConsequenceStatement { get; init; }

    /// <summary>
    /// Gets a value indicating whether the action outlives the request that starts it, so that the caller
    /// is told it was accepted rather than that it finished.
    /// </summary>
    public bool LongRunning { get; init; }

    /// <summary>
    /// Gets the values the caller must supply.
    /// </summary>
    public IReadOnlyList<ActionParameter> Parameters { get; init; } = [];

    /// <summary>
    /// Gets the <see cref="FieldDescriptor.FieldId"/> of a two-state field on the subject that decides
    /// whether the action is available. <see langword="null"/> means always available.
    /// </summary>
    public string? EnabledWhenFieldId { get; init; }
}

/// <summary>
/// What an action operates on.
/// </summary>
[Experimental(ExperimentalContracts.Intent, UrlFormat = ExperimentalContracts.UrlFormat)]
public enum ActionScope
{
    /// <summary>One item.</summary>
    Item = 0,

    /// <summary>Whichever items the user has chosen.</summary>
    Selection = 1,

    /// <summary>Every item at one level.</summary>
    Level = 2,

    /// <summary>A whole media kind.</summary>
    Kind = 3,

    /// <summary>The platform itself.</summary>
    System = 4,

    /// <summary>
    /// Every group on one of the kind's grouping axes, named by
    /// <see cref="ActionDescriptor.TargetGroupAxisId"/>.
    /// </summary>
    /// <remarks>
    /// A grouping axis is not a level and not a kind, so before this member an action over collections had
    /// to be filed under <see cref="Kind"/> as the least wrong of the available answers — which loses the
    /// only fact a consumer needed, namely which axis it acts on.
    /// </remarks>
    Group = 5
}

/// <summary>
/// How much an action costs and how far it can be undone.
/// </summary>
/// <remarks>
/// Valence, not appearance. A command line turns this into a prompt, a text interface into an emphasis, a
/// hands-free assistant into a spoken confirmation. Encoding an appearance instead would have made the
/// declaration meaningless to three of those four.
/// </remarks>
[Experimental(ExperimentalContracts.Intent, UrlFormat = ExperimentalContracts.UrlFormat)]
public enum Consequence
{
    /// <summary>Changes nothing that cannot be changed back, and costs nothing to try.</summary>
    Safe = 0,

    /// <summary>Spends time, bandwidth or a remote quota.</summary>
    Costly = 1,

    /// <summary>Removes something the user has.</summary>
    Destructive = 2,

    /// <summary>Cannot be undone by any means the platform offers.</summary>
    Irreversible = 3
}

/// <summary>
/// How sure a user must be before an action happens.
/// </summary>
[Experimental(ExperimentalContracts.Intent, UrlFormat = ExperimentalContracts.UrlFormat)]
public enum ConfirmationRequirement
{
    /// <summary>The request itself is enough.</summary>
    None = 0,

    /// <summary>The user must affirm the consequence once.</summary>
    Acknowledge = 1,

    /// <summary>The user must reproduce a value that proves they read what they are about to do.</summary>
    TypeToConfirm = 2
}

/// <summary>
/// One value an action's caller must supply.
/// </summary>
/// <param name="ParameterId">The key the value is supplied under.</param>
/// <param name="Name">The parameter's display name.</param>
/// <param name="ValueKind">The shape of the value.</param>
/// <param name="Required">Whether the action can run without it.</param>
/// <param name="Choices">The permitted values, when the parameter takes one of a fixed set.</param>
/// <param name="DefaultValue">The value used when the caller supplies none.</param>
/// <param name="OptionSourceId">
/// The identifier of a set of values resolved through the platform at the time of asking, for parameters
/// whose permitted values are not known in advance.
/// </param>
[Experimental(ExperimentalContracts.Intent, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed record ActionParameter(
    string ParameterId,
    string Name,
    FieldValueKind ValueKind,
    bool Required,
    IReadOnlyList<FacetValue> Choices,
    string? DefaultValue = null,
    string? OptionSourceId = null);
