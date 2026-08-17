using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Wire;

/// <summary>
/// What the platform will say about one installed extension.
/// </summary>
/// <param name="Id">The extension's identifier.</param>
/// <param name="Name">Its display name, when its manifest could be read.</param>
/// <param name="Version">Its version, when its manifest could be read.</param>
/// <param name="State">Where it got to in the load pipeline.</param>
/// <param name="Capabilities">The capabilities granted to it, as wire names.</param>
/// <param name="ErrorCode">The machine-readable code it failed with, when it failed.</param>
/// <param name="Message">Why it failed, in a sentence an operator can act on.</param>
/// <param name="Defects">Every individual fault found, not merely the first.</param>
/// <param name="ChangedAt">When it last changed state.</param>
/// <remarks>
/// <para>
/// A view rather than the manifest itself. Nothing outside the loader constructs a manifest, and an
/// operator needs a different set of facts than the loader does — above all, why an extension is not
/// running.
/// </para>
/// <para>
/// A failed extension is quarantined and reported here, never fatal to the platform and never deleted.
/// One surveyed application deletes stored configuration whose implementation has gone missing; under an
/// extension model that means uninstalling an extension destroys the user's configuration.
/// </para>
/// </remarks>
[Experimental(ExperimentalContracts.Wire, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed record PluginStatusView(
    string Id,
    string? Name,
    string? Version,
    string State,
    IReadOnlyList<string> Capabilities,
    int? ErrorCode,
    string? Message,
    IReadOnlyList<string> Defects,
    DateTimeOffset ChangedAt);
