using System.Collections.ObjectModel;
using System.Linq;
using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Wire;
using Arronix.Plugins.Registry;


namespace Arronix.Plugins.Tests.Registry;

/// <summary>
/// The rule that decides which declared client facets a host will actually serve.
/// </summary>
/// <remarks>
/// Tested directly rather than only through an installation: a fixed point over a graph is either tested or
/// believed. The shapes below separate a correct implementation from a plausible one — a direct binding, a
/// chain, an unrelated dependant, and the same graph in every presentation order. A single pass in
/// identifier order passes the direct case and fails the chain.
/// </remarks>
[TestFixture]
public sealed class ClientFacetResolverTests
{
    private static readonly string[] Admitted = ["Alpha", "Beta", "Gamma"];

    /// <summary>
    /// A package whose facet binds an admitted contract its own closure does not offer is withheld, and the
    /// refusal names the assemblies it could not reach.
    /// </summary>
    /// <remarks>
    /// The package here declares no dependency on the one that publishes <c>Beta</c>, so the requirement
    /// rule never sees it. This is the other half: global admission is not global visibility, and an
    /// assembly a package binds without declaring is one a browser would be handed without it.
    /// </remarks>
    [Test]
    public void APackageThatBindsAContractOutsideItsOwnClosureIsWithheld()
    {
        var resolved = ClientFacetResolver.Resolve(
            [
                Candidate("a", [], Offers("Alpha", "Beta")),
                Candidate("b", [], Offers("Beta")),
            ],
            Admitted);

        Assert.Multiple(() =>
        {
            Assert.That(resolved.Offering.Keys.Select(id => id.Value), Is.EqualTo(new[] { "b" }));

            var refused = Refusal(resolved, "a");
            Assert.That(refused.MissingAssemblies, Is.EqualTo(new[] { "Beta" }));
            Assert.That(refused.CausedBy, Is.Empty, "nothing was withdrawn; the package never declared it");
            Assert.That(refused.Reason, Does.Contain("client closure"));
        });
    }

    /// <summary>
    /// A facet whose declaration could not be read is withheld, and its dependants with it.
    /// </summary>
    /// <remarks>
    /// The narrowest outcome that is still honest. The assembly is an admitted shared contract either way —
    /// what failed to decode is its description of a client surface, not the contract every dependant of the
    /// package binds to — so quarantining the package would cost an installation a working media kind over
    /// a browser-only defect. Withholding the facet costs it the browser and nothing else.
    /// </remarks>
    [Test]
    public void AFacetWhoseDeclarationCouldNotBeReadIsWithheldWithoutQuarantiningAnything()
    {
        var resolved = ClientFacetResolver.Resolve(
            [
                Candidate("a", ["b"], Offers("Alpha", "Beta")),
                Candidate("b", [], Offers("Beta"), undeclarable: ["'Beta.dll': the blob is truncated."]),
            ],
            Admitted);

        Assert.Multiple(() =>
        {
            Assert.That(resolved.Offering, Is.Empty, "a dependant of a withheld facet is withheld too");

            var refused = Refusal(resolved, "b");
            Assert.That(refused.Reason, Does.Contain("could not read"));
            Assert.That(refused.Reason, Does.Contain("Beta.dll"));
            Assert.That(
                refused.Reason,
                Does.Contain("remain admitted"),
                "an operator reading this must not conclude the package was quarantined");
            Assert.That(refused.UnadmittedFiles, Is.Empty, "the file was admitted; its declaration was not read");
            Assert.That(refused.MissingAssemblies, Is.Empty);

            Assert.That(Refusal(resolved, "a").CausedBy.Select(id => id.Value), Is.EqualTo(new[] { "b" }));
        });
    }

    /// <summary>
    /// Withholding cascades along a chain, however the identifiers happen to sort.
    /// </summary>
    /// <remarks>
    /// The single-pass bug this replaces looked correct here: examined in identifier order, <c>a</c> is
    /// checked while <c>b</c> still offers, survives, and is never looked at again once <c>b</c> is
    /// withdrawn. Only re-running the rule to a fixed point catches it.
    /// </remarks>
    [Test]
    public void WithholdingCascadesAlongAChain()
    {
        var resolved = ClientFacetResolver.Resolve(
            [
                Candidate("a", ["b"], Offers("Alpha", "Beta")),
                Candidate("b", ["c"], Offers("Beta", "Gamma")),
                Candidate("c", [], Offers("Gamma"), unadmitted: ["Missing.dll"]),
            ],
            Admitted);

        Assert.Multiple(() =>
        {
            Assert.That(resolved.Offering, Is.Empty);
            Assert.That(resolved.Refusals.Select(refusal => refusal.Package.Value), Is.EqualTo(new[] { "a", "b", "c" }));
            Assert.That(Refusal(resolved, "a").CausedBy.Select(id => id.Value), Is.EqualTo(new[] { "b" }));
            Assert.That(Refusal(resolved, "b").CausedBy.Select(id => id.Value), Is.EqualTo(new[] { "c" }));
            Assert.That(Refusal(resolved, "c").CausedBy, Is.Empty);
        });
    }

