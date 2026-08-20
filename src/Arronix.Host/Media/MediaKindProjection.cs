using System.Linq;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Media;
using Arronix.Abstractions.Plugins;


namespace Arronix.Host.Media;

/// <summary>
/// The media-kind contract, built by the host from a validated shape and a manifest.
/// </summary>
/// <remarks>
/// <para>
/// Extensions never implement <see cref="IMediaKind"/>. They declare a shape and a capability set; the host
/// projects both into this. That inverts what the contract originally implied, and the inversion is the
/// point: a stringly-typed capability list an extension writes for itself is a claim, while one the host
/// derives from what the extension actually registered is a fact.
/// </para>
/// <para>
/// The interface survives the inversion on its own merit — it is the shape every reader of a media kind
/// already wants, and the projection gives it a source of truth it did not have. It is not retained because
/// it could not be replaced; below 1.0.0 it could be.
/// </para>
/// </remarks>
public sealed class MediaKindProjection : IMediaKind
{
    /// <summary>
    /// Projects a validated shape and its grant.
    /// </summary>
    /// <param name="shape">The validated shape.</param>
    /// <param name="version">The contributing extension's version.</param>
    /// <param name="capabilities">The capabilities granted to it, before implication.</param>
    /// <exception cref="ArgumentNullException"><paramref name="shape"/> is <see langword="null"/>.</exception>
    public MediaKindProjection(ValidatedShape shape, string version, CapabilitySet capabilities)
    {
        ArgumentNullException.ThrowIfNull(shape);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);

        Id = shape.Kind;
        Name = shape.Declaration.Name;
        Version = version;

        Capabilities = [.. capabilities.WithImplied().Enumerate().Select(CapabilityNames.ToWireName)];

        // The union across levels, de-duplicated: a scheme declared on both a work and its parts is one
        // identifier system, not two.
        SupportedIdentifiers =
        [
            .. shape.Levels
                .SelectMany(level => level.Identity.ExternalIds)
                .Select(scheme => scheme.Scheme)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];

        NamingTokens = [.. shape.Declaration.Tokens.Select(token => token.Name)];
    }

    /// <inheritdoc />
    public MediaKindId Id { get; }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public string Version { get; }

    /// <inheritdoc />
    public IReadOnlyList<string> Capabilities { get; }

    /// <inheritdoc />
    public IReadOnlyList<string> SupportedIdentifiers { get; }

    /// <inheritdoc />
    public IReadOnlyList<string> NamingTokens { get; }
}
