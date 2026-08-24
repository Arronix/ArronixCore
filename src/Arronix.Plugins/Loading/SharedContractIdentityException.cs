using Arronix.Abstractions.Errors;
using Arronix.Abstractions.Health;

namespace Arronix.Plugins.Loading;

/// <summary>
/// Thrown when a package asks for a shared contract assembly under an identity the installation did not
/// admit.
/// </summary>
/// <remarks>
/// <para>
/// The runtime performs no version check on an assembly a load context returns. Whatever
/// <see cref="System.Runtime.Loader.AssemblyLoadContext.Load(System.Reflection.AssemblyName)"/> hands back
/// is what the caller binds to, even if its name, version or public key token is not what the caller was
/// compiled against. Handing back a mismatched assembly would therefore succeed, silently, and produce a
/// <see cref="MissingMethodException"/> at some unrelated later moment.
/// </para>
/// <para>
/// So the check has to be Arronix's. The pre-load inspection makes the same comparison against every
/// package's reference table so the ordinary case fails before any code runs; this exception is the runtime
/// backstop for a request no reference table contained.
/// </para>
/// </remarks>
internal sealed class SharedContractIdentityException : ArronixException
{
    /// <summary>Initializes a new instance of the <see cref="SharedContractIdentityException"/> class.</summary>
    public SharedContractIdentityException()
        : base(
            CoreErrorCode.PluginContractMismatch,
            "A package asked for a shared contract assembly under an identity this installation did not admit.")
    {
    }

    /// <summary>Initializes a new instance of the <see cref="SharedContractIdentityException"/> class.</summary>
    /// <param name="message">The message describing the failure.</param>
    public SharedContractIdentityException(string message)
        : base(CoreErrorCode.PluginContractMismatch, message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="SharedContractIdentityException"/> class.</summary>
    /// <param name="message">The message describing the failure.</param>
    /// <param name="innerException">The failure that caused this one.</param>
    public SharedContractIdentityException(string message, Exception innerException)
        : base(CoreErrorCode.PluginContractMismatch, message, innerException)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SharedContractIdentityException"/> class naming both
    /// identities.
    /// </summary>
    /// <param name="requested">The identity that was asked for.</param>
    /// <param name="admitted">The identity this installation admitted.</param>
    /// <param name="requestedBy">Who asked.</param>
    public SharedContractIdentityException(string requested, string admitted, string requestedBy)
        : base(
            CoreErrorCode.PluginContractMismatch,
            $"'{requestedBy}' requires shared contract '{requested}', but this installation admitted '{admitted}'. "
            + "A shared contract binds by exact CLR assembly identity; a package version range cannot substitute for it.")
    {
        RequestedIdentity = requested;
        AdmittedIdentity = admitted;
        RequestedBy = requestedBy;
    }

    /// <summary>Gets the identity that was asked for.</summary>
    public string? RequestedIdentity { get; }

    /// <summary>Gets the identity this installation admitted.</summary>
    public string? AdmittedIdentity { get; }

    /// <summary>Gets what asked for it.</summary>
    public string? RequestedBy { get; }
}
