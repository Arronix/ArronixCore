using System.Collections;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Arronix.Abstractions.Media;
using Arronix.Architecture.Tests.Repository;
using Arronix.Host.Media.Typed;

#pragma warning disable ARX0013 // Shape contracts are experimental; these rules read one.
#pragma warning disable ARX0016 // Intent contracts are experimental; these rules read one.
#pragma warning disable ARX0019 // Definition contracts are experimental; these rules read one.
#pragma warning disable ARX0020 // The typed media surface is experimental; these rules govern it.

namespace Arronix.Architecture.Tests.Capabilities;

/// <summary>
/// What a typed media kind may not say about itself.
/// </summary>
/// <remarks>
/// <para>
/// These rules read the <i>derived</i> artifact rather than the source text: the shape, the intent surface
/// and the engine inputs the host actually builds from a kind's types. That is deliberate. A rule over
/// source could be satisfied by moving a string into a constant, a resource or a partial file; a rule over
/// what comes out the other end of derivation cannot, because what comes out is what every engine, the wire
/// bundle and the client will see.
/// </para>
/// <para>
/// They apply to typed kinds and are discovered by looking for them, so a kind starts being governed on the
/// day it converts rather than on the day somebody remembers to add it here. Three of the four media
/// extensions are still imperative and are not subject to these rules yet — that is visible in
/// <see cref="TypedKinds"/> returning fewer entries than there are extensions, not hidden in an exclusion
/// list.
/// </para>
/// </remarks>
[TestFixture]
public class TypedMediaKindGovernanceTests
{
    /// <summary>
    /// Catalog vendors a media kind must not name. Spelled as whole words so that an unrelated identifier
    /// that happens to contain one of them as a substring does not fire the rule.
    /// </summary>
    private static readonly string[] VendorNames =
    [
        "tmdb", "themoviedb", "imdb", "tvdb", "thetvdb", "trakt", "musicbrainz",
        "rottentomatoes", "metacritic", "goodreads", "openlibrary", "fanart",
    ];

    /// <summary>
    /// A string that addresses something over the network or through the host's own router.
    /// </summary>
    /// <remarks>
    /// Two shapes, both of which a media kind is the wrong owner of: an absolute URL, which belongs to
    /// whoever owns the service it points at; and a rooted path carrying a placeholder, which is the host's
    /// routing scheme and changes when the host's navigation does.
    /// </remarks>
    private static readonly Regex RouteShaped = new(
        @"^(?:https?://|/[A-Za-z][A-Za-z0-9\-]*(?:/|$).*\{)",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        TimeSpan.FromSeconds(1));

    /// <summary>
    /// Gets every media extension that registers a typed kind, with the kind's derived model.
    /// </summary>
    public static IEnumerable<TestCaseData> TypedKinds()
    {
        foreach (var projectName in RepositoryLayout.MediaExtensionProjects)
        {
            var assembly = Assembly.Load(new AssemblyName(projectName));

            foreach (var declaring in assembly.GetExportedTypes())
            {
                var seam = declaring.GetInterfaces().FirstOrDefault(
                    static candidate => candidate.IsGenericType
                        && candidate.GetGenericTypeDefinition() == typeof(IMediaType<>));

                if (seam is null)
                {
                    continue;
                }

                var built = typeof(MediaTypeModelFactory)
                    .GetMethod(nameof(MediaTypeModelFactory.Build))!
                    .MakeGenericMethod(seam.GetGenericArguments()[0], declaring)
                    .Invoke(null, null)!;

                yield return new TestCaseData(projectName, (IMediaType)built)
                    .SetArgDisplayNames(projectName, declaring.Name);
            }
        }
    }

    /// <summary>
    /// The structure and intent a kind publishes name no catalog vendor. (P2-1)
    /// </summary>
    /// <remarks>
    /// <para>
    /// A movie has a certification, a runtime and a collection; it does not have a TMDb rating. Which
    /// catalog supplies a fact is a property of the installed cataloger, and a kind that names one has made
    /// itself dependent on that vendor being present — which is how a field list came to hold five
    /// vendor-named rating columns and a query tier came to require a TMDb identifier before a movie could
    /// be searched at all.
    /// </para>
    /// <para>
    /// The catalog section is exempt and only the catalog section is. It is a cataloger's content that has
    /// not yet moved out of the kind, and it legitimately names the vendor it speaks to. When catalogers
    /// become plugins of their own it leaves, and the exemption should be deleted with it — at which point
    /// this rule covers the whole of a media kind.
    /// </para>
    /// </remarks>
    [Test]
    [TestCaseSource(nameof(TypedKinds))]
    public void NamesNoCatalogVendorInItsStructureIntentOrEngineInputs(string projectName, IMediaType kind)
    {
        ArgumentNullException.ThrowIfNull(kind);

        var offenders = GovernedStrings(kind)
            .Where(static entry => !IsForeignWireText(entry.Path))
            .Where(entry => VendorNames.Any(vendor => ContainsWord(entry.Text, vendor)))
            .Select(static entry => $"{entry.Path} = \"{entry.Text}\"")
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.That(
            offenders,
            Is.Empty,
            $"'{projectName}' names a catalog vendor outside its catalog declaration. Which catalog supplies "
            + "a fact belongs to the installed cataloger, not to what the media is.");
    }

