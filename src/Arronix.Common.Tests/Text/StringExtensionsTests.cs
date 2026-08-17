using System;
using Arronix.Common.Text;

namespace Arronix.Common.Tests.Text;

/// <summary>
/// Covers the surviving string helpers, and in particular the two places where the behavior was corrected
/// on the way over: suffix trimming is ordinal rather than culture-sensitive, and slugging folds accents by
/// canonical decomposition rather than through a globally registered table.
/// </summary>
[TestFixture]
public class StringExtensionsTests
{
    [TestCase("Hello", "hello")]
    [TestCase("hello", "hello")]
    [TestCase("H", "h")]
    [TestCase("", "")]
    public void FirstCharToLower_LowercasesOnlyTheFirstCharacter(string input, string expected)
    {
        Assert.That(input.FirstCharToLower(), Is.EqualTo(expected));
    }

    [TestCase("hello", "Hello")]
    [TestCase("Hello", "Hello")]
    [TestCase("h", "H")]
    [TestCase("", "")]
    public void FirstCharToUpper_UppercasesOnlyTheFirstCharacter(string input, string expected)
    {
        Assert.That(input.FirstCharToUpper(), Is.EqualTo(expected));
    }

    [Test]
    public void FirstCharToLower_RejectsAMissingValue()
    {
        Assert.That(() => ((string)null!).FirstCharToLower(), Throws.ArgumentNullException);
    }

    [TestCase("HttpRequestTimeout", "Http Request Timeout")]
    [TestCase("Simple", "Simple")]
    [TestCase("alreadyLower", "already Lower")]
    [TestCase("", "")]
    public void SplitCamelCase_SpacesInteriorCapitalsOnly(string input, string expected)
    {
        Assert.That(input.SplitCamelCase(), Is.EqualTo(expected));
    }

    [TestCase("archive.tar.gz", ".gz", "archive.tar")]
    [TestCase("archive.tar", ".gz", "archive.tar")]
    [TestCase("archive", "", "archive")]
    public void TrimSuffix_RemovesTheSuffixWhenPresent(string input, string suffix, string expected)
    {
        Assert.That(input.TrimSuffix(suffix), Is.EqualTo(expected));
    }

    [Test]
    public void TrimSuffix_ComparesOrdinallyByDefault()
    {
        // The member this replaces compared with the invariant culture, so it removed a suffix that differed
        // only in case from paths and protocol tokens that meant an exact match.
        Assert.That("archive.GZ".TrimSuffix(".gz"), Is.EqualTo("archive.GZ"));
    }

    [Test]
    public void TrimSuffix_HonorsAnExplicitComparison()
    {
        Assert.That(
            "archive.GZ".TrimSuffix(".gz", StringComparison.OrdinalIgnoreCase),
            Is.EqualTo("archive"));
    }

    [TestCase("no-spaces", "no-spaces")]
    [TestCase("has a space", "\"has a space\"")]
    [TestCase("", "")]
    public void WrapInQuotes_QuotesOnlyWhenASpaceIsPresent(string input, string expected)
    {
        Assert.That(input.WrapInQuotes(), Is.EqualTo(expected));
    }

    [TestCase("Hello World", "hello-world")]
    [TestCase("  leading and trailing  ", "leading-and-trailing")]
    [TestCase("Already-A-Slug", "already-a-slug")]
    [TestCase("keeps_underscores", "keeps_underscores")]
    [TestCase("collapses---separators", "collapses-separators")]
    [TestCase("drops!punctuation?", "dropspunctuation")]
    [TestCase("", "")]
    [TestCase("!!!", "")]
    public void ToUrlSlug_ProducesALowercaseSeparatedToken(string input, string expected)
    {
        Assert.That(input.ToUrlSlug(), Is.EqualTo(expected));
    }

    [Test]
    public void ToUrlSlug_FoldsAccentsOntoTheirBaseLetters()
    {
        // Without the folding step the accented letters would be stripped rather than mapped, leaving
        // "crme-brle" — an identifier that no longer resembles what it names.
        Assert.That("Crème Brûlée".ToUrlSlug(), Is.EqualTo("creme-brulee"));
    }

    [Test]
    public void ToUrlSlug_SurvivesTextThatCannotBeNormalized()
    {
        // A lone high surrogate is not valid Unicode and cannot be decomposed. A slug is never worth
        // failing an operation over, so the accent-folding step is skipped rather than throwing.
        var malformed = "ab\ud800cd";

        Assert.That(malformed.ToUrlSlug(), Is.EqualTo("abcd"));
    }
}
