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
        var assign = typeof(CatalogIdentity).GetMethod(nameof(CatalogIdentity.Identify))!;

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
