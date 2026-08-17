using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Arronix.Abstractions.Health;

namespace Arronix.Abstractions.Providers;

/// <summary>
/// The result of validating a provider definition or testing a provider's connection.
/// </summary>
/// <remarks>
/// A first-party type rather than a validation library's. The contract layer takes no package references
/// and must keep it that way: an extension compiled against one version of a validation library cannot
/// negotiate with a host built against the next, and baking a third-party type into a contract makes that
/// mismatch unresolvable rather than merely inconvenient.
/// </remarks>
[Experimental(ExperimentalContracts.Providers, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed record ValidationOutcome
{
    /// <summary>
    /// Gets the outcome carrying no failures.
    /// </summary>
    public static ValidationOutcome Success { get; } = new() { Failures = [] };

    /// <summary>
    /// Gets the failures, which may include warnings that do not invalidate the subject.
    /// </summary>
    public required IReadOnlyList<ValidationFailure> Failures { get; init; }

    /// <summary>
    /// Gets a value indicating whether the subject is usable — that is, whether no failure carries
    /// <see cref="ValidationSeverity.Error"/>.
    /// </summary>
    public bool IsValid => !Failures.Any(failure => failure.Severity == ValidationSeverity.Error);

    /// <summary>
    /// Creates a failed outcome.
    /// </summary>
    /// <param name="failures">The failures.</param>
    /// <returns>The outcome.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="failures"/> is <see langword="null"/>.</exception>
    public static ValidationOutcome Failed(params ValidationFailure[] failures)
    {
        ArgumentNullException.ThrowIfNull(failures);
        return new ValidationOutcome { Failures = failures };
    }

    /// <summary>
    /// Returns an outcome carrying this outcome's failures followed by another's.
    /// </summary>
    /// <param name="other">The outcome to append.</param>
    /// <returns>The combined outcome.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="other"/> is <see langword="null"/>.</exception>
    public ValidationOutcome Concat(ValidationOutcome other)
    {
        ArgumentNullException.ThrowIfNull(other);

        if (Failures.Count == 0)
        {
            return other;
        }

        return other.Failures.Count == 0
            ? this
            : new ValidationOutcome { Failures = [.. Failures, .. other.Failures] };
    }
}

/// <summary>
/// One thing wrong with a provider definition.
/// </summary>
/// <param name="FieldId">The setting at fault, or <see langword="null"/> when the definition as a whole is.</param>
/// <param name="Message">What is wrong, in a sentence an operator can act on.</param>
/// <param name="Severity">Whether the definition is unusable or merely questionable.</param>
/// <param name="Code">The machine-readable code, so a caller need not match on message text.</param>
[Experimental(ExperimentalContracts.Providers, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed record ValidationFailure(
    string? FieldId,
    string Message,
    ValidationSeverity Severity = ValidationSeverity.Error,
    CoreErrorCode Code = CoreErrorCode.InvalidConfiguration);

/// <summary>
/// Whether a validation failure invalidates its subject.
/// </summary>
[Experimental(ExperimentalContracts.Providers, UrlFormat = ExperimentalContracts.UrlFormat)]
public enum ValidationSeverity
{
    /// <summary>The subject is usable, but something about it is questionable.</summary>
    Warning = 0,

    /// <summary>The subject is unusable.</summary>
    Error = 1
}
