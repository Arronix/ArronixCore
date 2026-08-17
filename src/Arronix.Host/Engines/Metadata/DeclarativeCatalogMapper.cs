// The http (ARX0008), shape (ARX0013), providers (ARX0015) and definition (ARX0019) contracts are
// experimental until 1.0.
#pragma warning disable ARX0008
#pragma warning disable ARX0013
#pragma warning disable ARX0015
#pragma warning disable ARX0019

using System.Linq;
using System.Net;
using System.Text.Json;
using Arronix.Abstractions.Definition;
using Arronix.Abstractions.Http;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Providers;
using Arronix.Abstractions.Shape;

namespace Arronix.Host.Engines.Metadata;

/// <summary>
/// The host metadata mapper: an <see cref="ICataloger"/> built from a <see cref="CatalogDeclaration"/>,
/// executing declared request and response mappings over the host's outbound gateway.
/// </summary>
/// <remarks>
/// <para>
/// Engine E8 of <c>docs/design/declarative-media-kinds.md</c> §2.8. The definition never sees a socket:
/// the HTTP call is injected behind <see cref="IHttpGateway"/>, which is what makes the mapping logic
/// fully testable without network and the <c>Network</c> privilege structurally ungrantable for a
/// definition-mode plugin. Time comes from <see cref="TimeProvider"/>, never the wall clock.
/// </para>
/// <para>
/// Operation binding is by convention, because <see cref="RequestTemplate"/> declares no operation
/// role: <see cref="SearchAsync"/> runs the request named <c>search</c>,
/// <see cref="ChangedSinceAsync"/> the request named <c>changed</c>, and <see cref="GetAsync"/> the
/// request whose single route placeholder spells the identifier's scheme
/// (<c>{tmdbId}</c> ⇔ <c>tmdb</c>, <c>{collectionTmdbId}</c> ⇔ <c>tmdb-collection</c>). The missing
/// declaration slot is reported as a contract gap, not invented here.
/// </para>
/// </remarks>
internal sealed class DeclarativeCatalogMapper : ICataloger
{
    private const string SearchRequestId = "search";
    private const string ChangedRequestId = "changed";

    private readonly CatalogDeclaration _declaration;
    private readonly CatalogResponseMapper _mapper;
    private readonly CatalogIdRules _idRules;
    private readonly IHttpGateway _gateway;
    private readonly TimeProvider _clock;

    /// <summary>
    /// Initializes a new instance of the <see cref="DeclarativeCatalogMapper"/> class.
    /// </summary>
    /// <param name="id">The provider identity the host registered the definition under.</param>
    /// <param name="shape">The kind's shape.</param>
    /// <param name="declaration">The catalog declaration.</param>
    /// <param name="gateway">The outbound gateway, already scoped to the plugin's identity.</param>
    /// <param name="clock">The host clock.</param>
    public DeclarativeCatalogMapper(
        ProviderId id,
        MediaShape shape,
        CatalogDeclaration declaration,
        IHttpGateway gateway,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(shape);
        ArgumentNullException.ThrowIfNull(declaration);
        ArgumentNullException.ThrowIfNull(gateway);
        ArgumentNullException.ThrowIfNull(clock);

        Id = id;
        MediaKind = shape.Kind;
        _declaration = declaration;
        _idRules = new CatalogIdRules(declaration.IdRules, clock);
        _mapper = new CatalogResponseMapper(declaration, shape, new CatalogValueConverters(_idRules));
        _gateway = gateway;
        _clock = clock;
    }

    /// <inheritdoc />
    public ProviderId Id { get; }

    /// <inheritdoc />
    public ProviderFamily Family => ProviderFamily.Cataloger;

    /// <summary>
    /// Gets the media kind the declaration serves.
    /// </summary>
    public MediaKindId MediaKind { get; }

