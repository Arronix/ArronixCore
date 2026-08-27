using System.Linq;
using System.Reflection;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Media;
using Arronix.Abstractions.Providers;
using Arronix.Abstractions.Shape;
using Arronix.Architecture.Tests.Repository;
using Arronix.Host.Media;
using Arronix.Host.Media.Catalog;
using Arronix.Host.Providers;

namespace Arronix.Architecture.Tests.Topology;

/// <summary>
/// Who owns an item's identity before it is in the library, and who owns it after.
/// </summary>
/// <remarks>
/// A cataloger owns the item's identity in its own external identifier scheme. The host owns
/// <see cref="MediaItemId"/> and assigns it when a catalog item enters local library state. The rules below
/// assert the separation from the compiled contracts rather than from a convention: a provider has no
/// member through which a durable identity could arrive or leave, and a curator has no member through which
/// an item could.
/// </remarks>
[TestFixture]
public sealed class CatalogIdentityAuthorityTests
{
    /// <summary>The contracts a provider package writes against.</summary>
    private static IEnumerable<Type> ProviderContracts => typeof(IProvider).Assembly
        .GetTypes()
        .Where(static type => type.IsInterface && typeof(IProvider).IsAssignableFrom(type))
        .OrderBy(static type => type.FullName, StringComparer.Ordinal);

    /// <summary>The catalog half of them: the contracts that speak about items not yet in the library.</summary>
    private static IEnumerable<Type> CatalogContracts =>
    [
        typeof(ICataloger),
        typeof(ICataloger<>),
        typeof(ICurator<>),
        typeof(IClosedCataloger),
        typeof(IClosedCurator),
    ];

    /// <summary>
    /// The host-internal contract through which durable identity is assigned.
    /// </summary>
    /// <remarks>
    /// Found rather than named: this fixture must not reference Host internals, and the point of the rule
    /// it serves is that the contract is not public. A missing type would fail here rather than silently
    /// weaken the rules that use it.
    /// </remarks>
    private static Type Assignment => typeof(CatalogIdentity).Assembly
        .GetTypes()
        .Single(static type => type.IsInterface && type.Name == "ICatalogIdentityAssignment");

    /// <remarks>
    /// The detector's control. A closure walk that reached nothing would pass every rule below for the
    /// wrong reason.
    /// </remarks>
    [Test]
    public void TheProviderContractsUnderTestAreFound()
    {
        Assert.That(ProviderContracts.ToArray(), Has.Length.GreaterThanOrEqualTo(5));
        Assert.That(SignatureClosure(ProviderContracts).ToArray(), Has.Length.GreaterThanOrEqualTo(20));
    }

