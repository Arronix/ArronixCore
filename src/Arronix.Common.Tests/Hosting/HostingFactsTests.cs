using System;
using Arronix.Abstractions.Hosting;
using Arronix.Common.Hosting;

namespace Arronix.Common.Tests.Hosting;

/// <summary>
/// What the platform reports about the process and the operating system, branch by branch.
/// </summary>
[TestFixture]
public class HostingFactsTests
{
    private static readonly IOsVersionProbe[] NoProbes = [];

    [Test]
    public void APodmanContainerIsReportedAsPodmanAndNotAsDocker()
    {
        var facts = PlatformFactsStub.Linux()
            .WithFile("/run/.containerenv")
            .WithFile("/.dockerenv")
            .WithEnvironment("container", "podman");

        var operatingSystem = new OperatingSystemInfo(NoProbes, facts);

        Assert.Multiple(() =>
        {
            Assert.That(operatingSystem.IsPodman, Is.True);
            Assert.That(operatingSystem.IsDocker, Is.False, "a Podman container carrying a Docker marker is Podman");
            Assert.That(operatingSystem.IsContainerized, Is.True);
        });
    }

    [Test]
    public void ADockerContainerIsReportedAsDocker()
    {
        var facts = PlatformFactsStub.Linux().WithFile("/.dockerenv");

        var operatingSystem = new OperatingSystemInfo(NoProbes, facts);

        Assert.Multiple(() =>
        {
            Assert.That(operatingSystem.IsDocker, Is.True);
            Assert.That(operatingSystem.IsPodman, Is.False);
            Assert.That(operatingSystem.IsContainerized, Is.True);
        });
    }

    [Test]
    public void AnUnidentifiedContainerIsContainerizedWithoutClaimingARuntime()
    {
        var facts = PlatformFactsStub.Linux()
            .WithFile("/proc/1/cgroup", "0::/kubepods/besteffort/podabc/def");

        var operatingSystem = new OperatingSystemInfo(NoProbes, facts);

        Assert.Multiple(() =>
        {
            Assert.That(operatingSystem.IsContainerized, Is.True);
            Assert.That(operatingSystem.IsDocker, Is.False);
            Assert.That(operatingSystem.IsPodman, Is.False);
        });
    }

    [Test]
    public void AContainerdCgroupIsContainerizedWithoutClaimingARuntimeEither()
    {
        var facts = PlatformFactsStub.Linux()
            .WithFile("/proc/1/cgroup", "0::/system.slice/containerd.service/abc");

        var operatingSystem = new OperatingSystemInfo(NoProbes, facts);

        Assert.Multiple(() =>
        {
            Assert.That(operatingSystem.IsContainerized, Is.True);
            Assert.That(operatingSystem.IsDocker, Is.False, "containerd does not say who asked it");
            Assert.That(operatingSystem.IsPodman, Is.False);
        });
    }

    /// <remarks>A marker that names its runtime is read as naming it, not merely as saying "a container".</remarks>
    [Test]
    public void ADockerCgroupIsReportedAsDocker()
    {
        var facts = PlatformFactsStub.Linux()
            .WithFile("/proc/1/cgroup", "0::/docker/9f2c1b");

        var operatingSystem = new OperatingSystemInfo(NoProbes, facts);

        Assert.Multiple(() =>
        {
            Assert.That(operatingSystem.IsDocker, Is.True);
            Assert.That(operatingSystem.IsPodman, Is.False);
            Assert.That(operatingSystem.IsContainerized, Is.True);
        });
    }

    [Test]
    public void ALibpodCgroupIsReportedAsPodmanEvenAlongsideADockerPath()
    {
        var facts = PlatformFactsStub.Linux()
            .WithFile("/proc/1/cgroup", "0::/machine.slice/libpod-9f2c1b.scope/docker");

        var operatingSystem = new OperatingSystemInfo(NoProbes, facts);

        Assert.Multiple(() =>
        {
            Assert.That(operatingSystem.IsPodman, Is.True, "Podman takes precedence wherever both appear");
            Assert.That(operatingSystem.IsDocker, Is.False);
            Assert.That(operatingSystem.IsContainerized, Is.True);
        });
    }

