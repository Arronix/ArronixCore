using System.Collections.Generic;
using Arronix.Common.Serialization;

namespace Arronix.Common.Tests.Serialization;

/// <summary>
/// Covers the deep copy: that it detaches nested state, that it accepts the types the legacy constraints
/// excluded, and that a null copies to a null rather than to an empty instance.
/// </summary>
[TestFixture]
public class JsonClonerTests
{
    [Test]
    public void Clone_DetachesNestedState()
    {
        var original = new Node("north", ["first", "second"]);

        var copy = JsonCloner.Clone(original);

        copy!.Children.Add("third");

        Assert.Multiple(() =>
        {
            Assert.That(copy.Name, Is.EqualTo("north"));
            Assert.That(copy.Children, Has.Count.EqualTo(3));
            Assert.That(original.Children, Has.Count.EqualTo(2), "The copy shares no references with the original.");
        });
    }

    [Test]
    public void Clone_ReturnsNullForNull()
    {
        Assert.That(JsonCloner.Clone<Node>(null), Is.Null);
    }

    [Test]
    public void Clone_AcceptsATypeWithNoParameterlessConstructor()
    {
        // The legacy form constrained the payload to `class, new()`. Neither half survives: this record has
        // no parameterless constructor and the next case is not a class at all.
        Assert.That(JsonCloner.Clone(new Immutable("north", 3)), Is.EqualTo(new Immutable("north", 3)));
    }

    [Test]
    public void Clone_AcceptsAValueType()
    {
        Assert.Multiple(() =>
        {
            Assert.That(JsonCloner.Clone(42), Is.EqualTo(42));
            Assert.That(JsonCloner.Clone(new Point(1, 2)), Is.EqualTo(new Point(1, 2)));
        });
    }

    private sealed record Node(string Name, List<string> Children);

    private sealed record Immutable(string Name, int Count);

    private readonly record struct Point(int X, int Y);
}
