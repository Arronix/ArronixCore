using System.IO;
using System.Linq;
using Arronix.Common.Installation;

namespace Arronix.Installation;

/// <summary>What one reset actually removed, and what it found but left alone.</summary>
/// <param name="Removed">The paths this reset deleted, in the order they were removed.</param>
/// <param name="Remaining">
/// Entries directly inside the root a <c>reset --all</c> could not remove the root for, because this tool
/// does not own them. Empty for a narrow reset, and for a wide reset that did empty and remove the root;
/// non-empty when unrelated entries prevented the root from being removed.
/// </param>
internal sealed record ResetOutcome(IReadOnlyList<string> Removed, IReadOnlyList<string> Remaining);

/// <summary>
/// The reset orchestration: proving a root is an installation this tool owns, then deleting exactly the
/// finite set of paths it owns and nothing else.
/// </summary>
/// <remarks>
/// A supplied root is never self-authorizing for a destructive operation. The exact target must already carry
/// a manifest this tool wrote, whose schema, identity and every declared path validate against what is
/// actually on disk; a directory that merely looks like an installation is refused before anything is
/// deleted. A valid manifest proves the declared Arronix payload underneath the root — it proves nothing
/// about any other entry that root happens to contain, so a reset never deletes the root wholesale, only the
/// finite set of paths this tool itself ever creates beneath it, and removes the now-empty root only once
/// nothing else remains in it.
/// </remarks>
internal static class InstallationReset
{
    /// <summary>Resets an installation.</summary>
    /// <param name="layout">The installation to reset.</param>
    /// <param name="resetEverything">
    /// <see langword="true"/> to remove every path this tool ever creates, including the published server,
    /// client and packages; <see langword="false"/> to remove only the state a running installation
    /// accumulates.
    /// </param>
    /// <returns>What was removed, and what a wide reset found but could not remove the root for.</returns>
    /// <exception cref="InstallationException">
    /// There is no installation at that root, or its manifest does not describe what is actually on disk.
    /// </exception>
    public static ResetOutcome Execute(InstallationLayout layout, bool resetEverything)
    {
        ArgumentNullException.ThrowIfNull(layout);

        if (!Directory.Exists(layout.Root))
        {
            throw new InstallationException($"There is no installation at '{layout.Root}'.");
        }

        // The ownership check: the exact target must carry a manifest this tool wrote, whose schema,
        // identity and every declared path validate against what is actually on disk. Anything else — an
        // arbitrary directory that happens to be named right, or one whose manifest has drifted from
        // reality — is refused rather than trusted, and nothing below this line has run yet.
        InstallationManifest.ReadFrom(layout);

        var removed = new List<string>();

        foreach (var target in OwnedTargets(layout, resetEverything))
        {
            RequireOwned(layout, target);

            if (Directory.Exists(target))
            {
                Directory.Delete(target, recursive: true);
                removed.Add(target);
            }
            else if (File.Exists(target))
            {
                File.Delete(target);
                removed.Add(target);
            }
        }

        var remaining = Array.Empty<string>();

        if (resetEverything && Directory.Exists(layout.Root))
        {
            remaining = Directory.EnumerateFileSystemEntries(layout.Root)
                .Order(StringComparer.Ordinal)
                .ToArray();

            // Only ever removes a directory this loop just emptied, and only because it is now provably
            // empty — never a recursive delete of a root that might still hold something this tool does not
            // own.
            if (remaining.Length == 0)
            {
                Directory.Delete(layout.Root);
                removed.Add(layout.Root);
            }
        }

        return new ResetOutcome(removed, remaining);
    }

    /// <summary>
    /// The paths a reset owns. Narrow by default and explicit when wide: the paths an installation
    /// accumulates by being used are a reset's business; the published server, client and packages are
    /// rebuilt by composing again and are not, unless the caller asked to remove everything.
    /// </summary>
    private static IReadOnlyList<string> OwnedTargets(InstallationLayout layout, bool resetEverything)
        => resetEverything
            ? new[]
            {
                layout.ServerFolder,
                layout.ClientFolder,
                layout.PackagesFolder,
                layout.PackageStateFolder,
                layout.StateFolder,
                layout.ManifestFile,
            }
            : [layout.StateFolder, layout.PackageStateFolder];

    /// <summary>
    /// Refuses to treat a path as owned by a reset unless it is actually inside the installation. Every
    /// caller of <see cref="Execute"/> currently derives its targets from <paramref name="layout"/> itself,
    /// so this never fires in practice; it stands so that a future owned path can never escape the root it
    /// was computed from without being caught here first. Internal rather than private so this exact guard
    /// — the one <see cref="Execute"/> actually runs — can be proved directly against an escaping path.
    /// </summary>
    internal static void RequireOwned(InstallationLayout layout, string target)
    {
        if (!layout.Contains(target))
        {
            throw new InstallationException(
                $"'{target}' is not inside the installation at '{layout.Root}'; refusing to remove it.");
        }
    }
}
