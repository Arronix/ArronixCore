using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace Arronix.Abstractions.Http;

/// <summary>
/// Decides whether a server certificate that failed validation should nevertheless be accepted.
/// </summary>
/// <remarks>
/// <para>
/// Deployments that talk to services on a local network routinely face self-signed certificates. The
/// decision to accept one is an operator's, made once and applied everywhere, so it is a host-owned
/// policy — not something each caller opening a connection decides for itself.
/// </para>
/// <para>
/// The contract crosses the boundary because extensions open connections the outbound HTTP gateway does
/// not mediate — a mail notifier is the standard example — and those connections must honor the same
/// policy. Reaching into the HTTP stack for it, as the code this replaces did, made that a layering
/// accident rather than a contract.
/// </para>
/// <para>
/// The method is named for what a <see langword="true"/> result means. The name it replaces was a
/// mis-cased double negative that read as the opposite of its own behavior.
/// </para>
/// </remarks>
public interface ICertificateValidationPolicy
{
    /// <summary>
    /// Determines whether to accept a certificate the platform could not validate.
    /// </summary>
    /// <param name="requestUri">The endpoint being connected to.</param>
    /// <param name="certificate">The certificate the server presented, when there was one.</param>
    /// <param name="chain">The chain the platform built, when it built one.</param>
    /// <param name="sslPolicyErrors">What the platform objected to.</param>
    /// <returns>
    /// <see langword="true"/> to proceed with the connection. Implementations return
    /// <see langword="false"/> unless the operator has explicitly configured otherwise for that
    /// endpoint; a policy that accepts everything is indistinguishable from having no TLS at all.
    /// </returns>
    bool ShouldAccept(
        Uri requestUri,
        X509Certificate2? certificate,
        X509Chain? chain,
        SslPolicyErrors sslPolicyErrors);
}