    /// <summary>
    /// A package whose required client facet was withheld is withheld too, even when its own assemblies
    /// happen to bind nothing from it.
    /// </summary>
    /// <remarks>
    /// This is a deliberate reversal of an earlier, weaker rule that kept such a package. "It does not bind
    /// that assembly today" is a property of the current build, not of the dependency: the package declared
    /// the requirement, the closure a browser would be told to load contains it, and serving a dependant out
    /// of a closure this host has already refused publishes an installation the host does not stand behind.
    /// The conservative answer costs one media kind in a browser; the permissive answer costs the meaning of
    /// the closure.
    /// </remarks>
    [Test]
    public void APackageWhoseRequiredFacetWasWithheldIsWithheldEvenWhenItBindsNothingFromIt()
    {
        var resolved = ClientFacetResolver.Resolve(
            [
                Candidate("a", ["b"], Offers("Alpha")),
                Candidate("b", [], Offers("Beta"), unadmitted: ["Missing.dll"]),
            ],
            Admitted);

        Assert.Multiple(() =>
        {
            Assert.That(resolved.Offering, Is.Empty);
            Assert.That(resolved.Refusals.Select(refusal => refusal.Package.Value), Is.EqualTo(new[] { "a", "b" }));
            Assert.That(Refusal(resolved, "a").CausedBy.Select(id => id.Value), Is.EqualTo(new[] { "b" }));
            Assert.That(Refusal(resolved, "a").MissingAssemblies, Is.Empty);
        });
    }

    /// <summary>
    /// A requirement that never declared a client facet is not a withheld one.
    /// </summary>
    /// <remarks>
    /// Most installed packages offer a browser nothing. Treating "offers no facet" as "facet withheld" would
    /// withhold every dependant of every server-only package, which is every media kind.
    /// </remarks>
    [Test]
    public void ARequirementThatOffersNoClientFacetAtAllIsNotAWithheldOne()
    {
        var resolved = ClientFacetResolver.Resolve(
            [Candidate("a", ["server.only"], Offers("Alpha"))],
            Admitted);

        Assert.Multiple(() =>
        {
            Assert.That(resolved.Offering.Keys.Select(id => id.Value), Is.EqualTo(new[] { "a" }));
            Assert.That(resolved.Refusals, Is.Empty);
        });
    }

    /// <summary>
    /// The outcome is a property of the graph, not of the order the packages arrived in.
    /// </summary>
    /// <param name="order">One permutation of the same three packages.</param>
    [Test]
    [TestCase("a,b,c")]
    [TestCase("a,c,b")]
    [TestCase("b,a,c")]
    [TestCase("b,c,a")]
    [TestCase("c,a,b")]
    [TestCase("c,b,a")]
    public void EveryPresentationOrderProducesTheSameFacetsAndClosures(string order)
    {
        var byId = new Dictionary<string, ClientFacetCandidate>(StringComparer.Ordinal)
        {
            ["a"] = Candidate("a", [], Offers("Alpha")),
            ["c"] = Candidate("c", [], Offers("Gamma")),

            // Declared in reverse, so a resolver that trusted declaration order would produce a different
            // closure — and therefore a different closure hash — for the same installation.
            ["b"] = Candidate("b", ["c", "a"], Offers("Beta", "Alpha", "Gamma")),
        };

        var resolved = ClientFacetResolver.Resolve(
            [.. order.Split(',').Select(id => byId[id])],
            Admitted);

        Assert.Multiple(() =>
        {
            Assert.That(
                resolved.Offering.Keys.Select(id => id.Value).Order(StringComparer.Ordinal),
                Is.EqualTo(new[] { "a", "b", "c" }));
            Assert.That(
                resolved.InIdentifierOrder().Select(entry => entry.Id.Value),
                Is.EqualTo(new[] { "a", "b", "c" }));
            Assert.That(resolved.Refusals, Is.Empty);

            // Requirements are canonically ordered, so 'b' walks 'a' before 'c' whatever it declared.
            Assert.That(
                resolved.ClosureOf(resolved.Offering[PluginId.FromString("b")]).Select(entry => entry.Id.Value),
                Is.EqualTo(new[] { "a", "c", "b" }));
        });
    }

