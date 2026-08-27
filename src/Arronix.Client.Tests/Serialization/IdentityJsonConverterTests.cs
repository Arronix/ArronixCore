using System.Text.Json;
using Arronix.Abstractions.Providers;
using Arronix.Abstractions.Shape;
using Arronix.Client.Serialization;
using FluentAssertions;

namespace Arronix.Client.Tests.Serialization;

/// <summary>Client identifier readers refuse the same malformed wire values as the server.</summary>
[TestFixture]
internal sealed class IdentityJsonConverterTests
{
    [TestCase("\"\"")]
    [TestCase("\"   \"")]
    public void MediaLevelIdRefusesMalformedWireText(string json)
    {
        var read = () => JsonSerializer.Deserialize<MediaLevelId>(json, ApiJsonOptions.Default);

        read.Should().Throw<JsonException>();
    }

    [TestCase("\"\"")]
    [TestCase("\"not-a-provider\"")]
    public void ProviderIdRefusesMalformedWireText(string json)
    {
        var read = () => JsonSerializer.Deserialize<ProviderId>(json, ApiJsonOptions.Default);

        read.Should().Throw<JsonException>();
    }

    [TestCase("null")]
    [TestCase("\"\"")]
    public void OrdinalPathReadsNullAndEmptyAsTheEmptyPath(string json)
        => JsonSerializer.Deserialize<OrdinalPath>(json, ApiJsonOptions.Default).Should().Be(OrdinalPath.Empty);

    [TestCase("\"not.a.path\"")]
    [TestCase("\"1.2.3.4.5\"")]
    public void OrdinalPathRefusesMalformedNonEmptyWireText(string json)
    {
        var read = () => JsonSerializer.Deserialize<OrdinalPath>(json, ApiJsonOptions.Default);

        read.Should().Throw<JsonException>();
    }
}
