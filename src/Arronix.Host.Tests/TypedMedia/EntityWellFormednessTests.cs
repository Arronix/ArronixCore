using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Media;
using Arronix.Host.Media.Typed;
using FluentAssertions;

// Every contract these tests read is experimental.
#pragma warning disable ARX0020

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
    private sealed class NoIdentity : IMediaItem
    {
        [Title]
        public required string Title { get; init; }
    }

    private sealed class TwoIdentities : IMediaItem
    {
        [Identity]
        public required MediaItemId Id { get; init; }

        [Identity]
        public MediaItemId Other { get; init; }

        [Title]
        public required string Title { get; init; }
    }

    private sealed class WrongIdentityType : IMediaItem
    {
        [Identity]
        public required int Id { get; init; }

        [Title]
        public required string Title { get; init; }
    }

    private sealed class NoTitle : IMediaItem
    {
        [Identity]
        public required MediaItemId Id { get; init; }
    }

    private sealed class TwoTitles : IMediaItem
    {
        [Identity]
        public required MediaItemId Id { get; init; }

        [Title]
        public required string Title { get; init; }

        [Title]
        public string? Other { get; init; }
    }

    private sealed class StatusThatIsNotAnEnumeration : IMediaItem
    {
        [Identity]
        public required MediaItemId Id { get; init; }

        [Title]
        public required string Title { get; init; }

        [Status]
        public string? Stage { get; init; }
    }

    [TestCase(typeof(NoIdentity))]
    [TestCase(typeof(TwoIdentities))]
    [TestCase(typeof(WrongIdentityType))]
    [TestCase(typeof(NoTitle))]
    [TestCase(typeof(TwoTitles))]
    [TestCase(typeof(StatusThatIsNotAnEnumeration))]
    public void AMalformedEntityIsRefusedWithAReasonNamingIt(Type entityType) =>
        FluentActions.Invoking(() => ItemTypeReader.Read(entityType))
            .Should().Throw<ArgumentException>()
            .WithMessage($"*{entityType.FullName}*");

    [Test]
    public void AWellFormedEntityIsRead() =>
        FluentActions.Invoking(() => ItemTypeReader.Read(typeof(Work)))
            .Should().NotThrow();
}
