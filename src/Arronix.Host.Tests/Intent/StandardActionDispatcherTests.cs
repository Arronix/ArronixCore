using System.Linq;
using Arronix.Abstractions.Intent;
using Arronix.Abstractions.Shape;
using Arronix.Host.Intent;
using Arronix.Host.Storage;
using Arronix.Host.Tests.Support;
using FluentAssertions;


namespace Arronix.Host.Tests.Intent;

/// <summary>The standard action seam executes only capabilities the host genuinely owns.</summary>
[TestFixture]
internal sealed class StandardActionDispatcherTests
{
    [Test]
    public async Task SetMonitoringWritesTheHostOwnedLibraryFacet()
    {
        var store = new InMemoryMediaStore(
            TestOptions.RegistryWith(ContributionFixtures.For(ShapeFixtures.Fused())));
        var dispatcher = new StandardActionDispatcher(store, TimeProvider.System);
        var item = ShapeFixtures.Item(ShapeFixtures.Catalog, 42);

        var action = Descriptor(StandardMediaAction.SetMonitoring);
        var wantedParameter = action.Parameters.Single(parameter =>
            parameter.StandardParameter is StandardMediaActionParameter.Wanted);
        var result = await dispatcher.TryDispatchAsync(
            action,
            [item],
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [wantedParameter.ParameterId] = "true"
            });

        var stored = await store.FindLibraryAsync(item);

        Assert.Multiple(() =>
        {
            result.Should().NotBeNull();
            result!.Accepted.Should().BeTrue();
            stored.Should().NotBeNull();
            stored!.Monitor.Should().Contain("wanted", "true");
            stored.AddedAt.Should().NotBeNull();
        });
    }

    [Test]
    public async Task AnOperationWithoutAnExecutionCapabilityIsNotAccepted()
    {
        var store = new InMemoryMediaStore(
            TestOptions.RegistryWith(ContributionFixtures.For(ShapeFixtures.Fused())));
        var dispatcher = new StandardActionDispatcher(store, TimeProvider.System);

        var result = await dispatcher.TryDispatchAsync(
            Descriptor(StandardMediaAction.Search),
            [ShapeFixtures.Item(ShapeFixtures.Catalog, 42)],
            new Dictionary<string, string>(StringComparer.Ordinal));

        result.Should().BeNull();
    }

    private static ActionDescriptor Descriptor(StandardMediaAction action) => new()
    {
        StandardAction = action,
        ActionId = StandardActionIds.For(action),
        Name = action.ToString(),
        Scope = ActionScope.Selection,
        Consequence = Consequence.Safe,
        Confirmation = ConfirmationRequirement.None,
        Parameters = action is StandardMediaAction.SetMonitoring
            ?
            [
                new ActionParameter("wanted", "Wanted", FieldValueKind.Boolean, true, [])
                {
                    StandardParameter = StandardMediaActionParameter.Wanted
                }
            ]
            : []
    };
}
