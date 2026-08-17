using System.Linq;
using System.Reflection;
using Arronix.Abstractions.Media;
using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Scheduling;
using Arronix.Plugins.Registration;

#pragma warning disable ARX0006 // Health contracts are experimental; these tests reflect over them.
#pragma warning disable ARX0009 // Naming contracts are experimental; these tests reflect over them.
#pragma warning disable ARX0016 // Intent contracts are experimental; these tests reflect over them.
#pragma warning disable ARX0014 // The extension model is experimental; these tests exercise it.

#pragma warning disable ARX0020 // The typed media surface is experimental; this rule covers it.

namespace Arronix.Plugins.Tests.Registration;

/// <summary>
/// The table and the registration surface must agree, in both directions.
/// </summary>
/// <remarks>
/// A gated contract with no way to register it is a rule nothing can break, and a registration method with
/// no row in the table is a hole in the privilege model. Neither is visible by reading either file alone,
/// which is why the agreement is asserted rather than reviewed.
/// </remarks>
[TestFixture]
public sealed class CapabilityMatrixTests
{
    private static IEnumerable<(MethodInfo Method, Type Contract)> RegistrationMethods()
        => typeof(IPluginRegistry)
            .GetMethods()
            .Select(method => (Method: method, Contract: ContractOf(method)));

    /// <summary>
    /// The contract a registration method registers.
    /// </summary>
    /// <param name="method">The method.</param>
    /// <returns>The contract type.</returns>
    /// <remarks>
    /// Almost every registration method names its contract in its first parameter. Exactly one does not: a
    /// typed media kind's declaration <i>is</i> its type arguments, so nothing is passed and the contract is
    /// the captured form the registry records. That exception is listed rather than inferred, and a
    /// parameterless registration method nobody listed fails here rather than vanishing from every rule
    /// below — which is the failure mode this method exists to prevent.
    /// </remarks>
    private static Type ContractOf(MethodInfo method)
    {
        var parameters = method.GetParameters();

        if (parameters.Length > 0)
        {
            return Normalize(parameters[0].ParameterType);
        }

        return ParameterlessContracts.TryGetValue(method.Name, out var contract)
            ? contract
            : throw new InvalidOperationException(
                $"'{method.Name}' registers something without being passed it, and no contract is recorded "
                + "for it here. Add its captured form so the matrix rules keep covering it.");
    }

    private static readonly Dictionary<string, Type> ParameterlessContracts = new(StringComparer.Ordinal)
    {
        [nameof(IPluginRegistry.AddMediaType)] = typeof(IMediaTypeRegistration),
    };

    private static Type Normalize(Type type)
        => type.IsGenericType ? type.GetGenericTypeDefinition() : type;

    [Test]
    public void EveryRegistrationMethodHasARowInExactlyOneTable()
    {
        foreach (var (method, contract) in RegistrationMethods())
        {
            var gated = CapabilityMatrix.RegistrationRequirements.ContainsKey(contract);
            var ungated = CapabilityMatrix.UngatedContracts.Contains(contract);

            (gated ^ ungated).Should().BeTrue(
                $"'{method.Name}' registers '{contract.Name}', which must be listed as gated or as ungated and never as both or neither");
        }
    }

    [Test]
    public void EveryGatedRegistrationContractHasARegistrationMethod()
    {
        var registrable = RegistrationMethods().Select(entry => entry.Contract).ToHashSet();

        foreach (var contract in CapabilityMatrix.RegistrationRequirements.Keys)
        {
            registrable.Should().Contain(
                contract,
                $"'{contract.Name}' is gated as a registration but nothing can register it");
        }
    }

    [Test]
    public void NoDependencyContractIsAlsoRegistrable()
    {
        var registrable = RegistrationMethods().Select(entry => entry.Contract).ToHashSet();

        foreach (var contract in CapabilityMatrix.DependencyRequirements.Keys)
        {
            registrable.Should().NotContain(
                contract,
                $"'{contract.Name}' is reached for through the context, so treating it as a registration would make the forward check ask for something that can never exist");
        }
    }

    [Test]
    public void NoContractIsGatedTwice()
    {
        var overlap = CapabilityMatrix.RegistrationRequirements.Keys
            .Intersect(CapabilityMatrix.DependencyRequirements.Keys);

        overlap.Should().BeEmpty();
    }

    [Test]
    public void EveryGatedRowNamesAtLeastOneCapability()
    {
        foreach (var (contract, capabilities) in CapabilityMatrix.RegistrationRequirements)
        {
            capabilities.Should().NotBeEmpty($"'{contract.Name}' is listed as gated");
        }

        foreach (var (contract, capabilities) in CapabilityMatrix.DependencyRequirements)
        {
            capabilities.Should().NotBeEmpty($"'{contract.Name}' is listed as gated");
        }
    }

    [Test]
    public void TheForwardCheckableSetIsDerivedFromTheTableRatherThanHardCoded()
    {
        CapabilityMatrix.ForwardCheckableCapabilities.Should().NotContain(Capability.Network);
        CapabilityMatrix.ForwardCheckableCapabilities.Should().NotContain(Capability.Storage);

        foreach (var capability in Enum.GetValues<Capability>()
                     .Except([Capability.Network, Capability.Storage]))
        {
            CapabilityMatrix.ForwardCheckableCapabilities.Should().Contain(
                capability,
                $"'{CapabilityNames.ToWireName(capability)}' gates a registration, so declaring it without one must be catchable");
        }
    }

    [Test]
    public void TheOneAnyOfRowIsTheOnlyOne()
    {
        var anyOf = CapabilityMatrix.RegistrationRequirements
            .Where(row => row.Value.Length > 1)
            .Select(row => row.Key)
            .ToArray();

        anyOf.Should().ContainSingle();
        anyOf[0].Name.Should().Be("IDiacriticFoldingProvider");
    }

    [TestCase(typeof(IScheduledJob))]
    [TestCase(typeof(Abstractions.Intent.PluginIntentSurface))]
    [TestCase(typeof(Abstractions.Health.IHealthContributor))]
    public void AnUngatedContractIsPermittedWithNoCapabilitiesAtAll(Type contract)
        => CapabilityMatrix.IsPermitted(CapabilitySet.None, contract).Should().BeTrue();

    [Test]
    public void AGatedContractIsRefusedWithNoCapabilitiesAtAll()
        => CapabilityMatrix.IsPermitted(CapabilitySet.None, typeof(Abstractions.Parsing.IReleaseParser))
            .Should().BeFalse();

    [Test]
    public void AnyOneOfTheListedCapabilitiesSuffices()
    {
        var folding = typeof(Abstractions.Naming.IDiacriticFoldingProvider);

        CapabilityMatrix.IsPermitted(CapabilitySet.Of(Capability.Parsing), folding).Should().BeTrue();
        CapabilityMatrix.IsPermitted(CapabilitySet.Of(Capability.Renaming), folding).Should().BeTrue();
        CapabilityMatrix.IsPermitted(CapabilitySet.Of(Capability.Quality), folding).Should().BeFalse();
    }

    [Test]
    public void AskingWhatToReportForAnUngatedContractIsAProgrammingError()
    {
        var report = () => CapabilityMatrix.RequirementToReport(typeof(IScheduledJob));

        report.Should().Throw<ArgumentException>();
    }
}
