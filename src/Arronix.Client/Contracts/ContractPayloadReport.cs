using Arronix.Abstractions.Client;

namespace Arronix.Client.Contracts;

/// <summary>What became of one serialized payload offered to one admitted client contract.</summary>
/// <remarks>Each value is a different problem and a different place to look, never one "failed".</remarks>
public enum ContractPayloadOutcome
{
    /// <summary>Nothing was attempted. First so that a default value claims nothing.</summary>
    NotAttempted = 0,

    /// <summary>The payload was read, projected, and every invariant held.</summary>
    Projected = 1,

    /// <summary>This page holds no admitted client contract to read a payload through.</summary>
    NoAdmittedContract = 2,

    /// <summary>An address was refused: the payload's own, or one a projected value carried.</summary>
    AddressUnsafe = 3,

    /// <summary>The payload could not be fetched.</summary>
    Unavailable = 4,

    /// <summary>The contract refused the bytes, or read them into nothing.</summary>
    DeserializationFailed = 5,

    /// <summary>The contract read the bytes into a value that is not its own declared entity type.</summary>
    DeserializedTypeMismatch = 6,

    /// <summary>Projecting the typed value threw.</summary>
    ProjectionFailed = 7,

    /// <summary>The projection names an entity type other than the one the contract declared.</summary>
    ProjectedTypeMismatch = 8,

    /// <summary>
    /// The projected fields are not the contract's own schema objects, in its own order, one each.
    /// </summary>
    SchemaDisagreement = 9,

    /// <summary>A projected value does not hold together as the value its descriptor declares.</summary>
    ValueInvariant = 10
}

/// <summary>One admitted client contract a payload may be offered to.</summary>
/// <remarks>The contract is held privately: an offer is a target to choose and hand back, not a way in.</remarks>
public sealed class ContractPayloadOffer
{
    internal ContractPayloadOffer(string assemblyName, VerifiedClientContract contract)
    {
        AssemblyName = assemblyName;
        Contract = contract;
        EntryPointType = contract.EntryPointType.FullName ?? contract.EntryPointType.Name;
        EntityTypeName = contract.EntityType.FullName ?? contract.EntityType.Name;
    }

    /// <summary>Gets the simple name of the assembly that declared this contract.</summary>
    public string AssemblyName { get; }

    /// <summary>Gets the declared entry point's own type name.</summary>
    public string EntryPointType { get; }

    /// <summary>Gets the name of the entity this contract reads and projects.</summary>
    public string EntityTypeName { get; }

    /// <summary>Gets the assembly and entry point together, which is what names one offer.</summary>
    /// <remarks>
    /// Neither half identifies an offer alone. A selection key, not a proof: what is projected is re-proved
    /// by contract object.
    /// </remarks>
    public string Key => AssemblyName + "|" + EntryPointType;

    internal VerifiedClientContract Contract { get; }
}

/// <summary>Everything this browser knows about one payload it was asked to project.</summary>
/// <param name="Outcome">What became of it.</param>
/// <param name="Address">The address it was requested from.</param>
/// <param name="AssemblyName">The assembly whose contract was offered the payload.</param>
/// <param name="EntryPointType">The declared entry point that was offered the payload.</param>
/// <param name="EntityTypeName">The entity type that contract declares.</param>
/// <param name="PayloadLength">The number of bytes received, when any were.</param>
/// <param name="Projection">The projection, when one was produced and proved.</param>
/// <param name="Failure">Why it was refused, when it was.</param>
/// <remarks>
/// The projection is present only under <see cref="ContractPayloadOutcome.Projected"/>; every other outcome
/// carries diagnostics and no values.
/// </remarks>
public sealed record ContractPayloadReport(
    ContractPayloadOutcome Outcome,
    string Address,
    string? AssemblyName,
    string? EntryPointType,
    string? EntityTypeName,
    int? PayloadLength,
    ProjectedEntity? Projection,
    string? Failure)
{
    /// <summary>Gets whether this report carries values a consumer may render.</summary>
    public bool IsProjected => Outcome == ContractPayloadOutcome.Projected && Projection is not null;
}
