
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Intent;
using Arronix.Abstractions.Providers;
using Arronix.Abstractions.Shape;
using Arronix.Abstractions.Wire;
using Arronix.Client.Serialization;

namespace Arronix.Client.Services;

/// <summary>
/// Every request this client makes of the platform, in one place.
/// </summary>
/// <remarks>
/// <para>
/// Typed against the published wire contracts rather than against strings or dynamic objects, because the
/// client compiles against the same contract assembly the server does: an endpoint that changes shape is
/// then a compile error here rather than a null reference in a view.
/// </para>
/// <para>
/// Every call routes its transport failures through <see cref="HostConnectivity"/>. That is what turns a
/// server restart into a stated, self-recovering application state instead of a scattering of unrelated
/// failures in whichever views happened to be loading.
/// </para>
/// </remarks>
public sealed class ArronixApiClient
{
    private readonly HttpClient _http;
    private readonly HostConnectivity _connectivity;
    private readonly JsonSerializerOptions _json = ApiJsonOptions.Default;

    /// <summary>
    /// Initializes a new instance of the <see cref="ArronixApiClient"/> class.
    /// </summary>
    /// <param name="http">The client used to reach the server.</param>
    /// <param name="connectivity">Where transport outcomes are reported.</param>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    public ArronixApiClient(HttpClient http, HostConnectivity connectivity)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(connectivity);

