using System;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Arronix.Abstractions.Http;
using Arronix.Abstractions.Providers;
using Arronix.Provider.Tmdb.Settings;

namespace Arronix.Provider.Tmdb.Transport;

/// <summary>The real HTTP boundary to the TMDb v3 API, owned entirely by this provider.</summary>
/// <remarks>
/// Vendor JSON is deserialized directly, while every request still uses the capability-scoped
/// <see cref="IHttpGateway"/>. Statuses are inspected locally so a missing movie remains distinct from a
/// transport failure.
/// </remarks>
internal sealed class TmdbMovieClient(IHttpGateway gateway, TmdbSettingsValues settings)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Searches TMDb for movies matching free text.</summary>
    public Task<TmdbPagedResult<TmdbMovieSummaryDto>> SearchMoviesAsync(
        string query, int page, CancellationToken cancellationToken) =>
        GetRequiredAsync<TmdbPagedResult<TmdbMovieSummaryDto>>(
            "search/movie",
            [("query", query), ("page", Invariant(page))],
            cancellationToken);

    /// <summary>Fetches one movie's full details, including release dates and external ids.</summary>
    /// <returns>The details, or <see langword="null"/> when TMDb has no such movie.</returns>
    public Task<TmdbMovieDetailsDto?> GetMovieDetailsAsync(int id, CancellationToken cancellationToken) =>
        GetOptionalAsync<TmdbMovieDetailsDto>(
            $"movie/{Invariant(id)}",
            [("append_to_response", "release_dates,external_ids")],
            cancellationToken);

    /// <summary>Fetches one page of TMDb's popular-movies discovery list.</summary>
    public Task<TmdbPagedResult<TmdbMovieSummaryDto>> DiscoverPopularAsync(int page, CancellationToken cancellationToken) =>
        GetRequiredAsync<TmdbPagedResult<TmdbMovieSummaryDto>>(
            "movie/popular",
            [("page", Invariant(page))],
            cancellationToken);

    /// <summary>
    /// Fetches one page of the movies TMDb changed within one date window.
    /// </summary>
    /// <param name="start">The inclusive start of the window.</param>
    /// <param name="end">The inclusive end of the window. TMDb rejects a window spanning more than 14 days;
    /// windowing is the caller's responsibility (<see cref="TmdbChangeWindow"/>), not this method's.</param>
    /// <param name="page">The page to fetch.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<TmdbChangesResponseDto> GetChangedMoviesAsync(
        DateOnly start, DateOnly end, int page, CancellationToken cancellationToken) =>
        GetRequiredAsync<TmdbChangesResponseDto>(
            "movie/changes",
            [
                ("start_date", start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                ("end_date", end.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                ("page", Invariant(page)),
            ],
            cancellationToken);

    /// <summary>Proves the configured Read Access Token authenticates against TMDb.</summary>
    public async Task<ValidationOutcome> TestAuthenticationAsync(CancellationToken cancellationToken)
    {
        var response = await SendAsync("authentication", [], cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.OK)
        {
            return ValidationOutcome.Success;
        }

        return ValidationOutcome.Failed(new ValidationFailure(
            TmdbProviderSettings.ReadAccessTokenField,
            ReadErrorMessage(response, "authentication")));
    }

    private async Task<T> GetRequiredAsync<T>(
        string path, (string Name, string Value)[] query, CancellationToken cancellationToken)
        where T : class
    {
        var response = await SendAsync(path, query, cancellationToken).ConfigureAwait(false);
        EnsureSuccess(response, path);
        return Deserialize<T>(response, path);
    }

    private async Task<T?> GetOptionalAsync<T>(
        string path, (string Name, string Value)[] query, CancellationToken cancellationToken)
        where T : class
    {
        var response = await SendAsync(path, query, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        EnsureSuccess(response, path);
        return Deserialize<T>(response, path);
    }

    private async Task<OutboundHttpResponse> SendAsync(
        string path, (string Name, string Value)[] query, CancellationToken cancellationToken)
    {
        var request = new OutboundHttpRequest(BuildUri(path, query)) { SuppressHttpError = true };

        // Keep the token out of URLs and the diagnostics which record them.
        request.Headers.Set("Authorization", $"Bearer {settings.ReadAccessToken}");

        return await gateway.ExecuteAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private Uri BuildUri(string path, (string Name, string Value)[] query)
    {
        var builder = new StringBuilder(settings.BaseUrl.ToString().TrimEnd('/'))
            .Append('/')
            .Append(path);

        for (var index = 0; index < query.Length; index++)
        {
            var (name, value) = query[index];
            builder.Append(index == 0 ? '?' : '&').Append(name).Append('=').Append(Uri.EscapeDataString(value));
        }

        return new Uri(builder.ToString(), UriKind.Absolute);
    }

    private static void EnsureSuccess(OutboundHttpResponse response, string operation)
    {
        if (response.HasHttpError)
        {
            throw new TmdbApiException(operation, response.StatusCode, ReadErrorMessage(response, operation));
        }
    }

    private static string ReadErrorMessage(OutboundHttpResponse response, string operation)
    {
        if (response.ContentBytes.IsEmpty)
        {
            return $"TMDb '{operation}' returned no body.";
        }

        try
        {
            var error = JsonSerializer.Deserialize<TmdbErrorDto>(response.ContentBytes.Span, SerializerOptions);
            return error?.StatusMessage ?? $"TMDb '{operation}' returned {(int)response.StatusCode}.";
        }
        catch (JsonException)
        {
            return $"TMDb '{operation}' returned {(int)response.StatusCode} with an unreadable body.";
        }
    }

    private static T Deserialize<T>(OutboundHttpResponse response, string operation)
        where T : class
    {
        if (response.ContentBytes.IsEmpty)
        {
            throw new TmdbResponseFormatException(operation, "was empty.");
        }

        try
        {
            return JsonSerializer.Deserialize<T>(response.ContentBytes.Span, SerializerOptions)
                ?? throw new TmdbResponseFormatException(operation, "was JSON null.");
        }
        catch (JsonException exception)
        {
            throw new TmdbResponseFormatException(operation, "was not well-formed JSON.", exception);
        }
    }

    private static string Invariant(int value) => value.ToString(CultureInfo.InvariantCulture);
}
