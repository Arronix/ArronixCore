using Arronix.Abstractions.Health;

namespace Arronix.Abstractions.Errors;

/// <summary>
/// Base type for platform failures that carry a machine-readable <see cref="CoreErrorCode"/>
/// alongside the human-readable message.
/// </summary>
/// <remarks>
/// <para>
/// This type lives in the contract layer so plugins can throw and catch platform failures without
/// referencing any implementation assembly. It derives from <see cref="Exception"/> rather than
/// <c>ApplicationException</c>, which the framework has discouraged since .NET Framework 2.0.
/// </para>
/// <para>
/// There is deliberately no constructor overload taking a format string and arguments: formatting
/// inside an exception constructor turns an unbalanced brace in a file name or URL into a
/// <see cref="FormatException"/> thrown from the failure path itself. Callers interpolate first.
/// </para>
/// </remarks>
public class ArronixException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ArronixException"/> class with
    /// <see cref="CoreErrorCode.Unknown"/>.
    /// </summary>
    public ArronixException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ArronixException"/> class with
    /// <see cref="CoreErrorCode.Unknown"/>.
    /// </summary>
    /// <param name="message">The message describing the failure.</param>
    public ArronixException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ArronixException"/> class with
    /// <see cref="CoreErrorCode.Unknown"/>.
    /// </summary>
    /// <param name="message">The message describing the failure.</param>
    /// <param name="innerException">The failure that caused this one.</param>
    public ArronixException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ArronixException"/> class.
    /// </summary>
    /// <param name="errorCode">The machine-readable code identifying the failure.</param>
    /// <param name="message">The message describing the failure.</param>
    public ArronixException(CoreErrorCode errorCode, string message)
        : base(message) => ErrorCode = errorCode;

    /// <summary>
    /// Initializes a new instance of the <see cref="ArronixException"/> class.
    /// </summary>
    /// <param name="errorCode">The machine-readable code identifying the failure.</param>
    /// <param name="message">The message describing the failure.</param>
    /// <param name="innerException">The failure that caused this one.</param>
    public ArronixException(CoreErrorCode errorCode, string message, Exception innerException)
        : base(message, innerException) => ErrorCode = errorCode;

    /// <summary>
    /// Gets the machine-readable code identifying the failure. Consumers switch on this rather than
    /// on the exception's concrete type or its message text.
    /// </summary>
    public CoreErrorCode ErrorCode { get; } = CoreErrorCode.Unknown;
}
