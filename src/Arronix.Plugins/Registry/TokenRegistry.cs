using System.Collections.Frozen;
using System.Linq;
using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Health;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Plugins;
using Arronix.Plugins.Manifest;


namespace Arronix.Plugins.Registry;

/// <summary>
/// One extension's claim on one token, for one media kind.
/// </summary>
/// <param name="Plugin">The extension that claimed it.</param>
/// <param name="MediaKind">The media kind the claim is scoped to.</param>
/// <param name="Token">The token as declared.</param>
public sealed record TokenClaim(PluginId Plugin, MediaKindId MediaKind, NamingToken Token);

/// <summary>
/// Who owns which naming token.
/// </summary>
/// <remarks>
/// <para>
/// The governing architecture left the collision strategy open. It is decided here, in three rules, because
/// a loader cannot ship without one.
/// </para>
/// <para>
/// A token colliding with one the platform reserves rejects the extension. The platform's meaning for
/// quality, release group or file extension is not negotiable, and an extension that quietly redefined one
/// would change the meaning of naming templates an operator had already written.
/// </para>
/// <para>
/// The same token declared by two extensions for <i>different</i> media kinds is allowed, because the
/// stated goal is a stable meaning per token within a media context, not across the whole platform. A title
/// is a title.
/// </para>
/// <para>
/// The same token for the same media kind cannot arise: two extensions claiming one media kind is already a
/// conflict caught earlier. The registry still refuses it rather than trusting that, because a defense that
/// depends on an earlier check having run is not a defense.
/// </para>
/// </remarks>
public sealed class TokenRegistry
{
    private static readonly FrozenSet<string> Reserved = new[]
    {
        "{Quality}",
        "{Quality Full}",
        "{Release Group}",
        "{Original Title}",
        "{Original Filename}",
        "{Custom Formats}",
        "{Preferred Words}",
        "{Ext}"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The prefix of the reserved family of media-information tokens.
    /// </summary>
    private const string MediaInfoPrefix = "{MediaInfo";

    private readonly Dictionary<(string Kind, string Token), TokenClaim> _claims = [];
    private readonly Lock _gate = new();

    /// <summary>
    /// Gets the tokens the platform reserves for itself.
    /// </summary>
    public static IReadOnlySet<string> ReservedTokens => Reserved;

    /// <summary>
    /// Determines whether a token is one the platform reserves.
    /// </summary>
    /// <param name="tokenName">The token, brace-delimited.</param>
    /// <returns><see langword="true"/> when the platform owns it.</returns>
    public static bool IsReserved(string? tokenName)
        => tokenName is not null
            && (Reserved.Contains(tokenName)
                || tokenName.StartsWith(MediaInfoPrefix, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Gets every claim currently held.
    /// </summary>
    public IReadOnlyList<TokenClaim> Claims
    {
        get
        {
            lock (_gate)
            {
                return [.. _claims.Values];
            }
        }
    }

    /// <summary>
    /// Claims every token an extension declares, for every media kind it claims.
    /// </summary>
    /// <param name="plugin">The extension claiming.</param>
    /// <param name="mediaKinds">The media kinds it claims.</param>
    /// <param name="tokens">The tokens it declares.</param>
    /// <param name="defects">Everything that collided, or an empty list on success.</param>
    /// <returns><see langword="true"/> when every token was claimed.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// All or nothing. A partial claim would leave an extension owning some of its tokens while being
    /// quarantined for the rest, which is the half-registered state the pipeline exists to make impossible.
    /// </remarks>
    public bool TryClaimAll(
        PluginId plugin,
        IReadOnlyList<MediaKindId> mediaKinds,
        IReadOnlyList<NamingToken> tokens,
        out IReadOnlyList<ManifestDefect> defects)
    {
        ArgumentNullException.ThrowIfNull(mediaKinds);
        ArgumentNullException.ThrowIfNull(tokens);

        var found = new List<ManifestDefect>();

        lock (_gate)
        {
            var staged = new List<((string Kind, string Token) Key, TokenClaim Claim)>();

            foreach (var token in tokens)
            {
                if (IsReserved(token.Name))
                {
                    found.Add(new ManifestDefect(
                        $"tokens['{token.Name}']",
                        $"'{token.Name}' is reserved by the platform and cannot be redefined by an extension.",
                        CoreErrorCode.PluginTokenConflict));
                    continue;
                }

                foreach (var kind in mediaKinds)
                {
                    var key = (kind.Value, token.Name);

                    if (_claims.TryGetValue(key, out var existing))
                    {
                        found.Add(new ManifestDefect(
                            $"tokens['{token.Name}']",
                            $"'{token.Name}' is already declared for media kind '{kind.Value}' by extension '{existing.Plugin}'.",
                            CoreErrorCode.PluginTokenConflict));
                        continue;
                    }

                    staged.Add((key, new TokenClaim(plugin, kind, token)));
                }
            }

            if (found.Count > 0)
            {
                defects = found;
                return false;
            }

            foreach (var (key, claim) in staged)
            {
                _claims[key] = claim;
            }
        }

        defects = [];
        return true;
    }

    /// <summary>
    /// Gives up everything an extension claimed.
    /// </summary>
    /// <param name="plugin">The extension.</param>
    /// <remarks>
    /// Called when an extension is quarantined after its tokens were claimed. Without it a failed extension
    /// would keep its tokens hostage until the host restarted.
    /// </remarks>
    public void Release(PluginId plugin)
    {
        lock (_gate)
        {
            var owned = _claims
                .Where(entry => entry.Value.Plugin == plugin)
                .Select(entry => entry.Key)
                .ToArray();

            foreach (var key in owned)
            {
                _claims.Remove(key);
            }
        }
    }
}
