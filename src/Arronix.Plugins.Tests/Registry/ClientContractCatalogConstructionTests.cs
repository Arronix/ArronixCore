using System.Linq;
using System.Reflection;
using Arronix.Plugins.Registry;


namespace Arronix.Plugins.Tests.Registry;

/// <summary>
/// The catalog cannot be given a publication boundary that guards something else.
/// </summary>
/// <remarks>
/// Structural rather than behavioural: a read under the wrong gate looks synchronised and is not, and no
/// assertion about one read can show that the gate was the right one. Removing the second constructor
/// parameter is what makes the mismatch unconstructable, so that is what is asserted.
/// </remarks>
[TestFixture]
public sealed class ClientContractCatalogConstructionTests
{
    [Test]
    public void TheCatalogTakesOnlyTheRegistryAndDerivesItsGateFromIt()
    {
        var constructors = typeof(ClientContractCatalog)
            .GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.Multiple(() =>
        {
            Assert.That(constructors, Has.Length.EqualTo(1));
            Assert.That(
                constructors[0].GetParameters().Select(parameter => parameter.ParameterType),
                Is.EqualTo(new[] { typeof(PluginRuntimeRegistry) }),
                "a catalog that could be handed its own gate could be handed one guarding another registry");
        });
    }

    [Test]
    public void TheGateItReadsUnderIsTheRegistrysOwn()
    {
        var publication = new PluginPublicationGate();
        var registry = new PluginRuntimeRegistry(publication);
        var catalog = new ClientContractCatalog(registry);

        Assert.Multiple(() =>
        {
            Assert.That(catalog.PublicationGate, Is.SameAs(publication));
            Assert.That(catalog.PublicationGate, Is.SameAs(registry.PublicationGate));
            Assert.That(catalog.UsesRuntime(registry), Is.True);
            Assert.That(catalog.UsesRuntime(new PluginRuntimeRegistry()), Is.False);
        });
    }
}
