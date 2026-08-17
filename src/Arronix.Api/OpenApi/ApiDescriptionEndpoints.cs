using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using Arronix.Api.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;

namespace Arronix.Api.OpenApi;

/// <summary>
/// Publishes a machine-readable description of this API, built from the routing table itself.
/// </summary>
/// <remarks>
/// <para>
/// The description is generated at request time from the endpoints the application actually mapped, not
/// written by hand and not produced by a generator package. That is a deliberate consequence of this
/// project taking no package references: the usual generators can produce a document but none of the
/// packages that <em>serve</em> one is available here, so the choice was between no description at all and
/// a small one derived from the real routing table. The second is better, and it has a property the usual
/// arrangement does not — it cannot describe a route that does not exist, or miss one that does.
/// </para>
/// <para>
/// What it does not attempt is as important as what it does. There is no polymorphism, no discriminator,
/// no security scheme and no example generation, because none of those is present in this API; if any
/// arrives, the honest move is to take a real generator package rather than grow this into one.
/// </para>
/// </remarks>
internal static class ApiDescriptionEndpoints
{
    /// <summary>The route the description is published at.</summary>
    internal const string Path = "/openapi/" + ApiEndpoints.V1 + ".json";

    /// <summary>
    /// Maps the description route.
    /// </summary>
    /// <param name="app">The application being configured.</param>
    /// <returns>The same application, for chaining.</returns>
    internal static WebApplication MapApiDescription(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet(Path, (IEnumerable<EndpointDataSource> sources) =>
                Results.Text(Build(sources).ToJsonString(), "application/json", System.Text.Encoding.UTF8))
            .WithTags("Platform")
            .WithName("GetApiDescription")
            .WithSummary("Returns a machine-readable description of this API.");

        return app;
    }

    private static JsonObject Build(IEnumerable<EndpointDataSource> sources)
    {
        var schemas = new JsonSchemaWriter();
        var paths = new JsonObject();

        var endpoints = sources
            .SelectMany(static source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(static endpoint => endpoint.Metadata.GetMetadata<IExcludeFromDescriptionMetadata>()?.ExcludeFromDescription != true)
            .OrderBy(static endpoint => endpoint.RoutePattern.RawText, StringComparer.Ordinal);

        foreach (var endpoint in endpoints)
        {
            var template = Normalize(endpoint.RoutePattern.RawText);
            if (template is null)
            {
                continue;
            }

            var methods = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? [];
            if (methods.Count == 0)
            {
                continue;
            }

            if (paths[template] is not JsonObject item)
            {
                item = [];
                paths[template] = item;
            }

            var operation = Describe(endpoint, schemas);

            foreach (var method in methods.Where(static method =>
                         !string.Equals(method, HttpMethods.Head, StringComparison.OrdinalIgnoreCase)
                         && !string.Equals(method, HttpMethods.Options, StringComparison.OrdinalIgnoreCase)))
            {
                item[method.ToLowerInvariant()] = operation.DeepClone();
            }
        }

        var components = new JsonObject();
        foreach (var schema in schemas.Components.OrderBy(static entry => entry.Key, StringComparer.Ordinal))
        {
            components[schema.Key] = schema.Value.DeepClone();
        }

        return new JsonObject
        {
            ["openapi"] = "3.0.3",
            ["info"] = new JsonObject
            {
                ["title"] = "Arronix",
                ["version"] = ApiEndpoints.V1,
                ["description"] =
                    "The media-agnostic surface of an Arronix host. Every media kind, level, field, action "
                    + "and workbench in this API is declared at run time by a loaded extension, so this "
                    + "description tells you the shape of the envelope and not the shape of any one library.",
            },
            ["paths"] = paths,
            ["components"] = new JsonObject { ["schemas"] = components },
        };
    }

    private static JsonObject Describe(RouteEndpoint endpoint, JsonSchemaWriter schemas)
    {
        var operation = new JsonObject();

        if (endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName is { } name)
        {
            operation["operationId"] = name;
        }

        if (endpoint.Metadata.GetMetadata<IEndpointSummaryMetadata>()?.Summary is { } summary)
        {
            operation["summary"] = summary;
        }

        if (endpoint.Metadata.GetMetadata<IEndpointDescriptionMetadata>()?.Description is { } description)
        {
            operation["description"] = description;
        }

        if (endpoint.Metadata.GetMetadata<ITagsMetadata>()?.Tags is { Count: > 0 } tags)
        {
            operation["tags"] = new JsonArray([.. tags.Select(static tag => JsonValue.Create(tag))]);
        }

        var parameters = new JsonArray();
        foreach (var part in endpoint.RoutePattern.Parameters)
        {
            parameters.Add(new JsonObject
            {
                ["name"] = part.Name,
                ["in"] = "path",
                ["required"] = true,
                ["schema"] = new JsonObject
                {
                    ["type"] = part.ParameterPolicies.Any(static policy =>
                        string.Equals(policy.Content, "int", StringComparison.Ordinal))
                        ? "integer"
                        : "string",
                },
            });
        }

        if (parameters.Count > 0)
        {
            operation["parameters"] = parameters;
        }

        var responses = new JsonObject();
        foreach (var response in endpoint.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>())
        {
            var status = response.StatusCode.ToString(CultureInfo.InvariantCulture);
            var body = new JsonObject { ["description"] = DescribeStatus(response.StatusCode) };

            if (response.Type is { } type && type != typeof(void))
            {
                body["content"] = new JsonObject
                {
                    ["application/json"] = new JsonObject { ["schema"] = schemas.Describe(type) },
                };
            }

            responses[status] = body;
        }

        if (responses.Count == 0)
        {
            responses["200"] = new JsonObject { ["description"] = "Success." };
        }

        operation["responses"] = responses;
        return operation;
    }

    private static string DescribeStatus(int status) => status switch
    {
        StatusCodes.Status200OK => "Success.",
        StatusCodes.Status201Created => "Created.",
        StatusCodes.Status202Accepted => "Accepted; the work outlives this request and reports on the event stream.",
        StatusCodes.Status204NoContent => "Done; there is nothing to return.",
        StatusCodes.Status304NotModified => "The caller already holds the current version.",
        StatusCodes.Status400BadRequest => "The request was not well formed.",
        StatusCodes.Status404NotFound => "No such thing.",
        StatusCodes.Status409Conflict => "Refused in the current state.",
        StatusCodes.Status503ServiceUnavailable => "The platform is not working.",
        _ => "See the problem document.",
    };

    /// <summary>
    /// Strips the route constraints from a template, which the description format does not carry.
    /// </summary>
    private static string? Normalize(string? template)
    {
        if (string.IsNullOrEmpty(template))
        {
            return null;
        }

        var normalized = string.Concat(
            "/",
            string.Join('/', template.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(static segment =>
            {
                if (!segment.StartsWith('{') || !segment.EndsWith('}'))
                {
                    return segment;
                }

                var inner = segment[1..^1];
                var constraint = inner.IndexOf(':', StringComparison.Ordinal);
                return "{" + (constraint < 0 ? inner : inner[..constraint]) + "}";
            })));

        return normalized;
    }
}
