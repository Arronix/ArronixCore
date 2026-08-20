using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Intent;
using Arronix.Abstractions.Media;
using Arronix.Abstractions.Shape;
using Arronix.Host.Media.Typed;
using FluentAssertions;


namespace Arronix.Host.Tests.TypedMedia;

/// <summary>
/// The rules the type system cannot state, refused at the gate.
/// </summary>
/// <remarks>
/// "Exactly one title", "the identity is the host's identifier type", "a status is an enumeration" are not
/// expressible as constraints, so they are enforced twice: by an analyzer at the author's keyboard, where a
/// compile error belongs, and here, because a host that trusted a plugin to have been built with the
/// analyzer switched on would be trusting the plugin. These assert the second half. Without them the
/// guarantee would rest entirely on a build-time tool the host cannot observe.
/// </remarks>
[TestFixture]
internal sealed class EntityWellFormednessTests
{
    private sealed class NoIdentity
    {
        [Title]
        public required string Title { get; init; }
    }

    private sealed class TwoIdentities
    {
        [Identity]
        public required MediaItemId Id { get; init; }

        [Identity]
        public MediaItemId Other { get; init; }

        [Title]
        public required string Title { get; init; }
    }

    private sealed class WrongIdentityType
    {
        [Identity]
        public required int Id { get; init; }

        [Title]
        public required string Title { get; init; }
    }

    private sealed class NoTitle
    {
        [Identity]
        public required MediaItemId Id { get; init; }
    }

    private sealed class TwoTitles
    {
        [Identity]
        public required MediaItemId Id { get; init; }

        [Title]
        public required string Title { get; init; }

        [Title]
        public string? Other { get; init; }
    }

    private sealed class StatusThatIsNotAnEnumeration
    {
        [Identity]
        public required MediaItemId Id { get; init; }

        [Title]
        public required string Title { get; init; }

        [Status]
        public string? Stage { get; init; }
    }

    private static IEnumerable<TestCaseData> MalformedShapes()
    {
        yield return Case<NoIdentity>(Field(nameof(NoIdentity.Title), typeof(string), FieldSemantics.Title));
        yield return Case<TwoIdentities>(
            Field(nameof(TwoIdentities.Id), typeof(MediaItemId), FieldSemantics.Identity, explicitIdentity: true),
            Field(nameof(TwoIdentities.Other), typeof(MediaItemId), FieldSemantics.Identity, explicitIdentity: true),
            Field(nameof(TwoIdentities.Title), typeof(string), FieldSemantics.Title));
        yield return Case<WrongIdentityType>(
            Field(nameof(WrongIdentityType.Id), typeof(int), FieldSemantics.Identity, explicitIdentity: true),
            Field(nameof(WrongIdentityType.Title), typeof(string), FieldSemantics.Title));
        yield return Case<NoTitle>(
            Field(nameof(NoTitle.Id), typeof(MediaItemId), FieldSemantics.Identity, explicitIdentity: true));
        yield return Case<TwoTitles>(
            Field(nameof(TwoTitles.Id), typeof(MediaItemId), FieldSemantics.Identity, explicitIdentity: true),
            Field(nameof(TwoTitles.Title), typeof(string), FieldSemantics.Title),
            Field(nameof(TwoTitles.Other), typeof(string), FieldSemantics.Title));
        yield return Case<StatusThatIsNotAnEnumeration>(
            Field(nameof(StatusThatIsNotAnEnumeration.Id), typeof(MediaItemId), FieldSemantics.Identity, explicitIdentity: true),
            Field(nameof(StatusThatIsNotAnEnumeration.Title), typeof(string), FieldSemantics.Title),
            Field(nameof(StatusThatIsNotAnEnumeration.Stage), typeof(string), FieldSemantics.Status));
    }

    [TestCaseSource(nameof(MalformedShapes))]
    public void AMalformedEntityIsRefusedWithAReasonNamingIt(CompiledEntityShape shape) =>
        FluentActions.Invoking(() => ItemTypeReader.Read(shape))
            .Should().Throw<ArgumentException>()
            .WithMessage($"*{shape.EntityType.FullName}*");

    [Test]
    public void AWellFormedEntityIsRead() =>
        FluentActions.Invoking(() => ItemTypeReader.Read(new Works().CompiledShapes.Item))
            .Should().NotThrow();

    private static TestCaseData Case<TEntity>(params CompiledField[] fields) =>
        new TestCaseData(new CompiledEntityShape { EntityType = typeof(TEntity), Fields = fields })
            .SetName($"Malformed_{typeof(TEntity).Name}");

    private static CompiledField Field(
        string name,
        Type type,
        FieldSemantics semantics,
        bool explicitIdentity = false) =>
        new()
        {
            PropertyName = name,
            PropertyType = type,
            ElementType = type,
            FilterOperators = FilterOperators.Equals,
            ExplicitIdentity = explicitIdentity,
            IsNameable = true,
            Read = static _ => null,
            Descriptor = new FieldDescriptor
            {
                FieldId = char.ToLowerInvariant(name[0]) + name[1..],
                Name = name,
                ValueKind = type.IsEnum ? FieldValueKind.Enumerated : FieldValueKind.Text,
                Semantics = semantics
            }
        };
}
