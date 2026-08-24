using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Media;
using Arronix.Abstractions.Parsing;
using Arronix.Abstractions.Shape;
using Arronix.Plugins.Registration;


namespace Arronix.Plugins.Tests.Support;

/// <summary>
/// The smallest typed media kind the registration tests can hand the registry.
/// </summary>
/// <remarks>
/// Deliberately empty of detail. The registry never reads a kind's declaration — it prices the registration
/// through <see cref="IMediaTypeCapabilityReader"/> and records it — so what these tests need from a kind is
/// only that it exists and names itself. The sections that drive the capability demands are supplied by
/// <see cref="StubCapabilityReader"/> instead, which is what lets one pair of types cover every section
/// variant.
/// </remarks>
internal sealed class ExampleItem : IMediaItem
{
    public ExternalIdSet ExternalIds { get; init; } = ExternalIdSet.Empty;

    /// <summary>Gets the item's title.</summary>
    [Title]
    public required string Title { get; init; }

    public Language? TitleLanguage { get; init; }

    public string? Overview { get; init; }

    public ArtworkSet Artwork { get; init; } = ArtworkSet.Empty;

    public CatalogRecordState CatalogState { get; init; }
}

/// <summary>
/// The declaring half of <see cref="ExampleItem"/>.
/// </summary>
internal sealed class ExampleTarget : IReleaseTarget;

internal sealed record ExampleRelease(
    string Title = "Example",
    int? Year = null,
    string? Edition = null) : IRelease;

internal sealed class ExampleParser : IReleaseParser<ExampleRelease>
{
    public static ReleaseParseResult<ExampleRelease> Parse(ReleaseParseContext context) =>
        ReleaseParseResult<ExampleRelease>.Accepted(new ExampleRelease(context.Text));
}

internal sealed class ExampleRepresentation : IRepresentation;

internal static class ExampleFormat
{
    internal static FormatFamilyDefinition<ExampleRepresentation> Definition { get; } = new()
    {
        Id = "example",
        Name = "Example",
        FileExtensions = [".example"]
    };
}

internal sealed partial class ExampleKind() : MediaType<ExampleItem, ExampleTarget, ExampleRelease, ExampleParser>(
    MediaKindId.FromString("example"),
    "Example",
    "Examples",
    formats: [new FormatUse<ExampleRepresentation>(ExampleFormat.Definition)],
    availability: new ThresholdSelectionDefinition<ExampleItem>(
        "availability",
        "Minimum availability",
        "days",
        ThresholdDirection.AtLeast,
        0));

/// <summary>
/// A capability reader that prices every kind from one model the test chose.
/// </summary>
/// <remarks>
/// Stands in for the host's derivation. Substituting it here is what keeps these tests about the
/// <i>registry's</i> rule — demand before record, refuse before trace — rather than about whether the
/// derivation reads an attribute correctly, which is the host's own test.
/// </remarks>
internal sealed class StubCapabilityReader(MediaKindModel model) : IMediaTypeCapabilityReader
{
    /// <inheritdoc />
    public IReadOnlyList<DefinitionSectionRequirement> Requirements(IMediaTypeRegistration registration)
        => DefinitionCapabilityRules.Requirements(model);
}
