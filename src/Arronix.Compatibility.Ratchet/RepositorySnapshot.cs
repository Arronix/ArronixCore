using System.Diagnostics;
using System.Security.Cryptography;

namespace Arronix.Compatibility.Ratchet;

/// <summary>Content digests for the compatibility test sources in one repository checkout.</summary>
public sealed record RepositorySnapshot(
    IReadOnlyDictionary<string, string> FileDigests,
    IReadOnlyDictionary<string, bool>? SourceIdentityMatches = null,
    IReadOnlyDictionary<string, CompiledTestSourceVerification>? CompiledSourceVerifications = null)
{
    public static RepositorySnapshot Capture(
        string repositoryRoot,
        CompatibilityLedger ledger,
        NUnitTestRun? run = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentNullException.ThrowIfNull(ledger);

        var root = Path.GetFullPath(repositoryRoot);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"The repository root '{repositoryRoot}' does not exist.");
        }

        var rootPrefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        var digests = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var relativePath in ledger.Cases
                     .SelectMany(static value => EnumerateSourceFiles(value.Binding))
                     .Distinct(StringComparer.Ordinal))
        {
            var platformPath = relativePath.Replace('/', Path.DirectorySeparatorChar);
            var fullPath = Path.GetFullPath(Path.Combine(root, platformPath));
            if (!fullPath.StartsWith(rootPrefix, StringComparison.Ordinal))
            {
                throw new CompatibilityDocumentException(
                    $"Compatibility source path '{relativePath}' escapes the repository root.");
            }

            if (!File.Exists(fullPath))
            {
                continue;
            }

            digests.Add(relativePath, HashBytes(File.ReadAllBytes(fullPath)));
        }

        var sourceIdentities = ledger.Sources.ToDictionary(
            static source => source.SourceId,
            source => SourceIdentityMatchesRepository(root, rootPrefix, source),
            StringComparer.Ordinal);
        var compiledSources = run is null
            ? null
            : CompiledTestRunVerifier.VerifyCases(root, ledger, run);
        return new RepositorySnapshot(digests, sourceIdentities, compiledSources);
    }

    private static IEnumerable<string> EnumerateSourceFiles(CaseBinding binding)
    {
        yield return binding.SourceFile;
        foreach (var supportDocument in binding.SupportDocuments)
        {
            yield return supportDocument.SourceFile;
        }
    }

    internal static string HashBytes(ReadOnlySpan<byte> value)
        => "sha256:" + Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();

    private static bool SourceIdentityMatchesRepository(
        string root,
        string rootPrefix,
        CompatibilitySource source)
    {
        if (source.Locator is null || source.Revision is null)
        {
            return false;
        }

        var platformPath = source.Locator.Replace('/', Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(root, platformPath));
        if (!fullPath.StartsWith(rootPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        return source.Revision.Kind switch
        {
            RevisionKind.ArtifactSha256 => File.Exists(fullPath)
                && string.Equals(
                    HashBytes(File.ReadAllBytes(fullPath))[7..],
                    source.Revision.Value,
                    StringComparison.Ordinal),
            RevisionKind.RepositoryCommit => GitContainsPath(root, source.Revision.Value, source.Locator),
            _ => false
        };
    }

    private static bool GitContainsPath(string root, string commit, string relativePath)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = root,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("cat-file");
        startInfo.ArgumentList.Add("-e");
        startInfo.ArgumentList.Add($"{commit}:{relativePath}");

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return false;
            }

            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }
}
