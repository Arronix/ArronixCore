using Arronix.Abstractions.Health;
using Arronix.Abstractions.Plugins;
using Arronix.Plugins.Loading;
using Arronix.Plugins.Manifest;
using Arronix.Plugins.Registration;

namespace Arronix.Plugins.Tests.Support;

/// <summary>An explicit empty Host transaction used by loader-only tests.</summary>
internal sealed class NoOpAdmission : IPluginAdmissionCheck
{
    public static NoOpAdmission Instance { get; } = new();

    private NoOpAdmission()
    {
    }

    public PluginAdmissionResult Prepare(ValidatedManifest manifest, PluginRegistrationLedger ledger)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(ledger);

        return PluginAdmissionResult.Prepared(new Attempt(manifest.Id));
    }

    private sealed class Attempt(PluginId plugin) : IPluginAdmissionAttempt
    {
        public PluginId Plugin { get; } = plugin;

        public AdmittedInventory Inventory { get; } = new([]);

        public bool TryCommit(out CoreErrorCode errorCode, out IReadOnlyList<string> defects)
        {
            errorCode = default;
            defects = [];
            return true;
        }

        public void Rollback()
        {
        }
    }
}
