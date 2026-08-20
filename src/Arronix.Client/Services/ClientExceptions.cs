
using System.Net;
using Arronix.Abstractions.Errors;

namespace Arronix.Client.Services;

/// <summary>
/// The server could not be reached at all.
/// </summary>
/// <remarks>
/// Distinct from a request that was answered with a failure. Nothing the user did caused this and
/// nothing the user can do fixes it, so it is presented as a state of the application rather than as an
/// error against the thing they were trying to do.
/// </remarks>
public sealed class HostUnreachableException : ArronixException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="HostUnreachableException"/> class.
    /// </summary>
    public HostUnreachableException()
        : base("The Arronix server did not answer.")
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="HostUnreachableException"/> class.
    /// </summary>
    /// <param name="message">The message describing the failure.</param>
    public HostUnreachableException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="HostUnreachableException"/> class.
    /// </summary>
    /// <param name="message">The message describing the failure.</param>
    /// <param name="innerException">The failure that caused this one.</param>
    public HostUnreachableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// The server answered a request with a failure.
/// </summary>
public sealed class ApiRequestException : ArronixException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ApiRequestException"/> class.
    /// </summary>
    public ApiRequestException()
        : base("The request failed.")
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ApiRequestException"/> class.
    /// </summary>
    /// <param name="message">The message describing the failure.</param>
    public ApiRequestException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ApiRequestException"/> class.
    /// </summary>
    /// <param name="message">The message describing the failure.</param>
    /// <param name="innerException">The failure that caused this one.</param>
    public ApiRequestException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ApiRequestException"/> class.
    /// </summary>
    /// <param name="statusCode">The status the server answered with.</param>
    /// <param name="message">The message describing the failure.</param>
    public ApiRequestException(HttpStatusCode statusCode, string message)
        : base(message) => StatusCode = statusCode;

    /// <summary>
    /// Gets the status the server answered with.
    /// </summary>
    public HttpStatusCode StatusCode { get; } = HttpStatusCode.InternalServerError;
}
