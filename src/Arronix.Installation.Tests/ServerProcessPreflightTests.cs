using System;
using System.IO;
using Arronix.Common.Installation;
using NUnit.Framework;

namespace Arronix.Installation.Tests;

/// <summary>
/// Starting the installed server checks its own preflight condition before a process is ever spawned: the
/// entry assembly the manifest names must actually exist in the server folder.
/// </summary>
[TestFixture]
internal sealed class ServerProcessPreflightTests
{
    private string _root = string.Empty;

    [SetUp]
    public void SetUp() => _root = Path.Combine(Path.GetTempPath(), "arronix-server-preflight-" + Guid.NewGuid().ToString("N"));

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Test]
    public void StartRefusesWhenTheServerEntryAssemblyIsMissing()
    {
        var layout = InstallationLayout.At(_root);
        Directory.CreateDirectory(layout.ServerFolder);

        var manifest = new InstallationManifest(
            InstallationManifest.CurrentSchemaVersion,
            "test-sdk",
            "server",
            "Arronix.Api.dll",
            "client/wwwroot",
            "state/arronix.db",
            "package-state",
            []);

        // The preflight check happens before the SDK command is ever read, so a placeholder that is never
        // actually invoked is enough here.
        Assert.That(
            () => ServerProcess.Start(new DotNetCli("unused"), layout, manifest, 12345),
            Throws.TypeOf<InstallationException>().With.Message.Contains("no server at"));
    }
}
