using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Arronix.Abstractions.Media;
using Arronix.Abstractions.Parsing;
using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Providers;
using Arronix.Architecture.Tests.Repository;

namespace Arronix.Architecture.Tests.Topology;

/// <summary>
/// A package ships two unlike things, and this fixture is the line between them.
/// </summary>
/// <remarks>
/// <para>
/// One installable package may carry shared contract assemblies and one isolated executable assembly.
/// They differ in exactly one way that matters, and every rule below follows from it: a shared contract
/// assembly is admitted once per installation into a Host-owned collectible context and is released only
/// once every dependant has withdrawn, so its update and unload cadence belongs to the whole dependency
/// graph rather than to its own package. An executable assembly is private to one load context and can be
/// quarantined, updated and unloaded on its own terms.
/// </para>
/// <para>
/// Put a parser, a module or a provider in the shared half and the package has traded its own cadence for
/// somebody else's convenience: that code can then be released only once every dependant has withdrawn,
/// whether or not any of them ever used it. The trade is invisible at the point it is made - everything
/// still compiles and the tests still pass - and it arrives later as an extension that cannot be reloaded
/// while a dependant is active. So the rules are asserted here, mechanically, against the declaration and
/// against the compiled metadata both.
/// </para>
/// <para>
/// What a shared contract assembly <i>may</i> contain is owner-shaped semantics and behavior that is pure
/// and deterministic: <c>MovieReleaseTimeline.StageOn</c> decides a release stage from dates it is given,
/// and <c>VideoResolution.CompareTo</c> orders rasters. Both are the media or format owner's meaning, and
/// hoisting either into Host to keep the assembly "data only" would move domain semantics out of the
/// domain that owns them. The line is not behavior versus data; it is deterministic value semantics
/// versus execution with a lifecycle.
/// </para>
/// </remarks>
[TestFixture]
public sealed class PackageFacetTopologyTests
{
    /// <summary>
    /// Types whose presence means the assembly is executable, whatever else it holds.
    /// </summary>
    /// <remarks>
    /// Each entry names a distinct lifecycle the shared half must not acquire: a module is invoked once
    /// per load and disposed last in the stop transaction; a provider is constructed by Host through the
    /// capability-scoped context; a parser and a media definition are compiled into the kind and carry the
    /// generated reader delegates with them.
    /// </remarks>
    private static readonly (Type Contract, string Reason)[] ExecutableContracts =
    [
        (typeof(IPluginModule), "a module is registration code with a per-load lifetime"),
        (typeof(IProvider), "a provider implementation is activated and owned by Host"),
        (typeof(IReleaseParser<>), "a parser is executable interpretation that churns with naming discoveries"),
        (typeof(IMediaTypeDefinition), "a media definition compiles into the kind and carries generated delegates"),
    ];

    /// <summary>
    /// Source spellings a shared contract assembly must not contain.
    /// </summary>
    /// <remarks>
    /// Read from source rather than from metadata because that is where these are legible. A regular
    /// expression, a file handle and an HTTP client are all ordinary framework types, so they leave no
    /// assembly reference behind to inspect - the shared framework is referenced either way.
    /// </remarks>
    private static readonly (string Token, string Reason)[] ForbiddenSpellings =
    [
        ("Regex", "pattern matching is recognition vocabulary, and it belongs in the isolated half"),
        ("HttpClient", "a shared contract assembly performs no I/O"),
        ("File.", "a shared contract assembly performs no I/O"),
        ("Directory.", "a shared contract assembly performs no I/O"),
        ("ModuleInitializer", "a module initializer runs on load, which is the one thing loading a contract must not do"),
        ("ThreadStatic", "process-wide mutable state in a once-loaded assembly is shared across every dependant"),
        ("AsyncLocal", "process-wide mutable state in a once-loaded assembly is shared across every dependant"),
    ];

