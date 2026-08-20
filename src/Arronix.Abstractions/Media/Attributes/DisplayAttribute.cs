
namespace Arronix.Abstractions.Media;

/// <summary>
/// Carries the prose derivation cannot produce: a display name that differs from the property's own, the
/// sentence explaining what the property holds, and the example a naming token shows a user.
/// </summary>
/// <remarks>
/// The one thing reflection genuinely cannot recover. A property gives up its name and its type; it does
/// not give up "the year the film was first shown anywhere" or a worked example, because documentation
/// comments are not in the assembly at run time. Everything else on the vocabulary is derived; this is
/// written because there is nowhere else for it to come from.
/// </remarks>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class DisplayAttribute : Attribute
{
    /// <summary>
    /// Gets or sets the display name, when the property's own name split on case is not the right one.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the sentence explaining what the property holds.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets a worked example of the value, shown beside the property's naming token.
    /// </summary>
    public string? Example { get; set; }
}
