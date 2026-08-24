using System.Linq;
using System.Reflection;
using Arronix.Abstractions.Providers;
using Arronix.Architecture.Tests.MovieCatalogerFixture;
using Arronix.Architecture.Tests.Repository;
using Arronix.Media.Movies;

namespace Arronix.Architecture.Tests.Topology;

/// <summary>
/// What a separately shipped provider needs in order to pair with a media kind, checked rather than claimed.
/// </summary>
/// <remarks>
/// <para>
/// Before the package split there was no honest answer here. A package implementing
/// <c>ICataloger&lt;Movie&gt;</c> had to reference the assembly declaring <c>Movie</c>, and that assembly
/// also declared the module, the definition and the parser - so pairing meant either shipping a private
/// copy of all of it, which yields a second <c>Movie</c> that never casts, or sharing all of it, which
/// puts the module, the definition and the parser on the shared assembly's release cadence instead of the
/// extension's own.
/// </para>
/// <para>
/// These are compile-time and metadata facts only. That one CLR <c>Type</c> is observed across isolated
/// load contexts at run time is a loader property; it is proved by the loader's own fixtures when the
/// admitted-contract resolution exists, and nothing here should be read as claiming it.
/// </para>
/// </remarks>
[TestFixture]
public sealed class IndependentProviderPairingTests
{
    private static Assembly Fixture => typeof(IndependentMovieCataloger).Assembly;

    /// <remarks>
    /// The declaration half. This is the rule an author would otherwise have to remember, so it is checked
    /// against the fixture's own project file: a provider package compiles against the universal contracts
    /// and the media domain it pairs with, and against nothing else the media package ships.
    /// </remarks>
    [Test]
    public void ProviderPackageDeclaresOnlyContractsAndTheMediaDomainItPairsWith()
    {
        var project = ProjectFile.Load(RepositoryLayout.MovieCatalogerFixture);

        Assert.Multiple(() =>
        {
            Assert.That(
                project.ProjectReferences,
                Is.EquivalentTo(new[] { RepositoryLayout.Abstractions, RepositoryLayout.MoviesDomain }));

            Assert.That(
                project.ProjectReferences,
                Does.Not.Contain(RepositoryLayout.MoviesExtension),
                "a provider pairs with the item type, never with the extension that defines the kind");

            Assert.That(project.PackageReferences, Is.Empty);
        });
    }

    /// <remarks>
    /// The binary half, and the one that matters more: a project reference the author never used leaves no
    /// assembly reference, and a reference introduced by a target leaves no project reference. The loader
    /// judges the compiled reference table, so the compiled reference table is what is asserted.
    /// </remarks>
    [Test]
    public void ProviderAssemblyLinksNoPartOfTheMoviesExtension()
    {
        var linked = Fixture
            .GetReferencedAssemblies()
            .Select(static name => name.Name ?? string.Empty)
            .Where(static name => name.StartsWith("Arronix.", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.That(
            linked,
            Is.EqualTo(new[] { RepositoryLayout.Abstractions, RepositoryLayout.MoviesDomain }),
            "the compiled provider must need exactly the universal contracts and the movies media domain.");
    }

    /// <remarks>
    /// The pairing itself. <c>ICataloger&lt;Movie&gt;</c> is a closed generic, so its type argument records
    /// which <c>Movie</c> the provider was compiled against; if that ever became a second declaration of the
    /// same name, this is where it would show up, and it would show up as two types printing one name.
    /// </remarks>
    [Test]
    public void ProviderClosesItsCatalogerOverTheMediaDomainsMovie()
    {
        var closed = typeof(IndependentMovieCataloger)
            .GetInterfaces()
            .Single(static contract =>
                contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(ICataloger<>));

        var item = closed.GetGenericArguments().Single();

        Assert.Multiple(() =>
        {
            Assert.That(item, Is.SameAs(typeof(Movie)));
            Assert.That(item.Assembly.GetName().Name, Is.EqualTo(RepositoryLayout.MoviesDomain));
            Assert.That(
                item.Assembly,
                Is.Not.SameAs(typeof(Plugin.Movies.Movies).Assembly),
                "the item type and the definition that closes over it are deliberately separate assemblies");
        });
    }

    /// <remarks>
    /// The curator side of the same pairing. It exists because the item relationship is stated once per
    /// contract, not once per package: a package supplying both states <c>Movie</c> twice, in two ordinary
    /// generic positions, and never in a registration argument that repeats what the implementation
    /// already closed.
    /// </remarks>
    [Test]
    public void ProviderClosesItsCuratorOverTheSameMovie()
    {
        var closed = typeof(IndependentMovieCurator)
            .GetInterfaces()
            .Single(static contract =>
                contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(ICurator<>));

        Assert.That(closed.GetGenericArguments().Single(), Is.SameAs(typeof(Movie)));
    }

    /// <remarks>
    /// A provider is not merely able to name the item type; it is able to build one. The point of keeping
    /// the media domain typed - rather than handing a vendor a field dictionary - is that the compiler
    /// checks the shape at the vendor's own build, including the media-owned lifecycle whose stage the
    /// availability selection reads.
    /// </remarks>
    [Test]
    public async Task ProviderReturnsAFullyShapedMovieWithItsMediaOwnedLifecycle()
    {
        var cataloger = new IndependentMovieCataloger();

        // The invocation carries a configured definition and a session, and this rule is about the shape of
        // the answer rather than about a call reaching a service, so the default value is the honest one.
        // The invocation carries a configured definition and a session. This rule is about the shape of
        // the answer rather than about a call reaching a service, so the default value is the honest one.
        var results = await cataloger.SearchAsync(
            default,
            new CatalogQuery("Arrival"),
            CancellationToken.None);

        var movie = results.Single();

        Assert.Multiple(() =>
        {
            Assert.That(movie, Is.InstanceOf<Movie>());
            Assert.That(movie.Title, Is.EqualTo("Arrival"));
            Assert.That(movie.Lifecycle, Is.InstanceOf<MovieReleaseTimeline>());
            Assert.That(movie.Status, Is.EqualTo(MovieReleaseStage.Released));
        });
    }

    /// <remarks>
    /// The boundary in the other direction: the identifier spelling belongs to the cataloger that owns the
    /// namespace, and the movies package neither knows it nor can be made to. Recognition is local and
    /// performs no call.
    /// </remarks>
    [Test]
    public void ProviderOwnsItsIdentifierMarkerSpelling()
    {
        var readings = new IndependentMovieCataloger()
            .ReadExternalIds("Some.Movie.2024.1080p {fixture-4242} group");

        var reading = readings.Single();

        Assert.Multiple(() =>
        {
            Assert.That(reading.Id.Scheme, Is.EqualTo("fixture"));
            Assert.That(reading.Id.Value, Is.EqualTo("4242"));
            Assert.That(reading.Marker, Is.EqualTo("{fixture-4242}"));
        });
    }
}
