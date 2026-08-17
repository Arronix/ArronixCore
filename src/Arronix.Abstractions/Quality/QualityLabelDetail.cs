using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Quality;

/// <summary>How much of a point to spell.</summary>
[Experimental(ExperimentalContracts.Quality, UrlFormat = ExperimentalContracts.UrlFormat)]
public enum QualityLabelDetail
{
    /// <summary>The source word alone.</summary>
    Source = 0,

    /// <summary>Source and resolution. What a short quality token renders.</summary>
    Standard = 1,

    /// <summary>Standard plus the revision. What a full quality token renders.</summary>
    Full = 2,

    /// <summary>Every known axis, for a diagnostic view.</summary>
    Diagnostic = 3,
}
