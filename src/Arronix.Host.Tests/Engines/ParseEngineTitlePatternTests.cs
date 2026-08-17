using Arronix.Abstractions.Definition;
using Arronix.Abstractions.DTOs;
using Arronix.Host.Engines.Parsing;
using FluentAssertions;

// The shape (ARX0013) and definition (ARX0019) contracts are experimental.
#pragma warning disable ARX0013
#pragma warning disable ARX0019

namespace Arronix.Host.Tests.Engines;

/// <summary>
/// The declared pattern list: ordered claiming, provenance gating, guard gating, capture projection and
/// range expansion — the per-kind residue the engine executes byte-for-byte as declared.
/// </summary>
[TestFixture]
internal sealed class ParseEngineTitlePatternTests
{
    private static DeclarativeReleaseParser Parser() => ParseEngineFixtures.Parser();

    private static ParsedRelease Parsed(string title)
    {
        var parsed = Parser().Parse(title);

        parsed.Should().NotBeNull(because: $"'{title}' is a parseable release title");
        return parsed!;
    }

    [Test]
    public void ReadsTitleAndYear()
    {
        var parsed = Parsed("Some.Film.2019.1080p.BluRay.x264-SPARKS");

        parsed.Title.Should().Be("Some Film");
        parsed.Year.Should().Be("2019");
        parsed.AdditionalMetadata.Should().ContainKey(DeclarativeParseFields.PatternId)
            .WhoseValue.Should().Be("title-then-year");
    }

    /// <summary>A resolution is never a year: the lookahead rejects the 1080 of 1080p.</summary>
    [Test]
    public void AResolutionIsNotAYear()
    {
        var parsed = Parsed("Nineteen.Eighty.Four.1984.1080p.BluRay");

        parsed.Year.Should().Be("1984");
    }

    [Test]
    public void AnUnparseableTitleIsDeclinedNotGuessed()
    {
        Parser().Parse("no year no shape here").Should().BeNull();
        Parser().CanParse("no year no shape here").Should().BeFalse();
        Parser().Parse("   ").Should().BeNull();
    }

    /// <summary>
    /// Parse(string) reads release names, so a pattern restricted to folder names may never fire —
    /// the surveyed folder-only convention carried as data.
    /// </summary>
    [Test]
    public void AFolderOnlyPatternNeverFiresOnAReleaseName() =>
        Parser().Parse("2001 - A Spaced Odyssey").Should().BeNull();

    [Test]
    public void ProjectsTheReleaseGroupAndLanguages()
    {
        var parsed = Parsed("Some.Film.2019.FRENCH.1080p.BluRay.x264-SPARKS");

        parsed.ReleaseGroup.Should().Be("SPARKS");
        parsed.Languages.Should().NotBeNull();
        parsed.Languages.Should().Contain(new Language("fr", "French"));
    }

    /// <summary>
    /// The language scan runs with the work's own title masked out: a title containing the name of a
    /// language is not a language claim.
    /// </summary>
    [Test]
    public void ATitleNamingALanguageIsNotALanguageClaim()
    {
        var parsed = Parsed("The.French.Connection.1971.1080p.BluRay.x264");

        parsed.Languages.Should().BeNull();
    }

    [Test]
    public void TokenTablesWriteTagsAndMetadata()
    {
        var parsed = Parsed("Some.Film.2019.1080p.BluRay.tt1234567");

        parsed.AdditionalMetadata.Should()
            .ContainKey(DeclarativeParseFields.TagPrefix + "imdbId")
            .WhoseValue.Should().Be("tt1234567");
    }

    /// <summary>A token row's constraint refuses a capture outside its declared vocabulary.</summary>
    [Test]
    public void ATokenConstraintRefusesANonConformingCapture()
    {
        var parsing = new ParseDeclaration
        {
            TitlePatterns = ParseEngineFixtures.Parsing().TitlePatterns,
            Guards = ParseEngineFixtures.Parsing().Guards,
            TokenTables =
            [
                new TokenTable
                {
                    TableId = "serials",
                    Rows = [new TokenRow(@"ser-(?<serial>\w{1,8})", "serial", Constraint: "numeric")],
                },
            ],
            RungResolution = ParseEngineFixtures.RungTable(),
        };

        var parser = ParseEngineFixtures.Parser(parsing);

        parser.Parse("Some.Film.2019.ser-12345.1080p")!
            .AdditionalMetadata.Should().ContainKey(DeclarativeParseFields.TagPrefix + "serial")
            .WhoseValue.Should().Be("12345");

        parser.Parse("Some.Film.2019.ser-abcde.1080p")!
            .AdditionalMetadata.Should().NotContainKey(DeclarativeParseFields.TagPrefix + "serial");
    }

