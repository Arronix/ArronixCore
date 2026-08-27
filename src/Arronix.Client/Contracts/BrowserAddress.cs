using System.Linq;
using Arronix.Abstractions.Client;

namespace Arronix.Client.Contracts;

/// <summary>
/// Which addresses this client will write into a document, and which it refuses.
/// </summary>
/// <remarks>
/// A projected address becomes an <c>href</c> or a <c>src</c>, so schemes are an allowlist and a refusal is
/// never a rewrite. An inline image must be one of four raster types, base64, and decode to bytes whose
/// container signature is the type it claims.
/// </remarks>
internal static class BrowserAddress
{
    private const string Base64Marker = ";base64,";

    /// <summary>Describes why a payload address may not be requested, or nothing when it may.</summary>
    /// <param name="address">The address as a caller wrote it.</param>
    /// <returns>The refusal, or <see langword="null"/>.</returns>
    /// <remarks>Relative only: a payload is read from the host that served this client.</remarks>
    internal static string? DescribeRequest(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return "no payload address was given.";
        }

        if (address.Length > ClientContractLimits.MaxAddressLength)
        {
            return $"the payload address is {address.Length} characters, past the "
                + $"{ClientContractLimits.MaxAddressLength} allowed.";
        }

        // A protocol-relative address is an absolute one that looks like a path, and HttpClient resolves it
        // against the scheme rather than against the base address.
        if (address.StartsWith("//", StringComparison.Ordinal))
        {
            return $"'{address}' names another origin; a payload address is a path.";
        }

        if (address.Contains('\\', StringComparison.Ordinal))
        {
            return $"'{address}' contains a backslash, which is not a path separator in an address.";
        }

