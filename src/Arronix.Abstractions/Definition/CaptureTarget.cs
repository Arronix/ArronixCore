using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Definition;

/// <summary>
/// What a title-pattern capture becomes.
/// </summary>
[Experimental(ExperimentalContracts.Definition, UrlFormat = ExperimentalContracts.UrlFormat)]
public enum CaptureTarget
{
    /// <summary>A component of a declared coordinate space.</summary>
    CoordinateComponent = 0,

    /// <summary>The title text of the reading.</summary>
    TitleText = 1,

    /// <summary>An additional spelling of the same title.</summary>
    AlternateTitle = 2,

    /// <summary>The year stated alongside the title.</summary>
    TitleYear = 3,

    /// <summary>An external identifier embedded in the text, under the scheme the binding's key names.</summary>
    ExternalId = 4,

    /// <summary>The release-kind discriminator unit-resolution rules dispatch on.</summary>
    ReleaseKind = 5,

    /// <summary>A tag value, under the key the binding names.</summary>
    Tag = 6
}
