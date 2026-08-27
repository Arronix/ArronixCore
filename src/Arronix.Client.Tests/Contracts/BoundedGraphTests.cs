using System.Collections;
using Arronix.Abstractions.Shape;
using Arronix.Client.Contracts;
using FluentAssertions;
using FluentAssertions.Execution;

namespace Arronix.Client.Tests.Contracts;

/// <summary>The walk reads a contract's list once, and walks what it read.</summary>
[TestFixture]
internal sealed class BoundedGraphTests
{
    /// <remarks>
    /// A list is the contract's own object: it may answer differently on a second read. Reading twice — once
    /// to check and once to push — walks a value nothing checked, and this list is written to make that
    /// visible rather than lucky.
    /// </remarks>
    [Test]
    public void AnEntryIsReadOnceAndTheValueReadIsTheValueWalked()
    {
        var shifty = new Shifty();

        var defect = BoundedGraph.Exceeded(shifty, static field => field.Components, "a schema");

        using var assertions = new AssertionScope();
        defect.Should().BeNull("the one value it answered with is a well-formed field");
        shifty.Reads.Should().Be(1, "a second read is a second answer");
    }

    /// <summary>Answers with a field once, then with null, and counts how often it was asked.</summary>
    private sealed class Shifty : IReadOnlyList<FieldDescriptor>
    {
        private static readonly FieldDescriptor Leaf = new()
        {
            FieldId = "leaf",
            Name = "leaf",
            ValueKind = FieldValueKind.Text,
        };

        internal int Reads { get; private set; }

        public int Count => 1;

        public FieldDescriptor this[int index] => ++Reads == 1 ? Leaf : null!;

        public IEnumerator<FieldDescriptor> GetEnumerator()
        {
            yield return this[0];
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
