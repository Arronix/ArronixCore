using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using Arronix.Abstractions.Shape;

namespace Arronix.Abstractions.Providers;

/// <summary>
/// A resolved answer to every one of a media kind's selection facets, passed into a catalog fetch.
/// </summary>
/// <remarks>
/// <para>
/// The keys are <see cref="SelectionFacet.FacetId"/> values from the kind's own shape, so the policy is
/// meaningful without the platform knowing what any facet means. It is passed into the fetch rather than
/// applied afterwards because the surveyed catalogs can answer more cheaply when they know what will be
/// discarded.
/// </para>
/// <para>
/// Two host rules attach to it and are not optional. Items the user added by hand, or that have files on
/// disk, are never removed by a policy — that is the valve that stops a profile change deleting a
/// library. And a reserved empty policy exists per kind, cannot be edited or deleted, and a policy in use
/// cannot be removed.
/// </para>
/// </remarks>
[Experimental(ExperimentalContracts.Providers, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed record SelectionPolicy
{
    /// <summary>
    /// Gets the reserved policy that excludes nothing.
    /// </summary>
    public static SelectionPolicy None { get; } = new() { PolicyId = "none", Name = "None" };

    /// <summary>
    /// Gets the policy's identifier.
    /// </summary>
    public required string PolicyId { get; init; }

    /// <summary>
    /// Gets the policy's display name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the allowed values of each enumerated facet, keyed by facet identifier.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> AllowedValues { get; init; }
        = ReadOnlyDictionary<string, IReadOnlyList<string>>.Empty;

    /// <summary>
    /// Gets the bound of each threshold facet, keyed by facet identifier.
    /// </summary>
    public IReadOnlyDictionary<string, double> Thresholds { get; init; }
        = ReadOnlyDictionary<string, double>.Empty;

    /// <summary>
    /// Gets the state of each flag facet, keyed by facet identifier.
    /// </summary>
    public IReadOnlyDictionary<string, bool> Flags { get; init; }
        = ReadOnlyDictionary<string, bool>.Empty;
}
