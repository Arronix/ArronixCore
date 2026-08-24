using System.Linq;
using System.Reflection;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Media;
using Arronix.Abstractions.Providers;
using Arronix.Abstractions.Shape;
using Arronix.Format.Video;
using Arronix.Host.Media;
using Arronix.Host.Media.Typed;
using Arronix.Host.Storage;
using Arronix.Media.Movies;
using Arronix.Plugin.Movies.Definition;
using FluentAssertions;
using FluentAssertions.Execution;


namespace Arronix.Host.Tests.Providers;

/// <summary>
/// How far a typed cataloger's own answer actually travels, and where it stops.
/// </summary>
/// <remarks>
/// <para>
/// This fixture is a trace rather than a feature. It records exactly what the platform does today with a
/// media-shaped provider result, so that the part which is genuinely proved is separated from the part that
/// is not yet decided. The boundary it pins is a required durable key: a cataloger must return a fully
/// valid item, a valid item carries a <see cref="MediaItemId"/>, and no Host seam mints, reconciles or
/// validates one.
/// </para>
/// <para>
/// The unresolved decision is written up in <c>docs/research/g04/media-item-identity-decision.md</c>. It is
/// deliberately not settled here: choosing an identity rule silently would put a durable key policy into
/// the platform through a test fixture.
/// </para>
/// </remarks>
[TestFixture]
internal sealed class CatalogMaterializationBoundaryTests
{
    private static IMediaTypeRuntime Movies =>
        MediaTypeModelFactory.Build<Movie, ReleaseTarget<Movie>, Release<Video>, MovieReleaseParser, Plugin.Movies.Movies>();

    /// <summary>A movie shaped exactly as a paired cataloger returns one.</summary>
    private static Movie FromCataloger { get; } = new()
    {
        Key = MediaItemId.FromInt64(329865),
        Title = "Arrival",
        Year = 2016,
        Lifecycle = new MovieReleaseTimeline
        {
            InCinemas = new DateOnly(2016, 11, 11),
            Digital = new DateOnly(2017, 1, 31),
            EvaluatedOn = new DateOnly(2026, 8, 24),
        },
    };

    /// <summary>
    /// The step that does exist: a typed provider result crosses into Host as the exact item type and
    /// projects through the kind-blind runtime without becoming a field bag on the way.
    /// </summary>
    /// <remarks>
    /// The projection is the erasure the north star sanctions - one-way, derived from the compiled shape,
    /// and never an alternative authoring vocabulary. What matters here is that the media-owned lifecycle
    /// survives it: the stage the availability selection reads is computed by the movie's own timeline, not
    /// by a Host rule about dates.
    /// </remarks>
    [Test]
    public void ATypedCatalogerResultProjectsThroughTheKindBlindRuntimeAsTheExactItem()
    {
        var runtime = Movies;
        var view = runtime.Project(FromCataloger);

        using var assertions = new AssertionScope();

        runtime.ItemType.Should().BeSameAs(typeof(Movie));
        view.Ref.Kind.Should().Be(runtime.Kind);
        view.Ref.Id.Value.Should().Be(329865);
        view.Title.Should().Be("Arrival");
        runtime.Read(FromCataloger, "year").Number.Should().Be(2016);
        FromCataloger.Status.Should().Be(
            MovieReleaseStage.Released,
            "the stage is decided by the movie's own lifecycle object, which crossed the boundary intact");
    }

    /// <summary>
    /// The step that does not exist: nothing in Host consumes a typed cataloger's or curator's results.
    /// </summary>
    /// <remarks>
    /// Asserted as an absence because the absence is the finding. Host activates paired providers, holds
    /// them and reads the kind-blind cataloger floor for identifier markers; there is no member anywhere in
    /// Host that takes or returns the shaped result of <c>ICataloger&lt;TItem&gt;</c> or
    /// <c>ICurator&lt;TItem&gt;</c>, so catalog materialization has no call site to be right or wrong about.
    /// If one is added, this rule fails and the identity decision below has to have been made first.
    /// </remarks>
    [Test]
    public void NoHostSeamConsumesAShapedCatalogerOrCuratorResult()
    {
        Type[] shaped = [typeof(ICataloger<>), typeof(ICurator<>), typeof(CuratedListFetch<>)];

        var consumers = typeof(IMediaStore).Assembly
            .GetTypes()
            .SelectMany(type => type.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(method => Signature(method).Any(type => Mentions(type, shaped)))
            .Select(method => $"{method.DeclaringType?.FullName}.{method.Name}")
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        consumers.Should().BeEmpty(
            "catalog materialization is unbuilt, and reporting it as built is the failure this fixture "
            + "exists to prevent. Adding the first consumer means the durable identity rule has to exist.");
    }

    /// <summary>
    /// The precise unresolved decision, stated as the two facts that cannot both hold.
    /// </summary>
    /// <remarks>
    /// <see cref="MediaItemId"/> documents itself as a host-minted surrogate that nothing outside the
    /// platform chooses. A cataloger must nevertheless supply one on every item it returns, because the key
    /// is a required member of the item it is contractually obliged to shape - and the only way to make one
    /// is a public factory any caller can reach. So today the provider chooses the durable key, no Host seam
    /// mints or reconciles it, and the platform's own file identifiers show what the alternative looks like:
    /// the store mints those.
    /// </remarks>
    [Test]
    public void ADurableItemKeyIsRequiredOfTheProviderAndMintedByNobody()
    {
        var key = typeof(MediaItem<,,>).GetProperty(nameof(MediaItem<,,>.Key))!;

        var storeMints = typeof(IMediaStore)
            .GetMethods()
            .Select(method => method.ReturnType)
            .SelectMany(type => type.IsGenericType ? type.GetGenericArguments() : [type])
            .ToArray();

        using var assertions = new AssertionScope();

        key.PropertyType.Should().Be<MediaItemId>();
        key.GetCustomAttributes()
            .Any(attribute => attribute.GetType().Name == "RequiredMemberAttribute")
            .Should().BeTrue("a cataloger cannot return a valid item without choosing a durable key");

        typeof(MediaItemId).GetMethod(nameof(MediaItemId.FromInt64))!.IsPublic.Should().BeTrue(
            "so the value a provider supplies is unconstrained and unvalidated");

        storeMints.Should().Contain(
            typeof(MediaFileId),
            "the store already mints one kind of durable identifier, which is the shape the item decision "
            + "would have to take if minting is where it lands");

        storeMints.Should().NotContain(
            typeof(MediaItemId),
            "and it mints no item identifier, so no authority exists for the key a cataloger must invent");
    }

    private static IEnumerable<Type> Signature(MethodInfo method) =>
        [method.ReturnType, .. method.GetParameters().Select(parameter => parameter.ParameterType)];

    private static bool Mentions(Type type, Type[] wanted)
    {
        if (type.IsGenericType && wanted.Contains(type.GetGenericTypeDefinition()))
        {
            return true;
        }

        return type.IsGenericType && type.GetGenericArguments().Any(argument => Mentions(argument, wanted));
    }
}