    [Test]
    public void AFacetThisInstallationAdmittedNoContractForIsWithheldBeforeAnyClosureRule()
    {
        var resolved = ClientFacetResolver.Resolve(
            [Candidate("a", [], Offers("Alpha"), unadmitted: ["Ghost.dll"])],
            Admitted);

        var refusal = resolved.Refusals.Single();

        Assert.Multiple(() =>
        {
            Assert.That(resolved.Offering, Is.Empty);
            Assert.That(refusal.Package.Value, Is.EqualTo("a"));
            Assert.That(refusal.UnadmittedFiles, Is.EqualTo(new[] { "Ghost.dll" }));
            Assert.That(refusal.MissingAssemblies, Is.Empty);
            Assert.That(refusal.Reason, Does.Contain("admitted no shared contract"));
        });
    }

    /// <summary>
    /// A reference to something this installation never admitted is a framework assembly, not a leak.
    /// </summary>
    [Test]
    public void AReferenceOutsideTheAdmittedSetIsNotAFacetLeak()
    {
        var resolved = ClientFacetResolver.Resolve(
            [Candidate("a", [], Offers("Alpha", "System.Runtime"))],
            Admitted);

        Assert.Multiple(() =>
        {
            Assert.That(resolved.Offering.Keys.Select(id => id.Value), Is.EqualTo(new[] { "a" }));
            Assert.That(resolved.Refusals, Is.Empty);
        });
    }

    /// <summary>
    /// When both rules fire on one package, the refusal says both things.
    /// </summary>
    /// <remarks>
    /// A package can lose a required facet and, separately, bind a contract nothing in its closure offers.
    /// Reporting whichever check ran first would leave an operator fixing one problem and meeting the other,
    /// so the two are computed together and both survive into the refusal.
    /// </remarks>
    [Test]
    public void ARefusalCarriesEveryReasonThatApplies()
    {
        var resolved = ClientFacetResolver.Resolve(
            [
                Candidate("a", ["b"], Offers("Alpha", "Gamma")),
                Candidate("b", [], Offers("Beta"), unadmitted: ["Missing.dll"]),
                Candidate("c", [], Offers("Gamma")),
            ],
            Admitted);

        var refused = Refusal(resolved, "a");

        Assert.Multiple(() =>
        {
            Assert.That(refused.CausedBy.Select(id => id.Value), Is.EqualTo(new[] { "b" }), "the required facet it lost");
            Assert.That(refused.MissingAssemblies, Is.EqualTo(new[] { "Gamma" }), "and the contract it binds anyway");
            Assert.That(refused.Reason, Does.Contain("withheld").And.Contain("It also"));
        });
    }

    /// <summary>Every withheld requirement is named, not one of them.</summary>
    [Test]
    public void ARefusalNamesEveryRequiredFacetItLost()
    {
        var resolved = ClientFacetResolver.Resolve(
            [
                Candidate("a", ["b", "c"], Offers("Alpha")),
                Candidate("b", [], Offers("Beta"), unadmitted: ["Missing.dll"]),
                Candidate("c", [], Offers("Gamma"), unadmitted: ["Missing.dll"]),
            ],
            Admitted);

        var refused = Refusal(resolved, "a");

        Assert.Multiple(() =>
        {
            Assert.That(
                refused.CausedBy.Select(id => id.Value),
                Is.EqualTo(new[] { "b", "c" }),
                "naming one of two would send an operator to fix half the problem");
            Assert.That(refused.Reason, Does.Contain("'b'").And.Contain("'c'"));
        });
    }

    private static ClientContractRefusal Refusal(ResolvedClientFacets resolved, string package)
        => resolved.Refusals.Single(refusal => refusal.Package.Value == package);

    private static OfferedAssembly Offers(string assemblyName, params string[] references)
        => new(
            new ClientContractAssembly(
                assemblyName,
                assemblyName + ".dll",
                assemblyName + ", Version=1.0.0.0, Culture=neutral, PublicKeyToken=null",
                new string('a', 64),
                Guid.Empty,
                1,
                []),
            ReadOnlyMemory<byte>.Empty,
            new ReadOnlyCollection<string>(references));

    private static ClientFacetCandidate Candidate(
        string id,
        string[] requires,
        OfferedAssembly offers,
        string[]? unadmitted = null,
        string[]? undeclarable = null)
        => new(
            PluginId.FromString(id),
            "1.0.0",
            id,
            new ReadOnlyCollection<PluginId>([.. requires.Select(PluginId.FromString)]),
            new ReadOnlyCollection<OfferedAssembly>([offers]),
            new ReadOnlyCollection<string>(unadmitted ?? []),
            new ReadOnlyCollection<string>(undeclarable ?? []));
}
