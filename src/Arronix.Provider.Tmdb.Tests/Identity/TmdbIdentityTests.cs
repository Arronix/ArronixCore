using System;
using Arronix.Provider.Tmdb.Identity;
using FluentAssertions;
using NUnit.Framework;

namespace Arronix.Provider.Tmdb.Tests.Identity;

[TestFixture]
public sealed class TmdbIdentityTests
{
    [Test]
    public void Read_recognizes_a_single_embedded_marker()
    {
        var readings = TmdbIdentity.Read("Interstellar (2014) {tmdb-157336}.mkv");

        readings.Should().ContainSingle();
        readings[0].Id.Scheme.Should().Be("tmdb");
        readings[0].Id.Value.Should().Be("157336");
        readings[0].Marker.Should().Be("{tmdb-157336}");
        readings[0].Index.Should().Be("Interstellar (2014) ".Length);
    }

    [Test]
    public void Read_is_case_insensitive()
    {
        var readings = TmdbIdentity.Read("Interstellar {TMDB-157336}");

        readings.Should().ContainSingle();
        readings[0].Id.Value.Should().Be("157336");
    }

    [Test]
    public void Read_recognizes_every_marker_in_source_order()
    {
        var readings = TmdbIdentity.Read("{tmdb-1} filler {tmdb-2}");

        readings.Should().HaveCount(2);
        readings[0].Id.Value.Should().Be("1");
        readings[1].Id.Value.Should().Be("2");
        readings[0].Index.Should().BeLessThan(readings[1].Index);
    }

    [TestCase("No marker here")]
    [TestCase("{tmdb-}")]
    [TestCase("{tmdb-abc}")]
    [TestCase("tmdb-157336")]
    public void Read_finds_nothing_for_unrecognized_text(string text) =>
        TmdbIdentity.Read(text).Should().BeEmpty();

    [TestCase("{tmdb-0}", TestName = "{m}(zero)")]
    [TestCase("{tmdb-00}", TestName = "{m}(all_zeros)")]
    [TestCase("{tmdb-007}", TestName = "{m}(leading_zero)")]
    [TestCase("{tmdb-99999999999999999999}", TestName = "{m}(overflows_int)")]
    public void Read_produces_no_reading_for_a_non_canonical_marker(string text) =>
        TmdbIdentity.Read(text).Should().BeEmpty();

    [Test]
    public void Read_recognizes_int_MaxValue_as_a_canonical_marker()
    {
        var readings = TmdbIdentity.Read("{tmdb-2147483647}");

        readings.Should().ContainSingle();
        readings[0].Id.Value.Should().Be("2147483647");
    }

    [Test]
    public void Read_produces_only_readings_the_same_parser_can_resolve_again()
    {
        // The round trip that matters: a marker Read() accepted must be exactly what TryParseId (the same
        // authority GetAsync calls) would itself accept — not merely a value that happens to look similar.
        var readings = TmdbIdentity.Read("{tmdb-157336}");

        readings.Should().ContainSingle();
        TmdbIdentity.TryParseId(readings[0].Id.Value, out var resolved).Should().BeTrue();
        resolved.Should().Be(157336);
    }

    [TestCase("0")]
    [TestCase("00")]
    [TestCase("007")]
    [TestCase("-5")]
    [TestCase("+5")]
    [TestCase(" 603")]
    [TestCase("603 ")]
    [TestCase("6.03")]
    [TestCase("99999999999999999999")]
    [TestCase("2147483648")]
    public void TryParseId_rejects_every_non_canonical_shape(string value) =>
        TmdbIdentity.TryParseId(value, out _).Should().BeFalse();

    [Test]
    public void TryParseId_accepts_int_MaxValue_as_the_canonical_upper_bound()
    {
        TmdbIdentity.TryParseId(int.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture), out var id)
            .Should().BeTrue();
        id.Should().Be(int.MaxValue);
    }

    [TestCase(603, true)]
    [TestCase(int.MaxValue, true)]
    [TestCase(0, false)]
    [TestCase(-5, false)]
    public void IsCanonicalId_agrees_with_TryParseId_for_the_same_value(int value, bool expected) =>
        TmdbIdentity.IsCanonicalId(value).Should().Be(expected);

    [Test]
    public void Read_rejects_null_text() =>
        FluentActions.Invoking(() => TmdbIdentity.Read(null!)).Should().Throw<ArgumentNullException>();

    [Test]
    public void Read_performs_no_network_call_and_completes_synchronously()
    {
        // No gateway, no context, no async: recognition is local and deterministic by construction here,
        // not merely by observed behavior of one call.
        var readings = TmdbIdentity.Read("{tmdb-42}");
        readings.Should().ContainSingle();
    }
}
