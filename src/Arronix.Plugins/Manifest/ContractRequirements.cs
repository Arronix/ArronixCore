namespace Arronix.Plugins.Manifest;

/// <summary>
/// The contract versions an extension declares it works against.
/// </summary>
/// <remarks>
/// A record with one member rather than a bare string, because the manifest key is an object in every
/// governing document and adding a second contract family later must not change the shape of a file
/// operators have already written.
/// </remarks>
public sealed record ContractRequirements
{
    /// <summary>
    /// Gets the range of contract-assembly versions the extension accepts, in the grammar described by
    /// <see cref="Versioning.VersionRangeParser"/>.
    /// </summary>
    public required string Arronix { get; init; }
}
