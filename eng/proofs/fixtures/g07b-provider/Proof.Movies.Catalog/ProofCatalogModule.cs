using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Providers;
using Arronix.Abstractions.Shape;

namespace Proof.Movies.Catalog;

/// <summary>Registers the proof-owned cataloger without naming any Host or Client implementation.</summary>
public sealed class ProofCatalogModule : IPluginModule
{
    /// <inheritdoc />
    public PluginId Id { get; } = PluginId.FromString("proof.movies.catalog");

    /// <inheritdoc />
    public void Configure(IPluginContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Registry.AddCataloger<ProofMovieCataloger>(new ProviderDescriptor
        {
            LocalId = "proof-movies",
            Name = "G07B proof catalog",
            Description = "Deterministic proof-only Movie catalog data.",
            Settings =
            [
                new SettingsField
                {
                    FieldId = ProofMovieCataloger.RevisionField,
                    Name = "Revision",
                    ValueKind = FieldValueKind.Text,
                    Role = SettingRole.Value,
                    Required = true,
                    HelpText = "Use revision 1 or 2 to select the deterministic proof payload.",
                },
            ],
        });
    }
}
