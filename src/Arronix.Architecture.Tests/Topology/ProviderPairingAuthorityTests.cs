using System.Linq;
using System.Reflection;
using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Providers;
using Arronix.Abstractions.Shape;
using Arronix.Architecture.Tests.MovieCatalogerFixture;
using Arronix.Architecture.Tests.Repository;
using Arronix.Media.Movies;

namespace Arronix.Architecture.Tests.Topology;

/// <summary>
/// Who is allowed to say what a provider is, checked against the compiled contracts.
/// </summary>
/// <remarks>
/// <para>
/// Three facts used to have several authorities each. A cataloger's item type was closed in its contract
/// and repeated as a registration type argument; its family was fixed by the registration method, restated
/// on the declaration and restated again on the implementation; and its identifier was minted by the host
/// and minted a second time by the provider from the same two parts. Nothing compared them, so a
/// disagreement was silent - and the fixture in this repository actually carried one, calling itself
/// <c>independent-list</c> while its declaration said <c>independent-curator</c>.
/// </para>
/// <para>
/// The rules below are the replacement stated mechanically: the contract owns the item type, the
/// registration owns the family, and the host owns the identifier. Each is asserted from metadata rather
/// than from a convention, because a convention is what the previous arrangement had.
/// </para>
/// </remarks>
[TestFixture]
public sealed class ProviderPairingAuthorityTests
{
    /// <summary>Every assembly this fixture can see that may contain a provider implementation.</summary>
    private static IEnumerable<Assembly> ProviderAssemblies =>
    [
        typeof(IndependentMovieCataloger).Assembly,
        typeof(Plugin.Movies.Movies).Assembly,
        typeof(Plugin.Tv.TvPluginModule).Assembly,
        typeof(Plugin.Books.BooksPluginModule).Assembly,
        typeof(Plugin.Music.MusicPluginModule).Assembly,
    ];

    public static IEnumerable<Type> ProviderImplementations => ProviderAssemblies
        .SelectMany(assembly => assembly.GetTypes())
        .Where(type => type is { IsClass: true, IsAbstract: false } && typeof(IProvider).IsAssignableFrom(type))
        .OrderBy(type => type.FullName, StringComparer.Ordinal);

    /// <remarks>
    /// The detector's own control. A rule that silently matched nothing would pass for the wrong reason,
    /// and this fixture's whole subject is a fact that used to be restated in several places at once.
    /// </remarks>
    [Test]
    public void TheProviderImplementationsUnderTestAreFound()
        => Assert.That(
            ProviderImplementations.ToArray(),
            Has.Length.GreaterThanOrEqualTo(5),
            "the reference extensions and the provider fixture each supply at least one implementation");

    /// <remarks>
    /// Identity and family are host-owned, so a provider that publishes either has published an answer
    /// nothing checks. A provider that needs its own qualified identifier during a call reads it from the
    /// invocation, which is the one authority; a private field carrying an extension's own release-key
    /// namespace is not a published answer and is not what this rule is about.
    /// </remarks>
    [TestCaseSource(nameof(ProviderImplementations))]
    public void NoProviderPublishesItsOwnIdentifierOrFamily(Type implementation)
    {
        var restated = implementation
            .GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(member => MemberValueType(member) is { } type
                && (type == typeof(ProviderId) || type == typeof(ProviderFamily)))
            .Select(member => member.Name)
            .ToArray();

        Assert.That(
            restated,
            Is.Empty,
            $"'{implementation.Name}' publishes {string.Join(", ", restated)}. The host mints a provider's "
            + "identifier from the extension that supplies it, and the registration it is admitted through "
            + "fixes its family; a provider that answers either question is a second authority nothing "
            + "compares against the first.");
    }

    /// <remarks>
    /// The pairing members exist so that registration can read the closed relationship without a second
    /// type argument. They are supplied by the contract, so an implementation which writes them has taken
    /// on a binding-SPI obligation the SDK exists to keep away from authors.
    /// </remarks>
    [TestCaseSource(nameof(ProviderImplementations))]
    public void NoProviderWritesItsOwnPairingMembers(Type implementation)
    {
        var written = implementation
            .GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(member => member.Name.Contains("PairedItemType", StringComparison.Ordinal)
                || member.Name.Contains("PairedContractType", StringComparison.Ordinal))
            .Select(member => member.Name)
            .ToArray();

        Assert.That(
            written,
            Is.Empty,
            $"'{implementation.Name}' declares {string.Join(", ", written)}. The pairing is answered by "
            + "ICataloger<TItem> and ICurator<TItem> from their own type argument; an author states the "
            + "item type once, in the contract, and never implements the binding SPI that reads it back.");
    }

