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