    [Test]
    public void ARangePatternExpandsItsRun()
    {
        var parsed = Parsed("Saga #01-03 720p WEB-DL");

        parsed.Title.Should().Be("Saga");
        parsed.AdditionalMetadata.Should().ContainKey(DeclarativeParseFields.RangeFrom)
            .WhoseValue.Should().Be("01");
        parsed.AdditionalMetadata.Should().ContainKey(DeclarativeParseFields.RangeTo)
            .WhoseValue.Should().Be("03");
        parsed.AdditionalMetadata[DeclarativeParseFields.RangeIsSpan].Should().Be("false");
    }

    /// <summary>An absurd range is a mis-parse, not a very large release: the pattern declines.</summary>
    [Test]
    public void ARangeBeyondTheDeclaredCapDeclines() =>
        Parser().Parse("Saga #1-99 720p WEB-DL").Should().BeNull();

    [Test]
    public void WritesTheRevisionTripleAlways()
    {
        var parsed = Parsed("Some.Film.2019.PROPER.1080p.BluRay.x264");

        parsed.AdditionalMetadata![DeclarativeParseFields.RevisionVersion].Should().Be("2");
        parsed.AdditionalMetadata[DeclarativeParseFields.RevisionReal].Should().Be("0");
        parsed.AdditionalMetadata[DeclarativeParseFields.RevisionIsRepack].Should().Be("false");
    }

    /// <summary>
    /// Guard references gate a pattern's claim: the same expression, negated, splits one shape into two
    /// declared conventions.
    /// </summary>
    [Test]
    public void APatternGuardGatesTheClaim()
    {
        var parsing = new ParseDeclaration
        {
            Guards = [.. ParseEngineFixtures.Parsing().Guards, new GuardPattern("marker", @"\bMARKED\b")],
            TitlePatterns =
            [
                new TitlePattern
                {
                    PatternId = "marked",
                    Regex = @"^(?<title>.+?)[ ._](?<year>(19|20)\d{2})\b",
                    Captures =
                    [
                        new CaptureBinding("title", CaptureTarget.TitleText),
                        new CaptureBinding("year", CaptureTarget.TitleYear),
                    ],
                    Guards = [new GuardRef("marker")],
                },
                new TitlePattern
                {
                    PatternId = "unmarked",
                    Regex = @"^(?<title>.+?)[ ._](?<year>(19|20)\d{2})\b",
                    Captures =
                    [
                        new CaptureBinding("title", CaptureTarget.TitleText),
                        new CaptureBinding("year", CaptureTarget.TitleYear),
                    ],
                    Guards = [new GuardRef("marker", Negated: true)],
                },
            ],
            RungResolution = ParseEngineFixtures.RungTable(),
        };

        var parser = ParseEngineFixtures.Parser(parsing);

        parser.Parse("Some.Film.2019.MARKED.1080p")!
            .AdditionalMetadata![DeclarativeParseFields.PatternId].Should().Be("marked");
        parser.Parse("Some.Film.2019.1080p")!
            .AdditionalMetadata![DeclarativeParseFields.PatternId].Should().Be("unmarked");
    }

    /// <summary>Declared order is the algorithm: the first claiming pattern wins, not the best.</summary>
    [Test]
    public void TheFirstClaimingPatternWins()
    {
        var parsed = Parsed("Saga #02-04 2019 720p WEB-DL");

        // Both the range pattern and title-then-year could read this; the range pattern is declared
        // first and therefore owns it.
        parsed.AdditionalMetadata![DeclarativeParseFields.PatternId].Should().Be("numbered-run");
    }
}
