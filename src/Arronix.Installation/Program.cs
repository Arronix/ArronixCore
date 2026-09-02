using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using Arronix.Common.Installation;
using Arronix.Installation;

// The one route from this repository to a running Arronix.
//
// It reads as four short steps on purpose: work out what was asked, compose the installation, own exactly
// one server process, and say what is true. Anything longer than that belongs in one of the types beside
// this file, because the thing this tool exists to replace was a long script that only one person could
// follow.

try
{
    var options = CommandLine.Parse(args);

    if (options.Command == InstallationCommand.Help)
    {
        Report.Usage();
        return 0;
    }

    var repositoryRoot = RepositoryRoot();
    var layout = InstallationLayout.At(options.Root, repositoryRoot);

    // A supplied root is never self-authorizing. This runs before every command, not only the destructive
    // ones, because composing already clears and recreates whole subtrees under whatever root it is given.
    InstallationRootGuard.EnsureSafe(layout.Root, repositoryRoot);

    return options.Command switch
    {
        InstallationCommand.Reset => Reset(layout, options),
        InstallationCommand.Install => Install(repositoryRoot, layout, options),
        _ => await RunAsync(repositoryRoot, layout, options).ConfigureAwait(false),
    };
}
catch (InstallationException failure)
{
    Console.Error.WriteLine($"error: {failure.Message}");
    return 2;
}

static int Install(string repositoryRoot, InstallationLayout layout, CommandLine options)
{
    Compose(repositoryRoot, layout, options);
    Console.WriteLine();
    Console.WriteLine($"  Installed into {layout.Root}");
    Console.WriteLine();
    return 0;
}

static InstallationManifest Compose(string repositoryRoot, InstallationLayout layout, CommandLine options)
{
    var dotnet = DotNetCli.Resolve();
    var declared = Deliverables.Select(repositoryRoot, options.Samples, options.Packages);

    var packages = declared
        .Select(package => new PackageSource(
            package.Id,
            Deliverables.ProjectFile(repositoryRoot, package.ProjectName),
            package.Role))
        .Concat(options.ExternalPackages.Select(external => new PackageSource(
            external.Id,
            Path.GetFullPath(external.ProjectFile),
            PackageRole.Fixture)))
        .ToArray();

    var duplicate = packages
        .GroupBy(static package => package.Id, StringComparer.Ordinal)
        .FirstOrDefault(static group => group.Count() > 1);

    if (duplicate is not null)
    {
        throw new InstallationException(
            $"'{duplicate.Key}' is named more than once between --package, its dependencies and "
            + "--external-package. Each package identifier this run installs must be unique.");
    }

    var composer = new InstallationComposer(dotnet, repositoryRoot, layout);

    return composer.Install(packages, static message => Console.WriteLine($"==> {message}"));
}

static async Task<int> RunAsync(string repositoryRoot, InstallationLayout layout, CommandLine options)
{
    var manifest = options.Build
        ? Compose(repositoryRoot, layout, options)
        : InstallationManifest.ReadFrom(layout);

    var port = options.Port is { } requested ? LoopbackPort.Require(requested) : LoopbackPort.Choose();

    using var interrupted = new CancellationTokenSource();
    using var interrupt = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
    {
        context.Cancel = true;
        interrupted.Cancel();
    });

    Console.CancelKeyPress += (_, eventArguments) =>
    {
        eventArguments.Cancel = true;
        interrupted.Cancel();
    };

    using var server = ServerProcess.Start(DotNetCli.Resolve(), layout, manifest, port);
    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

    var stopped = false;

    try
    {
        await server.WaitUntilReadyAsync(client, interrupted.Token).ConfigureAwait(false);

        var notes = new List<string>();

        if (options.Samples && manifest.Packages.Any(static package => package.Role == PackageRole.Sample)
            && await SampleCatalogSetup
                .EnsureConfiguredAsync(client, server.Address, interrupted.Token)
                .ConfigureAwait(false) is { } note)
        {
            notes.Add(note);
        }

        Report.Running(layout, manifest, server.Address, notes);

        if (options.OpenBrowser)
        {
            OpenInBrowser(server.Address);
        }

        await server.WaitForExitAsync(interrupted.Token).ConfigureAwait(false);
    }
    catch (OperationCanceledException)
    {
        // Interrupted before the installation answered. Stopping the owned server is still this run's job.
    }
    finally
    {
        stopped = server.Stop(Console.WriteLine);
    }

    if (!stopped)
    {
        Console.Error.WriteLine(
            $"error: the server this run started (process {server.Id}) could not be stopped.");
        return 1;
    }

    return server.IsRunning || server.ExitCode == 0 ? 0 : server.ExitCode;
}

static int Reset(InstallationLayout layout, CommandLine options)
{
    var outcome = InstallationReset.Execute(layout, options.ResetEverything);
    Report.Reset(layout, outcome.Removed, outcome.Remaining);
    return 0;
}

static void OpenInBrowser(Uri address)
{
    var command = OperatingSystem.IsMacOS() ? "open"
        : OperatingSystem.IsWindows() ? "explorer"
        : "xdg-open";

    try
    {
        using var opened = Process.Start(new ProcessStartInfo(command)
        {
            ArgumentList = { address.ToString() },
            UseShellExecute = false,
        });
    }
    catch (System.ComponentModel.Win32Exception)
    {
        // A machine with no browser opener is not a failed run. The address was printed a line ago.
        Console.WriteLine($"    Could not open a browser here; the address above is the whole of it.");
    }
}

static string RepositoryRoot()
{
    var directory = new DirectoryInfo(AppContext.BaseDirectory);

    while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Arronix.sln")))
    {
        directory = directory.Parent;
    }

    return directory?.FullName
        ?? throw new InstallationException(
            $"Could not find 'Arronix.sln' above '{AppContext.BaseDirectory}'.");
}
