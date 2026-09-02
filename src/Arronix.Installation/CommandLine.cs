using System.Globalization;

namespace Arronix.Installation;

/// <summary>What this run was asked to do.</summary>
internal enum InstallationCommand
{
    /// <summary>Compose the installation, then run it.</summary>
    Run,

    /// <summary>Compose the installation and stop.</summary>
    Install,

    /// <summary>Empty what an installation accumulated.</summary>
    Reset,

    /// <summary>Explain the commands.</summary>
    Help,
}

/// <summary>
/// The arguments, parsed once.
/// </summary>
/// <param name="Command">What to do.</param>
/// <param name="Root">The installation directory, relative to the repository root unless rooted.</param>
/// <param name="Port">The port to bind, or <see langword="null"/> to choose one.</param>
/// <param name="Build">Whether deliverables are published before running.</param>
/// <param name="Samples">Whether sample packages are installed.</param>
/// <param name="Packages">The only packages to install, when the set was narrowed.</param>
/// <param name="OpenBrowser">Whether to open the address once the installation answers.</param>
/// <param name="ResetEverything">Whether a reset removes the whole installation rather than its state.</param>
internal sealed record CommandLine(
    InstallationCommand Command,
    string Root,
    int? Port,
    bool Build,
    bool Samples,
    IReadOnlyList<string> Packages,
    bool OpenBrowser,
    bool ResetEverything)
{
    /// <summary>The installation directory used when none is named. Git ignores it.</summary>
    public const string DefaultRoot = "artifacts/installation";

    /// <summary>
    /// Reads the arguments.
    /// </summary>
    /// <param name="arguments">The arguments as given.</param>
    /// <returns>The parsed run.</returns>
    /// <exception cref="InstallationException">An argument is unknown or malformed.</exception>
    public static CommandLine Parse(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var command = InstallationCommand.Run;
        var root = DefaultRoot;
        int? port = null;
        var build = true;
        var samples = true;
        var packages = new List<string>();
        var openBrowser = false;
        var resetEverything = false;
        var index = 0;

        if (arguments.Count > 0 && !arguments[0].StartsWith('-'))
        {
            command = arguments[0] switch
            {
                "run" => InstallationCommand.Run,
                "install" => InstallationCommand.Install,
                "reset" => InstallationCommand.Reset,
                "help" => InstallationCommand.Help,
                _ => throw new InstallationException(
                    $"There is no '{arguments[0]}' command. Try run, install, reset or help."),
            };

            index = 1;
        }

        for (; index < arguments.Count; index++)
        {
            switch (arguments[index])
            {
                case "--root":
                    root = Value(arguments, ref index);
                    break;
                case "--port":
                    port = ParsePort(Value(arguments, ref index));
                    break;
                case "--no-build":
                    build = false;
                    break;
                case "--no-sample-catalog":
                    samples = false;
                    break;
                case "--package":
                    packages.Add(Value(arguments, ref index));
                    break;
                case "--open":
                    openBrowser = true;
                    break;
                case "--all":
                    resetEverything = true;
                    break;
                case "--help" or "-h":
                    command = InstallationCommand.Help;
                    break;
                default:
                    throw new InstallationException(
                        $"'{arguments[index]}' is not an option this run understands. Try --help.");
            }
        }

        return new CommandLine(command, root, port, build, samples, packages, openBrowser, resetEverything);
    }

    private static string Value(IReadOnlyList<string> arguments, ref int index)
    {
        var option = arguments[index];

        return ++index < arguments.Count
            ? arguments[index]
            : throw new InstallationException($"'{option}' needs a value after it.");
    }

    private static int ParsePort(string text)
        => int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var port)
            && port is >= 1 and <= 65535
            ? port
            : throw new InstallationException($"'{text}' is not a port number between 1 and 65535.");
}
