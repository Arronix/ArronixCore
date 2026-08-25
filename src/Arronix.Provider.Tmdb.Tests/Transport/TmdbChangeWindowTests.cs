using System;
using Arronix.Provider.Tmdb.Transport;
using FluentAssertions;
using NUnit.Framework;

namespace Arronix.Provider.Tmdb.Tests.Transport;

[TestFixture]
public sealed class TmdbChangeWindowTests
{
    [Test]
    public void Partition_returns_one_window_for_a_range_of_exactly_14_days()
    {
        var windows = TmdbChangeWindow.Partition(new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 14));

        windows.Should().ContainSingle();
        windows[0].Start.Should().Be(new DateOnly(2024, 1, 1));
        windows[0].End.Should().Be(new DateOnly(2024, 1, 14));
    }

    [Test]
    public void Partition_returns_one_window_for_a_single_day_range()
    {
        var windows = TmdbChangeWindow.Partition(new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 1));

        windows.Should().ContainSingle();
        windows[0].Should().Be((new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 1)));
    }

    [Test]
    public void Partition_splits_a_15_day_range_into_a_14_day_window_and_a_1_day_remainder()
    {
        var windows = TmdbChangeWindow.Partition(new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 15));

        windows.Should().HaveCount(2);
        windows[0].Should().Be((new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 14)));
        windows[1].Should().Be((new DateOnly(2024, 1, 15), new DateOnly(2024, 1, 15)));
    }

    [Test]
    public void Partition_covers_a_31_day_range_with_no_gap_and_no_overlap_and_no_window_over_14_days()
    {
        var windows = TmdbChangeWindow.Partition(new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 31));

        windows.Should().HaveCount(3);
        windows[0].Should().Be((new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 14)));
        windows[1].Should().Be((new DateOnly(2024, 1, 15), new DateOnly(2024, 1, 28)));
        windows[2].Should().Be((new DateOnly(2024, 1, 29), new DateOnly(2024, 1, 31)));

        for (var index = 1; index < windows.Count; index++)
        {
            windows[index].Start.Should().Be(windows[index - 1].End.AddDays(1), "windows must not gap or overlap");
        }

        foreach (var window in windows)
        {
            (window.End.DayNumber - window.Start.DayNumber + 1).Should().BeLessThanOrEqualTo(14);
        }
    }

    [Test]
    public void Partition_returns_nothing_when_since_is_after_until()
    {
        var windows = TmdbChangeWindow.Partition(new DateOnly(2024, 1, 15), new DateOnly(2024, 1, 1));

        windows.Should().BeEmpty();
    }

    [Test]
    public void Partition_is_deterministic()
    {
        var first = TmdbChangeWindow.Partition(new DateOnly(2023, 6, 1), new DateOnly(2024, 6, 1));
        var second = TmdbChangeWindow.Partition(new DateOnly(2023, 6, 1), new DateOnly(2024, 6, 1));

        first.Should().Equal(second);
    }
}
