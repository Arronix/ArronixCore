using System.Diagnostics.CodeAnalysis;
using Arronix.Abstractions.Health;
using Arronix.Abstractions.Providers;
using Arronix.Common.Diagnostics;
using Arronix.Host.Media;
using Arronix.Abstractions.Shape;


namespace Arronix.Host.Providers;

/// <summary>
/// Tries a configured provider and reports what happened, without letting the attempt escape.
/// </summary>
/// <remarks>
/// Testing a provider is the one operation whose whole purpose is to fail informatively, so every failure
/// becomes a validation result rather than an exception. An operator who has just typed a wrong password
/// needs a sentence, not a stack trace, and the caller — an endpoint the operator is waiting on — must not
/// have to distinguish "the provider said no" from "the provider threw".
/// </remarks>
/// <param name="providers">Where implementations are looked up.</param>
/// <param name="definitions">Where configured definitions are read from.</param>
/// <param name="sessions">The scratch space handed to the provider.</param>
/// <param name="status">Where the outcome is recorded.</param>
public sealed class ProviderTestService(
    ProviderRegistry providers,
    ProviderDefinitionStore definitions,
    ProviderSessionStore sessions,
    ProviderStatusStore status)
{
    private readonly ProviderRegistry _providers = providers ?? throw new ArgumentNullException(nameof(providers));
    private readonly ProviderDefinitionStore _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
    private readonly ProviderSessionStore _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
    private readonly ProviderStatusStore _status = status ?? throw new ArgumentNullException(nameof(status));

    /// <summary>
    /// Builds the invocation a provider call needs.
    /// </summary>
    /// <param name="definition">The configured definition.</param>
    /// <param name="correlationId">The wider operation; one is minted when absent.</param>
    /// <returns>The invocation.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="definition"/> is <see langword="null"/>.</exception>
    public ProviderInvocation Invocation(ProviderDefinition definition, string? correlationId = null)
    {
        ArgumentNullException.ThrowIfNull(definition);

        return new ProviderInvocation(
            definition,
            _sessions.For(definition.Id),
            correlationId ?? CorrelationContext.Current ?? CorrelationContext.New());
    }

    /// <summary>
    /// Tests one configured definition.
    /// </summary>
    /// <param name="definitionId">The definition.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>What was wrong, or a success.</returns>
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "A provider is third-party code and the whole purpose of a test call is to report its failure to an operator; letting the exception escape would turn a wrong password into a server error.")]
    public async Task<ValidationOutcome> TestAsync(int definitionId, CancellationToken cancellationToken = default)
    {
        var definition = _definitions.Find(definitionId);

        if (definition is null)
        {
            return ValidationOutcome.Failed(new ValidationFailure(
                null,
                $"No provider definition {definitionId} exists.",
                ValidationSeverity.Error,
                CoreErrorCode.InvalidConfiguration));
        }

        if (!_providers.TryLease(definition.Provider, out var leased))
        {
            return ValidationOutcome.Failed(new ValidationFailure(
                null,
                $"The extension providing '{definition.Provider}' is not loaded, so this definition cannot be tested. Its settings are kept.",
                ValidationSeverity.Error,
                CoreErrorCode.PluginDisabled));
        }

        // Held from here, because the descriptor read below and the test call after it are both against
        // the leased provider.
        using var held = leased;
        var missing = MissingRequiredSettings(held.Value.Descriptor, definition);

        if (missing.Count > 0)
        {
            return new ValidationOutcome { Failures = missing };
        }

        try
        {
            // Copied inside the lease: the outcome's failure list is the provider's own collection and is
            // read after the call returns.
            var outcome = PluginBoundary.Snapshot(
                await held.Value.Provider
                    .TestAsync(Invocation(definition), cancellationToken)
                    .ConfigureAwait(false));

            if (outcome.IsValid)
            {
                _status.RecordSuccess(definitionId);
            }
            else
            {
                _status.RecordConnectionFailure(definitionId);
            }

            return outcome;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception failure)
        {
            _status.RecordFailure(definitionId);

            return ValidationOutcome.Failed(new ValidationFailure(
                null,
                failure.Message,
                ValidationSeverity.Error,
                CoreErrorCode.InvalidConfiguration));
        }
    }

    /// <summary>
    /// Asks a provider for the values of a dynamic option set.
    /// </summary>
    /// <param name="definitionId">The definition.</param>
    /// <param name="optionSourceId">Which option set.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The values, or an empty list when the provider cannot supply them.</returns>
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "An option set that cannot be fetched degrades to a free-text field; a provider's failure here must not fail the form the operator is filling in.")]
    public async Task<IReadOnlyList<FacetValue>> OptionsAsync(
        int definitionId,
        string optionSourceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(optionSourceId);

        var definition = _definitions.Find(definitionId);

        if (definition is null || !_providers.TryLease(definition.Provider, out var leased))
        {
            return [];
        }

        try
        {
            using var held = leased;

            // Copied inside the lease, so the provider's own list is not enumerated after its ticket ends.
            return PluginBoundary.Snapshot(
                await held.Value.Provider
                    .GetOptionsAsync(Invocation(definition), optionSourceId, cancellationToken)
                    .ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return [];
        }
    }

    private static IReadOnlyList<ValidationFailure> MissingRequiredSettings(
        ProviderDescriptor descriptor,
        ProviderDefinition definition)
    {
        var failures = new List<ValidationFailure>();

        foreach (var field in descriptor.Settings)
        {
            if (!field.Required)
            {
                continue;
            }

            if (!definition.Settings.TryGetValue(field.FieldId, out var value) || string.IsNullOrWhiteSpace(value))
            {
                failures.Add(new ValidationFailure(
                    field.FieldId,
                    $"'{field.Name}' is required.",
                    ValidationSeverity.Error,
                    CoreErrorCode.MissingConfiguration));
            }
        }

        return failures;
    }
}
