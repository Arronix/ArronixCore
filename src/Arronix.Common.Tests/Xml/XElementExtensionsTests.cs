using System.Linq;
using System.Xml.Linq;
using Arronix.Common.Xml;

namespace Arronix.Common.Tests.Xml;

/// <summary>
/// Covers namespace-agnostic traversal, and pins the annotation on the attribute reader that lets callers
/// use the value without re-checking it.
/// </summary>
[TestFixture]
public class XElementExtensionsTests
{
    private const string Document = """
        <feed xmlns="http://example.invalid/ns" xmlns:extra="http://example.invalid/extra">
          <entry id="one">
            <extra:label>first</extra:label>
          </entry>
          <entry>
            <LABEL>second</LABEL>
          </entry>
        </feed>
        """;

    [Test]
    public void FindDescendants_MatchesAcrossNamespaces()
    {
        var root = XElement.Parse(Document);

        var labels = root.FindDescendants("label").Select(element => element.Value).ToArray();

        Assert.That(labels, Is.EqualTo(new[] { "first", "second" }));
    }

    [Test]
    public void FindDescendants_IgnoresCase()
    {
        var root = XElement.Parse(Document);

        Assert.That(root.FindDescendants("LaBeL").Count(), Is.EqualTo(2));
    }

    [Test]
    public void FindDescendants_YieldsNothingWhenTheNameIsAbsent()
    {
        var root = XElement.Parse(Document);

        Assert.That(root.FindDescendants("absent"), Is.Empty);
    }

    [Test]
    public void FindDescendants_IsSpelledCorrectly()
    {
        // The member this replaces was named FindDecendants. A misspelled public member cannot be found by
        // anyone searching for the correct spelling, and every call site had to repeat the mistake.
        var found = typeof(XElementExtensions).GetMethod(nameof(XElementExtensions.FindDescendants));

        Assert.That(found, Is.Not.Null);
        Assert.That(
            typeof(XElementExtensions).GetMethod("FindDecendants"),
            Is.Null,
            "The misspelling must not survive as an alias.");
    }

    [Test]
    public void TryGetAttributeValue_ReadsAPresentAttribute()
    {
        var element = XElement.Parse("""<entry id="one" />""");

        if (!element.TryGetAttributeValue("id", out var value))
        {
            Assert.Fail("The attribute is present and should have been found.");
            return;
        }

        // The out parameter is annotated for the success path, so this reads Length without a null check
        // and without a suppression. That is the whole point of the annotation the previous version lacked.
        Assert.That(value.Length, Is.EqualTo(3));
    }

    [Test]
    public void TryGetAttributeValue_ReportsAnAbsentAttribute()
    {
        var element = XElement.Parse("""<entry id="one" />""");

        var found = element.TryGetAttributeValue("missing", out var value);

        Assert.That(found, Is.False);
        Assert.That(value, Is.Null);
    }

    [Test]
    public void TryGetAttributeValue_ReadsAnEmptyAttribute()
    {
        var element = XElement.Parse("""<entry id="" />""");

        var found = element.TryGetAttributeValue("id", out var value);

        Assert.That(found, Is.True);
        Assert.That(value, Is.Empty);
    }
}
