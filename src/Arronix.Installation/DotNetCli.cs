using System.Diagnostics;
using System.IO;

namespace Arronix.Installation;

/// <summary>
/// The .NET SDK, as this tool uses it.
/// </summary>
/// <remarks>
/// Building is MSBuild's job and stays MSBuild's job. What this tool owns is which projects are
/// deliverables and where their published output belongs; it does not reimplement a build, and it does not
/// read anyone's <c>bin</c> directory. A payload that was copied out of a build directory is not the
/// payload a publish computes, and installing the second one is the whole difference between a staged
/// closure and a listing of whatever MSBuild happened to leave behind.
/// </remarks>
internal sealed class DotNetCli(string command) : IDotNetCli
{
    /// <summary>Gets the command used to invoke the SDK.</summary>
    public string Command { get; } = command;

    /// <summary>
    /// Resolves the SDK command to use, honoring the same environment variable the proof rail reads.
    /// </summary>
    /// <returns>The command.</returns>
    public static DotNetCli Resolve()
        => new(Environment.GetEnvironmentVariable("DOTNET_COMMAND") is { Length: > 0 } configured
            ? configured
            : "dotnet");

    /// <summary>Reports the SDK version in use, for the installation manifest.</summary>
    /// <param name="workingDirectory">The directory the version is resolved from, so global.json applies.</param>
    /// <returns>The version text, or a stated absence.</returns>
    public string Version(string workingDirectory)
    {
        var start = Start(workingDirectory, ["--version"]);
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;

        using var process = Process.Start(start)
            ?? throw new InstallationException($"The .NET command '{Command}' could not be started.");

        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        return process.ExitCode == 0 && output.Trim() is { Length: > 0 } version
            ? version
            : "unknown";
    }

    /// <summary>
    /// Publishes one project into a directory, replacing whatever was there.
    /// </summary>
    /// <param name="projectFile">The project to publish.</param>
    /// <param name="destination">The directory to publish into.</param>
    /// <param name="workingDirectory">The directory the SDK is invoked from.</param>
    /// <exception cref="InstallationException">The publish failed.</exception>
    public void Publish(string projectFile, string destination, string workingDirectory)
    {
        if (!File.Exists(projectFile))
        {
            throw new InstallationException($"There is no project at '{projectFile}'.");
        }

        // Cleared first, deliberately. A publish computes a runtime closure; it does not remove a file an
        // earlier publish left behind, so publishing over an existing payload can install an assembly no
        // current project produces.
        if (Directory.Exists(destination))
        {
            Directory.Delete(destination, recursive: true);
        }

        Directory.CreateDirectory(destination);

        Run(
            workingDirectory,
            ["publish", projectFile, "--configuration", "Release", "--output", destination],
            $"publishing '{Path.GetFileNameWithoutExtension(projectFile)}'");
    }

    private void Run(string workingDirectory, IReadOnlyList<string> arguments, string what)
    {
        var start = Start(workingDirectory, arguments);

        using var process = Process.Start(start)
            ?? throw new InstallationException($"The .NET command '{Command}' could not be started.");

        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InstallationException(
                $"The SDK reported failure while {what} (exit code {process.ExitCode}). "
                + "Its output is above.");
        }
    }

    private ProcessStartInfo Start(string workingDirectory, IReadOnlyList<string> arguments)
    {
        var start = new ProcessStartInfo(Command)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
        };

        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        return start;
    }
}
