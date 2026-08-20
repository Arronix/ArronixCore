
namespace Arronix.Abstractions.Media;

/// <summary>Whether an authoritative catalog currently presents an item as live.</summary>
public enum CatalogRecordState
{
    /// <summary>The catalog currently presents the item.</summary>
    Active = 0,

    /// <summary>The catalog has withdrawn or deleted the item.</summary>
    Withdrawn = 1
}
