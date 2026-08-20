
namespace Arronix.Abstractions.Media;

/// <summary>
/// Marks a property that participates in free-text search.
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class SearchableAttribute : Attribute;
