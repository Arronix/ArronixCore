using Arronix.Abstractions.Diagnostics;
using Arronix.Common.Configuration;

namespace Arronix.Common.Telemetry;

/// <summary>
/// The rules the platform itself knows the shape of, independently of any vendor.
/// </summary>
/// <remarks>
/// <para>
/// Generic by construction: an authorization header, a credential in a URL, a query parameter whose name
/// says what it holds. None of them name a service, because a rule that names a service stops being true
/// the day that service changes and leaves an installation believing it is redacting something.
/// </para>
/// <para>
/// These exist because an installation that contributed no rules would otherwise redact nothing while its
/// options say redaction is on. An operator can switch any of them off by its qualified identifier.
/// </para>
/// </remarks>
internal sealed class CoreRedactionRules(RedactionOptions? settings = null) : IRedactionRuleProvider
{
    /// <summary>The owner every core rule identifier is qualified by.</summary>
    internal const string Owner = "arronix.core";

    private readonly RedactionOptions _settings = settings ?? new RedactionOptions();

    /// <inheritdoc />
    public IReadOnlyList<RedactionRule> Rules
    {
        get
        {
            var rules = new List<RedactionRule>
            {
                // Authorization: Bearer …, Authorization: Basic …, and the same words in a log line.
                // The scheme word is not the secret, so it is consumed and what follows it is masked.
                new(
                    "authorization-header",
                    @"(?i:authorization)\s*[:=]\s*(?:(?i:bearer|basic|token|digest)\s+)?(?<secret>[^\s""',;]+)"),

                // A credential in a URL's authority, which is where connection strings put them.
                new("url-credentials", @"//[^\s/@:]+:(?<secret>[^\s/@]+)@"),

                // A query parameter, header or setting whose name says what it holds. The optional quote
                // and the excluded quote in the class are what make the JSON form work: "password": "x".
                new(
                    "named-secret",
                    @"(?i:api[_-]?key|apikey|x-api-key|access[_-]?token|refresh[_-]?token|id[_-]?token|token|password|passwd|pwd|secret|client[_-]?secret|private[_-]?key|session)""?\s*[:=]\s*""?(?<secret>[^\s""',;&}]+)"),

                // A cookie header, whose whole value is credentials whatever the names inside it are.
                new("cookie-header", @"(?i:set-cookie|cookie)\s*:\s*(?<secret>[^\r\n]+)"),

                // A JSON web token, which carries its own claims wherever it is pasted.
                new(
                    "json-web-token",
                    @"(?<secret>\beyJ[A-Za-z0-9_-]{5,}\.[A-Za-z0-9_-]{5,}\.[A-Za-z0-9_-]+)",
                    IgnoreCase: false),

                // The body of a PEM block. The markers are left so the reader can see what was removed.
                new(
                    "pem-block",
                    @"-----BEGIN (?:[A-Z ]+)-----(?<secret>[\s\S]+?)-----END",
                    IgnoreCase: false),
            };

            if (_settings.MaskNetworkAddresses)
            {
                // Both families, because masking one leaks every host that moved to the other. Loopback is
                // deliberately not exempted: it is a host address like any other in a support bundle.
                //
                // At least one octet of two or more digits is required, which is the one cheap way to tell
                // an address from a version number. The cost is stated rather than hidden: an address whose
                // every octet is a single digit — 1.1.1.1, 0.0.0.0 — is left alone, and those are published
                // resolver and wildcard addresses rather than anything an installation reveals by logging.
                rules.Add(new RedactionRule(
                    "ipv4-address",
                    @"(?<secret>\b(?:\d{2,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}|\d{1,3}\.\d{2,3}\.\d{1,3}\.\d{1,3}|\d{1,3}\.\d{1,3}\.\d{2,3}\.\d{1,3}|\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{2,3})\b)",
                    IgnoreCase: false));

                // Both the full form and the compressed one: ::1 and 2001:db8::1 are addresses too, and a
                // pattern that only matched the long form would leak exactly the ones people write by hand.
                // Either the full eight groups, or a compressed form — which is to say one containing "::".
                // Anything between those two is a clock time, and masking every timestamp in the log would
                // cost more than the addresses are worth.
                rules.Add(new RedactionRule(
                    "ipv6-address",
                    @"(?<secret>(?:[0-9A-Fa-f]{1,4}:){7}[0-9A-Fa-f]{1,4}|(?:[0-9A-Fa-f]{1,4}:){1,7}:(?:[0-9A-Fa-f]{1,4}(?::[0-9A-Fa-f]{1,4}){0,6})?|::(?:[0-9A-Fa-f]{1,4}(?::[0-9A-Fa-f]{1,4}){0,6})?)",
                    IgnoreCase: false));
            }

            return rules;
        }
    }
}
