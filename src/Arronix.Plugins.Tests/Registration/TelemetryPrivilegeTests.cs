using System.Linq;
using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Telemetry;
using Arronix.Plugins.Registration;
using FluentAssertions.Execution;

namespace Arronix.Plugins.Tests.Registration;

/// <summary>
/// Two telemetry privileges, and what each one is worth.
/// </summary>
/// <remarks>
/// A sink reads the whole post-redaction stream — every extension's events and the host's — so it is
/// granted by an operator as well as declared by the package, and it implies the network privilege because
/// reading everything and being able to send it are one decision. Shaping your own telemetry is neither of
/// those things: the seam offers an enricher or a filter only the events its own extension raised and
/// exposes no sink, so it grants and implies neither privilege. Handing out the first to get the second
/// would give away the process's diagnostics.
/// </remarks>
[TestFixture]
public sealed class TelemetryPrivilegeTests
{
    private static readonly PluginId Plugin = PluginId.FromString("test.telemetry");

    [Test]
    public void ShapingYourOwnTelemetryNeedsOnlyItsOwnPrivilege()
    {
        var (registry, ledger) = Create(Capability.TelemetryProcessing);

        registry
            .AddTelemetryEnricher(new StubEnricher())
            .AddTelemetryEventFilter(new StubFilter());

        using var assertions = new AssertionScope();
        ledger.Count.Should().Be(2);
        ledger.SatisfiedCapabilities.Has(Capability.TelemetryProcessing).Should().BeTrue();
        CapabilitySet.Of(Capability.TelemetryProcessing).WithImplied()
            .Has(Capability.Network).Should().BeFalse("the seam it grants exposes no sink to send them to");
    }

    [Test]
    public void ShapingYourOwnTelemetryDoesNotBuyTheWholeStream()
    {
        var (registry, ledger) = Create(Capability.TelemetryProcessing);

        var register = () => registry.AddTelemetrySink(new StubSink());

        using var assertions = new AssertionScope();
        var failure = register.Should().Throw<PluginCapabilityException>().Which;
        failure.Required.Should().Be(Capability.TelemetrySink);
        failure.Message.Should().Contain(CapabilityNames.TelemetrySink);
        ledger.Count.Should().Be(0);
    }

    [Test]
    public void ReadingTheWholeStreamDoesNotBuyTheOtherOneEither()
    {
        var (registry, _) = Create(Capability.TelemetrySink);

        var register = () => registry.AddTelemetryEnricher(new StubEnricher());

        register.Should().Throw<PluginCapabilityException>()
            .Which.Required.Should().Be(Capability.TelemetryProcessing);
    }

    [Test]
    public void ReadingTheWholeStreamStillImpliesTheNetwork()
        => CapabilitySet.Of(Capability.TelemetrySink).WithImplied()
            .Has(Capability.Network).Should().BeTrue();

    /// <remarks>The manifest spelling is part of the contract: a renamed wire name is a broken manifest.</remarks>
    [Test]
    public void TheTwoPrivilegesAreSpelledApartOnTheWire()
    {
        using var assertions = new AssertionScope();
        CapabilityNames.ToWireName(Capability.TelemetryProcessing).Should().Be("telemetry-processing");
        CapabilityNames.ToWireName(Capability.TelemetrySink).Should().Be("telemetry-sink");
        CapabilityNames.TryParse("telemetry-processing", out var read).Should().BeTrue();
        read.Should().Be(Capability.TelemetryProcessing);
    }

    [Test]
    public void TheSetHoldsEveryDeclaredPrivilegeAtOnce()
    {
        var everything = CapabilitySet.Of([.. Enum.GetValues<Capability>()]);

        using var assertions = new AssertionScope();
        everything.Enumerate().Should().Equal(Enum.GetValues<Capability>().Order());
        everything.Has(Capability.TelemetryProcessing).Should()
            .BeTrue("a set that cannot hold the newest privilege has run out of room");
    }

    /// <remarks>
    /// C# masks a shift count, so an undeclared value is not merely meaningless: ordinal 32 would set the
    /// bit of ordinal 0. Both the constructor and the gate's own question are held to the exact vocabulary.
    /// </remarks>
    [TestCase(-1)]
    [TestCase(32)]
    [TestCase(33)]
    [TestCase(int.MaxValue)]
    public void APrivilegeTheVocabularyDoesNotDeclareIsRefusedAndNeverGranted(int ordinal)
    {
        var undeclared = (Capability)ordinal;

        using var assertions = new AssertionScope();

        var create = () => CapabilitySet.Of(undeclared);
        create.Should().Throw<ArgumentOutOfRangeException>();

        CapabilitySet.Of(Capability.Indexing).Has(undeclared).Should()
            .BeFalse("a privilege that does not exist is not one this set contains");
    }

    private static (PluginRegistry Registry, PluginRegistrationLedger Ledger) Create(params Capability[] granted)
    {
        var ledger = new PluginRegistrationLedger(Plugin);
        return (new PluginRegistry(Plugin, CapabilitySet.Of(granted), ledger), ledger);
    }

    private sealed class StubEnricher : ITelemetryEnricher
    {
        public TelemetryEvent Enrich(TelemetryEvent telemetryEvent) => telemetryEvent;
    }

    private sealed class StubFilter : ITelemetryEventFilter
    {
        public bool ShouldSend(TelemetryEvent telemetryEvent) => true;
    }

    private sealed class StubSink : ITelemetrySink
    {
        public string SinkId => "stub";

        public Task SendAsync(TelemetryEvent telemetryEvent, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task FlushAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
