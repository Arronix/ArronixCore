using System.Text.Json;
using System.Text.Json.Serialization;
using Arronix.Client.Serialization;
using FluentAssertions;

namespace Arronix.Client.Tests.Serialization;

/// <summary>Client-owned options preserve the API wire rules without importing server implementation.</summary>
[TestFixture]
internal sealed class ApiJsonOptionsTests
{
    [Test]
    public void DeclaresTheCanonicalPropertyDictionaryNullAndNumberRules()
    {
        var options = ApiJsonOptions.Default;

        using var _ = new FluentAssertions.Execution.AssertionScope();
        options.AllowTrailingCommas.Should().BeTrue();
        options.PropertyNameCaseInsensitive.Should().BeTrue();
        options.PropertyNamingPolicy.Should().Be(JsonNamingPolicy.CamelCase);
        options.DictionaryKeyPolicy.Should().BeNull();
        options.DefaultIgnoreCondition.Should().Be(JsonIgnoreCondition.WhenWritingNull);
        options.NumberHandling.Should().Be(JsonNumberHandling.Strict);
    }

    [Test]
    public void ReadsTrailingCommasButRefusesQuotedOrdinaryNumbers()
    {
        JsonSerializer.Deserialize<NumberEnvelope>("{\"count\":42,}", ApiJsonOptions.Default)
            .Should().Be(new NumberEnvelope(42));

        var quoted = () => JsonSerializer.Deserialize<NumberEnvelope>("{\"count\":\"42\"}", ApiJsonOptions.Default);
        quoted.Should().Throw<JsonException>();
    }

    private sealed record NumberEnvelope(int Count);
}
