using Arronix.Abstractions.Shape;

namespace Arronix.Abstractions.Providers;

/// <summary>
/// Declares one configurable setting of a provider.
/// </summary>
/// <remarks>
/// <para>
/// Declarative only. An attribute-driven twin was rejected: an extension reflecting over its own types
/// crosses no boundary and needs no contract, two representations of one schema are guaranteed to drift,
/// and an attribute path would force the <i>host</i> to reflect over extension types — which is exactly
/// what a closed registration surface exists to avoid.
/// </para>
/// <para>
/// Every member here is semantic. What a setting <i>is</i> — an endpoint, a credential, a reference to a
/// quality profile — is declared; how it should be presented is not, and cannot be.
/// </para>
/// </remarks>
public sealed record SettingsField
{
    /// <summary>
    /// Gets the key this setting is stored under in a definition.
    /// </summary>
    public required string FieldId { get; init; }

    /// <summary>
    /// Gets the setting's display name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the shape of the setting's value.
    /// </summary>
    public required FieldValueKind ValueKind { get; init; }

    /// <summary>
    /// Gets what the setting means to the platform.
    /// </summary>
    public required SettingRole Role { get; init; }

    /// <summary>
    /// Gets how sensitive the value is, which decides whether it may leave the process.
    /// </summary>
    public SettingSensitivity Sensitivity { get; init; } = SettingSensitivity.Public;

    /// <summary>
    /// Gets a value indicating whether a definition is invalid without this setting.
    /// </summary>
    public bool Required { get; init; }

    /// <summary>
    /// Gets a value indicating whether the setting is for unusual deployments and can be left alone.
    /// </summary>
    public bool Advanced { get; init; }

    /// <summary>
    /// Gets a value indicating whether the setting holds several values.
    /// </summary>
    public bool Multivalued { get; init; }

    /// <summary>
    /// Gets the sentence explaining what the setting does.
    /// </summary>
    public string? HelpText { get; init; }

    /// <summary>
    /// Gets the sentence warning what happens if the setting is misused.
    /// </summary>
    public string? HelpWarning { get; init; }

    /// <summary>
    /// Gets further reading about the setting.
    /// </summary>
    public Uri? HelpLink { get; init; }

    /// <summary>
    /// Gets the value applied when the user supplies none.
    /// </summary>
    public string? DefaultValue { get; init; }

    /// <summary>
    /// Gets the name of the group of related settings this one belongs to.
    /// </summary>
    public string? Section { get; init; }

    /// <summary>
    /// Gets the unit the value is expressed in, for presentation only.
    /// </summary>
    public string? Unit { get; init; }

    /// <summary>
    /// Gets the permitted values, when the setting takes one of a fixed set.
    /// </summary>
    public IReadOnlyList<FacetValue> Choices { get; init; } = [];

    /// <summary>
    /// Gets the identifier of a set of values the provider supplies at runtime, resolved through the
    /// platform rather than fetched directly. An identifier, never an address: an address is transport,
    /// and an identifier is intent.
    /// </summary>
    public string? OptionSourceId { get; init; }
}

/// <summary>
/// What a provider setting means to the platform.
/// </summary>
/// <remarks>
/// Semantics, not presentation. The platform uses these to validate, to redact, to offer the right values
/// and to tell an operator what a setting is for; nothing here says how a setting should look.
/// </remarks>
public enum SettingRole
{
    /// <summary>An ordinary value with no platform meaning.</summary>
    Value = 0,

    /// <summary>The address of the service.</summary>
    Endpoint = 1,

    /// <summary>A location on a storage mount.</summary>
    Path = 2,

    /// <summary>A length of time.</summary>
    Duration = 3,

    /// <summary>A size in bytes.</summary>
    ByteSize = 4,

    /// <summary>A quantity.</summary>
    Count = 5,

    /// <summary>A two-state choice.</summary>
    Flag = 6,

    /// <summary>One of the setting's declared values.</summary>
    Enumeration = 7,

    /// <summary>A relative position among peers.</summary>
    Ordering = 8,

    /// <summary>A set of platform tags.</summary>
    TagSet = 9,

    /// <summary>A set of release categories.</summary>
    CategorySet = 10,

    /// <summary>A reference to a configured quality profile.</summary>
    QualityProfileRef = 11,

    /// <summary>A reference to a registered media kind.</summary>
    MediaKindRef = 12,

    /// <summary>A reference to a configured root folder.</summary>
    RootFolderRef = 13,

    /// <summary>A reference to a transfer protocol.</summary>
    DownloadProtocolRef = 14,

    /// <summary>A naming template, validated against the registered tokens.</summary>
    NamingTemplate = 15,

    /// <summary>A reference to a configured selection policy.</summary>
    SelectionPolicyRef = 16
}

/// <summary>
/// How sensitive a provider setting's value is.
/// </summary>
/// <remarks>
/// One declaration discharges three obligations: it states the intent that a value is a secret, it drives
/// the rule that such a value is never sent outward and that a write carrying the placeholder means
/// "unchanged", and it contributes a redaction rule to the telemetry pipeline. Three mechanisms from one
/// declaration is the strongest argument for the setting schema living in the contract layer at all.
/// </remarks>
public enum SettingSensitivity
{
    /// <summary>Ordinary configuration. May be read back and may appear in diagnostics.</summary>
    Public = 0,

    /// <summary>Identifies a person. May be read back; redacted from diagnostics.</summary>
    UserName = 1,

    /// <summary>Authenticates a person. Never read back; redacted from diagnostics.</summary>
    Credential = 2,

    /// <summary>Authenticates the platform itself. Never read back; redacted from diagnostics.</summary>
    Secret = 3
}
