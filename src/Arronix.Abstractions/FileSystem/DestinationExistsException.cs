using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace Arronix.Abstractions.FileSystem;

/// <summary>
/// Thrown when a transfer or rename would overwrite something that already exists and the caller did
/// not ask for that.
/// </summary>
/// <remarks>
/// It derives from <see cref="IOException"/> so that the ordinary <c>catch (IOException)</c> around
/// file work catches it. The legacy pair of near-identical exceptions this replaces derived straight
/// from <see cref="Exception"/>, so that catch block silently missed them.
/// </remarks>
[Experimental(ExperimentalContracts.FileSystem, UrlFormat = ExperimentalContracts.UrlFormat)]
public class DestinationExistsException : IOException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DestinationExistsException"/> class.
    /// </summary>
    public DestinationExistsException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DestinationExistsException"/> class.
    /// </summary>
    /// <param name="message">The message describing the collision.</param>
    public DestinationExistsException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DestinationExistsException"/> class.
    /// </summary>
    /// <param name="message">The message describing the collision.</param>
    /// <param name="innerException">The failure that caused this one.</param>
    public DestinationExistsException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    private DestinationExistsException(string message, string destinationPath)
        : base(message) => DestinationPath = destinationPath;

    /// <summary>
    /// Gets the path that already existed, or <see langword="null"/> when the thrower did not record it.
    /// </summary>
    public string? DestinationPath { get; }

    /// <summary>
    /// Creates an exception describing a collision at a known path.
    /// </summary>
    /// <param name="destinationPath">The path that already exists.</param>
    /// <returns>The exception to throw.</returns>
    public static DestinationExistsException ForPath(string destinationPath) =>
        new($"The destination already exists: '{destinationPath}'.", destinationPath);
}
