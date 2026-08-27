using System.IO;
using System.Linq;
using System.Net.Http;
using Arronix.Abstractions.Client;
using Arronix.Client.Diagnostics;

namespace Arronix.Client.Contracts;

/// <summary>
/// Reads one serialized entity through an admitted client contract and proves the projection it produces.
/// </summary>
/// <remarks>
/// Names no media kind: the contract is whichever one the caller chose from <see cref="Offers"/>, re-proved
/// against the current installation by object identity before and after the read. Nothing here changes the
/// installation report, and the result is returned rather than held.
/// </remarks>
internal sealed class ContractPayloadLoader
{
    private readonly HttpClient _http;
    private readonly MediaContractLoader _contracts;

    /// <summary>
    /// Initializes a new instance of the <see cref="ContractPayloadLoader"/> class.
    /// </summary>
    /// <param name="http">The connection to the host that served this client.</param>
    /// <param name="contracts">The contracts this page has admitted.</param>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    internal ContractPayloadLoader(HttpClient http, MediaContractLoader contracts)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(contracts);

        _http = http;
        _contracts = contracts;
    }

    /// <summary>Lists the admitted contracts a payload may be offered to, in published order.</summary>
    /// <returns>The offers, which is empty whenever the installation may not be projected.</returns>
    public IReadOnlyList<ContractPayloadOffer> Offers()
        => [.. _contracts.Admitted().Select(entry => new ContractPayloadOffer(entry.AssemblyName, entry.Contract))];

    /// <summary>
    /// Fetches one serialized entity and projects it through one admitted contract.
    /// </summary>
    /// <param name="offer">The contract to read the payload through.</param>
    /// <param name="address">Where to read it from, as a path on the host that served this client.</param>
    /// <param name="cancellationToken">Abandons the read.</param>
    /// <returns>What became of the payload.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="offer"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was signaled.</exception>
    /// <remarks>A signaled token propagates; every other failure is contained and reported.</remarks>
    public async Task<ContractPayloadReport> ProjectAsync(
        ContractPayloadOffer offer,
        string address,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(offer);

        cancellationToken.ThrowIfCancellationRequested();

        var requested = address ?? string.Empty;

        // An offer is a capability a caller kept, and what this page admits changes under it.
        if (!StillAdmitted(offer))
        {
            return Withdrawn(offer, requested, "nothing was fetched and nothing was read through it");
        }

        return await ProjectCoreAsync(offer, requested, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Drops a projection this page may no longer show, and keeps one it still may.
    /// </summary>
    /// <param name="held">A report a caller is rendering, or <see langword="null"/>.</param>
    /// <param name="source">The offer that report was projected through.</param>
    /// <returns>The same report, or a refusal carrying no values.</returns>
    /// <remarks>
    /// The offer is required because the test is object identity. A diagnostic is kept as it is: it says
    /// nothing about what is admitted now.
    /// </remarks>
    public ContractPayloadReport? Revalidate(ContractPayloadReport? held, ContractPayloadOffer? source)
        => held is not { IsProjected: true } || (source is not null && StillAdmitted(source))
            ? held
            : held with
            {
                Outcome = ContractPayloadOutcome.NoAdmittedContract,
                Projection = null,
                Failure = $"'{held.EntryPointType}' is no longer a contract this page admits, so what it "
                    + "projected is no longer shown.",
            };

    /// <summary>Whether the exact contract an offer names is one this page currently admits.</summary>
    private bool StillAdmitted(ContractPayloadOffer offer)
        => _contracts.Admitted().Any(entry => ReferenceEquals(entry.Contract, offer.Contract));

    private static ContractPayloadReport Withdrawn(ContractPayloadOffer offer, string requested, string what)
        => new(
            ContractPayloadOutcome.NoAdmittedContract,
            requested,
            offer.AssemblyName,
            offer.EntryPointType,
            offer.EntityTypeName,
            null,
            null,
            $"'{offer.EntryPointType}' is not a contract this page currently admits, so {what}.");

    /// <summary>Projects through the first offered contract, for a caller that holds exactly one.</summary>
    /// <param name="address">Where to read the payload from.</param>
    /// <param name="cancellationToken">Abandons the read.</param>
    /// <returns>What became of the payload.</returns>
    public async Task<ContractPayloadReport> ProjectAsync(
        string address,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Offers() is [var first, ..]
            ? await ProjectAsync(first, address, cancellationToken).ConfigureAwait(false)
            : new ContractPayloadReport(
                ContractPayloadOutcome.NoAdmittedContract,
                address ?? string.Empty,
                null,
                null,
                null,
                null,
                null,
                "This page holds no admitted client contract, so there is nothing to read a payload "
                + "through. An installation is projected only when every required assembly is resident.");
    }

    private async Task<ContractPayloadReport> ProjectCoreAsync(
        ContractPayloadOffer offer,
        string requested,
        CancellationToken cancellationToken)
    {
        var contract = offer.Contract;

        ContractPayloadReport Refused(ContractPayloadOutcome outcome, int? length, string failure)
            => new(outcome, requested, offer.AssemblyName, offer.EntryPointType, offer.EntityTypeName, length, null, failure);

        if (BrowserAddress.DescribeRequest(requested) is { } unsafeAddress)
        {
            return Refused(ContractPayloadOutcome.AddressUnsafe, null, "Nothing was fetched: " + unsafeAddress);
        }

        byte[] payload;

        try
        {
            payload = await ReadAsync(requested, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception failure) when (!ProcessFailure.IsFatal(failure))
        {
            return Refused(
                ContractPayloadOutcome.Unavailable,
                null,
                $"'{requested}' could not be read from this host: {failure.Message}");
        }

        // Asked again after the fetch: an installation can be withdrawn while a payload is in flight, and
        // deserializing through a contract this page has stopped admitting is the same mistake later.
        if (!StillAdmitted(offer))
        {
            return Withdrawn(offer, requested, "the bytes it fetched were not read through it");
        }

        object? entity;

        try
        {
            entity = contract.Deserialize(payload);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception failure) when (!ProcessFailure.IsFatal(failure))
        {
            return Refused(
                ContractPayloadOutcome.DeserializationFailed,
                payload.Length,
                $"'{offer.EntryPointType}' refused the {payload.Length} bytes at '{requested}': "
                + failure.Message);
        }

        if (entity is null)
        {
            return Refused(
                ContractPayloadOutcome.DeserializationFailed,
                payload.Length,
                $"'{offer.EntryPointType}' read the payload into nothing.");
        }

        // The exact type object, not a name: two assemblies can spell one type, and only one of them is the
        // one this page proved and bound.
        if (!ReferenceEquals(entity.GetType(), contract.EntityType))
        {
            return Refused(
                ContractPayloadOutcome.DeserializedTypeMismatch,
                payload.Length,
                $"'{offer.EntryPointType}' read the payload into a "
                + $"'{entity.GetType().AssemblyQualifiedName}' where it declares '{offer.EntityTypeName}'.");
        }

        ProjectedEntity? projection;

        try
        {
            projection = contract.Project(entity);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception failure) when (!ProcessFailure.IsFatal(failure))
        {
            return Refused(
                ContractPayloadOutcome.ProjectionFailed,
                payload.Length,
                $"'{offer.EntryPointType}' could not project the value it read: {failure.Message}");
        }

        // What is returned is what the proof captured, not the graph it was captured from: a contract's own
        // list may answer one way while it is checked and another way while it is drawn.
        if (ProjectionAudit.Describe(contract.EntityType, contract.Schema, projection, out var trusted)
            is { } defect)
        {
            return Refused(defect.Outcome, payload.Length, defect.Message);
        }

        // The last question, because withdrawal can land during the read, the projection or the proof, and
        // values are handed back only when the contract they came from is one this page still admits.
        if (!StillAdmitted(offer))
        {
            return Withdrawn(offer, requested, "what it projected is not shown");
        }

        return new ContractPayloadReport(
            ContractPayloadOutcome.Projected,
            requested,
            offer.AssemblyName,
            offer.EntryPointType,
            offer.EntityTypeName,
            payload.Length,
            trusted,
            null);
    }

    /// <summary>
    /// Reads at most one payload's worth of bytes, refusing a larger response before holding it.
    /// </summary>
    /// <remarks>
    /// A declared length past the limit is refused before a byte is read; an undeclared body is refused at
    /// the first byte past it, rather than buffered to be measured.
    /// </remarks>
    private async Task<byte[]> ReadAsync(string address, CancellationToken cancellationToken)
    {
        const int limit = ClientContractLimits.MaxPayloadBytes;

        using var response = await _http
            .GetAsync(address, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        if (response.Content.Headers.ContentLength is { } declared && declared > limit)
        {
            throw new InvalidOperationException(
                $"the host declares a {declared}-byte payload, past the {limit} bytes one entity is read as.");
        }

        using var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var held = new MemoryStream();
        var buffer = new byte[8192];

        while (true)
        {
            var read = await body.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);

            if (read == 0)
            {
                return held.ToArray();
            }

            if (held.Length + read > limit)
            {
                throw new InvalidOperationException(
                    $"the payload is longer than the {limit} bytes one entity is read as.");
            }

            held.Write(buffer, 0, read);
        }
    }
}
