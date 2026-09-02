using System.Diagnostics;
using System.IO;
using System.Net.Http;
using Arronix.Common.Installation;

namespace Arronix.Installation;

/// <summary>
/// The one server process this run owns.
/// </summary>
/// <remarks>
/// <para>
/// Owned means exactly one process identifier, obtained by starting it. Nothing here ever looks up a
/// process by name, by port or by command line, so a second Arronix, an unrelated service, or a stale
/// process from a previous run is never signalled by this one. That is a property of the mechanism rather
/// than a promise: the only handle it holds is the one <see cref="Process.Start(ProcessStartInfo)"/>
/// returned.
/// </para>
/// <para>
/// Shutdown is bounded effort with a visible failure, not a guarantee. A polite request first, then a
/// bounded wait, then a forced stop, then a bounded wait again; a process that survives both is reported
/// with its identifier and makes the run non-zero rather than being quietly abandoned. On an interactive
/// terminal the server has usually already received the same interrupt the operator typed and is stopping
/// on its own, and asking twice costs nothing.
/// </para>
/// </remarks>
internal sealed partial class ServerProcess : IDisposable
{
    private const int SigTerm = 15;

    private static readonly TimeSpan GracefulLimit = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ForcedLimit = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan ReadinessLimit = TimeSpan.FromSeconds(120);

    private readonly Process _process;
    private bool _disposed;

    private ServerProcess(Process process, Uri address)
    {
        _process = process;
        Address = address;
    }

    /// <summary>Gets the address the server was told to listen on.</summary>
    public Uri Address { get; }

    /// <summary>Gets the identifier of the process this run started.</summary>
    public int Id => _process.Id;

    /// <summary>Gets a value indicating whether the owned process is still running.</summary>
    public bool IsRunning => !_process.HasExited;

    /// <summary>Gets the exit code of the owned process once it has stopped.</summary>
    public int ExitCode => _process.ExitCode;

    /// <summary>
    /// Starts the installed server against its own installation.
    /// </summary>
    /// <param name="dotnet">The SDK command.</param>
    /// <param name="layout">The installation.</param>
    /// <param name="manifest">What the installation holds.</param>
    /// <param name="port">The loopback port this run owns.</param>
    /// <returns>The running server.</returns>
    public static ServerProcess Start(
        DotNetCli dotnet,
        InstallationLayout layout,
        InstallationManifest manifest,
        int port)
    {
        ArgumentNullException.ThrowIfNull(dotnet);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(manifest);

        var entry = Path.Combine(layout.ServerFolder, manifest.ServerEntryAssembly);

        if (!File.Exists(entry))
        {
            throw new InstallationException(
                $"The installation at '{layout.Root}' has no server at '{entry}'.");
        }

        var address = new Uri($"http://127.0.0.1:{port}", UriKind.Absolute);

        var start = new ProcessStartInfo(dotnet.Command)
        {
            // The server's content root is its own folder, which is what makes the installation root the
            // installed appsettings.json states resolve to this installation and not to wherever the
            // operator happened to be standing.
            WorkingDirectory = layout.ServerFolder,
            UseShellExecute = false,
        };

        start.ArgumentList.Add(entry);
        start.Environment["ASPNETCORE_URLS"] = address.ToString().TrimEnd('/');
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";

        var process = Process.Start(start)
            ?? throw new InstallationException($"The .NET command '{dotnet.Command}' could not be started.");

        return new ServerProcess(process, address);
    }

    /// <summary>
    /// Waits until the server answers, it exits, or the wait is abandoned.
    /// </summary>
    /// <param name="client">The client used to ask.</param>
    /// <param name="cancellationToken">Abandons the wait.</param>
    /// <returns>A task that completes when the server is answering.</returns>
    /// <exception cref="InstallationException">The server stopped, or never answered.</exception>
    public async Task WaitUntilReadyAsync(HttpClient client, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);

        var deadline = DateTimeOffset.UtcNow + ReadinessLimit;
        var probe = new Uri(Address, "api");

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!IsRunning)
            {
                throw new InstallationException(
                    $"The server stopped before it began answering (exit code {ExitCode}). Its own output "
                    + "is above.");
            }

            try
            {
                using var response = await client.GetAsync(probe, cancellationToken).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
                // Not listening yet. The loop's own deadline decides when that stops being acceptable.
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
        }

        throw new InstallationException(
            $"The server did not answer at {Address} within {ReadinessLimit.TotalSeconds:F0} seconds.");
    }

    /// <summary>
    /// Waits for the owned process to exit, or for the wait to be abandoned.
    /// </summary>
    /// <param name="cancellationToken">Abandons the wait.</param>
    /// <returns>A task that completes when the process exits or the token is canceled.</returns>
    public async Task WaitForExitAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The caller asked to stop waiting, not to stop the server. Stopping it is the next step and
            // its own decision.
        }
    }

    /// <summary>
    /// Stops the owned process within a bounded effort.
    /// </summary>
    /// <param name="report">Where each step is reported.</param>
    /// <returns><see langword="true"/> when the process is gone.</returns>
    public bool Stop(Action<string> report)
    {
        ArgumentNullException.ThrowIfNull(report);

        if (!IsRunning)
        {
            return true;
        }

        report($"Stopping the server this run started (process {Id}).");

        if (RequestStop() && _process.WaitForExit((int)GracefulLimit.TotalMilliseconds))
        {
            return true;
        }

        if (!IsRunning)
        {
            return true;
        }

        report($"Process {Id} did not stop when asked; ending it.");

        try
        {
            _process.Kill(entireProcessTree: false);
        }
        catch (InvalidOperationException)
        {
            return true;
        }

        return _process.WaitForExit((int)ForcedLimit.TotalMilliseconds) || !IsRunning;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _process.Dispose();
    }

    [System.Runtime.InteropServices.LibraryImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static partial int Signal(int pid, int signal);

    /// <summary>
    /// Asks the process to stop, in whatever way this platform can ask politely.
    /// </summary>
    /// <returns><see langword="false"/> when there is no polite way to ask and force is the only step.</returns>
    private bool RequestStop()
    {
        if (OperatingSystem.IsWindows())
        {
            // Windows has no equivalent of a termination signal for a console process that is not sharing
            // this console, so the bounded forced stop below is the whole mechanism there. Said rather than
            // hidden: this run does not claim a graceful shutdown it cannot perform.
            return false;
        }

        try
        {
            return Signal(_process.Id, SigTerm) == 0;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
    }
}
