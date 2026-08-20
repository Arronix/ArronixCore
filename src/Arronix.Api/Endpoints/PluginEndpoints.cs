using Arronix.Abstractions.Wire;
using Arronix.Plugins.Registry;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;


namespace Arronix.Api.Endpoints;

/// <summary>
/// What is installed, what state it reached, and why it did not reach a later one.
/// </summary>
/// <remarks>
/// <para>
/// An extension that fails at any stage of loading is quarantined rather than being fatal, which means the
/// host starts with a working API and a broken extension instead of not starting at all. That is only a
/// good trade if the failure is visible, so this route exists to make it visible: every extension the host
/// discovered appears here, with the state it reached, the platform error code, the message, and the full
/// list of defects that stopped it. Nothing is filtered out for being embarrassing.
/// </para>
/// <para>
/// Zero extensions is a valid answer. The host serves an empty library rather than refusing to run, and a
/// person looking at this route can tell the difference between "nothing installed" and "everything
/// installed is broken" without reading a log file.
/// </para>
/// </remarks>
internal static class PluginEndpoints
{
    /// <summary>
    /// Maps the extension inventory route.
    /// </summary>
    /// <param name="group">The versioned route group.</param>
    /// <returns>The same group, for chaining.</returns>
    internal static RouteGroupBuilder MapPluginEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapGet("/plugins", ListPlugins)
            .WithTags("Extensions")
            .WithName("ListPlugins")
            .WithSummary("Lists every extension the host discovered, with the state it reached.");

        return group;
    }

    // The registry projects its own records, and that is deliberate rather than incidental: an extension too
    // broken to yield an identifier is still recorded there, and a projection written here would have to key
    // the same records by an identifier those ones do not have. Asking the registry for the projection is
    // what keeps the failure that is hardest to diagnose from being the one that is missing.
    private static Ok<IReadOnlyList<PluginStatusView>> ListPlugins(IPluginRuntimeRegistry plugins)
    {
        ArgumentNullException.ThrowIfNull(plugins);
        return TypedResults.Ok(plugins.Snapshot());
    }
}
