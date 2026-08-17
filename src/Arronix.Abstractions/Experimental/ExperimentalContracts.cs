namespace Arronix.Abstractions;

/// <summary>
/// Diagnostic identifiers used by <see cref="System.Diagnostics.CodeAnalysis.ExperimentalAttribute"/>
/// on the contract areas introduced in <c>Arronix.Abstractions</c> 0.2.0.
/// </summary>
/// <remarks>
/// <para>
/// Every experimental contract is tagged with the identifier of the area it belongs to, so a consumer
/// opts in to exactly one area at a time. Opting in is done with a file-scoped
/// <c>#pragma warning disable</c> naming the identifier, never with a project-wide <c>NoWarn</c>:
/// that keeps the set of files that depend on unstable contracts greppable and reviewable.
/// </para>
/// <para>
/// Identifiers are permanent. When an area is promoted to stable its attribute is removed, but the
/// identifier is never reused for a different area.
/// </para>
/// </remarks>
internal static class ExperimentalContracts
{
    /// <summary>
    /// Format string used to build the documentation link embedded in the diagnostic message.
    /// The single format argument is the diagnostic identifier.
    /// </summary>
    internal const string UrlFormat =
        "https://github.com/Arronix/ArronixCore/blob/main/docs/contracts/stability.md#{0}";

    /// <summary>Caching contracts (<c>Arronix.Abstractions.Caching</c>).</summary>
    internal const string Caching = "ARX0001";

    /// <summary>Diagnostics contracts (<c>Arronix.Abstractions.Diagnostics</c>).</summary>
    internal const string Diagnostics = "ARX0002";

    /// <summary>Error contracts (<c>Arronix.Abstractions.Errors</c>).</summary>
    internal const string Errors = "ARX0003";

    /// <summary>Event contracts (<c>Arronix.Abstractions.Events</c>).</summary>
    internal const string Events = "ARX0004";

    /// <summary>File system contracts (<c>Arronix.Abstractions.FileSystem</c>).</summary>
    internal const string FileSystem = "ARX0005";

    /// <summary>Health contribution contracts (<c>Arronix.Abstractions.Health</c>).</summary>
    internal const string Health = "ARX0006";

    /// <summary>Hosting contracts (<c>Arronix.Abstractions.Hosting</c>).</summary>
    internal const string Hosting = "ARX0007";

    /// <summary>Outbound HTTP contracts (<c>Arronix.Abstractions.Http</c>).</summary>
    internal const string Http = "ARX0008";

    /// <summary>Naming contracts (<c>Arronix.Abstractions.Naming</c>).</summary>
    internal const string Naming = "ARX0009";

    /// <summary>Serialization contracts (<c>Arronix.Abstractions.Serialization</c>).</summary>
    internal const string Serialization = "ARX0010";

    /// <summary>Telemetry contracts (<c>Arronix.Abstractions.Telemetry</c>).</summary>
    internal const string Telemetry = "ARX0011";

    /// <summary>Throttling contracts (<c>Arronix.Abstractions.Throttling</c>).</summary>
    internal const string Throttling = "ARX0012";

    /// <summary>Media-shape contracts (<c>Arronix.Abstractions.Shape</c>).</summary>
    internal const string Shape = "ARX0013";

    /// <summary>Plugin-model contracts (<c>Arronix.Abstractions.Plugins</c>).</summary>
    internal const string Plugins = "ARX0014";

    /// <summary>
    /// Provider-model contracts (<c>Arronix.Abstractions.Providers</c>), added in 0.3.0 alongside the
    /// stable 0.1.0 provider surface, which is untouched and remains stable.
    /// </summary>
    internal const string Providers = "ARX0015";

    /// <summary>Presentation-intent contracts (<c>Arronix.Abstractions.Intent</c>).</summary>
    internal const string Intent = "ARX0016";

    /// <summary>Wire contracts (<c>Arronix.Abstractions.Wire</c>).</summary>
    internal const string Wire = "ARX0017";

    /// <summary>
    /// Declarative media-kind contracts (<c>Arronix.Abstractions.Definition</c>): the aggregate a
    /// definition-mode plugin registers instead of imperative seams, and the vocabularies its sections
    /// are written in. <c>ARX0018</c> is deliberately skipped: two sibling designs contend for it and
    /// neither has landed, so this area takes the next uncontended identifier.
    /// </summary>
    internal const string Definition = "ARX0019";

    /// <summary>
    /// Typed media-model contracts (<c>Arronix.Abstractions.Media</c>): the entity marker, the group
    /// marker, the authoring seam and its builder, the property attribute vocabulary, and the runtime
    /// model the host derives from all of them. <c>ARX0018</c> stays unassigned for the reason recorded
    /// above, so this area takes the next free identifier.
    /// </summary>
    internal const string Media = "ARX0020";
}