    [Test]
    public void AnOrdinaryHostIsNotContainerized()
    {
        var operatingSystem = new OperatingSystemInfo(NoProbes, PlatformFactsStub.Linux());

        Assert.Multiple(() =>
        {
            Assert.That(operatingSystem.IsContainerized, Is.False);
            Assert.That(operatingSystem.IsDocker, Is.False);
            Assert.That(operatingSystem.IsPodman, Is.False);
        });
    }

    [TestCase(true)]
    [TestCase(false)]
    public void TheTwoContractsAgreeAboutContainerization(bool containerized)
    {
        var facts = PlatformFactsStub.Linux();

        if (containerized)
        {
            facts.WithFile("/.dockerenv");
        }

        var operatingSystem = new OperatingSystemInfo(NoProbes, facts);
        var runtime = new HostRuntimeInfo(operatingSystem, new StubDetector(false), facts);

        Assert.That(runtime.IsContainerized, Is.EqualTo(operatingSystem.IsContainerized));
        Assert.That(runtime.IsContainerized, Is.EqualTo(containerized));
    }

    [Test]
    public void AnElevatedProcessIsReportedAsAdministrator()
    {
        var facts = PlatformFactsStub.Linux();
        facts.IsPrivilegedProcess = true;

        var runtime = Runtime(facts);

        Assert.That(runtime.IsAdministrator, Is.True);
    }

    [Test]
    public void TheServiceControlManagerAnswerIsTheDetectorsAndNotAGuessFromInteractivity()
    {
        var facts = PlatformFactsStub.Windows();
        facts.IsUserInteractive = false;

        Assert.Multiple(() =>
        {
            Assert.That(
                Runtime(facts, service: true).IsWindowsService,
                Is.True,
                "a registered detector is the authority");
            Assert.That(
                Runtime(facts, service: false).IsWindowsService,
                Is.False,
                "a non-interactive Windows process may be a scheduled task or a container entry point");
        });
    }

    [Test]
    public void AHostThatRegistersNoDetectorReportsNoService()
    {
        var facts = PlatformFactsStub.Windows();
        facts.IsUserInteractive = false;

        Assert.That(Runtime(facts).IsWindowsService, Is.False);
    }

    [Test]
    public void NoNonWindowsProcessIsAWindowsServiceEvenWhenADetectorSaysSo()
    {
        var facts = PlatformFactsStub.Linux();
        facts.IsUserInteractive = false;

        Assert.That(
            Runtime(facts, service: true).IsWindowsService,
            Is.False,
            "the contract's member is Windows-specific and must not be widened to mean 'started by something'");
    }

    [Test]
    public void AHostThatCannotReadItsProcessStartTimeSaysSoRatherThanSubstitutingOne()
    {
        var facts = PlatformFactsStub.Linux();
        facts.ProcessStartTime = null;

        var runtime = new HostRuntimeInfo(new OperatingSystemInfo(NoProbes, facts), new StubDetector(false), facts);

        Assert.That(
            runtime.StartTime,
            Is.Null,
            "composition time would be presented as an uptime the process never had");
    }

    [Test]
    public void TheProcessStartTimeIsReportedWhenThePlatformHasOne()
    {
        var started = new DateTimeOffset(2026, 8, 25, 9, 30, 0, TimeSpan.Zero);
        var facts = PlatformFactsStub.Linux();
        facts.ProcessStartTime = started;

        Assert.That(Runtime(facts, service: false).StartTime, Is.EqualTo(started));
    }

    [Test]
    public void TheExecutablePathIsReportedWhenThePlatformHasOne()
    {
        var facts = PlatformFactsStub.Linux();
        facts.ProcessPath = "/opt/arronix/Arronix.Api";

        Assert.That(Runtime(facts).ExecutingApplication, Is.EqualTo("/opt/arronix/Arronix.Api"));

        facts.ProcessPath = null;

        Assert.That(Runtime(facts).ExecutingApplication, Is.Null);
    }

