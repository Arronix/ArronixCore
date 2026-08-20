
namespace Arronix.Abstractions.Definition;

/// <summary>
/// How a span-scoped reading expands into member units.
/// </summary>
public enum SpanExpansion
{
    /// <summary>No expansion; the reading's units are taken as read.</summary>
    None = 0,

    /// <summary>The span expands to every member unit along its sequence axis.</summary>
    SequenceMembers = 1,

    /// <summary>
    /// The match resolves at the file-binding anchor and the units are the selected variant's children
    /// in running order, per the declared binding.
    /// </summary>
    BindingUnits = 2
}
