using System.Diagnostics;
using System.IO;
using System.Linq;
using FluentAssertions;
using FluentAssertions.Execution;

namespace Arronix.Host.Tests.Runtime;

/// <summary>
/// A stale assembly in a project's own build output cannot reach a staged package payload.
/// </summary>
/// <remarks>
/// <para>
/// This is the mechanism, not the detector. <see cref="StagedPayloadDetectorTests"/> proves the payload rule
/// notices a stale file planted into an already-staged folder, which is a statement about the rule. It says
/// nothing about staging, and the two can disagree: MSBuild does not delete an assembly that a removed
/// <c>ProjectReference</c> stopped producing, so a project's <c>bin</c> can keep a file the project no
/// longer depends on. A recursive copy would carry it, clearing the destination would not help because the
/// stale file is in the source, and nothing would fail.
/// </para>
/// <para>
/// So this plants a controlled stale assembly in the real source build-output directory, invokes the real
/// publish the staging targets use, and asserts the payload does not contain it. Publish computes the
/// runtime closure from the current reference set rather than listing a directory, which is why it cannot.
/// The planted file is removed in a finally block, so a failure here does not leave the working tree dirty
/// for the next run.
/// </para>
/// </remarks>
[TestFixture]
internal sealed class StagingExcludesStaleBuildOutputTests
{
    private const string StaleAssembly = "Arronix.Stale.Planted.dll";

    [Test]
    public void APlantedStaleAssemblyInTheSourceBuildOutputCannotEnterAStagedPayload()
    {
        var project = Path.Combine(RepositoryRoot(), "src", "Arronix.Plugin.Movies");
        var output = Path.Combine(project, "bin", "Release", "net11.0");

        Directory.Exists(output).Should().BeTrue(
            "the movies project must have been built before its staging can be exercised");

        var planted = Path.Combine(output, StaleAssembly);
        var payload = Path.Combine(
            Path.GetTempPath(),
            $"arronix-staging-{Guid.NewGuid():N}",
            "movies");

        try
        {
            // A real managed assembly, so nothing can dismiss it as an unreadable file the payload rules
            // would have skipped anyway.
            File.Copy(
                Path.Combine(AppContext.BaseDirectory, "Arronix.Abstractions.dll"),
                planted,
                overwrite: true);

            File.Exists(planted).Should().BeTrue("the fault must exist before staging is asked to exclude it");

            Publish(Path.Combine(project, "Arronix.Plugin.Movies.csproj"), payload);

            var staged = Directory.EnumerateFiles(payload, "*.dll")
                .Select(Path.GetFileName)
                .ToArray();

            using var assertions = new AssertionScope();

            staged.Should().NotBeEmpty("publish must have produced a payload for the rule to mean anything");
            staged.Should().Contain(
                "Arronix.Plugin.Movies.dll",
                "the control: the payload really is the movies package");
            staged.Should().NotContain(
                StaleAssembly,
                "publish computes the runtime closure from the current reference set, so a file the project "
                + "does not depend on cannot enter the payload however it got into bin");
        }
        finally
        {
            if (File.Exists(planted))
            {
                File.Delete(planted);
            }

            var root = Path.GetDirectoryName(payload);

            if (root is not null && Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    /// <summary>Finds the repository root by walking up from the test output.</summary>
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Arronix.sln")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("this proof runs against the working tree it was built from");
        return directory!.FullName;
    }

    /// <summary>Runs the same publish the staging targets run, into a directory of this test's own.</summary>
    private static void Publish(string project, string payload)
    {
        var dotnet = Environment.GetEnvironmentVariable("DOTNET_COMMAND") is { Length: > 0 } configured
            ? configured
            : "dotnet";

        var start = new ProcessStartInfo(dotnet)
        {
            ArgumentList =
            {
                "publish",
                project,
                "--configuration",
                "Release",
                "--no-build",
                "--nologo",
                $"--output={payload}",
            },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = RepositoryRoot(),
        };

        // Reusable MSBuild nodes inherit redirected handles from this child on macOS. If they outlive the
        // publish process, ReadToEnd never observes EOF even though publishing finished. This proof owns a
        // single nested build, so its nodes must end with it.
        start.Environment["MSBUILDDISABLENODEREUSE"] = "1";

        using var process = Process.Start(start);

        process.Should().NotBeNull("the .NET command must be runnable for this proof to mean anything");

        var output = process!.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        process.ExitCode.Should().Be(0, $"publish must succeed: {output}\n{error}");
    }
}
