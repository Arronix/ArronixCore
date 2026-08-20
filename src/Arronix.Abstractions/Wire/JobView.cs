
namespace Arronix.Abstractions.Wire;

/// <summary>
/// What the platform will say about one registered background job.
/// </summary>
/// <param name="JobId">The job's identifier.</param>
/// <param name="Name">Its display name.</param>
/// <param name="Description">What it does.</param>
/// <param name="Owner">The extension that registered it, or the platform itself.</param>
/// <param name="Schedule">Its schedule, verbatim as it was registered.</param>
/// <param name="LastRun">When it last ran, when it has.</param>
/// <param name="NextRun">When it will next run, when it is scheduled to.</param>
/// <param name="LastSucceeded">Whether the last run succeeded.</param>
/// <param name="Priority">Its rank among jobs competing for the same capacity.</param>
/// <remarks>
/// The schedule is carried as the text it was registered with rather than as a parsed structure: the
/// registration contract takes a string, and publishing a parsed form would create a second answer to
/// what a job's schedule is.
/// </remarks>
public sealed record JobView(
    string JobId,
    string Name,
    string Description,
    string Owner,
    string Schedule,
    DateTimeOffset? LastRun,
    DateTimeOffset? NextRun,
    bool LastSucceeded,
    int Priority);
