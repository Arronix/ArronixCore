using System.Net;
using System.Net.Sockets;

namespace Arronix.Installation;

/// <summary>
/// Choosing the loopback port this run will own.
/// </summary>
/// <remarks>
/// <para>
/// There are exactly two honest outcomes and this type produces only those. An operator who named a port
/// gets that port or a refusal; nothing silently moves a service somebody was going to connect to. An
/// operator who named none gets the first free port in a small stated range, so a second Arronix, a stale
/// process, or an unrelated service already on the usual port is an inconvenience rather than a failure.
/// </para>
/// <para>
/// A port is tested by binding it, not by connecting to it. Connecting says only that nothing answered a
/// moment ago; binding is the same question the server is about to ask, and asking it with exclusive use
/// on means a port another socket merely shares is reported busy rather than handed over.
/// </para>
/// </remarks>
internal static class LoopbackPort
{
    /// <summary>The first port considered when none was named.</summary>
    public const int DefaultPort = 5227;

    /// <summary>How many consecutive ports are considered before giving up.</summary>
    public const int SearchWidth = 32;

    /// <summary>
    /// Takes the port an operator named, or refuses.
    /// </summary>
    /// <param name="port">The requested port.</param>
    /// <returns>The same port.</returns>
    /// <exception cref="InstallationException">Something already holds that port.</exception>
    public static int Require(int port)
        => IsFree(port)
            ? port
            : throw new InstallationException(
                $"Port {port} on 127.0.0.1 is already in use. This run only ever binds the port it was "
                + "given and never signals a process it did not start, so it stops here. Choose another "
                + "port, or stop whatever holds that one.");

    /// <summary>
    /// Finds the first free port at or after the default.
    /// </summary>
    /// <returns>The port.</returns>
    /// <exception cref="InstallationException">Every candidate is busy.</exception>
    public static int Choose()
    {
        for (var port = DefaultPort; port < DefaultPort + SearchWidth; port++)
        {
            if (IsFree(port))
            {
                return port;
            }
        }

        throw new InstallationException(
            $"Every port from {DefaultPort} to {DefaultPort + SearchWidth - 1} on 127.0.0.1 is in use. "
            + "Name a free one with --port.");
    }

    /// <summary>Determines whether a port can be bound on the loopback interface right now.</summary>
    /// <param name="port">The port.</param>
    /// <returns><see langword="true"/> when it is free.</returns>
    public static bool IsFree(int port)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(port, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, 65535);

        var listener = new TcpListener(IPAddress.Loopback, port) { ExclusiveAddressUse = true };

        try
        {
            listener.Start();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
        finally
        {
            listener.Dispose();
        }
    }
}
