using System.Collections.Frozen;
using System.Linq;
using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Intent;
using Arronix.Abstractions.Providers;
using Arronix.Abstractions.Shape;
using Arronix.Abstractions.Wire;

namespace Arronix.Host.Media;

/// <summary>
/// Re-materializes what a plugin returned into host-owned values, while its ticket is still held.
/// </summary>
/// <remarks>
/// <para>
/// A contract says <c>IReadOnlyList&lt;T&gt;</c>; what arrives can be any type the extension chose,
/// including a lazy sequence that calls back into the extension when the host enumerates it, and including
/// a type defined in the extension's own collectible context. Either one, enumerated or held after the
/// ticket is released, is the exact thing the ticket exists to prevent.
/// </para>
/// <para>
/// So every collection reachable from a returned value is copied, recursively, inside the leased scope.
/// What is left afterwards is records from the contract assembly holding framework collections.
/// </para>
/// </remarks>
internal static class PluginBoundary
{
    /// <summary>Copies a returned sequence into a host-owned array.</summary>
    /// <typeparam name="TValue">The element type.</typeparam>
    /// <param name="values">What the extension returned.</param>
    /// <returns>A host-owned read-only list.</returns>
    internal static IReadOnlyList<TValue> Snapshot<TValue>(IReadOnlyList<TValue>? values)
        => values is null or { Count: 0 } ? [] : [.. values];

    /// <summary>Copies a returned map into a host-owned frozen dictionary.</summary>
    /// <typeparam name="TValue">The value type.</typeparam>
    /// <param name="values">What the extension returned.</param>
    /// <returns>A host-owned read-only dictionary.</returns>
    internal static IReadOnlyDictionary<string, TValue> Snapshot<TValue>(
        IReadOnlyDictionary<string, TValue>? values)
        => values is null or { Count: 0 }
            ? FrozenDictionary<string, TValue>.Empty
            : values.ToFrozenDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    /// <summary>Copies a returned field value, including a composite or multivalued payload.</summary>
    /// <param name="value">What the extension returned.</param>
    /// <returns>The value, with host-owned collections.</returns>
    internal static FieldValue Snapshot(FieldValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value.Items is null ? value : value with { Items = [.. value.Items.Select(Snapshot)] };
    }

    /// <summary>Copies a returned item, including its fields, coordinates and identifiers.</summary>
    /// <param name="item">What the extension returned.</param>
    /// <returns>The item, with host-owned collections.</returns>
    internal static ItemView Snapshot(ItemView item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return item with
        {
            Fields = item.Fields is null or { Count: 0 }
                ? FrozenDictionary<string, FieldValue>.Empty
                : item.Fields.ToFrozenDictionary(
                    pair => pair.Key,
                    pair => Snapshot(pair.Value),
                    StringComparer.Ordinal),
            Coordinates = item.Coordinates is null
                ? CoordinateSet.Empty
                : item.Coordinates with { Readings = Snapshot(item.Coordinates.Readings) },
            ExternalIds = Snapshot(item.ExternalIds),
        };
    }

    /// <summary>Copies a returned page of items.</summary>
    /// <param name="page">What the extension returned.</param>
    /// <returns>The page, with host-owned collections.</returns>
    internal static ItemPage? Snapshot(ItemPage? page)
        => page is null ? null : page with { Items = [.. page.Items.Select(Snapshot)] };

    /// <summary>Copies a returned proposal, including every row and every row value.</summary>
    /// <param name="proposal">What the extension returned.</param>
    /// <returns>The proposal, with host-owned collections.</returns>
    internal static WorkbenchProposal? Snapshot(WorkbenchProposal? proposal)
        => proposal is null
            ? null
            : proposal with
            {
                Rows =
                [
                    .. proposal.Rows.Select(row => row with
                    {
                        Values = row.Values is null or { Count: 0 }
                            ? FrozenDictionary<string, FieldValue>.Empty
                            : row.Values.ToFrozenDictionary(
                                pair => pair.Key,
                                pair => Snapshot(pair.Value),
                                StringComparer.Ordinal),
                        Issues = Snapshot(row.Issues),
                    }),
                ],
                Issues = Snapshot(proposal.Issues),
            };

    /// <summary>Copies a returned validation outcome, including its failures.</summary>
    /// <param name="outcome">What the extension returned.</param>
    /// <returns>The outcome, with host-owned collections.</returns>
    internal static ValidationOutcome Snapshot(ValidationOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        return outcome with { Failures = Snapshot(outcome.Failures) };
    }

