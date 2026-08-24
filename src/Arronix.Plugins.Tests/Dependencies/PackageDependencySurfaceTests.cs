using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Plugins;
using Arronix.Plugins.Dependencies;
using Arronix.Plugins.Versioning;

namespace Arronix.Plugins.Tests.Dependencies;

/// <summary>
/// What the dependency graph is allowed to be made of.
/// </summary>
/// <remarks>
/// <para>
/// Three constraints, each of which would be expensive to recover once broken. It stays internal, because
/// the manifest declaration is the only shape an extension author writes; a resolved graph is Host
/// infrastructure and never becomes authoring vocabulary by being reachable. It reuses the platform's
/// identifier, version and range types rather than growing its own. And nothing it holds is a CLR type, an
/// assembly or a media-kind identity — a package graph decides what may be activated, and deciding what a
/// package means is for the code that has been allowed to run.
/// </para>
/// <para>
/// The third rule is enforced structurally rather than by reading the implementation: if no member of the
/// namespace can accept, return or hold a <see cref="Type"/>, an assembly or a
/// <see cref="MediaKindId"/>, then no amount of later editing inside it can turn a media-kind string back
/// into a CLR type without failing this fixture first.
/// </para>
/// </remarks>
[TestFixture]
public sealed class PackageDependencySurfaceTests
{
    private const string Namespace = "Arronix.Plugins.Dependencies";

    private const BindingFlags AllDeclared =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static
        | BindingFlags.DeclaredOnly;

    /// <summary>
    /// The types a package graph has no business holding.
    /// </summary>
    private static readonly Type[] Forbidden =
    [
        typeof(Type),
        typeof(Assembly),
        typeof(AssemblyName),
        typeof(AssemblyLoadContext),
        typeof(MediaKindId)
    ];

    private static Type[] DeclaredTypes => [.. typeof(PackageDependencyEngine).Assembly
        .GetTypes()
        .Where(static type => string.Equals(type.Namespace, Namespace, StringComparison.Ordinal))];

    [Test]
    public void TheNamespaceIsNotEmpty()
    {
        // Without this the other three rules would pass while checking nothing.
        DeclaredTypes.Should().NotBeEmpty();
        DeclaredTypes.Select(static type => type.Name).Should().Contain(nameof(PackageDependencyEngine));
    }

    [Test]
    public void EveryCandidateTypeIsInternal()
    {
        DeclaredTypes
            .Where(static type => type.IsVisible)
            .Select(static type => type.FullName)
            .Should().BeEmpty("a resolved package graph is Host infrastructure, never authoring contract");
    }

    [Test]
    public void NoMemberHoldsAClrTypeAnAssemblyOrAMediaKindIdentity()
    {
        var offenders = DeclaredTypes
            .SelectMany(static type => type.GetMembers(AllDeclared).Select(member => (type, member)))
            // A record's compiler-generated EqualityContract returns the record's own Type. It is the
            // language's equality plumbing rather than a member holding a CLR type as data, and a rule that
            // counted it would forbid records in this namespace for no reason anyone could act on.
            .Where(static entry => !entry.member.Name.EndsWith("EqualityContract", StringComparison.Ordinal))
            .SelectMany(static entry => SignatureTypes(entry.member)
                .SelectMany(Flatten)
                .Where(static used => Forbidden.Contains(used))
                .Select(used => $"{entry.type.Name}.{entry.member.Name}: {used.Name}"))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        offenders.Should().BeEmpty();
    }

    /// <summary>
    /// Availability is a closed state with exactly the members the platform can produce.
    /// </summary>
    /// <remarks>
    /// The rule this pins is "do not generalize speculative reasons". A caller-supplied string, or a member
    /// for a state nothing can reach, would both make the graph carry a semantics it cannot act on, and
    /// would let two callers spell one state two ways. Adding a member is allowed the day something can
    /// actually produce it — and requires editing this line, deliberately.
    /// </remarks>
    [Test]
    public void AvailabilityIsAClosedStateWithNoSpeculativeMembers()
        => Enum.GetNames<PackageAvailability>()
            .Should().Equal("Available", "DisabledByConfiguration");

    /// <summary>
    /// An available package has no reason to render, and asking for one is a defect rather than an empty
    /// string.
    /// </summary>
    [Test]
    public void RenderingAReasonForAnAvailablePackageIsRefused()
    {
        var describe = () => PackageAvailabilityReason.Describe(PackageAvailability.Available);

        describe.Should().Throw<ArgumentOutOfRangeException>();
    }

    /// <summary>
    /// Every unavailable state renders one phrase, and the graph is the only thing that reads it.
    /// </summary>
    [Test]
    public void EveryUnavailableStateRendersAPhrase()
    {
        foreach (var state in Enum.GetValues<PackageAvailability>().Where(static state => state != PackageAvailability.Available))
        {
            PackageAvailabilityReason.Describe(state).Should().NotBeNullOrWhiteSpace();
        }
    }

    [Test]
    public void TheGraphDeclaresNoSecondVersionParser()
    {
        DeclaredTypes
            .SelectMany(static type => type.GetMethods(AllDeclared).Select(method => (type, method)))
            .Where(static entry => entry.method.Name is "Parse" or "TryParse")
            .Select(static entry => $"{entry.type.Name}.{entry.method.Name}")
            .Should().BeEmpty("versions and ranges are read by VersionRangeParser and by nothing else");

        DeclaredTypes
            .Select(static type => type.Name)
            .Where(static name => name.Contains("Version", StringComparison.Ordinal)
                || name.Contains("Range", StringComparison.Ordinal))
            .Should().BeEmpty("a second version or range model would be a second meaning for a version");
    }

    [Test]
    public void TheGraphIsBuiltOnThePlatformsOwnIdentifierVersionAndRangeTypes()
    {
        var used = DeclaredTypes
            .SelectMany(static type => type.GetMembers(AllDeclared))
            .SelectMany(SignatureTypes)
            .SelectMany(Flatten)
            .ToHashSet();

        used.Should().Contain(typeof(PluginId));
        used.Should().Contain(typeof(SemanticVersion));
        used.Should().Contain(typeof(VersionRange));
    }

    private static IEnumerable<Type> SignatureTypes(MemberInfo member)
    {
        switch (member)
        {
            case MethodInfo method:
                return method.GetParameters()
                    .Select(static parameter => parameter.ParameterType)
                    .Append(method.ReturnType);
            case ConstructorInfo constructor:
                return constructor.GetParameters().Select(static parameter => parameter.ParameterType);
            case PropertyInfo property:
                return new[] { property.PropertyType };
            case FieldInfo field:
                return new[] { field.FieldType };
            default:
                return [];
        }
    }

    private static IEnumerable<Type> Flatten(Type type)
    {
        yield return type;

        if (type.HasElementType && type.GetElementType() is { } element)
        {
            foreach (var inner in Flatten(element))
            {
                yield return inner;
            }
        }

        foreach (var argument in type.GetGenericArguments())
        {
            foreach (var inner in Flatten(argument))
            {
                yield return inner;
            }
        }
    }
}