    /// <summary>
    /// No catalog contract reaches a durable identity, so a cataloger or curator has nothing to mint one
    /// into and nothing to read one out of.
    /// </summary>
    [Test]
    public void NoCatalogContractReachesTheDurableIdentity()
    {
        var reached = SignatureClosure(CatalogContracts)
            .Where(static type => type == typeof(MediaItemId) || type == typeof(MediaItemRef))
            .Select(static type => type.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.That(
            reached,
            Is.Empty,
            "a cataloger states catalog identity; the host states durable identity, and the two never meet "
            + "in a signature a cataloger or curator can see");
    }

    /// <summary>
    /// No provider contract of any family names a durable identity directly. The other families reach one
    /// only through <see cref="MediaItemRef"/>, which addresses an item already in the library.
    /// </summary>
    [Test]
    public void NoProviderContractNamesADurableIdentityDirectly()
    {
        var named = ProviderContracts
            .SelectMany(static contract => contract.GetMembers())
            .Where(static member => Carried(member).Contains(typeof(MediaItemId)))
            .Select(static member => $"{member.DeclaringType?.Name}.{member.Name}")
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(named, Is.Empty);
            Assert.That(
                SignatureClosure(ProviderContracts),
                Does.Contain(typeof(MediaItemRef)),
                "the control: the release and notification families do address library items, so the walk "
                + "would have found a bare identity if one were there");
        });
    }

    /// <summary>
    /// A media entity carries no durable identity either, so an item a cataloger shapes is complete without
    /// one.
    /// </summary>
    [Test]
    public void AMediaEntityCarriesNoDurableIdentity()
    {
        Type[] entities =
        [
            typeof(IMediaEntity),
            typeof(IMediaItem),
            typeof(IMediaGroup<>),
            typeof(MediaItem<,,>),
            typeof(MediaCollection<>),
        ];

        var carriers = entities
            .SelectMany(static type => type.GetProperties())
            .Where(static property => property.PropertyType == typeof(MediaItemId))
            .Select(static property => $"{property.DeclaringType?.Name}.{property.Name}")
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.That(carriers, Is.Empty);
    }

    /// <summary>
    /// The independently packaged provider fixture names the durable identity nowhere at all, which is the
    /// same statement made against a compiled provider package rather than against a contract.
    /// </summary>
    [Test]
    public void AnIndependentlyPackagedProviderNamesTheDurableIdentityNowhere()
    {
        const string project = "Arronix.Architecture.Tests.MovieCatalogerFixture";
        var referenced = AssemblyMetadata.ReferencedTypeNames(project);

        Assert.That(referenced, Is.Not.Empty, "the provider fixture must be built for this rule to mean anything");
        Assert.That(
            referenced,
            Does.Contain(typeof(ExternalId).FullName),
            "the control: the fixture does name catalog identity");
        Assert.That(
            referenced,
            Does.Not.Contain(typeof(MediaItemId).FullName),
            "and names durable identity nowhere, because nothing it can reach carries one");
    }

    /// <summary>A cataloger returns the media type's own item, not a projection or a wrapper of one.</summary>
    [Test]
    public void ACatalogerReturnsTheExactItemType()
    {
        var item = typeof(ICataloger<>).GetGenericArguments()[0];
        var search = typeof(ICataloger<>).GetMethod(nameof(ICataloger<IMediaItem>.SearchAsync))!;
        var get = typeof(ICataloger<>).GetMethod(nameof(ICataloger<IMediaItem>.GetAsync))!;

        Assert.Multiple(() =>
        {
            Assert.That(
                search.ReturnType.GetGenericArguments()[0].GetGenericArguments()[0],
                Is.EqualTo(item),
                "a search returns the item type the contract closed over");
            Assert.That(
                get.ReturnType.GetGenericArguments()[0],
                Is.EqualTo(item),
                "and so does a fetch");
        });
    }

    /// <summary>
    /// A cataloger declares the one scheme it is the authority for, and that declaration is what the host
    /// routes by.
    /// </summary>
    [Test]
    public void ACatalogerDeclaresTheSchemeItIsTheAuthorityFor()
    {
        var scheme = typeof(ICataloger).GetProperty(nameof(ICataloger.CatalogScheme));
        var routing = typeof(CatalogDispatcher).GetMethod(nameof(CatalogDispatcher.AuthoritiesFor))!;

        Assert.Multiple(() =>
        {
            Assert.That(scheme, Is.Not.Null);
            Assert.That(scheme!.PropertyType, Is.EqualTo(typeof(string)));
            Assert.That(
                routing.GetParameters().Select(static parameter => parameter.ParameterType),
                Is.EqualTo(new[] { typeof(IMediaTypeRuntime), typeof(string) }),
                "routing takes a kind and a scheme; it takes no provider identifier and no implementation type");
        });
    }

    /// <summary>A curator proposes references. No item of the paired type crosses its contract.</summary>
    [Test]
    public void ACuratorProposesReferencesRatherThanItems()
    {
        var item = typeof(ICurator<>).GetGenericArguments()[0];
        var fetch = typeof(ICurator<>).GetMethod(nameof(ICurator<IMediaItem>.FetchAsync))!;
        var result = fetch.ReturnType.GetGenericArguments()[0];

        var carried = result
            .GetProperties()
            .Select(static property => property.PropertyType)
            .SelectMany(static type => type.IsGenericType ? type.GetGenericArguments() : [type])
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(result.GetGenericTypeDefinition(), Is.EqualTo(typeof(CuratedListFetch<>)));
            Assert.That(carried, Does.Not.Contain(item), "a curated list carries no item of the paired type");
            Assert.That(carried, Does.Contain(typeof(CuratedReference)), "it carries references instead");
        });
    }

    /// <summary>
    /// A curator's own identifier is a different type from a catalog identifier, with no conversion between
    /// them, so one cannot be supplied where the other is meant.
    /// </summary>
    [Test]
    public void ACuratorOwnedIdentifierCannotBeMistakenForACatalogIdentifier()
    {
        var conversions = new[] { typeof(CuratedEntryId), typeof(ExternalId) }
            .SelectMany(static type => type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Where(static method =>
                method.Name is "op_Implicit" or "op_Explicit"
                && Signature(method).Any(static type =>
                    type == typeof(CuratedEntryId) || type == typeof(ExternalId)))
            .Where(static method => Signature(method).Contains(typeof(CuratedEntryId))
                && Signature(method).Contains(typeof(ExternalId)))
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(typeof(CuratedEntryId).IsAssignableTo(typeof(ExternalId)), Is.False);
            Assert.That(typeof(ExternalId).IsAssignableTo(typeof(CuratedEntryId)), Is.False);
            Assert.That(conversions, Is.Empty, "and neither converts to the other");
            Assert.That(
                typeof(CuratedReference).GetProperty(nameof(CuratedReference.CatalogId))!.PropertyType,
                Is.EqualTo(typeof(ExternalId)));
            Assert.That(
                typeof(CuratedReference).GetProperty(nameof(CuratedReference.EntryId))!.PropertyType,
                Is.EqualTo(typeof(CuratedEntryId?)),
                "the curator's own identifier is optional and separately typed");
        });
    }

    /// <summary>
    /// A catalog reference names a scheme and a value. It names no provider, no configured definition and
    /// no implementation type, so it survives the implementation that produced it.
    /// </summary>
    [Test]
    public void ACatalogReferenceNamesNoProviderOrImplementation()
    {
        Type[] forbidden = [typeof(ProviderId), typeof(ProviderDefinition), typeof(ProviderDescriptor), typeof(Type)];

        var named = SignatureClosure([typeof(ExternalId), typeof(CuratedReference), typeof(CuratedEntryId)])
            .Where(forbidden.Contains)
            .Select(static type => type.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.That(named, Is.Empty);
    }

    /// <summary>
    /// Durable identity is host state with the host's lifetime, not something a reloadable media runtime
    /// owns: a runtime rebuilt on reload would reissue identities the library is already keyed by.
    /// </summary>
    [Test]
    public void DurableIdentityIsHostStateAndNotTheMediaRuntimesToAssign()
    {
        var assign = Assignment.GetMethod("Identify")!;

        Assert.Multiple(() =>
        {
            Assert.That(typeof(CatalogIdentity).Assembly.GetName().Name, Is.EqualTo("Arronix.Host"));
            Assert.That(assign.ReturnType, Is.EqualTo(typeof(MediaItemRef)));
            Assert.That(
                assign.GetParameters().Select(static parameter => parameter.ParameterType),
                Is.EqualTo(new[] { typeof(MediaKindId), typeof(MediaLevelId), typeof(IReadOnlyCollection<ExternalId>) }),
                "scoped by kind and level, and assigned from catalog identifiers, which is the only identity "
                + "a provider supplies");
            Assert.That(
                typeof(IMediaTypeRuntime).GetMembers().Where(static member => Carried(member).Contains(typeof(MediaItemId))),
                Is.Empty,
                "the media runtime exposes no bare durable-identity assignment seam");
        });
    }

    /// <summary>
    /// Resolving an identity and assigning one are separate contracts, and only the first is public.
    /// </summary>
    /// <remarks>
    /// The rule is mechanical rather than advisory: a caller holding <see cref="CatalogIdentity"/> or
    /// <see cref="ICatalogIdentityReader"/> has no member through which the identity space can grow, because
    /// assignment is implemented explicitly against a host-internal interface. That is what makes "a search
    /// allocates nothing" and "a read never allocates" statements the compiler enforces rather than
    /// statements a later change can quietly break.
    /// </remarks>
    [Test]
    public void AssigningIdentityIsSeparatedFromResolvingItAndIsNotPublic()
    {
        var readerMembers = typeof(ICatalogIdentityReader)
            .GetMethods()
            .Select(static method => method.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        var publiclyAssigning = typeof(CatalogIdentity)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(static method => method.ReturnType == typeof(MediaItemRef)
                && method.GetParameters().Any(static parameter =>
                    parameter.ParameterType == typeof(IReadOnlyCollection<ExternalId>)))
            .Select(static method => method.Name)
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(
                typeof(CatalogIdentity).IsAssignableTo(typeof(ICatalogIdentityReader)),
                Is.True,
                "one state, read through the narrow contract");
            Assert.That(readerMembers, Is.EqualTo(new[] { "Canonical", "TryFind" }),
                "the reader resolves and canonicalizes, and does nothing else");
            Assert.That(publiclyAssigning, Is.Empty,
                "assignment is not reachable from the public surface of identity state");
            Assert.That(Assignment.IsPublic, Is.False, "and the contract that does assign is host-internal");
            Assert.That(
                Assignment.IsAssignableTo(typeof(ICatalogIdentityReader)),
                Is.False,
                "the two are separate contracts, so holding one is not holding the other");
        });
    }

    /// <summary>
    /// The kind-blind projection bridge is handed the reader, so browsing and reading a field cannot
    /// allocate a durable identity for anything they render.
    /// </summary>
    /// <remarks>
    /// The control is the second assertion: the same walk does find the assigning contract on the type that
    /// is supposed to have it, so an empty result above means the projection bridge lacks it rather than
    /// that the walk found nothing anywhere.
    /// </remarks>
    [Test]
    public void TheProjectionBridgeIsHandedTheReaderAndNeverTheAssigningContract()
    {
        var projectionParameters = typeof(IMediaTypeRuntime)
            .GetMethods()
            .SelectMany(static method => method.GetParameters())
            .Select(static parameter => parameter.ParameterType)
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(projectionParameters, Does.Contain(typeof(ICatalogIdentityReader)));
            Assert.That(projectionParameters, Does.Not.Contain(typeof(CatalogIdentity)));
            Assert.That(projectionParameters, Does.Not.Contain(Assignment));
            Assert.That(
                typeof(CatalogIdentity).GetInterfaces(),
                Does.Contain(Assignment),
                "the control: identity state really does implement the assigning contract, so its absence "
                + "from the projection bridge is a fact about the bridge");
        });
    }

    /// <summary>
    /// The catalog dispatcher's public surface answers with candidates and cannot allocate.
    /// </summary>
    /// <remarks>
    /// O-40 in the shape of the compiled surface, and the honest limit of this milestone. Naming a record
    /// and recording that the library holds it are one transaction, and only the first half exists — so the
    /// member that assigns is host-internal rather than an operation the platform offers. Everything a
    /// caller outside Host can reach is a read. The second assertion is the control: the same walk does find
    /// the assigning member among the non-public ones, so the empty public result is a fact about the
    /// surface rather than a walk that found nothing.
    /// </remarks>
    [Test]
    public void ThePublicCatalogSurfaceAnswersWithCandidatesAndNeverAssigns()
    {
        var materialized = typeof(CatalogDispatcher).Assembly
            .GetTypes()
            .Single(static type => type.Name == "MaterializedItem`1");

        var naming = (BindingFlags visibility) => typeof(CatalogDispatcher)
            .GetMethods(visibility | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => Returned(method.ReturnType).Any(type =>
                type.IsGenericType && type.GetGenericTypeDefinition() == materialized))
            .Select(static method => method.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        var publicMembers = typeof(CatalogDispatcher)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(static method => method.Name)
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(
                publicMembers,
                Is.SupersetOf(new[] { "FetchAsync", "ResolveAsync", "SearchAsync" }),
                "the read members are public, and are what a caller is offered");
            Assert.That(
                naming(BindingFlags.Public),
                Is.Empty,
                "no public member answers with a host-named item, because none of them assigns");
            Assert.That(materialized.IsPublic, Is.False, "and the value that means 'named' is host-internal");
            Assert.That(
                naming(BindingFlags.NonPublic),
                Is.EqualTo(new[] { "Materialize" }),
                "the control: assignment exists, host-internal, awaiting the transaction it belongs in");
        });
    }

    /// <summary>Every type one return type is built from, however deeply nested.</summary>
    private static IEnumerable<Type> Returned(Type type) =>
        [type, .. type.GetGenericArguments().SelectMany(Returned)];

    /// <summary>Expands the types reachable through a set of types' public member signatures.</summary>
    private static IReadOnlyCollection<Type> SignatureClosure(IEnumerable<Type> roots)
    {
        var seen = new HashSet<Type>();
        var pending = new Stack<Type>(roots);

        while (pending.Count > 0)
        {
            var current = Unwrap(pending.Pop());

            if (current is null || !seen.Add(current))
            {
                continue;
            }

            // A constructed generic carries its arguments, so those are part of the signature even when the
            // construction itself belongs to the framework.
            foreach (var argument in current.GetGenericArguments())
            {
                pending.Push(argument);
            }

            // Only Arronix types are expanded further. Walking into the framework would add nothing a rule
            // here asks about and would not terminate usefully.
            if (current.Assembly.GetName().Name?.StartsWith("Arronix", StringComparison.Ordinal) != true)
            {
                continue;
            }

            foreach (var member in current
                .GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                foreach (var type in member switch
                {
                    MethodInfo method => Signature(method),
                    PropertyInfo property => [property.PropertyType],
                    FieldInfo field => [field.FieldType],
                    ConstructorInfo constructor =>
                        constructor.GetParameters().Select(static parameter => parameter.ParameterType),
                    _ => Enumerable.Empty<Type>(),
                })
                {
                    pending.Push(type);
                }
            }
        }

        return seen;
    }

    /// <summary>The types one member's own signature carries, generic arguments included.</summary>
    private static IReadOnlyCollection<Type> Carried(MemberInfo member)
    {
        var stated = member switch
        {
            MethodBase method => Signature(method),
            PropertyInfo property => (IEnumerable<Type>)[property.PropertyType],
            FieldInfo field => [field.FieldType],
            _ => [],
        };

        return
        [
            .. stated
                .Select(Unwrap)
                .OfType<Type>()
                .SelectMany(static type => new[] { type }.Concat(type.GetGenericArguments()))
                .Select(Unwrap)
                .OfType<Type>(),
        ];
    }

    private static IEnumerable<Type> Signature(MethodBase method) =>
        [
            .. method is MethodInfo typed ? new[] { typed.ReturnType } : [],
            .. method.GetParameters().Select(static parameter => parameter.ParameterType),
        ];

    /// <summary>Reduces a signature type to the types actually carried by it.</summary>
    private static Type? Unwrap(Type type)
    {
        while (true)
        {
            if (type.IsByRef || type.IsArray || type.IsPointer)
            {
                type = type.GetElementType()!;
                continue;
            }

            if (Nullable.GetUnderlyingType(type) is { } underlying)
            {
                type = underlying;
                continue;
            }

            return type;
        }
    }
}
