using System.Linq;
using Arronix.Abstractions.Health;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Intent;
using Arronix.Abstractions.Scheduling;
using Arronix.Abstractions.Shape;
using Arronix.Abstractions.Wire;
using Arronix.Host.Configuration;
using Arronix.Host.Media;
using Microsoft.Extensions.Options;


namespace Arronix.Host.Tests.Support;

/// <summary>
/// A catalog projection backed by a dictionary.
/// </summary>
/// <remarks>
/// The store needs coordinates to enforce a span rule and gets them from the owning extension's projection,
/// so a store test needs a projection. This is that, and nothing more.
/// </remarks>
internal sealed class FakeItemSource(MediaKindId kind) : IMediaItemSource
{
    private readonly Dictionary<MediaItemRef, ItemView> _items = [];

    public MediaKindId MediaKind => kind;

    public FakeItemSource With(MediaItemRef reference, CoordinateSet coordinates)
    {
        _items[reference] = new ItemView
        {
            Ref = reference,
            Title = reference.ToString(),
            Fields = new Dictionary<string, FieldValue>(StringComparer.Ordinal),
            Coordinates = coordinates,
        };

        return this;
    }

    public Task<ItemPage> QueryAsync(ItemQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var matching = _items.Values.Where(item => item.Ref.Level == query.Level).ToList();
        return Task.FromResult(new ItemPage(matching, 1, matching.Count, matching.Count));
    }

    public Task<ItemView?> GetAsync(MediaItemRef reference, CancellationToken cancellationToken = default)
        => Task.FromResult(_items.GetValueOrDefault(reference));

    public Task<MediaItemRef?> ResolveExternalAsync(
        ExternalId externalId,
        CancellationToken cancellationToken = default)
        => Task.FromResult<MediaItemRef?>(null);

    public Task<WorkbenchProposal> ProposeAsync(
        string workbenchId,
        IReadOnlyDictionary<string, string> inputs,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new WorkbenchProposal { WorkbenchId = workbenchId, Rows = [] });

    public Task<ActionResult> CommitAsync(WorkbenchCommit commit, CancellationToken cancellationToken = default)
        => Task.FromResult(new ActionResult(true, null, null, null));
}

/// <summary>
/// A job whose behavior each test states outright.
/// </summary>
internal sealed class FakeJob(
    string jobId,
    Func<JobExecutionContext, CancellationToken, Task<JobExecutionResult>> body,
    int maxConcurrency = 1) : IScheduledJob
{
    private int _runs;

    public string JobId => jobId;

    public string Name => jobId;

    public string Description => jobId;

    public int Priority { get; init; }

    public int MaxConcurrency => maxConcurrency;

    public TimeSpan ShutdownDeadline { get; init; } = TimeSpan.FromSeconds(5);

    public int Runs => Volatile.Read(ref _runs);

    public Task<JobExecutionResult> ExecuteAsync(
        JobExecutionContext context,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _runs);
        return body(context, cancellationToken);
    }

    internal static FakeJob Succeeding(string jobId, int maxConcurrency = 1)
        => new(jobId, (_, _) => Task.FromResult(new JobExecutionResult(true)), maxConcurrency);

    internal static FakeJob Throwing(string jobId, Exception failure)
        => new(jobId, (_, _) => Task.FromException<JobExecutionResult>(failure));
}

/// <summary>
/// A health contributor whose behavior each test states outright.
/// </summary>
internal sealed class FakeContributor(
    string contributorId,
    Func<CancellationToken, Task<IReadOnlyList<HealthCheck>>> body) : IHealthContributor
{
    public string ContributorId => contributorId;

    public Task<IReadOnlyList<HealthCheck>> CheckAsync(CancellationToken cancellationToken = default)
        => body(cancellationToken);

    internal static FakeContributor Healthy(string id)
        => new(id, _ => Task.FromResult<IReadOnlyList<HealthCheck>>(
            [HealthCheck.Healthy(id, id)]));

    internal static FakeContributor Reporting(string id, HealthStatus status)
        => new(id, _ => Task.FromResult<IReadOnlyList<HealthCheck>>(
        [
            status switch
            {
                HealthStatus.Healthy => HealthCheck.Healthy(id, id),
                HealthStatus.Degraded => HealthCheck.Degraded(id, id, HealthSeverity.Warning, "degraded"),
                _ => HealthCheck.Unhealthy(id, id, HealthSeverity.Error, "unhealthy"),
            },
        ]));

    internal static FakeContributor Throwing(string id)
        => new(id, _ => throw new InvalidOperationException("this contributor is broken"));

    internal static FakeContributor Hanging(string id)
        => new(id, async token =>
        {
            await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);
            return [];
        });
}

/// <summary>
/// Options helpers, so a test states only the setting it is about.
/// </summary>
internal static class TestOptions
{
    internal static IOptions<TOptions> Of<TOptions>(TOptions value)
        where TOptions : class
        => Microsoft.Extensions.Options.Options.Create(value);

    internal static MediaKindRegistry RegistryWith(params MediaKindContribution[] contributions)
    {
        var registry = new MediaKindRegistry(Of(new LibraryOptions()));

        foreach (var contribution in contributions)
        {
            if (!registry.TryRegister(contribution, out _, out var defects))
            {
                throw new InvalidOperationException(
                    $"The fixture shape did not validate: {string.Join("; ", defects.Select(defect => defect.Message))}");
            }
        }

        return registry;
    }
}
