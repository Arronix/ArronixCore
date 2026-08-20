using System.Linq;
using Arronix.Abstractions.Shape;
using Arronix.Host.Storage;
using Arronix.Host.Tests.Support;
using FluentAssertions;


namespace Arronix.Host.Tests.Storage;

/// <summary>
/// The store's ordinary behavior: library state, files and cross-cutting groups.
/// </summary>
[TestFixture]
internal sealed class InMemoryMediaStoreTests
{
    private static IMediaStore Fused()
        => new InMemoryMediaStore(
            TestOptions.RegistryWith(ContributionFixtures.For(ShapeFixtures.Fused())));

    private static IMediaStore Layered()
        => new InMemoryMediaStore(
            TestOptions.RegistryWith(ContributionFixtures.For(ShapeFixtures.Layered())));

    [Test]
    public async Task AnItemNobodyHasExpressedAnythingAboutHasNoFacet()
    {
        var store = Fused();

        (await store.FindLibraryAsync(ShapeFixtures.Item(ShapeFixtures.Catalog, 1))).Should().BeNull();
    }

    [Test]
    public async Task ReadingManyFacetsOmitsTheOnesThatDoNotExist()
    {
        var store = Fused();
        var stored = ShapeFixtures.Item(ShapeFixtures.Catalog, 1);
        var absent = ShapeFixtures.Item(ShapeFixtures.Catalog, 2);

        await store.UpsertLibraryAsync(new LibraryFacet { Ref = stored, Path = "/library/one" });

        var found = await store.FindLibraryManyAsync([stored, absent]);

        found.Should().ContainKey(stored);
        found.Should().NotContainKey(absent);
    }

    [Test]
    public async Task AFileWithNoIdentifierIsGivenOne()
    {
        var store = Fused();

        var first = await store.UpsertFileAsync(new MediaFileRecord
        {
            Id = MediaFileId.FromInt64(0),
            Anchor = ShapeFixtures.Item(ShapeFixtures.Catalog, 1),
            Path = "/library/one/file.mkv",
            Size = 10,
        });

        var second = await store.UpsertFileAsync(new MediaFileRecord
        {
            Id = MediaFileId.FromInt64(0),
            Anchor = ShapeFixtures.Item(ShapeFixtures.Catalog, 2),
            Path = "/library/two/file.mkv",
            Size = 20,
        });

        first.Should().NotBe(second);
        (await store.FindFileAsync(first))!.Size.Should().Be(10);
    }

    [Test]
    public async Task GroupMembershipIsReadableFromBothEnds()
    {
        var store = Layered();
        var group = ShapeFixtures.Item(ShapeFixtures.Catalog, 100);
        var member = ShapeFixtures.Item(ShapeFixtures.Catalog, 1);

        await store.SetGroupMembershipAsync(new GroupMembership(group, member, "2.5", 25, IsPrimary: true));

        (await store.MembersOfAsync(group)).Should().ContainSingle(m => m.Member == member);
        (await store.GroupsOfAsync(member)).Should().ContainSingle(m => m.Group == group);
    }

    [Test]
    public async Task MembersAreOrderedByTheirSortIndexRatherThanTheirDeclaredPosition()
    {
        var store = Layered();
        var group = ShapeFixtures.Item(ShapeFixtures.Catalog, 100);

        await store.SetGroupMembershipAsync(new GroupMembership(
            group, ShapeFixtures.Item(ShapeFixtures.Catalog, 2), "10", 100, false));
        await store.SetGroupMembershipAsync(new GroupMembership(
            group, ShapeFixtures.Item(ShapeFixtures.Catalog, 1), "2.5", 25, false));

        (await store.MembersOfAsync(group)).Select(m => m.Position).Should().Equal("2.5", "10");
    }

    [Test]
    public async Task DesignatingAPrimaryMembershipClearsTheMembersOtherOne()
    {
        var store = Layered();
        var member = ShapeFixtures.Item(ShapeFixtures.Catalog, 1);
        var first = ShapeFixtures.Item(ShapeFixtures.Catalog, 100);
        var second = ShapeFixtures.Item(ShapeFixtures.Catalog, 200);

        await store.SetGroupMembershipAsync(new GroupMembership(first, member, null, 0, IsPrimary: true));
        await store.SetGroupMembershipAsync(new GroupMembership(second, member, null, 0, IsPrimary: true));

        var groups = await store.GroupsOfAsync(member);

        groups.Count(membership => membership.IsPrimary).Should().Be(1);
        groups.Single(membership => membership.IsPrimary).Group.Should().Be(second);
    }

    [Test]
    public async Task RemovingAMembershipRemovesItFromBothEnds()
    {
        var store = Layered();
        var group = ShapeFixtures.Item(ShapeFixtures.Catalog, 100);
        var member = ShapeFixtures.Item(ShapeFixtures.Catalog, 1);

        await store.SetGroupMembershipAsync(new GroupMembership(group, member, null, 0, false));
        await store.RemoveGroupMembershipAsync(group, member);

        (await store.MembersOfAsync(group)).Should().BeEmpty();
        (await store.GroupsOfAsync(member)).Should().BeEmpty();
    }
}
