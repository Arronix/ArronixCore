using NUnit.Framework;

namespace Arronix.Installation.Tests;

/// <summary>Parsing the arguments this tool accepts, independent of anything it does with them.</summary>
[TestFixture]
internal sealed class CommandLineTests
{
    [Test]
    public void NoArgumentsMeanRunWithDefaults()
    {
        var options = CommandLine.Parse([]);

        Assert.Multiple(() =>
        {
            Assert.That(options.Command, Is.EqualTo(InstallationCommand.Run));
            Assert.That(options.Root, Is.EqualTo(CommandLine.DefaultRoot));
            Assert.That(options.Port, Is.Null);
            Assert.That(options.Build, Is.True);
            Assert.That(options.Samples, Is.True);
            Assert.That(options.Packages, Is.Empty);
            Assert.That(options.ExternalPackages, Is.Empty);
            Assert.That(options.OpenBrowser, Is.False);
            Assert.That(options.ResetEverything, Is.False);
        });
    }

    [TestCase("run", InstallationCommand.Run)]
    [TestCase("install", InstallationCommand.Install)]
    [TestCase("reset", InstallationCommand.Reset)]
    [TestCase("help", InstallationCommand.Help)]
    public void TheFirstBareWordSelectsTheCommand(string word, InstallationCommand expected)
        => Assert.That(CommandLine.Parse([word]).Command, Is.EqualTo(expected));

    [Test]
    public void AnUnknownCommandIsRefused()
        => Assert.That(() => CommandLine.Parse(["frobnicate"]), Throws.TypeOf<InstallationException>());

    [Test]
    public void AnUnknownOptionIsRefused()
        => Assert.That(() => CommandLine.Parse(["--nonsense"]), Throws.TypeOf<InstallationException>());

    [Test]
    public void AnOptionMissingItsValueIsRefused()
        => Assert.That(() => CommandLine.Parse(["--root"]), Throws.TypeOf<InstallationException>());

    [TestCase("0")]
    [TestCase("65536")]
    [TestCase("not-a-number")]
    [TestCase("-1")]
    public void AnInvalidPortIsRefused(string port)
        => Assert.That(() => CommandLine.Parse(["--port", port]), Throws.TypeOf<InstallationException>());

    [Test]
    public void AValidPortIsAccepted()
        => Assert.That(CommandLine.Parse(["--port", "5300"]).Port, Is.EqualTo(5300));

    [Test]
    public void PackageIsRepeatable()
    {
        var options = CommandLine.Parse(["--package", "movies", "--package", "arronix.format.video"]);

        Assert.That(options.Packages, Is.EqualTo(new[] { "movies", "arronix.format.video" }));
    }

    [Test]
    public void ExternalPackageParsesIdAndProjectFile()
    {
        var options = CommandLine.Parse(["--external-package", "proof.fixture=/path/to/Fixture.csproj"]);

        Assert.Multiple(() =>
        {
            Assert.That(options.ExternalPackages, Has.Count.EqualTo(1));
            Assert.That(options.ExternalPackages[0].Id, Is.EqualTo("proof.fixture"));
            Assert.That(options.ExternalPackages[0].ProjectFile, Is.EqualTo("/path/to/Fixture.csproj"));
        });
    }

    [Test]
    public void ExternalPackageIsRepeatable()
    {
        var options = CommandLine.Parse(
        [
            "--external-package", "a=/proj/a.csproj",
            "--external-package", "b=/proj/b.csproj",
        ]);

        Assert.That(options.ExternalPackages, Has.Count.EqualTo(2));
    }

    [TestCase("no-equals-sign")]
    [TestCase("=/path/to/project.csproj")]
    [TestCase("id=")]
    public void AMalformedExternalPackageArgumentIsRefused(string argument)
        => Assert.That(
            () => CommandLine.Parse(["--external-package", argument]),
            Throws.TypeOf<InstallationException>());

    [Test]
    public void NoBuildDisablesComposing()
        => Assert.That(CommandLine.Parse(["--no-build"]).Build, Is.False);

    [Test]
    public void NoSampleCatalogDisablesSamples()
        => Assert.That(CommandLine.Parse(["--no-sample-catalog"]).Samples, Is.False);

    [Test]
    public void OpenSetsOpenBrowser()
        => Assert.That(CommandLine.Parse(["--open"]).OpenBrowser, Is.True);

    [Test]
    public void ResetAllSetsResetEverything()
        => Assert.That(CommandLine.Parse(["reset", "--all"]).ResetEverything, Is.True);

    [Test]
    public void HelpFlagOverridesAnEarlierCommand()
        => Assert.That(CommandLine.Parse(["install", "--help"]).Command, Is.EqualTo(InstallationCommand.Help));

    [Test]
    public void RootAcceptsAnArbitraryValue()
        => Assert.That(CommandLine.Parse(["--root", "some/where"]).Root, Is.EqualTo("some/where"));
}