    /// <summary>Copies a returned action result, including its validation failures.</summary>
    /// <param name="result">What the extension returned.</param>
    /// <returns>The result, with host-owned collections.</returns>
    internal static ActionResult? Snapshot(ActionResult? result)
        => result is null
            ? null
            : result with
            {
                Validation = result.Validation is null ? null : Snapshot(result.Validation),
            };

    /// <summary>
    /// Copies a declaration the host publishes and keeps, including every collection and address in it.
    /// </summary>
    /// <param name="descriptor">What the extension declared.</param>
    /// <returns>The declaration, built entirely from host-owned values.</returns>
    /// <remarks>
    /// A provider declaration is the longest-lived thing an extension hands over: the host retains it for
    /// as long as the provider is published and serializes it to every consumer that lists providers. A
    /// plugin-defined list, map or <see cref="Uri"/> left in it would therefore run extension code long
    /// after any lease, and would keep the extension's context alive for the life of the process.
    /// </remarks>
    internal static ProviderDescriptor Snapshot(ProviderDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        return descriptor with
        {
            Settings =
            [
                .. descriptor.Settings.Select(field => field with
                {
                    Choices = Snapshot(field.Choices),
                    HelpLink = Snapshot(field.HelpLink),
                }),
            ],
            Protocols = Snapshot(descriptor.Protocols),
            Presets =
            [
                .. descriptor.Presets.Select(preset => preset with { Settings = Snapshot(preset.Settings) }),
            ],
            InfoLink = Snapshot(descriptor.InfoLink),
        };
    }

    /// <summary>
    /// Rebuilds an address as an ordinary <see cref="Uri"/>.
    /// </summary>
    /// <param name="address">What the extension supplied.</param>
    /// <returns>A host-owned address, or <see langword="null"/>.</returns>
    /// <remarks>
    /// <see cref="Uri"/> is not sealed, so a declaration can carry a subclass whose overrides are extension
    /// code and whose type is defined in the extension's context.
    /// </remarks>
    internal static Uri? Snapshot(Uri? address)
        => address is null || address.GetType() == typeof(Uri)
            ? address
            : new Uri(address.OriginalString, address.IsAbsoluteUri ? UriKind.Absolute : UriKind.Relative);

    /// <summary>Copies a returned listing, including the free-form map it may carry.</summary>
    /// <param name="listing">What the extension returned.</param>
    /// <returns>The listing, with host-owned collections.</returns>
    internal static ReleaseListing Snapshot(ReleaseListing listing)
    {
        ArgumentNullException.ThrowIfNull(listing);

        return listing.AdditionalData is null
            ? listing
            : listing with { AdditionalData = Snapshot(listing.AdditionalData) };
    }

    /// <summary>Copies a returned query result, including every listing in it.</summary>
    /// <param name="result">What the extension returned.</param>
    /// <returns>The result, with host-owned collections.</returns>
    internal static ReleaseQueryResult Snapshot(ReleaseQueryResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result with
        {
            Releases = [.. result.Releases.Select(Snapshot)],
            Warnings = Snapshot(result.Warnings),
        };
    }

    /// <summary>Copies a returned indexer profile, including its search profiles and categories.</summary>
    /// <param name="profile">What the extension returned.</param>
    /// <returns>The profile, with host-owned collections.</returns>
    internal static IndexerProfile? Snapshot(IndexerProfile? profile)
        => profile is null
            ? null
            : profile with
            {
                SearchProfiles =
                [
                    .. profile.SearchProfiles.Select(search => search with
                    {
                        Terms = Snapshot(search.Terms),
                        Categories = Snapshot(search.Categories),
                        Bindings = Snapshot(search.Bindings),
                    }),
                ],
                Categories = Snapshot(profile.Categories),
                Flags = Snapshot(profile.Flags),
            };

    /// <summary>
    /// Copies a fetched artifact's bytes into memory the host owns.
    /// </summary>
    /// <param name="fetch">What the extension returned.</param>
    /// <returns>The artifact, over host-owned memory.</returns>
    /// <remarks>
    /// <see cref="ReadOnlyMemory{T}"/> is a window onto storage the extension still owns, and an extension
    /// is free to reuse or release that storage once the call returns. The bytes are copied so what the
    /// host holds afterwards is its own.
    /// </remarks>
    internal static ReleaseFetch Snapshot(ReleaseFetch fetch)
    {
        ArgumentNullException.ThrowIfNull(fetch);
        return fetch with { Content = fetch.Content.ToArray() };
    }
}
