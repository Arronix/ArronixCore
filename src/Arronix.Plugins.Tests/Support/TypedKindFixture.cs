using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Media;
using Arronix.Plugins.Registration;

#pragma warning disable ARX0020 // The typed media surface is experimental; these fixtures declare one.

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
    /// <summary>Gets the item's title.</summary>
    [Title]
    public required string Title { get; init; }
}

/// <summary>
/// The declaring half of <see cref="ExampleItem"/>.
/// </summary>
internal sealed class ExampleKind : IMediaType<ExampleItem>
{
    /// <inheritdoc />
    public static MediaKindId Kind => MediaKindId.FromString("example");

    /// <inheritdoc />
    public static void Configure(IMediaTypeBuilder<ExampleItem> builder)
    {
        // Nothing. See the remarks on ExampleItem: this call is never replayed by these tests.
    }
}

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