    /// <summary>Gets the shared contract projects, for the parameterized cases below.</summary>
    public static IEnumerable<string> SharedContracts => RepositoryLayout.SharedContractProjects;

    /// <summary>Gets the media extension projects, for the parameterized cases below.</summary>
    public static IEnumerable<string> MediaExtensions => RepositoryLayout.MediaExtensionProjects;

    [Test]
    public void TheWorkingTreeContainsTheSharedContractAssembliesTheRulesAreAbout()
    {
        // Guards every parameterized case below: a rule applied to an empty discovery set reports success
        // while asserting nothing.
        Assert.Multiple(() =>
        {
            Assert.That(RepositoryLayout.SharedContractProjects, Is.Not.Empty);

            foreach (var project in RepositoryLayout.SharedContractProjects)
            {
                Assert.That(
                    RepositoryLayout.ProjectExists(project),
                    Is.True,
                    $"'{project}' is listed as a shared contract assembly but is not in the working tree.");
            }
        });
    }

    /// <remarks>
    /// The permitted set is deliberately tiny. A shared assembly's own dependencies are shared too - the
    /// method bodies of a shared type bind against whatever that assembly resolved - so every reference
    /// here is a second thing the installation must resolve to exactly one copy. Abstractions already is
    /// that, and another shared contract assembly can be made to be. Nothing else can.
    /// </remarks>
    /// <param name="projectName">The shared contract project under test.</param>
    [Test]
    [TestCaseSource(nameof(SharedContracts))]
    public void SharedContractProjectReferencesOnlyContractsAndOtherSharedContracts(string projectName)
    {
        var project = ProjectFile.Load(projectName);
        var permitted = RepositoryLayout.SharedContractProjects
            .Where(other => !string.Equals(other, projectName, StringComparison.Ordinal))
            .Append(RepositoryLayout.Abstractions)
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(
                project.RuntimeProjectReferences,
                Is.SubsetOf(permitted),
                $"'{projectName}' is loaded once for the whole installation, so anything it references has "
                + "to be resolvable to one copy as well.");

            Assert.That(
                project.RuntimeProjectReferences,
                Does.Contain(RepositoryLayout.Abstractions),
                $"'{projectName}' closes generics declared in the universal contracts, so it references them.");

            // An analyzer produces source, never a runtime dependency, so it is exempt from the rule above
            // and held to its own: exactly one, and it is this repository's generator.
            Assert.That(
                project.AnalyzerProjectReferences,
                Is.SubsetOf(new[] { RepositoryLayout.Generators }),
                $"'{projectName}' takes an analyzer that is not the Arronix generator.");

            Assert.That(
                project.PackageReferences,
                Is.Empty,
                $"'{projectName}' must take no package. Its bytes are the unit a dynamically loaded client "
                + "would fetch, and a package taken here is a second copy of something the host already owns.");
        });
    }

    /// <remarks>
    /// <para>
    /// Stated as an explicit deny list rather than left implicit in the subset rule above, because these
    /// are the references whose absence a reader is actually asking about. Host and the loader would make
    /// the assembly unloadable outside this repository at all, and the executable half of its own package
    /// would invert the one-way direction the whole split rests on.
    /// </para>
    /// <para>
    /// The generator is denied as a <i>runtime</i> reference and permitted as an analyzer. What the earlier
    /// rule was protecting against was a media definition moving here and bringing its compiled reader
    /// delegates onto the shared cadence, and that is asserted directly by
    /// <see cref="SharedContractAssemblyDeclaresNoExecutablePlatformType"/> and
    /// <see cref="SharedContractAssemblyHoldsNoMutableOrExecutableStaticState"/>. A generator whose output
    /// holds no delegate, runs nothing on load and declares no definition acquires no cadence at all, and
    /// it is how a contract says what it holds to a browser that may not enumerate it.
    /// </para>
    /// </remarks>
    /// <param name="projectName">The shared contract project under test.</param>
    [Test]
    [TestCaseSource(nameof(SharedContracts))]
    public void SharedContractProjectReferencesNoHostLoaderOrExecutableProject(string projectName)
    {
        var project = ProjectFile.Load(projectName);
        var forbidden = new[]
        {
            RepositoryLayout.Common,
            RepositoryLayout.Plugins,
            RepositoryLayout.Host,
            RepositoryLayout.Api,
            RepositoryLayout.Client,
            RepositoryLayout.VideoFormatContributions,
            RepositoryLayout.ReferenceLanguages,
        }.Concat(RepositoryLayout.MediaExtensionProjects).ToArray();

        var offenders = project.ProjectReferences.Intersect(forbidden, StringComparer.Ordinal)
            .Concat(project.RuntimeProjectReferences.Intersect(
                new[] { RepositoryLayout.Generators }, StringComparer.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.That(
            offenders,
            Is.Empty,
            $"'{projectName}' references an executable or platform project. The shared half is what a "
            + "dependant is pinned to; reaching an executable project from it would pin that too.");
    }

    /// <remarks>
    /// The binary half. The compiler emits an assembly reference only for an assembly a type is actually
    /// used from, so a declared-but-unused reference is invisible here and is caught by the declaration
    /// rule above; a reference that arrives through a target rather than through an author is invisible
    /// there and is caught here. The loader judges the binary.
    /// </remarks>
    /// <param name="projectName">The shared contract project under test.</param>
    [Test]
    [TestCaseSource(nameof(SharedContracts))]
    public void SharedContractAssemblyLinksNoArronixAssemblyOutsideTheSharedSet(string projectName)
    {
        var permitted = RepositoryLayout.SharedContractProjects
            .Append(RepositoryLayout.Abstractions)
            .ToArray();

        var linked = Load(projectName)
            .GetReferencedAssemblies()
            .Select(static name => name.Name ?? string.Empty)
            .Where(static name => name.StartsWith("Arronix.", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.That(
            linked,
            Is.SubsetOf(permitted),
            $"The compiled '{projectName}' links an assembly outside the shared set.");
    }

    /// <remarks>
    /// The rule the whole split exists for. Each contract in the list is a lifecycle, not a style
    /// preference: sharing any of them would put code that is supposed to be quarantinable, updatable and
    /// unloadable on its own terms onto the shared assembly's cadence, where it can be released only once
    /// every dependant of the package has withdrawn.
    /// </remarks>
    /// <param name="projectName">The shared contract project under test.</param>
    [Test]
    [TestCaseSource(nameof(SharedContracts))]
    public void SharedContractAssemblyDeclaresNoExecutablePlatformType(string projectName)
    {
        var declared = Load(projectName).GetTypes();

        var offenders = declared
            .SelectMany(type => ExecutableContracts
                .Where(entry => Implements(type, entry.Contract))
                .Select(entry => $"{type.FullName} : {entry.Contract.Name} - {entry.Reason}"))
            .Concat(declared
                .Where(IsMediaDefinition)
                .Select(type => $"{type.FullName} derives MediaType - a definition is compiled into the kind"))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.That(offenders, Is.Empty, $"'{projectName}' declares an executable platform type.");
    }

    /// <remarks>
    /// <para>
    /// Two static shapes are refused. A writable static field is mutable process state, and in an assembly
    /// loaded once per installation that state is shared by every dependant that never agreed to share it -
    /// the exact defect the isolation model exists to prevent, and one that presents as unrelated packages
    /// interfering rather than as any kind of error.
    /// </para>
    /// <para>
    /// A static field holding a delegate is refused for the same reason plus one more: a delegate captures
    /// the assembly that created it, so a shared assembly holding one roots code the installation is
    /// otherwise entitled to unload.
    /// </para>
    /// <para>
    /// A <c>static readonly</c> value is allowed, and that is the point of the distinction: constants and
    /// canonical instances such as <c>ReleaseRevision.Initial</c> are owner-shaped meaning, not state.
    /// </para>
    /// <para>
    /// Compiler-generated members are excluded, and the exclusion is narrow on purpose. C# caches a static
    /// lambda in a generated holder field that is written once on first use; every assembly containing a
    /// lambda has them, they are unreachable and unobservable, and refusing them would mean refusing
    /// lambdas - which would refuse <c>VideoReleasePolicyDefaults</c>, whose whole job is to hand a
    /// dependant's policy builder a few deterministic selectors. The rule is about state an author holds.
    /// </para>
    /// </remarks>
    /// <param name="projectName">The shared contract project under test.</param>
    [Test]
    [TestCaseSource(nameof(SharedContracts))]
    public void SharedContractAssemblyHoldsNoMutableOrExecutableStaticState(string projectName)
    {
        var fields = Load(projectName)
            .GetTypes()
            .Where(static type => !IsCompilerGenerated(type))
            .SelectMany(static type => type.GetFields(
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            .Where(static field => !IsCompilerGenerated(field))
            .ToArray();

        var offenders = fields
            .Where(static field => !field.IsInitOnly && !field.IsLiteral)
            .Select(static field => $"{field.DeclaringType?.FullName}.{field.Name} is a writable static field")
            .Concat(fields
                .Where(static field => typeof(Delegate).IsAssignableFrom(field.FieldType))
                .Select(static field => $"{field.DeclaringType?.FullName}.{field.Name} is a static delegate"))
            .Concat(fields
                .Where(static field => IsEditableCollection(field.FieldType))
                .Select(static field =>
                    $"{field.DeclaringType?.FullName}.{field.Name} is a static collection a caller can edit"))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.That(offenders, Is.Empty, $"'{projectName}' holds static state a shared assembly must not.");
    }

    /// <summary>
    /// Determines whether a field's declared type is a collection a caller can edit through it.
    /// </summary>
    /// <param name="type">The field type.</param>
    /// <returns><see langword="true"/> when the value is shared and mutable.</returns>
    /// <remarks>
    /// A <c>static readonly</c> field only stops the field being reassigned. A shared assembly's static
    /// value is process-global, so an array or a mutable collection behind it is state every dependant can
    /// change — which is exactly the defect the wrapper on <c>VideoFormat</c>'s extension list closed.
    /// </remarks>
    private static bool IsEditableCollection(Type type)
    {
        if (type.IsArray)
        {
            return true;
        }

        if (!type.IsGenericType)
        {
            return false;
        }

        var isCollection = type.GetInterfaces().Any(static contract =>
            contract.IsGenericType
            && contract.GetGenericTypeDefinition() == typeof(ICollection<>));

        if (!isCollection)
        {
            return false;
        }

        var definition = type.GetGenericTypeDefinition();

        return definition != typeof(ReadOnlyCollection<>)
            && !definition.Namespace!.StartsWith("System.Collections.Immutable", StringComparison.Ordinal)
            && !definition.Namespace.StartsWith("System.Collections.Frozen", StringComparison.Ordinal);
    }

    /// <remarks>
    /// Nothing may run when the assembly is loaded. Admission resolves a shared contract assembly before
    /// any dependant is prepared, and the G02 transaction's guarantee is that a failed attempt leaves
    /// nothing behind; a module initializer would have run before that transaction could refuse anything.
    /// </remarks>
    /// <param name="projectName">The shared contract project under test.</param>
    [Test]
    [TestCaseSource(nameof(SharedContracts))]
    public void SharedContractAssemblyRunsNothingWhenItIsLoaded(string projectName)
    {
        var offenders = Load(projectName)
            .GetTypes()
            .SelectMany(static type => type.GetMethods(
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            .Where(static method => method.GetCustomAttribute<ModuleInitializerAttribute>() is not null)
            .Select(static method => $"{method.DeclaringType?.FullName}.{method.Name}")
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.That(offenders, Is.Empty, $"'{projectName}' runs code when it is loaded.");
    }

    /// <summary>
    /// A source-text screen for spellings that do not belong in a shared assembly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A heuristic, and stated as one. It catches the spellings an author would reach for — a regular
    /// expression, a file or network call, an ambient clock — where they are written literally, which is how
    /// they arrive in practice. It does not prove their absence: the same behavior reached through
    /// reflection, an alias, a computed type name or a helper in another assembly would pass.
    /// </para>
    /// <para>
    /// The rules that do prove something are the structural ones above: no executable platform type, no
    /// mutable or editable static state, nothing running on load, and a reference closure limited to the
    /// universal contracts. This screen is a cheap early warning in front of them, not a substitute.
    /// </para>
    /// </remarks>
    /// <param name="projectName">The shared contract project under test.</param>
    [Test]
    [TestCaseSource(nameof(SharedContracts))]
    public void SharedContractSourceCarriesNoRecognitionOrIoSpelling(string projectName)
    {
        var code = SourceScanner.CodeLines(projectName).ToArray();

        Assert.That(code, Is.Not.Empty, $"No source was read for '{projectName}', so this rule found nothing.");

        var offenders = code
            .SelectMany(entry => ForbiddenSpellings
                .Where(forbidden => entry.Text.Contains(forbidden.Token, StringComparison.Ordinal))
                .Select(forbidden => $"{entry.File}:{entry.Line}: {forbidden.Token} - {forbidden.Reason}"))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.That(offenders, Is.Empty, $"'{projectName}' contains a spelling a shared assembly must not.");
    }

    /// <remarks>
    /// The other direction of the same rule. The split buys nothing if the shared half is correct but the
    /// executable half is empty, so the assemblies that were supposed to stay isolated are checked for
    /// still holding the things that were supposed to stay there.
    /// </remarks>
    [Test]
    public void TheIsolatedAssembliesStillHoldTheExecutableCode()
    {
        var movies = Load(RepositoryLayout.MoviesExtension).GetTypes();
        var video = AssemblyMetadata.PublicTypes(RepositoryLayout.VideoFormatContributions);

        Assert.Multiple(() =>
        {
            Assert.That(
                movies.Where(type => Implements(type, typeof(IPluginModule))),
                Is.Not.Empty,
                "the movies module belongs to the isolated entry assembly");
            Assert.That(
                movies.Where(IsMediaDefinition),
                Is.Not.Empty,
                "the Movies definition and its generated shape catalog belong to the isolated entry assembly");
            Assert.That(
                movies.Where(type => Implements(type, typeof(IReleaseParser<>))),
                Is.Not.Empty,
                "the movie release parser belongs to the isolated entry assembly");
            Assert.That(
                video.Select(static type => type.Name),
                Does.Contain("VideoReleaseVocabulary"),
                "video's release-term recognition vocabulary reads text and belongs to the isolated half");
            Assert.That(
                AssemblyMetadata.PublicTypes(RepositoryLayout.VideoFormat).Select(static type => type.Name),
                Is.SupersetOf(new[] { "VideoFormat", "VideoReleasePolicyDefaults" }),
                "while the family definition and the policy defaults are domain semantics a media "
                + "declaration composes, so they are in the domain assembly it already references");
        });
    }

    /// <remarks>
    /// An extension may reference the universal contracts, the format contracts it composes, the isolated
    /// format assemblies whose defaults it compiles in, and its own media domain. It may not reference
    /// another kind's media domain: pairing runs through Host, not through one extension calling another.
    /// </remarks>
    /// <param name="projectName">The media extension under test.</param>
    [Test]
    [TestCaseSource(nameof(MediaExtensions))]
    public void MediaExtensionReferencesNoOtherKindsMediaDomain(string projectName)
    {
        var project = ProjectFile.Load(projectName);
        var own = RepositoryLayout.MediaDomainOf(projectName);

        var offenders = project.ProjectReferences
            .Where(static name => name.StartsWith(RepositoryLayout.MediaDomainPrefix, StringComparison.Ordinal))
            .Where(name => !string.Equals(name, own, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.That(
            offenders,
            Is.Empty,
            $"'{projectName}' references a media domain it does not own. A dependency between packages is "
            + "a type edge and a lifecycle edge; it is never a call edge between two extensions.");
    }

    /// <summary>
    /// The same rule read from the compiled reference table rather than from the project file.
    /// </summary>
    /// <remarks>
    /// The declaration rule above reads project XML, which a raw <c>Reference</c>, an imported targets file
    /// or a package that brought the assembly along could bypass without editing a <c>ProjectReference</c>.
    /// The compiled reference table is what the runtime actually binds, so a reference that arrived by any
    /// route at all is visible here.
    /// </remarks>
    /// <param name="projectName">The extension under test.</param>
    [Test]
    [TestCaseSource(nameof(MediaExtensions))]
    public void MediaExtensionLinksNoOtherKindsMediaDomain(string projectName)
    {
        var own = RepositoryLayout.MediaDomainOf(projectName);

        var linked = Load(projectName)
            .GetReferencedAssemblies()
            .Select(static name => name.Name ?? string.Empty)
            .ToArray();

        Assert.Multiple(() =>
        {
            // Without this, an unreadable reference table would satisfy the rule by containing nothing.
            Assert.That(linked, Is.Not.Empty, $"No reference table was read for '{projectName}'.");

            Assert.That(
                linked
                    .Where(static name => name.StartsWith(
                        RepositoryLayout.MediaDomainPrefix,
                        StringComparison.Ordinal))
                    .Where(name => !string.Equals(name, own, StringComparison.Ordinal))
                    .Order(StringComparer.Ordinal),
                Is.Empty,
                $"The compiled '{projectName}' links a media domain it does not own, whatever its project "
                + "file says.");
        });
    }

    /// <summary>
    /// Assemblies that hold a package's executable half and that nothing outside that package may reference.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A media declaration composes a format by naming its family and its policy defaults. Both are domain
    /// semantics and both live in the format's domain assembly, so a declaration never needs the format's
    /// executable half - and taking it anyway would copy an independently updatable assembly into the
    /// declaring package's payload and pin the two together for no gain.
    /// </para>
    /// <para>
    /// These two rules read the declaration and the compiled reference table. What actually lands in a
    /// package payload is asserted where the payload is defined - the staged fixtures in
    /// <c>Arronix.Host.Tests</c>, which clear and re-copy their directories - because a project's own
    /// <c>bin</c> can keep a file that a removed reference stopped producing, and a rule that read it could
    /// fail on yesterday's build.
    /// </para>
    /// </remarks>
    private static readonly string[] FormatExecutableProjects = [RepositoryLayout.VideoFormatContributions];

    /// <param name="projectName">The extension under test.</param>
    [Test]
    [TestCaseSource(nameof(MediaExtensions))]
    public void MediaExtensionDeclaresNoReferenceToAFormatExecutableAssembly(string projectName)
    {
        var project = ProjectFile.Load(projectName);

        var offenders = project.ProjectReferences
            .Intersect(FormatExecutableProjects, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.That(
            offenders,
            Is.Empty,
            $"'{projectName}' declares a reference to a format's executable half. Everything a media "
            + "declaration needs from a format is in that format's domain assembly.");
    }

    /// <remarks>
    /// The binary half of the same rule, and the one that catches what a project file cannot say: an
    /// assembly reference introduced by a target, or a transitive reference arriving through some other
    /// project. The loader judges the reference table, so the reference table is what is asserted.
    /// </remarks>
    /// <param name="projectName">The extension under test.</param>
    [Test]
    [TestCaseSource(nameof(MediaExtensions))]
    public void MediaExtensionLinksNoFormatExecutableAssembly(string projectName)
    {
        var linked = Load(projectName)
            .GetReferencedAssemblies()
            .Select(static name => name.Name ?? string.Empty)
            .ToArray();

        Assert.Multiple(() =>
        {
            // Without this, an unreadable reference table would satisfy the rule by containing nothing.
            Assert.That(linked, Is.Not.Empty, $"No reference table was read for '{projectName}'.");

            Assert.That(
                linked.Intersect(FormatExecutableProjects, StringComparer.Ordinal).Order(StringComparer.Ordinal),
                Is.Empty,
                $"The compiled '{projectName}' links a format's executable half.");
        });
    }

    /// <remarks>
    /// One name, one declaring assembly, one namespace. This is what "one canonical CLR identity" means
    /// before a loader is involved: if a second declaration of the same full name existed anywhere in the
    /// solution - a forwarded type, a duplicated shape kept for compatibility, a copy in the executable
    /// half - the two would print identically and never cast, which is the failure the package split is
    /// there to prevent. The check runs over every Arronix assembly loaded into this test process, so a
    /// duplicate would have to not be built at all to escape it.
    /// </remarks>
    [Test]
    public void EveryPackageDomainTypeIsDeclaredExactlyOnceAcrossTheSolution()
    {
        var domainTypes = RepositoryLayout.SharedContractProjects
            .SelectMany(AssemblyMetadata.PublicTypes)
            .Select(static type => type.FullName)
            .ToHashSet(StringComparer.Ordinal);

        Assert.That(domainTypes, Is.Not.Empty, "no domain types were read, so this rule found nothing");

        var duplicates = RepositoryLayout.AllProjects
            .Where(project => !RepositoryLayout.SharedContractProjects.Contains(project, StringComparer.Ordinal))
            .SelectMany(AssemblyMetadata.PublicTypes)
            .Where(type => domainTypes.Contains(type.FullName))
            .Select(static type => $"{type.FullName} is declared again in {type.Assembly}")
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.That(duplicates, Is.Empty, "a package domain type has a second declaration.");
    }

    /// <remarks>
    /// The movies half of the same rule, spelled out because it is the one an author reads. The item type a
    /// separately shipped cataloger closes its generic over is <c>Arronix.Media.Movies.Movie</c>, declared
    /// once, in the media domain assembly, in the namespace that names the domain rather than the mechanism
    /// that delivers it. No forwarding type and no compatibility shape keeps the old spelling alive.
    /// </remarks>
    [Test]
    public void TheMoviesDomainDeclaresItsTypesOnceUnderTheMediaNamespace()
    {
        var declared = AssemblyMetadata.PublicTypes(RepositoryLayout.MoviesDomain);
        const string DomainNamespace = "Arronix.Media.Movies";

        Assert.Multiple(() =>
        {
            Assert.That(
                declared.Select(static type => type.FullName),
                Is.EqualTo(new[]
                {
                    DomainNamespace + ".Movie",
                    DomainNamespace + ".MovieReleaseStage",
                    DomainNamespace + ".MovieReleaseTimeline",
                }),
                "the movies domain publishes exactly the item type and the lifecycle it closes over");

            Assert.That(
                RepositoryLayout.AllProjects
                    .SelectMany(AssemblyMetadata.PublicTypes)
                    .Where(static type => type.Namespace == "Arronix.Plugin.Movies")
                    .Select(static type => type.Name)
                    .Order(StringComparer.Ordinal),
                Is.EqualTo(new[] { "Movies", "MoviesPluginModule" }),
                "and nothing domain-shaped is left behind in the extension's namespace");
        });
    }

    /// <remarks>
    /// The naming half of the same boundary. A shared domain type is spelled in its package's domain
    /// namespace and an executable-only type is not, so the assembly a reader is looking at is legible from
    /// the using site and not only from the project file. The rule is checked both ways: nothing executable
    /// hides inside a domain namespace, and nothing in the domain namespace ships from the executable half.
    /// </remarks>
    [Test]
    public void TheVideoPackageSpellsItsTwoHalvesInTwoNamespaces()
    {
        var domain = AssemblyMetadata.PublicTypes(RepositoryLayout.VideoFormat);
        var executable = AssemblyMetadata.PublicTypes(RepositoryLayout.VideoFormatContributions);
        const string DomainNamespace = "Arronix.Format.Video";
        const string ExecutableNamespace = "Arronix.Format.Video.Contributions";

        Assert.Multiple(() =>
        {
            Assert.That(domain, Is.Not.Empty);
            Assert.That(executable, Is.Not.Empty);

            Assert.That(
                domain.Select(static type => type.Namespace).Distinct(),
                Is.EqualTo(new[] { DomainNamespace }),
                "every public type in the video domain assembly is spelled in the domain namespace");

            Assert.That(
                executable.Select(static type => type.Namespace).Distinct(),
                Is.EqualTo(new[] { ExecutableNamespace }),
                "and every public type in its executable half is spelled in the Contributions namespace");
        });
    }

    /// <summary>
    /// Matches an item that reaches into another project's build output directory.
    /// </summary>
    private static readonly Regex ReachesIntoAnotherProjectsOutput = new(
        @"Include\s*=\s*""[^""]*\.\.[\\/][^""\\/]+[\\/](bin|obj)[\\/]",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        TimeSpan.FromSeconds(1));

    /// <summary>Gets every project this delivery owns, for the parameterized case below.</summary>
    public static IEnumerable<string> AllProjects => RepositoryLayout.AllProjects;

    /// <remarks>
    /// <para>
    /// A package payload is staged by publishing, which computes the runtime closure from the current
    /// reference set. Listing another project's <c>bin</c> instead would look equivalent and is not: MSBuild
    /// does not delete an assembly that a removed <c>ProjectReference</c> stopped producing, so that folder
    /// can hold a file the project no longer depends on, and a recursive copy carries it into the payload
    /// where every downstream assertion then reports a dependency that does not exist.
    /// </para>
    /// <para>
    /// The payload rules in <c>Arronix.Host.Tests</c> catch the resulting file. This rule catches the cause,
    /// which is worth having separately: the failure it prevents is one where nothing breaks.
    /// </para>
    /// </remarks>
    /// <param name="projectName">The project under test.</param>
    [Test]
    [TestCaseSource(nameof(AllProjects))]
    public void ProjectStagesNothingByReadingAnotherProjectsBuildOutput(string projectName)
    {
        var project = ProjectFile.Load(projectName);

        var offenders = project.Text
            .Split('\n')
            .Select(static (line, index) => (Line: index + 1, Text: line))
            .Where(entry => ReachesIntoAnotherProjectsOutput.IsMatch(entry.Text))
            .Select(entry => $"{projectName}.csproj:{entry.Line}: {entry.Text.Trim()}")
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.That(
            offenders,
            Is.Empty,
            $"'{projectName}' stages files by listing another project's output directory. Publish that "
            + "project to the staging directory instead, so the payload is the computed reference closure "
            + "rather than whatever the directory happens to still contain.");
    }

    private static bool IsCompilerGenerated(MemberInfo member) =>
        member.GetCustomAttribute<CompilerGeneratedAttribute>() is not null
        || (member.DeclaringType is { } declaring
            && declaring != member
            && IsCompilerGenerated(declaring));

    private static bool IsMediaDefinition(Type type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(MediaType<,,,>))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Implements(Type type, Type contract) =>
        type.GetInterfaces().Any(implemented =>
            implemented == contract
            || (implemented.IsGenericType && implemented.GetGenericTypeDefinition() == contract));

    private static Assembly Load(string projectName)
    {
        try
        {
            return Assembly.Load(new AssemblyName(projectName));
        }
        catch (Exception failure) when (failure is System.IO.FileNotFoundException or BadImageFormatException)
        {
            Assert.Fail($"'{projectName}' could not be loaded from the test output: {failure.Message}.");
            throw;
        }
    }
}
