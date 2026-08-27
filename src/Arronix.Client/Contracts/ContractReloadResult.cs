namespace Arronix.Client.Contracts;

/// <summary>What one contract transaction did, sealed under the lease that did it.</summary>
/// <param name="Sequence">Its position in the one order every transaction is granted, from one.</param>
/// <param name="Report">The installation it left this page describing, or <see langword="null"/> before the first.</param>
/// <param name="StoredKeys">The content hashes this browser held when it finished.</param>
/// <param name="Failures">Every failure it contained, in the order they happened.</param>
/// <remarks>
/// One value for one moment. The keys are read inside the lease, after the sweep, so nothing here pairs an
/// installation with a store another transaction has since changed.
/// </remarks>
internal sealed record ContractReloadResult(
    long Sequence,
    ContractLoadReport? Report,
    IReadOnlyList<string> StoredKeys,
    IReadOnlyList<ContractFailure> Failures)
{
    /// <summary>Gets what a view shows before any transaction has run.</summary>
    internal static ContractReloadResult None { get; } = new(0, null, [], []);
}
