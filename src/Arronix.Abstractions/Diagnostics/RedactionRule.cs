using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Diagnostics;

/// <summary>
/// One pattern describing where a secret appears in text, so the redaction engine can mask it before
/// the text reaches a log file or a telemetry sink.
/// </summary>
/// <param name="RuleId">
/// Stable identifier of the rule, unique within the provider that owns it. It appears in diagnostics
/// when a rule fails to compile, and lets an operator disable one rule without disabling the rest.
/// </param>
/// <param name="Pattern">
/// A .NET regular expression matching the surrounding text. The portion to mask must be captured by a
/// group named <see cref="SecretGroupName"/>; anything outside that group is preserved verbatim, which
/// is what keeps redacted text readable.
/// </param>
/// <param name="SecretGroupName">Name of the capture group holding the secret.</param>
/// <param name="IgnoreCase">Whether the pattern matches case-insensitively.</param>
/// <remarks>
/// A rule is owned by whoever knows the shape of the secret. Keeping the rules contributed rather than
/// hard-coded means a component that stops shipping also stops leaking its pattern into every install.
/// </remarks>
[Experimental(ExperimentalContracts.Diagnostics, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed record RedactionRule(
    string RuleId,
    string Pattern,
    string SecretGroupName = "secret",
    bool IgnoreCase = true);
