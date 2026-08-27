namespace Arronix.Compatibility.Ratchet.Tests;

[TestFixture]
public class RatchetApplicationTests
{
    [Test]
    public void HelpDocumentsTheCanonicalDirectoryContract()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = RatchetApplication.Run(["--help"], output, error);

        Assert.Multiple(() =>
        {
            Assert.That(exitCode, Is.Zero);
            Assert.That(output.ToString(), Does.Contain("--ledger <directory>"));
            Assert.That(output.ToString(), Does.Contain("--results <file-or-directory>"));
            Assert.That(output.ToString(), Does.Contain("--required-tests <registry.tsv>"));
            Assert.That(output.ToString(), Does.Contain("--compile-inputs <directory>"));
            Assert.That(output.ToString(), Does.Contain("--classification-report <file>"));
            Assert.That(output.ToString(), Does.Not.Contain("--matrix"));
            Assert.That(error.ToString(), Is.Empty);
        });
    }

    [Test]
    public void MissingRequiredOptionsIsAUsageFailure()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = RatchetApplication.Run(["validate", "--ledger", "ledger"], output, error);

        Assert.Multiple(() =>
        {
            Assert.That(exitCode, Is.EqualTo(2));
            Assert.That(error.ToString(), Does.Contain("requires --ledger"));
        });
    }

    [Test]
    public void ARepeatedSingletonOptionIsAUsageFailure()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = RatchetApplication.Run(
            ["validate", "--ledger", "one", "--ledger", "two", "--results", "results.xml"],
            output,
            error);

        Assert.Multiple(() =>
        {
            Assert.That(exitCode, Is.EqualTo(2));
            Assert.That(error.ToString(), Does.Contain("only once"));
        });
    }

    [Test]
    public void ARepeatedClassificationReportOptionIsAUsageFailure()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = RatchetApplication.Run(
            [
                "validate",
                "--ledger", "ledger",
                "--results", "results.xml",
                "--required-tests", "required.tsv",
                "--compile-inputs", "compile-inputs",
                "--classification-report", "first.json",
                "--classification-report", "second.json"
            ],
            output,
            error);

        Assert.Multiple(() =>
        {
            Assert.That(exitCode, Is.EqualTo(2));
            Assert.That(error.ToString(), Does.Contain("only once"));
        });
    }

    [Test]
    public void MissingInputFilesAreAnInputFailure()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = RatchetApplication.Run(
            [
                "validate",
                "--ledger", "/definitely/missing",
                "--results", "/also/missing",
                "--required-tests", "/required/missing.tsv",
                "--compile-inputs", "/compile-inputs/missing"
            ],
            output,
            error);

        Assert.Multiple(() =>
        {
            Assert.That(exitCode, Is.EqualTo(2));
            Assert.That(error.ToString(), Does.StartWith("input error:"));
        });
    }

    [Test]
    public void InputFailureRemovesTheRequestedStaleClassificationReport()
    {
        var directory = Path.Combine(Path.GetTempPath(), "arronix-ratchet-stale-report-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var report = Path.Combine(directory, "classification-report.json");
            File.WriteAllText(report, "stale successful report");
            using var output = new StringWriter();
            using var error = new StringWriter();

            var exitCode = RatchetApplication.Run(
                [
                    "validate",
                    "--ledger", "/definitely/missing",
                    "--results", "/also/missing",
                    "--required-tests", "/required/missing.tsv",
                    "--compile-inputs", "/compile-inputs/missing",
                    "--classification-report", report
                ],
                output,
                error);

            Assert.Multiple(() =>
            {
                Assert.That(exitCode, Is.EqualTo(2));
                Assert.That(File.Exists(report), Is.False);
                Assert.That(error.ToString(), Does.StartWith("input error:"));
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
