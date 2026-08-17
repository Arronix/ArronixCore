using System;
using System.Globalization;
using System.Text.Json;
using Arronix.Common.Serialization;

namespace Arronix.Common.Tests.Serialization;

/// <summary>
/// Covers the one converter the platform still carries: that a timestamp is UTC on the wire whatever kind
/// it had in memory, that it is read back as the same instant on any machine, and that neither direction
/// depends on the ambient culture.
/// </summary>
[TestFixture]
public class UtcDateTimeJsonConverterTests
{
    private const string WireValue = "\"2024-03-01T10:20:30Z\"";

    private static readonly DateTime Instant = new(2024, 3, 1, 10, 20, 30, DateTimeKind.Utc);

    [Test]
    public void Write_EmitsIso8601InUtcToTheSecond()
    {
        Assert.That(JsonSerializer.Serialize(Instant, JsonDefaults.Compact), Is.EqualTo(WireValue));
    }

    [Test]
    [SetCulture("ar-SA")]
    public void Write_IsUnaffectedByTheAmbientCulture()
    {
        // The legacy converter formatted against the ambient culture. Under a culture whose default
        // calendar is not Gregorian that does not merely restyle the value, it renumbers it: the same
        // instant came out as "1445-08-20T10:20:30Z", which is well-formed and wrong.
        Assert.That(JsonSerializer.Serialize(Instant, JsonDefaults.Compact), Is.EqualTo(WireValue));
    }

    [Test]
    [SetCulture("ar-SA")]
    public void Read_IsUnaffectedByTheAmbientCulture()
    {
        var value = JsonSerializer.Deserialize<DateTime>(WireValue, JsonDefaults.Compact);

        Assert.That(value, Is.EqualTo(Instant));
    }

    [Test]
    public void Read_ConvertsAnOffsetToUtc()
    {
        var value = JsonSerializer.Deserialize<DateTime>("\"2024-03-01T12:50:30+02:30\"", JsonDefaults.Compact);

        Assert.Multiple(() =>
        {
            Assert.That(value, Is.EqualTo(Instant));
            Assert.That(value.Kind, Is.EqualTo(DateTimeKind.Utc));
        });
    }

    [Test]
    public void Read_TakesAStringWithNoZoneAsUtcRatherThanAsLocalTime()
    {
        // Reading an unzoned string as local time makes the value depend on the reading machine's
        // configuration, so the same payload means two different instants on two hosts.
        var value = JsonSerializer.Deserialize<DateTime>("\"2024-03-01T10:20:30\"", JsonDefaults.Compact);

        Assert.Multiple(() =>
        {
            Assert.That(value, Is.EqualTo(Instant));
            Assert.That(value.Kind, Is.EqualTo(DateTimeKind.Utc));
        });
    }

    [Test]
    public void Write_TakesAValueWithNoKindAsUtcSoARoundTripDoesNotDrift()
    {
        var unspecified = new DateTime(2024, 3, 1, 10, 20, 30, DateTimeKind.Unspecified);

        var json = JsonSerializer.Serialize(unspecified, JsonDefaults.Compact);

        Assert.Multiple(() =>
        {
            Assert.That(json, Is.EqualTo(WireValue));
            Assert.That(
                JsonSerializer.Deserialize<DateTime>(json, JsonDefaults.Compact),
                Is.EqualTo(Instant),
                "Writing an unzoned value as local and reading it back as UTC shifts it once per round trip.");
        });
    }

    [Test]
    public void Write_ConvertsALocalValueToTheInstantItNames()
    {
        var local = new DateTime(2024, 3, 1, 10, 20, 30, DateTimeKind.Local);
        var expected = local.ToUniversalTime()
            .ToString("yyyy'-'MM'-'dd'T'HH':'mm':'ss'Z'", CultureInfo.InvariantCulture);

        Assert.That(JsonSerializer.Serialize(local, JsonDefaults.Compact), Is.EqualTo($"\"{expected}\""));
    }

    [Test]
    public void Write_DropsSubSecondPrecision()
    {
        var precise = Instant.AddTicks(1_234_567);

        Assert.That(JsonSerializer.Serialize(precise, JsonDefaults.Compact), Is.EqualTo(WireValue));
    }

    [Test]
    public void Read_RejectsAValueThatIsNotAString()
    {
        Assert.That(
            () => JsonSerializer.Deserialize<DateTime>("1709288430", JsonDefaults.Compact),
            Throws.InstanceOf<JsonException>());
    }

    [Test]
    public void Read_RejectsAStringNoTimestampCanBeReadFrom()
    {
        Assert.That(
            () => JsonSerializer.Deserialize<DateTime>("\"the first of never\"", JsonDefaults.Compact),
            Throws.InstanceOf<JsonException>());
    }

    [Test]
    public void Read_AcceptsASpaceSeparatedTimestamp()
    {
        var value = JsonSerializer.Deserialize<DateTime>("\"2024-03-01 10:20:30\"", JsonDefaults.Compact);

        Assert.That(value, Is.EqualTo(Instant));
    }

    [Test]
    public void AnAbsentTimestamp_StaysAbsent()
    {
        Assert.Multiple(() =>
        {
            Assert.That(JsonSerializer.Deserialize<DateTime?>("null", JsonDefaults.Compact), Is.Null);
            Assert.That(JsonSerializer.Serialize<DateTime?>(null, JsonDefaults.Compact), Is.EqualTo("null"));
        });
    }

    [Test]
    public void AnOptionalTimestamp_UsesTheSameConversion()
    {
        var value = JsonSerializer.Deserialize<DateTime?>("\"2024-03-01T10:20:30\"", JsonDefaults.Compact);

        Assert.That(value, Is.EqualTo(Instant));
    }
}
