using System.Linq;
using Arronix.Abstractions.Health;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Intent;
using Arronix.Abstractions.Shape;
using Arronix.Abstractions.Wire;
using Arronix.Host.Media;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;


namespace Arronix.Api.Endpoints;

/// <summary>
/// The editable grid that covers the work no vocabulary of actions and fields can express.
/// </summary>
/// <remarks>
/// <para>
/// Assigning a folder of loose files to the right places in a library is a daily task, not an exotic one,
/// and it is not an action with parameters: it is a table the extension proposes, a person corrects, and
/// the extension then commits. Three routes are enough for that — ask for the proposal, resolve the choices
/// a column offers, post the corrected table back — and one generic grid renders all of them.
/// </para>
/// <para>
/// The same three routes also cover interactive release search and bulk editing, which is the reason this
/// was worth a primitive rather than a special case: it is the smallest thing that turns "not expressible"
/// into "expressible" for the three hardest screens in this class of application, and it still names no
/// control and ships no markup.
/// </para>
/// <para>
/// <strong>One honest limitation.</strong> A column's choices are resolved from the extension's declaration
/// of that column, which means they are the same for every row. Row-scoped choices — the legal destinations
/// for file A not being the legal destinations for file B — are asked for by the route and cannot yet be
/// answered, because the extension-side contract has a method for proposing and a method for committing and
/// none for resolving an option source. The row identifier is accepted and passed nowhere; adding one method
/// to that contract is what makes it real.
/// </para>
/// </remarks>
internal static class WorkbenchEndpoints
{
    private static readonly string[] ReservedInputNames = ["row"];

    /// <summary>
    /// Maps the workbench routes.
    /// </summary>
    /// <param name="group">The versioned route group.</param>
    /// <returns>The same group, for chaining.</returns>
    internal static RouteGroupBuilder MapWorkbenchEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        var workbenches = group.MapGroup("/kinds/{kind}/workbenches/{workbenchId}").WithTags("Workbench");

        workbenches.MapGet("/proposal", GetProposal)
            .WithName("GetWorkbenchProposal")
            .WithSummary("Asks the extension for its proposed table, given the inputs the declaration asked for.");

        workbenches.MapGet("/options/{sourceId}", GetOptions)
            .WithName("GetWorkbenchOptions")
            .WithSummary("Resolves the values a column offers.");

        workbenches.MapPost("/commit", Commit)
            .WithName("CommitWorkbench")
            .WithSummary("Posts the corrected table back for the extension to act on.");

        return group;
    }

    private static async Task<Results<Ok<WorkbenchProposal>, ProblemHttpResult>> GetProposal(
        string kind,
        string workbenchId,
        IMediaKindRegistry registry,
        MediaItemBroker items,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(context);

        if (!TryResolve(registry, kind, workbenchId, out var registered, out var descriptor, out var problem))
        {
            return problem;
        }

        var inputs = ApiRequests.Inputs(context.Request, ReservedInputNames);

        var missing = descriptor.Inputs
            .Where(input => input.Required && !inputs.ContainsKey(input.ParameterId))
            .Select(static input => input.Name)
            .ToList();

        if (missing.Count > 0)
        {
            return ApiRequests.Problem(
                StatusCodes.Status400BadRequest,
                CoreErrorCode.InvalidConfiguration,
                $"'{descriptor.Name}' needs {string.Join(", ", missing)} before it can propose anything.");
        }

        var proposal = await items
            .ProposeAsync(registered.Kind, descriptor.WorkbenchId, inputs, cancellationToken)
            .ConfigureAwait(false);

        if (proposal is null)
        {
            return ApiRequests.Problem(
                StatusCodes.Status404NotFound,
                CoreErrorCode.MediaKindNotFound,
                $"'{kind}' is no longer installed.");
        }

        return TypedResults.Ok(proposal);
    }

    private static Results<Ok<IReadOnlyList<FacetValue>>, ProblemHttpResult> GetOptions(
        string kind,
        string workbenchId,
        string sourceId,
        IMediaKindRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        if (!TryResolve(registry, kind, workbenchId, out _, out var descriptor, out var problem))
        {
            return problem;
        }

        var column = descriptor.Columns.FirstOrDefault(candidate =>
            string.Equals(candidate.OptionSourceId, sourceId, StringComparison.Ordinal));

        if (column is null)
        {
            return ApiRequests.Problem(
                StatusCodes.Status404NotFound,
                CoreErrorCode.MediaKindNotFound,
                $"'{descriptor.Name}' declares no column resolved by option source '{sourceId}'.");
        }

        return TypedResults.Ok(column.Field.Choices);
    }

    private static async Task<Results<Ok<ActionResult>, Accepted<ActionResult>, ProblemHttpResult>> Commit(
        string kind,
        string workbenchId,
        WorkbenchCommit? commit,
        IMediaKindRegistry registry,
        MediaItemBroker items,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(registry);

        if (!TryResolve(registry, kind, workbenchId, out var registered, out var descriptor, out var problem))
        {
            return problem;
        }

        if (commit is null)
        {
            return ApiRequests.Problem(
                StatusCodes.Status400BadRequest,
                CoreErrorCode.InvalidConfiguration,
                "A commit needs a body carrying the rows to apply.");
        }

        if (!string.Equals(commit.WorkbenchId, descriptor.WorkbenchId, StringComparison.Ordinal))
        {
            return ApiRequests.Problem(
                StatusCodes.Status400BadRequest,
                CoreErrorCode.InvalidConfiguration,
                $"The body commits '{commit.WorkbenchId}' but the route addresses '{descriptor.WorkbenchId}'.");
        }

        if (!descriptor.AllowsRowExclusion && commit.ExcludedRowIds.Count > 0)
        {
            return ApiRequests.Problem(
                StatusCodes.Status400BadRequest,
                CoreErrorCode.InvalidConfiguration,
                $"'{descriptor.Name}' does not allow rows to be excluded.");
        }

        var result = await items.CommitAsync(registered.Kind, commit, cancellationToken).ConfigureAwait(false);

        if (result is null)
        {
            return ApiRequests.Problem(
                StatusCodes.Status404NotFound,
                CoreErrorCode.MediaKindNotFound,
                $"'{kind}' is no longer installed.");
        }

        if (!result.Accepted)
        {
            return ApiRequests.Problem(
                StatusCodes.Status409Conflict,
                CoreErrorCode.ImportValidationFailed,
                result.Message ?? $"'{descriptor.Name}' refused the commit.");
        }

        // Committing a table of files is work that outlives the request in every case worth building this
        // for, so the caller is given the correlation identifier and told to watch the stream.
        return TypedResults.Accepted((string?)null, result);
    }

    private static bool TryResolve(
        IMediaKindRegistry registry,
        string kind,
        string workbenchId,
        out RegisteredMediaKind registered,
        out WorkbenchDescriptor descriptor,
        out ProblemHttpResult problem)
    {
        registered = null!;
        descriptor = null!;

        if (!registry.TryGet(MediaKindId.FromString(kind), out var found) || found is null)
        {
            problem = ApiRequests.UnknownKind(kind);
            return false;
        }

        var workbench = found.Descriptor.Intent.Workbenches.FirstOrDefault(candidate =>
            string.Equals(candidate.WorkbenchId, workbenchId, StringComparison.Ordinal));

        if (workbench is null)
        {
            problem = ApiRequests.Problem(
                StatusCodes.Status404NotFound,
                CoreErrorCode.MediaKindNotFound,
                $"'{kind}' declares no workbench '{workbenchId}'.");
            return false;
        }

        registered = found;
        descriptor = workbench;
        problem = null!;
        return true;
    }
}