    /// <inheritdoc />
    public CatalogerCapabilities Capabilities
    {
        get
        {
            var capabilities = CatalogerCapabilities.None;

            if (HasRequest(SearchRequestId))
            {
                capabilities |= CatalogerCapabilities.Search;
            }

            if (_declaration.Delta is not null && HasRequest(ChangedRequestId))
            {
                capabilities |= CatalogerCapabilities.DeltaSync;
            }

            if (HasRequest("discover"))
            {
                capabilities |= CatalogerCapabilities.Discovery;
            }

            return capabilities;
        }
    }

    /// <inheritdoc />
    public Task<ValidationOutcome> TestAsync(
        ProviderInvocation invocation,
        CancellationToken cancellationToken = default)
    {
        var endpointField = _declaration.Settings.FirstOrDefault(field => field.Role == SettingRole.Endpoint);

        if (endpointField is null)
        {
            return Task.FromResult(ValidationOutcome.Failed(
                new ValidationFailure(null, "The catalog declaration names no endpoint setting.")));
        }

        return Task.FromResult(TryEndpoint(invocation, out _, out var failure)
            ? ValidationOutcome.Success
            : ValidationOutcome.Failed(failure!));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<FacetValue>> GetOptionsAsync(
        ProviderInvocation invocation,
        string optionSourceId,
        CancellationToken cancellationToken = default)
    {
        var field = _declaration.Settings.FirstOrDefault(candidate =>
            string.Equals(candidate.OptionSourceId, optionSourceId, StringComparison.Ordinal));

        return Task.FromResult<IReadOnlyList<FacetValue>>(field?.Choices ?? []);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MetadataSearchHit>> SearchAsync(
        ProviderInvocation invocation,
        MetadataQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        // An identifier — supplied or recognized in the text via the declared id rules — resolves
        // instead of searching, exactly as the surveyed lookup boxes do.
        var id = query.Id;

        if (id is null && _idRules.TryRecognize(query.Text, out var recognized))
        {
            id = recognized;
        }

        if (id is { } known)
        {
            var graph = await GetAsync(invocation, known, SelectionPolicy.None, cancellationToken)
                .ConfigureAwait(false);

            return graph is { Nodes.Count: > 0 } ? [ToHit(graph.Nodes[0])] : [];
        }

        var template = FindRequest(SearchRequestId);

        if (template is null)
        {
            return [];
        }

        var arguments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (_idRules.TrySplitTrailingYear(query.Text, out var title, out var year))
        {
            arguments["text"] = title;
            arguments["year"] = year.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        else
        {
            arguments["text"] = query.Text.Trim();
        }

        var map = _mapper.MapForLevel(query.Level?.ToString());

        if (map is null)
        {
            return [];
        }

        var response = await ExecuteAsync(invocation, template, arguments, cancellationToken).ConfigureAwait(false);

        if (response is null)
        {
            return [];
        }

        using var document = JsonDocument.Parse(response.Value.Content);
        var hits = new List<MetadataSearchHit>();
        var now = _clock.GetUtcNow();

        foreach (var element in EnumerateResults(document.RootElement))
        {
            foreach (var node in _mapper.Map(element, map, parentId: null, Derive(invocation, now)))
            {
                hits.Add(ToHit(node));
            }
        }

        return hits;
    }

    /// <inheritdoc />
    public async Task<MetadataGraph?> GetAsync(
        ProviderInvocation invocation,
        ExternalId id,
        SelectionPolicy policy,
        CancellationToken cancellationToken = default)
    {
        var normalized = _idRules.Normalize(id);
        var template = FindRequestForScheme(normalized.Scheme)
            ?? throw new InvalidOperationException(
                $"No declared request fetches scheme '{normalized.Scheme}'.");

        var placeholder = RoutePlaceholders(template.Route).First();
        var arguments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [placeholder] = normalized.Value,
        };

        var response = await ExecuteAsync(invocation, template, arguments, cancellationToken).ConfigureAwait(false);

        if (response is null)
        {
            return null;
        }

        var map = _declaration.Responses.FirstOrDefault(candidate =>
                string.Equals(candidate.ExternalIdScheme, normalized.Scheme, StringComparison.OrdinalIgnoreCase))
            ?? _mapper.MapForLevel(null);

        if (map is null)
        {
            return null;
        }

        using var document = JsonDocument.Parse(response.Value.Content);
        var nodes = _mapper.Map(document.RootElement, map, parentId: null, Derive(invocation, _clock.GetUtcNow()));

        return nodes.Count == 0 ? null : new MetadataGraph(nodes);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ExternalId>> ChangedSinceAsync(
        ProviderInvocation invocation,
        DateTimeOffset since,
        CancellationToken cancellationToken = default)
    {
        var template = FindRequest(ChangedRequestId);

        if (template is null || _declaration.Delta is not { } delta)
        {
            // Declared capability honesty: without delta support the answer is "nothing reported",
            // never a guess (ICataloger contract).
            return [];
        }

        var windowed = Window(since, delta);
        var arguments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["since"] = windowed.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
        };

        var response = await ExecuteAsync(invocation, template, arguments, cancellationToken).ConfigureAwait(false);

        if (response is null)
        {
            return [];
        }

        var map = _mapper.MapForLevel(null);
        using var document = JsonDocument.Parse(response.Value.Content);
        var ids = new List<ExternalId>();

        foreach (var element in EnumerateResults(document.RootElement))
        {
            var text = element.ValueKind is JsonValueKind.Number or JsonValueKind.String
                ? JsonPathReader.Text(element)
                : map is null ? null : JsonPathReader.FirstText(element, map.ExternalIdPath);

            if (text is { Length: > 0 })
            {
                ids.Add(ExternalId.Of(map?.ExternalIdScheme ?? "id", text));
            }
        }

        return ids;
    }

    private static IEnumerable<JsonElement> EnumerateResults(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var element in root.EnumerateArray())
            {
                yield return element;
            }

            yield break;
        }

        // Some catalogs wrap the list; a bare object is a single result.
        if (root.ValueKind == JsonValueKind.Object)
        {
            if (root.TryGetProperty("results", out var wrapped) && wrapped.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in wrapped.EnumerateArray())
                {
                    yield return element;
                }

                yield break;
            }

            yield return root;
        }
    }

    private static MetadataSearchHit ToHit(MetadataNode node)
    {
        int? year = node.Fields.TryGetValue("year", out var yearValue) && yearValue.Number is { } number
            ? (int)number
            : null;

        var disambiguation = node.Fields.TryGetValue("disambiguation", out var text) ? text.Text : null;

        var artwork = node.Fields
            .Where(pair => pair.Value is { Kind: FieldValueKind.Artwork, Link: not null })
            .Select(pair => new ArtworkRef(pair.Key, pair.Value.Link!, null, null))
            .ToList();

        return new MetadataSearchHit(node.Id, node.Level, node.Title, year, disambiguation, artwork);
    }

    private static DateTimeOffset Window(DateTimeOffset since, DeltaSyncPolicy delta)
    {
        // Back off, then floor: the catalog caches on time boundaries, and an exact-instant request
        // straddling one silently drops updates (DeltaSyncPolicy contract; SkyHookProxy.cs:57-59).
        var backed = since.AddMinutes(-delta.BackoffMinutes).ToUniversalTime();

        return delta.FloorTo switch
        {
            TimeFloor.Hour => new DateTimeOffset(backed.Year, backed.Month, backed.Day, backed.Hour, 0, 0, TimeSpan.Zero),
            TimeFloor.Day => new DateTimeOffset(backed.Year, backed.Month, backed.Day, 0, 0, 0, TimeSpan.Zero),
            _ => backed,
        };
    }

    private Action<IDictionary<string, FieldValue>, JsonElement> Derive(ProviderInvocation invocation, DateTimeOffset now) =>
        (fields, element) => CatalogDerivations.Apply(
            _declaration.Derivations,
            fields,
            element,
            SettingsOf(invocation),
            now);

    private IReadOnlyDictionary<string, string> SettingsOf(ProviderInvocation invocation)
    {
        // Configured values over declared defaults, so a derivation referencing a settings field
        // (the certification region) always resolves.
        var settings = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var field in _declaration.Settings)
        {
            if (field.DefaultValue is { Length: > 0 } fallback)
            {
                settings[field.FieldId] = fallback;
            }
        }

        foreach (var (key, value) in invocation.Definition.Settings)
        {
            settings[key] = value;
        }

        return settings;
    }

    private async Task<(string Content, HttpStatusCode Status)?> ExecuteAsync(
        ProviderInvocation invocation,
        RequestTemplate template,
        IReadOnlyDictionary<string, string> arguments,
        CancellationToken cancellationToken)
    {
        if (!TryEndpoint(invocation, out var endpoint, out var failure))
        {
            throw new InvalidOperationException(failure!.Message);
        }

        var request = CatalogRequestBuilder.Build(template, endpoint!, arguments);

        // Absence is an answer, not an error: 404 maps to null upstream (ICataloger.GetAsync).
        request.SuppressedStatusCodes = [HttpStatusCode.NotFound];

        var response = await _gateway.ExecuteAsync(request, cancellationToken).ConfigureAwait(false);

        return response.StatusCode == HttpStatusCode.NotFound
            ? null
            : (response.Content, response.StatusCode);
    }

    private bool TryEndpoint(ProviderInvocation invocation, out Uri? endpoint, out ValidationFailure? failure)
    {
        var field = _declaration.Settings.FirstOrDefault(candidate => candidate.Role == SettingRole.Endpoint);
        var text = field is not null
            && invocation.Definition.Settings.TryGetValue(field.FieldId, out var configured)
            && configured.Length > 0
            ? configured
            : field?.DefaultValue;

        if (Uri.TryCreate(text, UriKind.Absolute, out var parsed)
            && parsed.Scheme is "http" or "https")
        {
            endpoint = parsed;
            failure = null;
            return true;
        }

        endpoint = null;
        failure = new ValidationFailure(
            field?.FieldId,
            "The catalog endpoint is not an absolute http or https address.");
        return false;
    }

    private bool HasRequest(string requestId) => FindRequest(requestId) is not null;

    private RequestTemplate? FindRequest(string requestId) =>
        _declaration.Requests.FirstOrDefault(candidate =>
            string.Equals(candidate.RequestId, requestId, StringComparison.OrdinalIgnoreCase));

    private RequestTemplate? FindRequestForScheme(string scheme) =>
        _declaration.Requests.FirstOrDefault(candidate =>
        {
            var placeholders = RoutePlaceholders(candidate.Route);

            return placeholders.Count == 1 && PlaceholderSpellsScheme(placeholders[0], scheme);
        });

    private static List<string> RoutePlaceholders(string route)
    {
        var placeholders = new List<string>();
        var position = 0;

        while (position < route.Length)
        {
            var open = route.IndexOf('{', position);

            if (open < 0)
            {
                break;
            }

            var close = route.IndexOf('}', open + 1);

            if (close < 0)
            {
                break;
            }

            placeholders.Add(route[(open + 1)..close]);
            position = close + 1;
        }

        return placeholders;
    }

    private static bool PlaceholderSpellsScheme(string placeholder, string scheme)
    {
        // "{tmdbId}" spells "tmdb"; "{collectionTmdbId}" spells "tmdb-collection": the placeholder,
        // lower-cased and with the trailing "id" dropped, must be a concatenation of exactly the
        // scheme's hyphen-separated parts, in any order.
        var canonical = placeholder.ToLowerInvariant();

        if (canonical.EndsWith("id", StringComparison.Ordinal))
        {
            canonical = canonical[..^2];
        }

        var parts = scheme.ToLowerInvariant()
            .Split('-', StringSplitOptions.RemoveEmptyEntries)
            .OrderByDescending(part => part.Length);

        foreach (var part in parts)
        {
            var index = canonical.IndexOf(part, StringComparison.Ordinal);

            if (index < 0)
            {
                return false;
            }

            canonical = canonical.Remove(index, part.Length);
        }

        return canonical.Length == 0;
    }
}
