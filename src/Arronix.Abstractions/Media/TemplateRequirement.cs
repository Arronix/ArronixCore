using Arronix.Abstractions.Shape;

namespace Arronix.Abstractions.Media;

/// <summary>
/// What a user's template does and does not mention, as the host checks it.
/// </summary>
/// <remarks>
/// The derived counterpart of the typed authoring facts: an author writes property expressions, and by the
/// time the rule reaches the host those have become the field identifiers the derived token set is keyed
/// by. One arity apart from <see cref="INamingTemplateFacts{TItem}"/> and deliberately so — the host holds
/// a model it cannot name the item type of.
/// </remarks>
public interface INamingTemplateFacts
{
    /// <summary>
    /// Reports whether the template mentions the token derived from a field.
    /// </summary>
    /// <param name="fieldId">The <see cref="FieldDescriptor.FieldId"/>.</param>
    /// <returns><see langword="true"/> when the template mentions it.</returns>
    bool HasField(string fieldId);

    /// <summary>
    /// Reports whether the template mentions the token for a host-owned file fact.
    /// </summary>
    /// <param name="fact">The fact.</param>
    /// <returns><see langword="true"/> when the template mentions it.</returns>
    bool Has(FileFact fact);
}

/// <summary>
/// One rule a user's file template has to satisfy before it is saved.
/// </summary>
/// <param name="RuleId">The rule's identifier, for diagnostics.</param>
/// <param name="Requirement">
/// The sentence shown to whoever wrote a template that fails, phrased as what the template must do.
/// </param>
/// <param name="IsSatisfied">The rule.</param>
/// <remarks>
/// A predicate rather than a flag per token, because the real rules are not conjunctions. The rule that
/// motivated this is a disjunction with an exclusivity between its branches, which a per-token "is
/// required" boolean cannot express at all and which is ten lines of ordinary code here.
/// </remarks>
public sealed record TemplateRequirement(
    string RuleId,
    string Requirement,
    Func<INamingTemplateFacts, bool> IsSatisfied);