    /// <remarks>
    /// The compile-time half of "stated once", read from the registration methods themselves. One type
    /// parameter means there is no second argument to keep in step, and the pairing constraint means a type
    /// that closed no media contract is rejected at the call site rather than after admission.
    /// </remarks>
    [Test]
    public void TheRegistrationMethodsTakeTheImplementationAloneAndDemandItsPairing()
    {
        var cataloger = typeof(IPluginRegistry).GetMethod(nameof(IPluginRegistry.AddCataloger))!;
        var curator = typeof(IPluginRegistry).GetMethod(nameof(IPluginRegistry.AddCurator))!;

        Assert.Multiple(() =>
        {
            Assert.That(cataloger.GetGenericArguments(), Has.Length.EqualTo(1));
            Assert.That(curator.GetGenericArguments(), Has.Length.EqualTo(1));

            Assert.That(
                cataloger.GetGenericArguments()[0].GetGenericParameterConstraints(),
                Does.Contain(typeof(ICatalogerPairing)).And.Contain(typeof(ICataloger)));

            Assert.That(
                curator.GetGenericArguments()[0].GetGenericParameterConstraints(),
                Does.Contain(typeof(ICuratorPairing)));
        });
    }

    /// <remarks>
    /// The pairing interfaces deliberately share no base. A common base would make one class serving both
    /// families ambiguous at its own compile time, and would let a curator satisfy a cataloger registration.
    /// </remarks>
    [Test]
    public void TheTwoPairingContractsAreIndependent()
        => Assert.Multiple(() =>
        {
            Assert.That(typeof(ICatalogerPairing).GetInterfaces(), Is.Empty);
            Assert.That(typeof(ICuratorPairing).GetInterfaces(), Is.Empty);
            Assert.That(typeof(ICuratorPairing).IsAssignableFrom(typeof(ICataloger<Movie>)), Is.False);
            Assert.That(typeof(ICatalogerPairing).IsAssignableFrom(typeof(ICurator<Movie>)), Is.False);
        });

    /// <remarks>
    /// The registration reads all three facts without being told any of them: the item type and the closed
    /// contract come from the interface the implementation closed, and the family comes from which
    /// registration was called.
    /// </remarks>
    [Test]
    public void ARegistrationDerivesTheItemTypeContractAndFamilyFromTheContractAlone()
    {
        var cataloger = ProviderTypeRegistration.ForCataloger<IndependentMovieCataloger>(Declaration("cat"));
        var curator = ProviderTypeRegistration.ForCurator<IndependentMovieCurator>(Declaration("list"));

        Assert.Multiple(() =>
        {
            Assert.That(cataloger.MediaItemType, Is.SameAs(typeof(Movie)));
            Assert.That(cataloger.ContractType, Is.SameAs(typeof(ICataloger<Movie>)));
            Assert.That(cataloger.ImplementationType, Is.SameAs(typeof(IndependentMovieCataloger)));
            Assert.That(cataloger.Family, Is.EqualTo(ProviderFamily.Cataloger));

            Assert.That(curator.MediaItemType, Is.SameAs(typeof(Movie)));
            Assert.That(curator.ContractType, Is.SameAs(typeof(ICurator<Movie>)));
            Assert.That(curator.Family, Is.EqualTo(ProviderFamily.Curator));
        });
    }