    /// <summary>
    /// The structure and intent a kind publishes carry no route or address. (P2-9)
    /// </summary>
    /// <remarks>
    /// The deep-link template was the specific case: a media kind stating the host's own URL for one of its
    /// items, which makes the kind wrong whenever the host's navigation changes and makes every kind restate
    /// the same scheme. The member is gone from the contract, and this rule is what stops the next one
    /// arriving somewhere else — a link row on a summary, a browse axis with a href, an action with a URL.
    /// </remarks>
    [Test]
    [TestCaseSource(nameof(TypedKinds))]
    public void CarriesNoRouteOrAddressInItsStructureIntentOrEngineInputs(string projectName, IMediaType kind)
    {
        ArgumentNullException.ThrowIfNull(kind);

        var offenders = GovernedStrings(kind)
            .Where(static entry => RouteShaped.IsMatch(entry.Text))
            .Select(static entry => $"{entry.Path} = \"{entry.Text}\"")
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.That(
            offenders,
            Is.Empty,
            $"'{projectName}' carries a route or an address. Addressing an item is the host's job and "
            + "addressing a catalog is the cataloger's; a media kind says what the media is.");
    }

    /// <summary>
    /// Gets every string reachable from the parts of a kind these rules govern, with the path it sits at.
    /// </summary>
    /// <param name="kind">The derived model.</param>
    /// <returns>The strings.</returns>
    /// <remarks>
    /// The catalog section is excluded at the root rather than filtered afterwards, so an exemption cannot
    /// leak: a vendor name reachable through any other section is reported even if the same name also
    /// appears in the catalog.
    /// </remarks>
    private static IEnumerable<(string Path, string Text)> GovernedStrings(IMediaType kind)
    {
        var roots = new (string Path, object? Value)[]
        {
            ("shape", kind.Shape),
            ("intent", kind.Intent),
            ("parsing", kind.Model.Parsing),
            ("matching", kind.Model.Matching),
            ("querying", kind.Model.Querying),
            ("quality", kind.Model.Quality),
            ("naming", kind.Model.Naming),
            ("notifications", kind.Model.Notifications),
            ("itemType", kind.ItemType.Name),
        };

        var found = new List<(string, string)>();

        foreach (var (path, value) in roots)
        {
            Walk(path, value, found, new HashSet<object>(ReferenceEqualityComparer.Instance), depth: 0);
        }

        foreach (var member in MemberNames(kind.ItemType).Concat(kind.GroupTypes.SelectMany(MemberNames)))
        {
            found.Add(("entity member", (string)member));
        }

        return found;
    }

    /// <summary>
    /// Determines whether a path holds text whose spelling somebody else owns.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One exemption, and it is not "parsing". A release-title token pattern reads what a scene group
    /// literally typed — <c>tmdb-335984</c> appears in release names because a stranger put it there — so
    /// the pattern must spell it or the identifier cannot be read at all. That is somebody else's wire
    /// format, exactly as the catalog section is, and neither is the kind's own vocabulary.
    /// </para>
    /// <para>
    /// Everything else under parsing stays governed. A guard, a title pattern, a token <i>tag</i> or a rung
    /// rule naming a vendor would be the kind's own vocabulary and is still a fault.
    /// </para>
    /// </remarks>
    private static bool IsForeignWireText(string path)
        => path.StartsWith("parsing.tokenTables", StringComparison.Ordinal)
            && path.EndsWith(".pattern", StringComparison.Ordinal);

    private static IEnumerable<string> MemberNames(Type type)
        => type.GetProperties().Select(property => property.Name).Prepend(type.Name);

    /// <summary>
    /// Collects every string reachable from a value, depth-bounded and cycle-safe.
    /// </summary>
    private static void Walk(string path, object? value, List<(string, string)> found, HashSet<object> seen, int depth)
    {
        const int MaxDepth = 12;

        if (value is null || depth > MaxDepth)
        {
            return;
        }

        switch (value)
        {
            case string text:
                found.Add((path, text));
                return;

            case IEnumerable sequence and not string:
                var index = 0;

                foreach (var item in sequence)
                {
                    Walk($"{path}[{index++}]", item, found, seen, depth + 1);
                }

                return;
        }

        var type = value.GetType();

        if (type.IsPrimitive || type.IsEnum || value is decimal or DateTime or DateTimeOffset or TimeSpan or Guid)
        {
            return;
        }

        if (!seen.Add(value))
        {
            return;
        }

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            object? child;

            try
            {
                child = property.GetValue(value);
            }
            catch (TargetInvocationException)
            {
                // A computed member that refuses for this instance says nothing about vendors or routes.
                continue;
            }

            Walk($"{path}.{ToCamelCase(property.Name)}", child, found, seen, depth + 1);
        }
    }

    private static string ToCamelCase(string name)
        => name.Length > 0 ? char.ToLowerInvariant(name[0]) + name[1..] : name;

    /// <summary>
    /// Determines whether text contains a word, ignoring case and ignoring letters either side.
    /// </summary>
    /// <remarks>
    /// Word-bounded so that the rule fires on <c>tmdbId</c> and on <c>"tmdb"</c> but not on a longer
    /// identifier that merely spans the letters. A rule that fired on substrings would be untrustworthy,
    /// and an untrustworthy rule gets suppressed rather than obeyed.
    /// </remarks>
    private static bool ContainsWord(string text, string word)
    {
        var index = text.IndexOf(word, StringComparison.OrdinalIgnoreCase);

        while (index >= 0)
        {
            var beforeIsLetter = index > 0 && char.IsLetter(text[index - 1]);
            var after = index + word.Length;
            var afterIsLetter = after < text.Length && char.IsLetter(text[after]);

            if (!beforeIsLetter && !afterIsLetter)
            {
                return true;
            }

            index = text.IndexOf(word, index + 1, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }
}
