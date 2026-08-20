
using Arronix.Abstractions.Intent;

namespace Arronix.Client.Rendering;

/// <summary>
/// Turns a declared cost and a declared certainty requirement into how this client asks before acting.
/// </summary>
/// <remarks>
/// <para>
/// Twelve combinations, written out. An extension declares that removing a library entry is destructive
/// and needs the user to reproduce a phrase; it does not declare a dialog, a color or a button. The
/// whole of the difference between those two statements is this file.
/// </para>
/// <para>
/// The cost and the certainty requirement are read together rather than separately because they are not
/// independent: an irreversible action with no declared certainty requirement is still worth stating the
/// consequence of, and a safe action that asks for confirmation should ask quietly.
/// </para>
/// </remarks>
public static class ConsequenceMap
{
    /// <summary>
    /// Gets how to ask before an action runs.
    /// </summary>
    /// <param name="consequence">How much the action costs and how far it can be undone.</param>
    /// <param name="confirmation">How sure the user must be.</param>
    /// <returns>What this client will do before invoking.</returns>
    public static ConfirmationPolicy For(Consequence consequence, ConfirmationRequirement confirmation)
        => (consequence, confirmation) switch
        {
            (Consequence.Safe, ConfirmationRequirement.None) => ConfirmationPolicy.Immediate("emphasis-quiet"),
            (Consequence.Safe, ConfirmationRequirement.Acknowledge) => ConfirmationPolicy.Ask("emphasis-quiet"),
            (Consequence.Safe, ConfirmationRequirement.TypeToConfirm) => ConfirmationPolicy.AskWithPhrase("emphasis-quiet"),

            (Consequence.Costly, ConfirmationRequirement.None) => ConfirmationPolicy.Immediate("emphasis-notable"),
            (Consequence.Costly, ConfirmationRequirement.Acknowledge) => ConfirmationPolicy.Ask("emphasis-notable"),
            (Consequence.Costly, ConfirmationRequirement.TypeToConfirm) => ConfirmationPolicy.AskWithPhrase("emphasis-notable"),

            (Consequence.Destructive, ConfirmationRequirement.None) => ConfirmationPolicy.Ask("emphasis-severe"),
            (Consequence.Destructive, ConfirmationRequirement.Acknowledge) => ConfirmationPolicy.Ask("emphasis-severe"),
            (Consequence.Destructive, ConfirmationRequirement.TypeToConfirm) => ConfirmationPolicy.AskWithPhrase("emphasis-severe"),

            (Consequence.Irreversible, ConfirmationRequirement.None) => ConfirmationPolicy.AskWithPhrase("emphasis-severe"),
            (Consequence.Irreversible, ConfirmationRequirement.Acknowledge) => ConfirmationPolicy.AskWithPhrase("emphasis-severe"),
            (Consequence.Irreversible, ConfirmationRequirement.TypeToConfirm) => ConfirmationPolicy.AskWithPhrase("emphasis-severe"),
        };

    /// <summary>
    /// Gets the phrase the user must reproduce, when one is required.
    /// </summary>
    /// <param name="actionName">The action's declared name.</param>
    /// <returns>The phrase.</returns>
    /// <remarks>
    /// Derived from the declaration rather than asked for in it: a phrase is a presentation of certainty,
    /// and an extension that could choose it could choose an empty one.
    /// </remarks>
    public static string PhraseFor(string actionName)
        => string.IsNullOrWhiteSpace(actionName) ? "CONFIRM" : actionName.Trim().ToUpperInvariant();
}

/// <summary>
/// What this client does before invoking an action.
/// </summary>
/// <param name="RequiresPrompt">Whether the user is asked first.</param>
/// <param name="RequiresPhrase">Whether the user must reproduce a phrase to proceed.</param>
/// <param name="EmphasisClass">The style class the offer and the prompt carry.</param>
public sealed record ConfirmationPolicy(bool RequiresPrompt, bool RequiresPhrase, string EmphasisClass)
{
    /// <summary>Creates a policy that invokes without asking.</summary>
    /// <param name="emphasis">The style class.</param>
    /// <returns>The policy.</returns>
    public static ConfirmationPolicy Immediate(string emphasis) => new(false, false, emphasis);

    /// <summary>Creates a policy that asks once.</summary>
    /// <param name="emphasis">The style class.</param>
    /// <returns>The policy.</returns>
    public static ConfirmationPolicy Ask(string emphasis) => new(true, false, emphasis);

    /// <summary>Creates a policy that asks and requires a reproduced phrase.</summary>
    /// <param name="emphasis">The style class.</param>
    /// <returns>The policy.</returns>
    public static ConfirmationPolicy AskWithPhrase(string emphasis) => new(true, true, emphasis);
}
