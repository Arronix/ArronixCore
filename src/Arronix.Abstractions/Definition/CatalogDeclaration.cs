using System.Diagnostics.CodeAnalysis;
using Arronix.Abstractions.Providers;

namespace Arronix.Abstractions.Definition;

/// <summary>
/// Metadata mapping as data: request templates, response field maps, derivations and identifier rules.
/// </summary>
/// <remarks>
/// <para>
/// Executed entirely by the host over the host's outbound gateway, attributed and rate-limited under the
/// plugin's identity — the definition never sees a socket, which is what makes the network privilege
/// structurally ungrantable for a definition-mode plugin.
/// </para>
/// <para>
/// The converter and derivation vocabularies are closed. A catalog protocol they cannot express is an
/// integration plugin registering a real cataloger, never a richer template grammar; the at-scale
/// precedent this design leans on reached its scale <i>with</i> embedded conditionals and a native-code
/// escape rate near a tenth, so the closed vocabulary here is a measured budget, not a proven ceiling.
/// </para>
/// </remarks>
[Experimental(ExperimentalContracts.Definition, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed record CatalogDeclaration
{
    /// <summary>
    /// Gets the named request templates.
    /// </summary>
    public required IReadOnlyList<RequestTemplate> Requests { get; init; }

    /// <summary>
    /// Gets the response-to-field maps, per level or grouping axis.
    /// </summary>
    public required IReadOnlyList<ResponseMap> Responses { get; init; }

    /// <summary>
    /// Gets the parameterized derivation rules applied after mapping.
    /// </summary>
    public IReadOnlyList<DerivationRule> Derivations { get; init; } = [];

    /// <summary>
    /// Gets the identifier normalization and lookup-form rules.
    /// </summary>
    public IReadOnlyList<IdNormalization> IdRules { get; init; } = [];

    /// <summary>
    /// Gets the changed-since window policy, when the catalog supports delta synchronization.
    /// </summary>
    public DeltaSyncPolicy? Delta { get; init; }

    /// <summary>
    /// Gets the paging policy.
    /// </summary>
    public PagingPolicy Paging { get; init; } = PagingPolicy.Default;

    /// <summary>
    /// Gets the catalog's settings schema. Derivation rules may reference a settings field by
    /// identifier.
    /// </summary>
    public IReadOnlyList<SettingsField> Settings { get; init; } = [];
}