    /// <summary>
    /// One class serving both families keeps one pairing per family.
    /// </summary>
    /// <remarks>
    /// A vendor whose service answers both "what is it" and "which ones do you want" may reasonably write
    /// one class. That this compiles at all is the assertion: the pairing members are inherited statics, so
    /// a single base interface declaring them would leave the class with two implementations of one member
    /// and no most specific one - a diamond the author would have to resolve and could not sensibly. The
    /// values below then prove the two registrations stay apart rather than collapsing into whichever
    /// contract happened to be found first.
    /// </remarks>
    [Test]
    public void OneImplementationMayServeBothFamiliesAndEachRegistrationKeepsItsOwnPairing()
    {
        var cataloger = ProviderTypeRegistration.ForCataloger<DualFamilyMovieProvider>(Declaration("both"));
        var curator = ProviderTypeRegistration.ForCurator<DualFamilyMovieProvider>(Declaration("both-list"));

        Assert.Multiple(() =>
        {
            Assert.That(cataloger.ContractType, Is.SameAs(typeof(ICataloger<Movie>)));
            Assert.That(cataloger.Family, Is.EqualTo(ProviderFamily.Cataloger));

            Assert.That(curator.ContractType, Is.SameAs(typeof(ICurator<Movie>)));
            Assert.That(curator.Family, Is.EqualTo(ProviderFamily.Curator));

            Assert.That(cataloger.MediaItemType, Is.SameAs(curator.MediaItemType));
            Assert.That(cataloger.MediaItemType, Is.SameAs(typeof(Movie)));
        });
    }

    /// <remarks>
    /// <para>
    /// The consumer half of host-owned identity. A provider listing now carries the qualified identifier a
    /// configuration must point at, so neither edge has any reason to build one - and the edge that used to
    /// try could not succeed: reading an extension's identity out of a local name is not a parse, it is a
    /// guess, and it rendered every provider in settings as unavailable.
    /// </para>
    /// <para>
    /// Serialization is exempt and is the only exemption. Reading the qualified form back off the wire is
    /// what a converter is for; it reconstructs the value that travelled rather than inventing one.
    /// </para>
    /// </remarks>
    [TestCase(RepositoryLayout.Api)]
    [TestCase(RepositoryLayout.Client)]
    public void NoConsumerEdgeMintsOrParsesAProviderIdentityOutsideSerialization(string project)
    {
        var offenders = SourceScanner
            .Lines(project, "*.cs", "*.razor")
            .Where(line => !line.File.Contains("Serialization", StringComparison.Ordinal))
            .Where(line => line.Text.Contains("ProviderId.Create", StringComparison.Ordinal)
                || line.Text.Contains("ProviderId.TryParse", StringComparison.Ordinal))
            .Select(line => $"{line.File}:{line.Line}")
            .ToArray();

        Assert.That(
            offenders,
            Is.Empty,
            "A provider's qualified identifier is minted once, by the host registry, and travels to the "
            + "consumer on the catalog entry. An edge that rebuilds one has invented an identity the "
            + "installation never issued.");
    }

    private static ProviderDescriptor Declaration(string localId) => new()
    {
        LocalId = localId,
        Name = localId,
        Settings = [],
    };

    private static Type? MemberValueType(MemberInfo member) => member switch
    {
        PropertyInfo property => property.PropertyType,
        FieldInfo field => field.FieldType,
        _ => null,
    };

    /// <summary>A provider that answers both catalog and curation questions from one class.</summary>
    /// <remarks>
    /// Declared here rather than in the packaged fixture: it exists to prove the contracts compose, and
    /// installing it would add contributions the packaged-admission tests do not expect.
    /// </remarks>
    private sealed class DualFamilyMovieProvider : ICataloger<Movie>, ICurator<Movie>
    {
        public CatalogerCapabilities Capabilities => CatalogerCapabilities.Search;

        public TimeSpan MinimumRefreshInterval => TimeSpan.FromHours(6);

        public IReadOnlyList<ExternalIdReading> ReadExternalIds(string text) => [];

        public Task<IReadOnlyList<Movie>> SearchAsync(
            ProviderInvocation invocation,
            CatalogQuery query,
            CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Movie>>([]);

        public Task<Movie?> GetAsync(
            ProviderInvocation invocation,
            ExternalId id,
            CancellationToken cancellationToken = default) => Task.FromResult<Movie?>(null);

        public Task<IReadOnlyList<ExternalId>> ChangedSinceAsync(
            ProviderInvocation invocation,
            DateTimeOffset since,
            CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ExternalId>>([]);

        public Task<CuratedListFetch<Movie>> FetchAsync(
            ProviderInvocation invocation,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new CuratedListFetch<Movie>([], AnyFailure: false, Warnings: []));

        public Task<ValidationOutcome> TestAsync(
            ProviderInvocation invocation,
            CancellationToken cancellationToken = default) => Task.FromResult(ValidationOutcome.Success);

        public Task<IReadOnlyList<FacetValue>> GetOptionsAsync(
            ProviderInvocation invocation,
            string optionSourceId,
            CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<FacetValue>>([]);
    }
}
