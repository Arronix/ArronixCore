using Arronix.Abstractions.Errors;
using Arronix.Abstractions.Health;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Intent;
using Arronix.Abstractions.Shape;
using Arronix.Host.Engines.Items;
using Arronix.Host.Media;
using Arronix.Host.Media.Typed;
using Arronix.Host.Tests.TypedMedia;
using FluentAssertions;

namespace Arronix.Host.Tests.Storage;

/// <summary>
/// What the host's item source refuses to answer, and why each refusal is louder than the plausible answer
/// it replaces.
/// </summary>
[TestFixture]
internal sealed class HostItemSourceRefusalTests
{
    private DurableStoreFixture _store = null!;
    private HostItemSource _items = null!;

    [SetUp]
    public void SetUp()
    {
        _store = new DurableStoreFixture();

        ValidatedDefinition.TryValidate(
            MediaTypeModelFactory.Build<Work, WorkTarget, WorkRelease, WorkParser, Works>(),
            out var validated,
            out _).Should().BeTrue();

        _items = new HostItemSource(validated!, _store.Records(), _store.Identity());
    }

    [TearDown]
    public void TearDown() => _store.Dispose();

    /// <summary>An empty proposal is a proposal, and this kind has no surface that produced one.</summary>
    [Test]
    public async Task ProposingOverASurfaceThatDoesNotExistIsRefused()
    {
        var act = async () => await _items.ProposeAsync("manual-import", new Dictionary<string, string>());

        (await act.Should().ThrowAsync<ArronixException>())
            .Which.ErrorCode.Should().Be(CoreErrorCode.InvalidConfiguration);
    }

    /// <summary>An unsuccessful action is what a surface that ran and failed answers with.</summary>
    [Test]
    public async Task CommittingToASurfaceThatDoesNotExistIsRefused()
    {
        var act = async () => await _items.CommitAsync(new WorkbenchCommit("manual-import", [], []));

        (await act.Should().ThrowAsync<ArronixException>())
            .Which.ErrorCode.Should().Be(CoreErrorCode.InvalidConfiguration);
    }

    /// <summary>
    /// A local identity is unique within its kind, so the same number in another kind is another entity.
    /// </summary>
    [Test]
    public async Task AnItemOfAnotherKindIsRefusedRatherThanReportedMissing()
    {
        var elsewhere = new MediaItemRef(
            MediaKindId.FromString("elsewhere"),
            MediaLevelId.FromString("work"),
            MediaItemId.FromInt64(1));

        var act = async () => await _items.GetAsync(elsewhere);

        (await act.Should().ThrowAsync<ArronixException>())
            .Which.ErrorCode.Should().Be(CoreErrorCode.MediaItemNotFound);
    }

    /// <summary>
    /// A malformed identifier is a question the platform cannot ask, not an item nobody holds.
    /// </summary>
    [TestCase("", "1")]
    [TestCase("Alpha", "1")]
    [TestCase("al pha", "1")]
    [TestCase("al:pha", "1")]
    [TestCase("alpha", "")]
    public async Task AMalformedIdentifierIsRefusedRatherThanTranslatedIntoNotFound(string scheme, string value)
    {
        var act = async () => await _items.ResolveExternalAsync(new ExternalId(scheme, value));

        await act.Should().ThrowAsync<ArgumentException>();
    }

    /// <summary>
    /// A kind whose items have no generated storage bridge says so, rather than reporting an empty library.
    /// </summary>
    /// <remarks>
    /// The fixture's item implements the entity contract directly rather than deriving from the common
    /// item, which is a supported way to declare a kind and the reason nothing refuses it at admission.
    /// </remarks>
    [Test]
    public async Task AKindWhoseItemsCannotBeStoredSaysSoInsteadOfAnsweringEmpty()
    {
        var query = new ItemQuery
        {
            Kind = _items.MediaKind,
            Level = MediaLevelId.FromString("work"),
        };

        var act = async () => await _items.QueryAsync(query);

        (await act.Should().ThrowAsync<ArronixException>())
            .Which.ErrorCode.Should().Be(CoreErrorCode.CatalogRecordUnreadable);
    }
}
