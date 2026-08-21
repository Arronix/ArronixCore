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
}
