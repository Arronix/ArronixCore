using System.Collections.Generic;
using Arronix.Abstractions.Client;
using Arronix.Abstractions.Shape;

namespace Arronix.Abstractions.Tests.Client;

/// <summary>
/// Whether a value in a rendering can be mistaken for the structure around it.
/// </summary>
/// <remarks>
/// A field's identifier, name, description, unit and choice text are all author-supplied. Concatenated raw
/// with a separator, an author who used that separator moves the boundary between two values, and two
/// different schemas render identically and hash alike. The hash would then say two contracts agree when
/// they do not — the one thing it exists to rule out.
/// </remarks>
[TestFixture]
public sealed class ClientContractDigestEncodingTests
{
    private static FieldDescriptor Field(string id, string name, string? description = null) =>
        new() { FieldId = id, Name = name, Description = description, ValueKind = FieldValueKind.Text };

    [Test]
    public void MovingTheSeparatorBetweenTwoValuesChangesTheHash()
    {
        var left = new[] { Field("a|b", "c") };
        var right = new[] { Field("a", "b|c") };

        Assert.That(
            ClientContractDigest.OfProjection(typeof(string), left),
            Is.Not.EqualTo(ClientContractDigest.OfProjection(typeof(string), right)));
    }

    [Test]
    public void AValueContainingALineBreakCannotImpersonateAnotherField()
    {
        var forged = new[] { Field("one", "1:x\n  field=3:two|3:two|~|0|0|2|one|read-only|~") };
        var honest = new[] { Field("one", "x"), Field("two", "two") };

        Assert.That(
            ClientContractDigest.OfProjection(typeof(string), forged),
            Is.Not.EqualTo(ClientContractDigest.OfProjection(typeof(string), honest)));
    }

    [Test]
    public void EveryAuthorSuppliedValueStatesItsOwnLength()
    {
        var rendering = ClientContractDigest.RenderProjection(
            typeof(string),
            new[] { Field("id", "Na|me", "de\nsc") });

        Assert.Multiple(() =>
        {
            Assert.That(rendering, Does.Contain("field=2:id|5:Na|me|5:de\nsc|"));
            Assert.That(rendering, Does.Contain("entity=13:System.String"));
        });
    }

    /// <remarks>
    /// Absent is not empty. A description nobody wrote and a description written as an empty string are
    /// different facts, and a rendering that spelled both as nothing would hash them alike.
    /// </remarks>
    [Test]
    public void AnAbsentValueIsDistinctFromAnEmptyOne()
    {
        var absent = new[] { Field("id", "name") };
        var empty = new[] { Field("id", "name", string.Empty) };

        Assert.That(
            ClientContractDigest.OfProjection(typeof(string), absent),
            Is.Not.EqualTo(ClientContractDigest.OfProjection(typeof(string), empty)));
    }

    [Test]
    public void ChoiceTextIsEncodedTheSameWay()
    {
        IReadOnlyList<FieldDescriptor> Schema(string value, string name) =>
            new[]
            {
                new FieldDescriptor
                {
                    FieldId = "id",
                    Name = "name",
                    ValueKind = FieldValueKind.Enumerated,
                    Choices = [new FacetValue(value, name)],
                },
            };

        Assert.That(
            ClientContractDigest.OfProjection(typeof(string), Schema("a|b", "c")),
            Is.Not.EqualTo(ClientContractDigest.OfProjection(typeof(string), Schema("a", "b|c"))));
    }
}