        _http = http;
        _connectivity = connectivity;
    }

    /// <summary>Reads every media kind the platform has registered.</summary>
    /// <param name="cancellationToken">Abandons the request.</param>
    /// <returns>The media kinds.</returns>
    public Task<IReadOnlyList<MediaKindDescriptor>> GetKindsAsync(CancellationToken cancellationToken = default)
        => GetListAsync<MediaKindDescriptor>(ApiPaths.Kinds, cancellationToken);

    /// <summary>Reads one media kind's whole description.</summary>
    /// <param name="kind">The media kind.</param>
    /// <param name="cancellationToken">Abandons the request.</param>
    /// <returns>The description, or <see langword="null"/> when the kind is not registered.</returns>
    public Task<MediaKindDescriptor?> GetKindAsync(MediaKindId kind, CancellationToken cancellationToken = default)
        => GetOrNullAsync<MediaKindDescriptor>(ApiPaths.Kind(kind.Value), cancellationToken);

    /// <summary>Reads one page of one level's items.</summary>
    /// <param name="kind">The media kind.</param>
    /// <param name="level">The level.</param>
    /// <param name="request">What is being asked for.</param>
    /// <param name="cancellationToken">Abandons the request.</param>
    /// <returns>The page.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    public Task<ItemDetailPage> GetItemsAsync(
        MediaKindId kind,
        MediaLevelId level,
        ItemBrowseRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return GetAsync<ItemDetailPage>(
            ApiPaths.LevelItems(kind.Value, level.ToString(), request.ToQuery()),
            cancellationToken);
    }

    /// <summary>Reads one item.</summary>
    /// <param name="kind">The media kind.</param>
    /// <param name="itemId">The item's identifier.</param>
    /// <param name="cancellationToken">Abandons the request.</param>
    /// <returns>The item, or <see langword="null"/> when it does not exist.</returns>
    public Task<ItemDetail?> GetItemAsync(
        MediaKindId kind,
        MediaItemId itemId,
        CancellationToken cancellationToken = default)
        => GetOrNullAsync<ItemDetail>(ApiPaths.Item(kind.Value, itemId.Value), cancellationToken);

    /// <summary>Reads one page of an item's contents.</summary>
    /// <param name="kind">The media kind.</param>
    /// <param name="itemId">The containing item's identifier.</param>
    /// <param name="request">What is being asked for.</param>
    /// <param name="cancellationToken">Abandons the request.</param>
    /// <returns>The page.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    public Task<ItemDetailPage> GetChildrenAsync(
        MediaKindId kind,
        MediaItemId itemId,
        ItemBrowseRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return GetAsync<ItemDetailPage>(
            ApiPaths.ItemChildren(kind.Value, itemId.Value, request.ToQuery()),
            cancellationToken);
    }

    /// <summary>Reads one page of a cross-cutting collection.</summary>
    /// <param name="kind">The media kind.</param>
    /// <param name="axisId">The grouping axis.</param>
    /// <param name="request">What is being asked for.</param>
    /// <param name="cancellationToken">Abandons the request.</param>
    /// <returns>The page.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    public Task<ItemDetailPage> GetGroupsAsync(
        MediaKindId kind,
        string axisId,
        ItemBrowseRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return GetAsync<ItemDetailPage>(
            ApiPaths.Groups(kind.Value, axisId, request.ToQuery()),
            cancellationToken);
    }

    /// <summary>Asks the platform to do something.</summary>
    /// <param name="kind">The media kind.</param>
    /// <param name="actionId">The declared action.</param>
    /// <param name="request">What the action is being done to.</param>
    /// <param name="cancellationToken">Abandons the request.</param>
    /// <returns>Whether the platform took the request on.</returns>
    public Task<ActionResult> InvokeActionAsync(
        MediaKindId kind,
        string actionId,
        ActionRequest request,
        CancellationToken cancellationToken = default)
        => PostAsync<ActionRequest, ActionResult>(ApiPaths.Action(kind.Value, actionId), request, cancellationToken);

    /// <summary>Asks an extension to propose a set of decisions.</summary>
    /// <param name="kind">The media kind.</param>
    /// <param name="workbenchId">The working surface.</param>
    /// <param name="inputs">The values the surface declared it needed.</param>
    /// <param name="cancellationToken">Abandons the request.</param>
    /// <returns>The proposal.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="inputs"/> is <see langword="null"/>.</exception>
    public Task<WorkbenchProposal?> GetWorkbenchProposalAsync(
        MediaKindId kind,
        string workbenchId,
        IReadOnlyDictionary<string, string> inputs,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        var query = inputs
            .OrderBy(input => input.Key, StringComparer.Ordinal)
            .Select(input => new KeyValuePair<string, string?>(input.Key, input.Value))
            .ToList();

        return GetOrNullAsync<WorkbenchProposal>(
            ApiPaths.WorkbenchProposal(kind.Value, workbenchId, query),
            cancellationToken);
    }

    /// <summary>Reads the values one working-surface cell may take.</summary>
    /// <param name="kind">The media kind.</param>
    /// <param name="workbenchId">The working surface.</param>
    /// <param name="sourceId">The set of values.</param>
    /// <param name="rowId">The row the values are for.</param>
    /// <param name="cancellationToken">Abandons the request.</param>
    /// <returns>The permitted values.</returns>
    public Task<IReadOnlyList<FacetValue>> GetWorkbenchOptionsAsync(
        MediaKindId kind,
        string workbenchId,
        string sourceId,
        string? rowId,
        CancellationToken cancellationToken = default)
        => GetListAsync<FacetValue>(
            ApiPaths.WorkbenchOptions(kind.Value, workbenchId, sourceId, rowId),
            cancellationToken);

    /// <summary>Applies a working surface's decisions.</summary>
    /// <param name="kind">The media kind.</param>
    /// <param name="commit">The decisions as the user left them.</param>
    /// <param name="cancellationToken">Abandons the request.</param>
    /// <returns>Whether the platform took the request on.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="commit"/> is <see langword="null"/>.</exception>
    public Task<ActionResult> CommitWorkbenchAsync(
        MediaKindId kind,
        WorkbenchCommit commit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commit);
        return PostAsync<WorkbenchCommit, ActionResult>(
            ApiPaths.WorkbenchCommit(kind.Value, commit.WorkbenchId),
            commit,
            cancellationToken);
    }

    /// <summary>Reads the providers that can be configured.</summary>
    /// <param name="family">The family to narrow to.</param>
    /// <param name="kind">The media kind to narrow to.</param>
    /// <param name="cancellationToken">Abandons the request.</param>
    /// <returns>The providers.</returns>
    public Task<IReadOnlyList<ProviderDescriptor>> GetProvidersAsync(
        ProviderFamily? family = null,
        MediaKindId? kind = null,
        CancellationToken cancellationToken = default)
        => GetListAsync<ProviderDescriptor>(
            ApiPaths.Providers(
                family?.ToString(),
                kind?.Value),
            cancellationToken);

    /// <summary>Reads the configured providers.</summary>
    /// <param name="cancellationToken">Abandons the request.</param>
    /// <returns>The configurations, with their secrets elided.</returns>
    public Task<IReadOnlyList<ProviderDefinition>> GetProviderDefinitionsAsync(
        CancellationToken cancellationToken = default)
        => GetListAsync<ProviderDefinition>(ApiPaths.ProviderDefinitions, cancellationToken);

    /// <summary>Adds a configured provider.</summary>
    /// <param name="definition">The configuration.</param>
    /// <param name="cancellationToken">Abandons the request.</param>
    /// <returns>The stored configuration.</returns>
    public Task<ProviderDefinition> CreateProviderDefinitionAsync(
        ProviderDefinition definition,
        CancellationToken cancellationToken = default)
        => PostAsync<ProviderDefinition, ProviderDefinition>(
            ApiPaths.ProviderDefinitions,
            definition,
            cancellationToken);

    /// <summary>Replaces a configured provider.</summary>
    /// <param name="definition">The configuration.</param>
    /// <param name="cancellationToken">Abandons the request.</param>
    /// <returns>A task that completes when the server has stored it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="definition"/> is <see langword="null"/>.</exception>
    public async Task UpdateProviderDefinitionAsync(
        ProviderDefinition definition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);

        using var content = JsonContent.Create(definition, options: _json);
        using var response = await SendAsync(
            HttpMethod.Put,
            ApiPaths.ProviderDefinition(definition.Id),
            content,
            cancellationToken).ConfigureAwait(false);

        await EnsureSuccessAsync(response).ConfigureAwait(false);
    }

    /// <summary>Removes a configured provider.</summary>
    /// <param name="definitionId">The configuration's identifier.</param>
    /// <param name="cancellationToken">Abandons the request.</param>
    /// <returns>A task that completes when the server has removed it.</returns>
    public async Task DeleteProviderDefinitionAsync(
        int definitionId,
        CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(
            HttpMethod.Delete,
            ApiPaths.ProviderDefinition(definitionId),
            content: null,
            cancellationToken).ConfigureAwait(false);

        await EnsureSuccessAsync(response).ConfigureAwait(false);
    }

    /// <summary>Asks the platform to check that a configured provider works.</summary>
    /// <param name="definitionId">The configuration's identifier.</param>
    /// <param name="cancellationToken">Abandons the request.</param>
    /// <returns>What was wrong with it, if anything.</returns>
    public async Task<ValidationOutcome> TestProviderDefinitionAsync(
        int definitionId,
        CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(
            HttpMethod.Post,
            ApiPaths.ProviderTest(definitionId),
            content: null,
            cancellationToken).ConfigureAwait(false);

        await EnsureSuccessAsync(response).ConfigureAwait(false);
        return await ReadAsync<ValidationOutcome>(response, cancellationToken).ConfigureAwait(false)
            ?? ValidationOutcome.Success;
    }

    /// <summary>Reads the values one provider setting may take.</summary>
    /// <param name="definitionId">The configuration's identifier.</param>
    /// <param name="sourceId">The set of values.</param>
    /// <param name="cancellationToken">Abandons the request.</param>
    /// <returns>The permitted values.</returns>
    public Task<IReadOnlyList<FacetValue>> GetProviderOptionsAsync(
        int definitionId,
        string sourceId,
        CancellationToken cancellationToken = default)
        => GetListAsync<FacetValue>(ApiPaths.ProviderOptions(definitionId, sourceId), cancellationToken);

    /// <summary>Reads the installed extensions.</summary>
    /// <param name="cancellationToken">Abandons the request.</param>
    /// <returns>The extensions and where each got to in the load pipeline.</returns>
    public Task<IReadOnlyList<PluginStatusView>> GetPluginsAsync(CancellationToken cancellationToken = default)
        => GetListAsync<PluginStatusView>(ApiPaths.Plugins, cancellationToken);

    /// <summary>Reads the registered background jobs.</summary>
    /// <param name="cancellationToken">Abandons the request.</param>
    /// <returns>The jobs.</returns>
    public Task<IReadOnlyList<JobView>> GetJobsAsync(CancellationToken cancellationToken = default)
        => GetListAsync<JobView>(ApiPaths.Jobs, cancellationToken);

    /// <summary>Asks the platform to run a job now.</summary>
    /// <param name="jobId">The job.</param>
    /// <param name="cancellationToken">Abandons the request.</param>
    /// <returns>Whether the platform took the request on.</returns>
    public async Task<ActionResult> TriggerJobAsync(string jobId, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(
            HttpMethod.Post,
            ApiPaths.JobTrigger(jobId),
            content: null,
            cancellationToken).ConfigureAwait(false);

        await EnsureSuccessAsync(response).ConfigureAwait(false);
        return await ReadAsync<ActionResult>(response, cancellationToken).ConfigureAwait(false)
            ?? new ActionResult(true, null, null, null);
    }

    /// <summary>Reads the work queue.</summary>
    /// <param name="cancellationToken">Abandons the request.</param>
    /// <returns>The queued work.</returns>
    public Task<IReadOnlyList<QueueEntryView>> GetQueueAsync(CancellationToken cancellationToken = default)
        => GetListAsync<QueueEntryView>(ApiPaths.Queue, cancellationToken);

    /// <summary>Reads the platform's health.</summary>
    /// <param name="cancellationToken">Abandons the request.</param>
    /// <returns>The health snapshot.</returns>
    public Task<HealthSnapshotView?> GetHealthAsync(CancellationToken cancellationToken = default)
        => GetOrNullAsync<HealthSnapshotView>(ApiPaths.Health, cancellationToken);

    private async Task<T> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        var value = await GetOrNullAsync<T>(path, cancellationToken).ConfigureAwait(false);
        return value ?? throw new ApiRequestException(
            HttpStatusCode.NoContent,
            $"The server answered '{path}' with no content.");
    }

    private async Task<IReadOnlyList<T>> GetListAsync<T>(string path, CancellationToken cancellationToken)
        => await GetOrNullAsync<IReadOnlyList<T>>(path, cancellationToken).ConfigureAwait(false) ?? [];

    private async Task<T?> GetOrNullAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, path, content: null, cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.NoContent)
        {
            return default;
        }

        await EnsureSuccessAsync(response).ConfigureAwait(false);
        return await ReadAsync<T>(response, cancellationToken).ConfigureAwait(false);
    }

    private async Task<TResponse> PostAsync<TRequest, TResponse>(
        string path,
        TRequest body,
        CancellationToken cancellationToken)
    {
        using var content = JsonContent.Create(body, options: _json);
        using var response = await SendAsync(HttpMethod.Post, path, content, cancellationToken)
            .ConfigureAwait(false);

        await EnsureSuccessAsync(response).ConfigureAwait(false);
        return await ReadAsync<TResponse>(response, cancellationToken).ConfigureAwait(false)
            ?? throw new ApiRequestException(
                HttpStatusCode.NoContent,
                $"The server answered '{path}' with no content.");
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string path,
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative))
        {
            Content = content,
        };

        try
        {
            var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            _connectivity.ReportReachable();
            return response;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _connectivity.ReportUnreachable();
            throw new HostUnreachableException($"The request to '{path}' timed out.");
        }
        catch (HttpRequestException failure)
        {
            _connectivity.ReportUnreachable();
            throw new HostUnreachableException($"The request to '{path}' could not be sent.", failure);
        }
    }

    private async Task<T?> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content
                .ReadFromJsonAsync<T>(_json, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (JsonException failure)
        {
            throw new ApiRequestException(
                $"The server's answer could not be read as {typeof(T).Name}.",
                failure);
        }
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var status = ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture);
        var detail = string.IsNullOrWhiteSpace(body) ? response.ReasonPhrase ?? string.Empty : body.Trim();

        throw new ApiRequestException(
            response.StatusCode,
            detail.Length == 0 ? $"The server answered {status}." : $"{status}: {Shorten(detail)}");
    }

    private static string Shorten(string text) => text.Length <= 300 ? text : text[..300] + "…";
}
