namespace Arronix.Installation;

/// <summary>
/// A refusal this tool can state in one sentence.
/// </summary>
/// <remarks>
/// Anything thrown as one of these is a condition an operator can act on — a busy port, an unknown package,
/// a missing installation — and is reported as a message rather than a stack trace. Everything else is a
/// defect in this tool and is allowed to propagate with everything the runtime knows about it.
/// </remarks>
public sealed class InstallationException : Exception
{
    /// <summary>Creates the refusal.</summary>
    public InstallationException()
    {
    }

    /// <summary>Creates the refusal.</summary>
    /// <param name="message">What the operator has to change.</param>
    public InstallationException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the refusal.</summary>
    /// <param name="message">What the operator has to change.</param>
    /// <param name="innerException">The failure underneath it.</param>
    public InstallationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
