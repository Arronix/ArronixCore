using System;
using System.Net;

namespace Arronix.Provider.Tmdb.Transport;

/// <summary>Thrown when a TMDb response body could not be read as the shape the request expected.</summary>
/// <remarks>
/// Raised only for a response TMDb answered with a success status: an error status is
/// <see cref="TmdbApiException"/> instead. Malformed-body and failed-status are different failure
/// categories, and a caller correcting one should not need to guess whether it also fixed the other.
/// </remarks>
public sealed class TmdbResponseFormatException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="operation">The TMDb operation that produced the response, for diagnostics.</param>
    /// <param name="reason">What was wrong with the body.</param>
    /// <param name="innerException">The parsing failure, when one was caught.</param>
    public TmdbResponseFormatException(string operation, string reason, Exception? innerException = null)
        : base($"TMDb '{operation}' response {reason}", innerException) => Operation = operation;

    /// <summary>Gets the TMDb operation that produced the malformed response.</summary>
    public string Operation { get; }
}

/// <summary>Thrown when TMDb answered a request with an error HTTP status.</summary>
public sealed class TmdbApiException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="operation">The TMDb operation that failed, for diagnostics.</param>
    /// <param name="statusCode">The HTTP status TMDb returned.</param>
    /// <param name="message">TMDb's own status message, when one could be read.</param>
    public TmdbApiException(string operation, HttpStatusCode statusCode, string message)
        : base($"TMDb '{operation}' failed with {(int)statusCode} {statusCode}: {message}")
    {
        Operation = operation;
        StatusCode = statusCode;
    }

    /// <summary>Gets the TMDb operation that failed.</summary>
    public string Operation { get; }

    /// <summary>Gets the HTTP status TMDb returned.</summary>
    public HttpStatusCode StatusCode { get; }
}