        return Uri.TryCreate(address, UriKind.Relative, out _)
            ? null
            : $"'{address}' is not a path relative to this host.";
    }

    /// <summary>Describes why a link value's address may not be rendered, or nothing when it may.</summary>
    /// <param name="address">The address the projected value carries.</param>
    /// <returns>The refusal, or <see langword="null"/>.</returns>
    internal static string? DescribeLink(Uri? address) => DescribeWeb(address, "a link");

    /// <summary>Describes why an artwork address may not be rendered, or nothing when it may.</summary>
    /// <param name="address">The address the projected value carries.</param>
    /// <returns>The refusal, or <see langword="null"/>.</returns>
    internal static string? DescribeArtwork(Uri? address)
    {
        if (address is null)
        {
            return "artwork states no address.";
        }

        if (!address.IsAbsoluteUri)
        {
            return $"artwork states the relative address '{address}', and only an absolute one names an image.";
        }

        return string.Equals(address.Scheme, "data", StringComparison.Ordinal)
            ? DescribeInlineImage(address.ToString())
            : DescribeWeb(address, "artwork");
    }

    /// <summary>Holds an absolute address to the two schemes a browser may follow.</summary>
    private static string? DescribeWeb(Uri? address, string subject)
    {
        if (address is null)
        {
            return $"{subject} states no address.";
        }

        if (!address.IsAbsoluteUri)
        {
            return $"{subject} states the relative address '{address}', and a value a browser follows must "
                + "say where it points.";
        }

        // The text this client writes into the document, which is what the browser acts on.
        var rendered = address.ToString();

        if (rendered.Length > ClientContractLimits.MaxAddressLength)
        {
            return $"{subject} states an address of {rendered.Length} characters, past the "
                + $"{ClientContractLimits.MaxAddressLength} allowed.";
        }

        if (!string.Equals(address.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal)
            && !string.Equals(address.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal))
        {
            return $"{subject} states the scheme '{address.Scheme}'; a browser is offered http and https only.";
        }

        if (address.UserInfo.Length > 0)
        {
            return $"{subject} states an address carrying user information, which a browser sends and a "
                + "reader cannot see.";
        }

        return address.Host.Length == 0 ? $"{subject} states an http address with no host." : null;
    }

    /// <summary>Holds an inline image to a declared raster type whose bytes are that type.</summary>
    private static string? DescribeInlineImage(string rendered)
    {
        if (rendered.Length > ClientContractLimits.MaxAddressLength)
        {
            return $"an inline image of {rendered.Length} characters is past the "
                + $"{ClientContractLimits.MaxAddressLength} an address carries.";
        }

        var marker = rendered.IndexOf(Base64Marker, StringComparison.Ordinal);

        if (marker < 0)
        {
            return $"'{Excerpt(rendered)}' is an inline image that is not base64 encoded; its payload would "
                + "be read as escaped text rather than as bytes.";
        }

        var mediaType = rendered[..marker];
        var format = Raster.For(mediaType);

        if (format is null)
        {
            return $"'{mediaType}' is not one of the inline image types a browser is offered "
                + $"({Raster.Offered}); a document type inlined as an image is a document the browser parses.";
        }

        var payload = rendered.AsSpan(marker + Base64Marker.Length);

        if (Shape(payload) is { } malformed)
        {
            return malformed;
        }

        var decoded = new byte[payload.Length / 4 * 3];

        if (!Convert.TryFromBase64Chars(payload, decoded, out var written) || written == 0)
        {
            return "an inline image carries a base64 payload that does not decode.";
        }

        return format.Matches(decoded.AsSpan(0, written))
            ? null
            : $"an inline image labeled '{mediaType}' decodes to bytes that are not {format.Name}.";
    }

    /// <summary>Refuses a base64 payload that is empty, mis-grouped, or padded anywhere but at its end.</summary>
    private static string? Shape(ReadOnlySpan<char> payload)
    {
        if (payload.Length == 0)
        {
            return "an inline image carries no payload.";
        }

        if (payload.Length % 4 != 0)
        {
            return $"an inline image carries {payload.Length} base64 characters, which is not a whole "
                + "number of encoded groups.";
        }

        var body = payload.Length;

        while (body > 0 && payload[body - 1] == '=')
        {
            body--;
        }

        var padding = payload.Length - body;

        if (padding > 2)
        {
            return $"an inline image carries {padding} base64 padding characters, and at most two end a "
                + "payload.";
        }

        for (var index = 0; index < body; index++)
        {
            var character = payload[index];

            if (!char.IsAsciiLetterOrDigit(character) && character != '+' && character != '/')
            {
                return character == '='
                    ? "an inline image carries base64 padding somewhere other than at its end."
                    : $"an inline image carries '{character}', which is not a base64 character.";
            }
        }

        return null;
    }

    private static string Excerpt(string value) => value.Length <= 64 ? value : value[..64] + "…";

    /// <summary>One raster container, and how its first bytes identify it.</summary>
    private sealed record Raster(string MediaType, string Name, byte[][] Signatures)
    {
        private static readonly Raster[] Offers =
        [
            new("data:image/png", "a PNG", [[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]]),
            new("data:image/jpeg", "a JPEG", [[0xFF, 0xD8, 0xFF]]),
            new("data:image/gif", "a GIF", [
                [0x47, 0x49, 0x46, 0x38, 0x37, 0x61],
                [0x47, 0x49, 0x46, 0x38, 0x39, 0x61]
            ]),

            // RIFF carries its form four bytes after a length the signature skips.
            new("data:image/webp", "a WebP", [[0x52, 0x49, 0x46, 0x46]])
            {
                Trailing = ([0x57, 0x45, 0x42, 0x50], 8),
            },
        ];

        /// <summary>Gets the media types a browser is offered, for a refusal to name.</summary>
        internal static string Offered { get; } = string.Join(", ", Offers.Select(offer => offer.MediaType));

        /// <summary>Gets a second signature and where it sits, for a container that needs one.</summary>
        private (byte[] Bytes, int Offset)? Trailing { get; init; }

        /// <summary>Finds the raster a media type names, or nothing.</summary>
        internal static Raster? For(string mediaType)
        {
            foreach (var offer in Offers)
            {
                if (string.Equals(offer.MediaType, mediaType, StringComparison.Ordinal))
                {
                    return offer;
                }
            }

            return null;
        }

        /// <summary>Determines whether decoded bytes are this container.</summary>
        internal bool Matches(ReadOnlySpan<byte> decoded)
        {
            var matched = false;

            foreach (var signature in Signatures)
            {
                if (decoded.StartsWith(signature))
                {
                    matched = true;
                    break;
                }
            }

            if (!matched)
            {
                return false;
            }

            if (Trailing is not { } trailing)
            {
                return true;
            }

            var end = trailing.Offset + trailing.Bytes.Length;

            return decoded.Length >= end
                && decoded.Slice(trailing.Offset, trailing.Bytes.Length).SequenceEqual(trailing.Bytes);
        }
    }
}
