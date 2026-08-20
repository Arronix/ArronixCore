
namespace Arronix.Abstractions.Intent;

/// <summary>How far taking one catalog item on extends through its declared grouping.</summary>
public enum MonitoringScope
{
    /// <summary>Add the item without making any acquisition target wanted.</summary>
    None = 0,

    /// <summary>Make only the selected item wanted.</summary>
    Item = 1,

    /// <summary>Make the selected item and every member of its containing group wanted.</summary>
    ItemAndGroup = 2
}
