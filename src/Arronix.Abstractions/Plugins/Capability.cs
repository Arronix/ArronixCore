
namespace Arronix.Abstractions.Plugins;

/// <summary>
/// One privilege an extension may be granted.
/// </summary>
/// <remarks>
/// <para>
/// Closed, and a typed value rather than free text, because the host both grants and enforces on it: the
/// gate that refuses an undeclared registration and the gate that refuses an undeclared dependency read
/// the same vocabulary, and a misspelled privilege must be a load failure rather than a silently
/// unenforceable one.
/// </para>
/// <para>
/// The list is deliberately short. Three further privileges were considered and left out: one that would
/// have gated nothing the file-handling privileges do not already gate, and two that would have made the
/// check "every declared privilege has a matching registration" vacuous. Two more with real evidence but
/// no implementer are deferred, because adding a member later is additive and removing one is not.
/// </para>
/// </remarks>
public enum Capability
{
    /// <summary>Contributing release sources and planning release queries.</summary>
    Indexing = 0,

    /// <summary>Contributing metadata catalogs and resolving external identifiers.</summary>
    Metadata = 1,

    /// <summary>Contributing release-name parsing.</summary>
    Parsing = 2,

    /// <summary>Deciding which items a release or a file refers to.</summary>
    Matching = 3,

    /// <summary>Contributing quality evaluation and upgrade decisions.</summary>
    Quality = 4,

    /// <summary>Contributing naming templates and folder layout.</summary>
    Renaming = 5,

    /// <summary>Taking files into the library.</summary>
    Import = 6,

    /// <summary>Contributing transfer clients.</summary>
    Download = 7,

    /// <summary>Contributing notification destinations.</summary>
    Notification = 8,

    /// <summary>
    /// Contributing a media shape, a catalog source and the media seams. Without it there would be no
    /// way to check that an extension calling itself a media extension registered anything at all.
    /// </summary>
    MediaKind = 9,

    /// <summary>Contributing curated lists that select what belongs in the library.</summary>
    Curation = 10,

    /// <summary>Making outbound network calls.</summary>
    Network = 11,

    /// <summary>Reading and writing files on the platform's storage mounts.</summary>
    Storage = 12,

    /// <summary>Receiving the platform's telemetry stream.</summary>
    TelemetrySink = 13,

    /// <summary>Contributing language-owned title comparison, query and sorting rules.</summary>
    Language = 14
}
