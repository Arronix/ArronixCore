
namespace Arronix.Abstractions.Providers;

/// <summary>
/// Everything one provider call needs that is not part of the call itself.
/// </summary>
/// <param name="Definition">The configured instance being called.</param>
/// <param name="Session">Per-definition scratch state.</param>
/// <param name="CorrelationId">The wider operation this call belongs to.</param>
/// <remarks>
/// <para>
/// Every provider method takes one. Providers are therefore <b>stateless</b>: there is no per-definition
/// state on the implementation, so there is nothing for two concurrent definitions to race over.
/// </para>
/// <para>
/// This is the direct fix for a surveyed pattern in which the active definition is assigned onto a
/// container-resolved singleton before each call. Under a unified host that races, and least-privilege
/// gating computed from "the current definition" would race with it. Here shared mutable per-definition
/// state is not discouraged; it is unrepresentable.
/// </para>
/// </remarks>
public readonly record struct ProviderInvocation(
    ProviderDefinition Definition,
    IProviderSessionStore Session,
    string CorrelationId);
