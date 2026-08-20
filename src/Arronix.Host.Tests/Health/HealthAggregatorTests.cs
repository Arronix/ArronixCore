using System.Linq;
using Arronix.Abstractions.Health;
using Arronix.Host.Configuration;
using Arronix.Host.Health;
using Arronix.Host.Tests.Support;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;


namespace Arronix.Host.Tests.Health;

/// <summary>
/// The three rules that make a health endpoint answer: isolation, a timeout, and a cache that is invalidated.
/// </summary>
[TestFixture]
internal sealed class HealthAggregatorTests
{
    private static (HealthAggregator Aggregator, FakeTimeProvider Clock) Build(
        IEnumerable<IHealthContributor> contributors,
        HealthOptions? options = null)
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var settings = options ?? new HealthOptions { CacheLifetime = TimeSpan.Zero };

        return (new HealthAggregator(contributors, clock, TestOptions.Of(settings)), clock);
    }

    [Test]
    public async Task AHostWithNoContributorsIsHealthy()
    {
        var (aggregator, _) = Build([]);

        var snapshot = await aggregator.CollectAsync();

        snapshot.Status.Should().Be(HealthStatus.Healthy);
        snapshot.Checks.Should().BeEmpty();
    }

    [Test]
    public async Task TheOverallStatusIsTheWorstOfTheChecks()
    {
        var (aggregator, _) = Build(
        [
            FakeContributor.Reporting("a", HealthStatus.Healthy),
            FakeContributor.Reporting("b", HealthStatus.Degraded),
            FakeContributor.Reporting("c", HealthStatus.Unhealthy),
        ]);

        (await aggregator.CollectAsync()).Status.Should().Be(HealthStatus.Unhealthy);
    }

    [Test]
    public async Task DegradedIsWorseThanHealthyAndBetterThanUnhealthy()
    {
        var (aggregator, _) = Build(
        [
            FakeContributor.Reporting("a", HealthStatus.Healthy),
            FakeContributor.Reporting("b", HealthStatus.Degraded),
        ]);

        (await aggregator.CollectAsync()).Status.Should().Be(HealthStatus.Degraded);
    }

    [Test]
    public async Task ChecksAreOrderedWorstFirst()
    {
        var (aggregator, _) = Build(
        [
            FakeContributor.Reporting("a", HealthStatus.Healthy),
            FakeContributor.Reporting("b", HealthStatus.Unhealthy),
            FakeContributor.Reporting("c", HealthStatus.Degraded),
        ]);

        (await aggregator.CollectAsync()).Checks.Select(check => check.CheckId)
            .Should().Equal("b", "c", "a");
    }

    [Test]
    public async Task AThrowingContributorBecomesAnUnhealthyCheckAttributedToItself()
    {
        var (aggregator, _) = Build([FakeContributor.Throwing("broken"), FakeContributor.Healthy("fine")]);

        var snapshot = await aggregator.CollectAsync();

        snapshot.Checks.Should().HaveCount(2);
        snapshot.Checks.Should().Contain(check =>
            check.CheckId == "broken" && check.Status == HealthStatus.Unhealthy);

        // The point of the isolation: the healthy contributor still reported.
        snapshot.Checks.Should().Contain(check => check.CheckId == "fine" && check.Status == HealthStatus.Healthy);
    }

    [Test]
    public async Task AHangingContributorIsAbandonedAtItsTimeout()
    {
        var (aggregator, clock) = Build(
            [FakeContributor.Hanging("stuck")],
            new HealthOptions { ContributorTimeout = TimeSpan.FromSeconds(1), CacheLifetime = TimeSpan.Zero });

        var collecting = aggregator.CollectAsync();

        clock.Advance(TimeSpan.FromSeconds(2));

        var snapshot = await collecting;

        snapshot.Status.Should().Be(HealthStatus.Unhealthy);
        snapshot.Checks.Should().ContainSingle().Which.Message.Should().Contain("did not answer");
    }

    [Test]
    public async Task ASuppressedContributorIsNotRunAtAll()
    {
        var options = new HealthOptions { CacheLifetime = TimeSpan.Zero };
        options.SuppressedContributors.Add("noisy");

        var (aggregator, _) = Build(
            [FakeContributor.Throwing("noisy"), FakeContributor.Healthy("fine")],
            options);

        (await aggregator.CollectAsync()).Checks.Should().ContainSingle().Which.CheckId.Should().Be("fine");
    }

    [Test]
    public async Task ARepeatedCollectionInsideTheCacheLifetimeReusesTheReport()
    {
        var runs = 0;
        var counting = new FakeContributor("counting", _ =>
        {
            runs++;
            return Task.FromResult<IReadOnlyList<HealthCheck>>([HealthCheck.Healthy("counting", "counting")]);
        });

        var (aggregator, clock) = Build(
            [counting],
            new HealthOptions { CacheLifetime = TimeSpan.FromSeconds(30) });

        await aggregator.CollectAsync();
        await aggregator.CollectAsync();

        runs.Should().Be(1);

        clock.Advance(TimeSpan.FromSeconds(31));
        await aggregator.CollectAsync();

        runs.Should().Be(2);
    }

    [Test]
    public async Task InvalidatingTheCacheRunsTheContributorsAgainImmediately()
    {
        var runs = 0;
        var counting = new FakeContributor("counting", _ =>
        {
            runs++;
            return Task.FromResult<IReadOnlyList<HealthCheck>>([HealthCheck.Healthy("counting", "counting")]);
        });

        var (aggregator, _) = Build(
            [counting],
            new HealthOptions { CacheLifetime = TimeSpan.FromMinutes(10) });

        await aggregator.CollectAsync();
        aggregator.Invalidate();
        await aggregator.CollectAsync();

        // Without this, a report would keep saying "healthy" for ten minutes after an extension broke, which
        // is exactly the moment it matters.
        runs.Should().Be(2);
    }

    [Test]
    public async Task TheReportIsStampedWithTheInjectedClock()
    {
        var (aggregator, clock) = Build([FakeContributor.Healthy("fine")]);

        (await aggregator.CollectAsync()).CheckedAt.Should().Be(clock.GetUtcNow());
    }

    [Test]
    public async Task TheViewCarriesTheStatusAsTextForConsumersBuiltAgainstAnOlderContract()
    {
        var (aggregator, _) = Build([FakeContributor.Reporting("b", HealthStatus.Degraded)]);

        var view = (await aggregator.CollectAsync()).ToView();

        view.Status.Should().Be("Degraded");
        view.Checks.Should().ContainSingle();
    }
}
