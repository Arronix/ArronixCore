using System.IO;
using System.Linq;
using Arronix.Common.Installation;

namespace Arronix.Installation;

/// <summary>
/// What this run tells the person who started it.
/// </summary>
/// <remarks>
/// One block, printed once the installation is actually answering. Everything in it is a fact about this
/// run rather than an instruction: the address to open, the directory that now holds the state, and the
/// packages the installation admitted with what each of them is. An evaluator should not have to read a
/// script to find out where their data went.
/// </remarks>
internal static class Report
{
    /// <summary>Writes the block that says the installation is up.</summary>
    /// <param name="layout">The installation.</param>
    /// <param name="manifest">What it holds.</param>
    /// <param name="address">Where it is answering.</param>
    /// <param name="notes">Anything this run did on the operator's behalf.</param>
    public static void Running(
        InstallationLayout layout,
        InstallationManifest manifest,
        Uri address,
        IReadOnlyList<string> notes)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(notes);

        var open = address.ToString().TrimEnd('/');

        Console.WriteLine();
        Console.WriteLine("  Arronix is running.");
        Console.WriteLine();
        Console.WriteLine($"    Open           {open}");
        Console.WriteLine($"    Installation   {layout.Root}");
        Console.WriteLine($"    State          {layout.StoreDataSource}");
        Console.WriteLine($"    Package state  {layout.PackageStateFolder}");
        Console.WriteLine($"    Client         {layout.ClientStaticRoot}");
        Console.WriteLine($"    .NET SDK       {manifest.Sdk}");
        Console.WriteLine();
        Console.WriteLine("    Installed packages");

        var width = manifest.Packages.Count == 0
            ? 0
            : manifest.Packages.Max(static package => package.Id.Length);

        foreach (var package in manifest.Packages)
        {
            Console.WriteLine(
                $"      {package.Id.PadRight(width)}  {package.Version}  {package.Name}{Note(package.Role)}");
        }

        foreach (var note in notes)
        {
            Console.WriteLine();
            Console.WriteLine($"    {note}");
        }

        Console.WriteLine();
        Console.WriteLine("    Press Ctrl-C to stop. Only the server this run started is stopped.");
        Console.WriteLine();
    }

    /// <summary>Writes the help text.</summary>
    public static void Usage()
    {
        Console.WriteLine(
            """
            Arronix — build, install and run a real Arronix installation.

              bash eng/run-arronix.sh                 install if needed, then run it
              bash eng/run-arronix.sh install         compose the installation and stop
              bash eng/run-arronix.sh reset           empty the installation's state
              bash eng/run-arronix.sh reset --all     remove the whole installation directory

            Options

              --root PATH            the installation directory
                                     (default: artifacts/installation, which git ignores)
              --port N               the loopback port to bind, or refuse if it is busy
                                     (default: the first free port from 5227)
              --no-build             run what is already installed, publishing nothing
              --no-sample-catalog    install no sample data
              --package ID           install only the named package (with its dependencies); repeatable
              --external-package ID=PROJECT
                                     also install the package that PROJECT publishes, under ID;
                                     repeatable. For proofs and fixtures that need a real composed
                                     installation beside a package this repository does not ship.
              --open                 open the address in the default browser once it answers
              --help                 this text
            """);
    }

    /// <summary>Writes what a reset removed.</summary>
    /// <param name="layout">The installation.</param>
    /// <param name="removed">The paths that were removed.</param>
    /// <param name="remaining">
    /// Entries a <c>reset --all</c> found directly under the root that this tool does not own, and therefore
    /// left in place along with the root itself.
    /// </param>
    public static void Reset(InstallationLayout layout, IReadOnlyList<string> removed, IReadOnlyList<string> remaining)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(removed);
        ArgumentNullException.ThrowIfNull(remaining);

        Console.WriteLine($"Installation {layout.Root}");

        if (removed.Count == 0)
        {
            Console.WriteLine("  Nothing to remove; it held none of the paths a reset owns.");
        }
        else
        {
            foreach (var path in removed)
            {
                Console.WriteLine($"  Removed {Path.GetRelativePath(layout.Root, path)}");
            }
        }

        if (remaining.Count > 0)
        {
            Console.WriteLine(
                "  Left in place, because this tool did not create it and a valid installation manifest "
                + "does not prove ownership of anything beyond what it declares:");

            foreach (var path in remaining)
            {
                Console.WriteLine($"    {Path.GetRelativePath(layout.Root, path)}");
            }
        }
    }

    private static string Note(PackageRole role) => role switch
    {
        PackageRole.NeedsCredentials => "  — needs credentials before it can answer",
        PackageRole.Sample => "  — sample data, shipped for evaluation",
        PackageRole.Fixture => "  — named on the command line; not shipped by this repository",
        PackageRole.Product => string.Empty,
        _ => string.Empty,
    };
}