    [Test]
    public void AnUnrecognizedPlatformIsNamedUnknownRatherThanGuessed()
    {
        var facts = new PlatformFactsStub { OperatingSystemDescription = "Something 9" };

        var operatingSystem = new OperatingSystemInfo(NoProbes, facts);

        Assert.Multiple(() =>
        {
            Assert.That(operatingSystem.Name, Is.EqualTo(OperatingSystemInfo.UnknownName));
            Assert.That(operatingSystem.Version, Is.EqualTo(OperatingSystemInfo.UnknownName));
            Assert.That(operatingSystem.FullName, Does.Contain("Something 9"));
        });
    }

    [Test]
    public void ALinuxHostWithNoReleaseFileIsNamedLinuxRatherThanADistribution()
    {
        var facts = PlatformFactsStub.Linux();
        facts.OperatingSystemVersion = new Version(6, 8, 0);

        var operatingSystem = new OperatingSystemInfo([new OsReleaseProbe(facts)], facts);

        Assert.Multiple(() =>
        {
            Assert.That(operatingSystem.Name, Is.EqualTo("Linux"));
            Assert.That(operatingSystem.Version, Is.EqualTo("6.8.0"));
        });
    }

    [Test]
    public void TheReleaseFileProbeReadsTheDistributionIdentity()
    {
        var facts = PlatformFactsStub.Linux().WithFile(
            "/etc/os-release",
            """
            # a comment
            NAME="Ubuntu"
            VERSION_ID="24.04"
            PRETTY_NAME="Ubuntu 24.04.1 LTS"
            ID=ubuntu
            malformed-line
            """);

        var operatingSystem = new OperatingSystemInfo([new OsReleaseProbe(facts)], facts);

        Assert.Multiple(() =>
        {
            Assert.That(operatingSystem.Name, Is.EqualTo("Ubuntu"));
            Assert.That(operatingSystem.Version, Is.EqualTo("24.04"));
            Assert.That(operatingSystem.FullName, Is.EqualTo("Ubuntu 24.04.1 LTS"));
        });
    }

    [Test]
    public void ProbesAreConsultedInRegistrationOrderAndTheFirstAnswerWins()
    {
        var facts = PlatformFactsStub.Linux();
        var unsupported = new StubProbe("Never", supported: false);
        var silent = new StubProbe("Silent", answers: false);
        var first = new StubProbe("First");
        var second = new StubProbe("Second");

        var operatingSystem = new OperatingSystemInfo([unsupported, silent, first, second], facts);

        Assert.Multiple(() =>
        {
            Assert.That(operatingSystem.Name, Is.EqualTo("First"));
            Assert.That(unsupported.Reads, Is.Zero, "an unsupported probe is never read");
            Assert.That(silent.Reads, Is.EqualTo(1));
            Assert.That(second.Reads, Is.Zero, "nothing is asked after an answer");
        });
    }

    [Test]
    public void AProbeThatThrowsInsteadOfAnsweringIsReportedWithItsIdentity()
    {
        var facts = PlatformFactsStub.Linux();

        var failure = Assert.Throws<InvalidOperationException>(
            () => new OperatingSystemInfo([new ThrowingProbe()], facts));

        Assert.Multiple(() =>
        {
            Assert.That(failure!.Message, Does.Contain(typeof(ThrowingProbe).FullName!));
            Assert.That(failure.InnerException, Is.TypeOf<NotSupportedException>());
        });
    }

    private static HostRuntimeInfo Runtime(IPlatformFacts facts, bool service = false)
        => new(new OperatingSystemInfo(NoProbes, facts), new StubDetector(service), facts);

    private sealed class StubDetector(bool isWindowsService) : IWindowsServiceDetector
    {
        public bool IsWindowsService => isWindowsService;
    }

    private sealed class StubProbe(string name, bool supported = true, bool answers = true) : IOsVersionProbe
    {
        internal int Reads { get; private set; }

        public bool IsSupported => supported;

        public OsVersionDescriptor? Read()
        {
            Reads++;
            return answers ? OsVersionDescriptor.Create(name, "1.0") : null;
        }
    }

    private sealed class ThrowingProbe : IOsVersionProbe
    {
        public bool IsSupported => true;

        public OsVersionDescriptor? Read() => throw new NotSupportedException("the release file was unreadable");
    }
}
