using System.Text;
using System.Text.Json;
using Arronix.Abstractions.Client;

namespace Arronix.Plugin.Movies.Tests.ClientContract;

/// <summary>
/// What the contract refuses to read.
/// </summary>
/// <remarks>
/// A client contract payload is untrusted input, so the reader runs on strict defaults. Each case here is
/// a permissive default that would otherwise let a payload mean something its sender did not write.
/// </remarks>
[TestFixture]
public sealed class StrictPayloadTests
{
    private static ClientContractEntryPointAttribute Declaration => MovieClientContractTests.Declaration;

    private static string Valid() =>
        Encoding.UTF8.GetString(Declaration.Serialize(MovieClientContractTests.Complete()));

    private static void Read(string payload) => Declaration.Deserialize(Encoding.UTF8.GetBytes(payload));

    [Test]
    public void TheValidPayloadIsReadable()
    {
        Assert.That(() => Read(Valid()), Throws.Nothing);
    }

    [Test]
    public void AnUnknownPropertyIsRefused()
    {
        var payload = Valid().Insert(1, "\"somethingElse\":1,");

        Assert.That(() => Read(payload), Throws.InstanceOf<JsonException>());
    }

    /// <remarks>
    /// A derived value is not on the wire, so a payload that carries one is claiming to state something the
    /// contract computes. It is refused for the same reason any other unknown property is: the reader has
    /// nowhere to put it, and silently dropping it would let a sender believe it had been read.
    /// </remarks>
    [Test]
    public void AForgedDerivedMemberIsRefused()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => Read(Valid().Insert(1, "\"status\":\"Released\",")),
                Throws.InstanceOf<JsonException>());
            Assert.That(() => Read(Valid().Insert(1, "\"normalizedValue\":0.9,")),
                Throws.InstanceOf<JsonException>());
        });
    }

    [Test]
    public void ADuplicatePropertyIsRefused()
    {
        var payload = Valid().Insert(1, "\"title\":\"Something Else\",");

        Assert.That(() => Read(payload), Throws.InstanceOf<JsonException>());
    }

    [Test]
    public void NullIntoANonNullableMemberIsRefused()
    {
        var payload = Valid().Replace("\"title\":\"Inception\"", "\"title\":null", StringComparison.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(payload, Does.Contain("\"title\":null"), "the payload under test was actually built");
            Assert.That(() => Read(payload), Throws.InstanceOf<JsonException>());
        });
    }

    /// <remarks>
    /// A rating's source has no setter: the constructor is the only way one is written, and the constructor
    /// parameter has no default. A payload that omits it is asking for a rating that was never constructible.
    /// </remarks>
    [Test]
    public void AMissingRequiredConstructorArgumentIsRefused()
    {
        var payload = Valid().Replace("\"source\":\"tmdb\",", string.Empty, StringComparison.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(payload, Does.Not.Contain("\"source\":\"tmdb\""), "the payload under test was actually built");
            Assert.That(() => Read(payload), Throws.InstanceOf<JsonException>());
        });
    }

    [Test]
    public void AMissingRequiredMemberIsRefused()
    {
        var payload = Valid().Replace("\"title\":\"Inception\",", string.Empty, StringComparison.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(payload, Does.Not.Contain("\"title\":\"Inception\""), "the payload under test was actually built");
            Assert.That(() => Read(payload), Throws.InstanceOf<JsonException>());
        });
    }

    /// <remarks>
    /// Case sensitivity is part of the same posture: a payload spelling a member differently is a payload
    /// naming a member this contract does not have.
    /// </remarks>
    [Test]
    public void AMemberSpelledWithADifferentCaseIsRefused()
    {
        var payload = Valid().Replace("\"title\":\"Inception\"", "\"Title\":\"Inception\"", StringComparison.Ordinal);

        Assert.That(() => Read(payload), Throws.InstanceOf<JsonException>());
    }
}
