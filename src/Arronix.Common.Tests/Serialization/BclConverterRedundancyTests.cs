using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Arronix.Common.Serialization;
using Arronix.Common.Serialization.Converters;

namespace Arronix.Common.Tests.Serialization;

/// <summary>
/// Locks in the decision to drop the hand-written <see cref="Version"/>, <see cref="TimeSpan"/> and
/// <see cref="Uri"/> converters.
/// </summary>
/// <remarks>
/// <para>
/// Each legacy converter did exactly one thing: write the value's own string form and parse it back. The
/// framework has done the same thing since it grew built-in support, and these tests re-verify that on the
/// machine actually building the code rather than trusting a note in a plan. If a framework update ever
/// changes the shape, this fixture fails and the decision is reopened — which is the only reason to assert
/// against literal payloads rather than against a round trip alone.
/// </para>
/// <para>
/// Dropping them also removes two defects that came with them: parsing a <see cref="TimeSpan"/> with no
/// format provider is culture-sensitive, and the version converter declared a non-nullable parameter and
/// then null-checked it.
/// </para>
/// </remarks>
[TestFixture]
public class BclConverterRedundancyTests
{
    [Test]
    public void TimeSpan_IsWrittenExactlyAsTheDroppedConverterWroteIt()
    {
        var value = new TimeSpan(1, 2, 3, 4, 5);

        Assert.Multiple(() =>
        {
            Assert.That(JsonSerializer.Serialize(value, JsonDefaults.Compact), Is.EqualTo("\"1.02:03:04.0050000\""));
            Assert.That(
                JsonSerializer.Serialize(value, JsonDefaults.Compact),
                Is.EqualTo($"\"{value}\""),
                "The dropped converter wrote TimeSpan.ToString() and nothing else.");
        });
    }

    [TestCase(0, 0, 0, 0, 0)]
    [TestCase(1, 2, 3, 4, 5)]
    [TestCase(-1, -2, -3, -4, -5)]
    [TestCase(400, 23, 59, 59, 999)]
    public void TimeSpan_RoundTripsThroughTheBuiltInSupport(
        int days,
        int hours,
        int minutes,
        int seconds,
        int milliseconds)
    {
        var value = new TimeSpan(days, hours, minutes, seconds, milliseconds);

        var json = JsonSerializer.Serialize(value, JsonDefaults.Compact);

        Assert.Multiple(() =>
        {
            Assert.That(json, Is.EqualTo($"\"{value}\""));
            Assert.That(JsonSerializer.Deserialize<TimeSpan>(json, JsonDefaults.Compact), Is.EqualTo(value));
        });
    }

    [TestCase("1.2")]
    [TestCase("1.2.3")]
    [TestCase("1.2.3.4")]
    [TestCase("0.0.0.0")]
    public void Version_RoundTripsThroughTheBuiltInSupport(string text)
    {
        var value = Version.Parse(text);

        var json = JsonSerializer.Serialize(value, JsonDefaults.Compact);

        Assert.Multiple(() =>
        {
            Assert.That(json, Is.EqualTo($"\"{text}\""));
            Assert.That(JsonSerializer.Deserialize<Version>(json, JsonDefaults.Compact), Is.EqualTo(value));
        });
    }

    [TestCase("https://example.test/a/b?c=d")]
    [TestCase("http://example.test:8080/")]
    public void Uri_RoundTripsThroughTheBuiltInSupport(string text)
    {
        var value = new Uri(text);

        var json = JsonSerializer.Serialize(value, JsonDefaults.Compact);

        Assert.Multiple(() =>
        {
            Assert.That(json, Is.EqualTo($"\"{text}\""));
            Assert.That(JsonSerializer.Deserialize<Uri>(json, JsonDefaults.Compact), Is.EqualTo(value));
        });
    }

    [Test]
    public void CanonicalOptions_CarryOnlyTheTwoConversionsTheFrameworkDoesNotMake()
    {
        var converters = JsonDefaults.Compact.Converters
            .Select(static converter => converter.GetType())
            .ToArray();

        Assert.That(
            converters,
            Is.EqualTo(new[] { typeof(JsonStringEnumConverter), typeof(UtcDateTimeJsonConverter) }));
    }

    [Test]
    public void TheConverterSet_IsExactlyOneConverter()
    {
        var converters = typeof(JsonDefaults).Assembly
            .GetExportedTypes()
            .Where(static type => typeof(JsonConverter).IsAssignableFrom(type))
            .ToArray();

        Assert.That(converters, Is.EqualTo(new[] { typeof(UtcDateTimeJsonConverter) }));
    }
}
