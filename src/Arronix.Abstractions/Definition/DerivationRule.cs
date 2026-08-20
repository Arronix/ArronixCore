using System.Collections.ObjectModel;
using Arronix.Abstractions.Shape;

namespace Arronix.Abstractions.Definition;

/// <summary>
/// One parameterized derivation applied after response mapping.
/// </summary>
/// <remarks>
/// Each kind of derivation is a host-implemented rule with declared parameters — the closed list covers
/// the decisions catalog integrations actually get wrong: staged status with a windowed final stage,
/// date reduction, region-keyed selection with no cross-region fallback, image-role selection, and a
/// guarded conditional value.
/// </remarks>
public sealed record DerivationRule
{
    /// <summary>
    /// Gets the rule's identifier, for diagnostics.
    /// </summary>
    public required string RuleId { get; init; }

    /// <summary>
    /// Gets which host derivation the rule invokes.
    /// </summary>
    public required DerivationKind Kind { get; init; }

    /// <summary>
    /// Gets the declared field the derived value lands in, for derivations that target one field.
    /// </summary>
    public string? TargetFieldId { get; init; }

    /// <summary>
    /// Gets the derivation's parameters, validated against the kind's declared parameter schema.
    /// </summary>
    public IReadOnlyDictionary<string, FieldValue> Parameters { get; init; }
        = ReadOnlyDictionary<string, FieldValue>.Empty;
}
