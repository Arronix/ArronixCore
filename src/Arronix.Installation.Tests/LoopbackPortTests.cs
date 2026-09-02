using System.Net;
using System.Net.Sockets;
using NUnit.Framework;

namespace Arronix.Installation.Tests;

/// <summary>An operator who named a port gets it or a refusal; one who named none gets the first free port.</summary>
[TestFixture]
internal sealed class LoopbackPortTests
{
    [Test]
    public void RequireReturnsAFreePortItWasGiven()
    {
        var port = FindFreePort();

        Assert.That(LoopbackPort.Require(port), Is.EqualTo(port));
    }

    [Test]
    public void RequireRefusesAPortSomethingElseAlreadyHolds()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0) { ExclusiveAddressUse = true };
        listener.Start();
        var busyPort = ((IPEndPoint)listener.LocalEndpoint).Port;

        try
        {
            Assert.That(() => LoopbackPort.Require(busyPort), Throws.TypeOf<InstallationException>());
        }
        finally
        {
            listener.Stop();
        }
    }

    [Test]
    public void ChooseSkipsAPortSomethingElseAlreadyHolds()
    {
        using var listener = new TcpListener(IPAddress.Loopback, LoopbackPort.DefaultPort) { ExclusiveAddressUse = true };

        try
        {
            listener.Start();
        }
        catch (SocketException)
        {
            Assert.Ignore("The default port is already in use by something outside this test.");
            return;
        }

        try
        {
            var chosen = LoopbackPort.Choose();

            Assert.Multiple(() =>
            {
                Assert.That(chosen, Is.Not.EqualTo(LoopbackPort.DefaultPort));
                Assert.That(LoopbackPort.IsFree(chosen), Is.True);
            });
        }
        finally
        {
            listener.Stop();
        }
    }

    [TestCase(0)]
    [TestCase(65536)]
    [TestCase(-1)]
    public void IsFreeRefusesAnOutOfRangePort(int port)
        => Assert.That(() => LoopbackPort.IsFree(port), Throws.TypeOf<System.ArgumentOutOfRangeException>());

    private static int FindFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
