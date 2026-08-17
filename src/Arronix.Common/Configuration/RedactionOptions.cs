using System.ComponentModel.DataAnnotations;

namespace Arronix.Common.Configuration;

/// <summary>
/// Operator control over the removal of secrets from text before it reaches a log file or a telemetry sink.
/// </summary>
/// <remarks>
/// Redaction is on by default and stays on in every environment. The legacy arrangement redacted only in
/// production, which has the control backwards: a developer machine and a support bundle are exactly where
/// a captured credential travels furthest.
/// </remarks>
public sealed class RedactionOptions
{
    /// <summary>
    /// The configuration section this options type binds from.
    /// </summary>
    public const string SectionName = "Arronix:Redaction";

    /// <summary>
    /// Gets or sets a value indicating whether text is redacted before it is written.
    /// </summary>
    /// <remarks>
    /// Turning this off is a deliberate, auditable act with a narrow purpose: reproducing a redaction bug
    /// against a known input. It is not a performance setting.
    /// </remarks>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the text substituted for a matched secret. It replaces the secret regardless of the
    /// secret's length, so the length of the original is not recoverable from the redacted text.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    [StringLength(32, MinimumLength = 1)]
    public string Replacement { get; set; } = "(redacted)";

    /// <summary>
    /// Gets or sets a value indicating whether the host portion of an address is masked. Applies to both
    /// address families: masking only one of them leaks the addresses of every host that has moved to the
    /// other.
    /// </summary>
    public bool MaskNetworkAddresses { get; set; } = true;

    /// <summary>
    /// Gets the identifiers of contributed rules that are not applied. A rule that produces a false
    /// positive against one installation's data can be switched off there without switching off redaction
    /// or waiting for the component that contributed the rule to ship a fix.
    /// </summary>
    public IList<string> DisabledRuleIds { get; } = [];
}
