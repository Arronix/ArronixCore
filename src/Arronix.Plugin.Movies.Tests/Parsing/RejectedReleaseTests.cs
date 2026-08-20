using System.Linq;
using Arronix.Plugin.Movies.Tests.Support;

namespace Arronix.Plugin.Movies.Tests.Parsing;

/// <summary>
/// Names that must not parse, ported from Radarr's <c>ParserTests/CrapParserFixture</c> and the rejection
/// half of <c>HashedReleaseFixture</c>.
/// </summary>
/// <remarks>
/// <para>
/// A usenet or torrent feed carries a large amount of text that is not a release name at all: obfuscated
/// file names, bare hashes, password lines. Nothing in this fixture is movie-specific — a bare digest is
/// not a television episode either — which was always the finding, and the conversion acts on it: junk
/// rejection is the host's kind-agnostic parse layer now and this extension carries none of it.
/// </para>
/// <para>
/// The corpus runs again. The layer is still host code, but it is reached the way any consumer reaches it:
/// through the <c>IReleaseParser</c> the host builds from this declaration, bound by the public binder. A
/// rejection is a null reading, which is what every caller downstream sees.
/// </para>
/// </remarks>
[TestFixture]
public class RejectedReleaseTests
{
    [TestCase("76El6LcgLzqb426WoVFg1vVVVGx4uCYopQkfjmLe")]
    [TestCase("Vrq6e1Aba3U amCjuEgV5R2QvdsLEGYF3YQAQkw8")]
    [TestCase("TDAsqTea7k4o6iofVx3MQGuDK116FSjPobMuh8oB")]
    [TestCase("yp4nFodAAzoeoRc467HRh1mzuT17qeekmuJ3zFnL")]
    [TestCase("oxXo8S2272KE1 lfppvxo3iwEJBrBmhlQVK1gqGc")]
    [TestCase("dPBAtu681Ycy3A4NpJDH6kNVQooLxqtnsW1Umfiv")]
    [TestCase("password - \"bdc435cb-93c4-4902-97ea-ca00568c3887.337\" yEnc")]
    [TestCase("185d86a343e39f3341e35c4dad3f9959")]
    [TestCase("ba27283b17c00d01193eacc02a8ba98eeb523a76")]
    [TestCase("45a55debe3856da318cc35882ad07e43cd32fd15")]
    [TestCase("86420f8ee425340d8894bf3bc636b66404b95f18")]
    [TestCase("ce39afb7da6cf7c04eba3090f0a309f609883862")]
    [TestCase("THIS SHOULD NEVER PARSE")]
    [TestCase("Vh1FvU3bJXw6zs8EEUX4bMo5vbbMdHghxHirc.mkv")]
    [TestCase("0e895c37245186812cb08aab1529cf8ee389dd05.mkv")]
    [TestCase("08bbc153931ce3ca5fcafe1b92d3297285feb061.mkv")]
    [TestCase("185d86a343e39f3341e35c4dad3ff159")]
    [TestCase("ah63jka93jf0jh26ahjas961.mkv")]
    [TestCase("qrdSD3rYzWb7cPdVIGSn4E7")]
    [TestCase("QZC4HDl7ncmzyUj9amucWe1ddKU1oFMZDd8r0dEDUsTd")]
    [TestCase("abc.xyz.af6021c37f7852")]
    [TestCase("thebiggestmovie1618finale")]
    public void RefusesTextThatIsNotAReleaseName(string releaseTitle)
        => Assert.That(MoviesEngines.Parse(releaseTitle), Is.Null, releaseTitle);

    /// <summary>
    /// An obfuscation suffix names no group, and the text before it does. This is the recovery half of the
    /// same rule the rejection half above states.
    /// </summary>
    [Test]
    [Ignore("The assertion was not preserved. The conversion replaced every body in this fixture with a placeholder, and unlike its siblings this case's [TestCase] rows went with it, so there is no corpus left to state what 'only when the file name follows it' meant. Restoring it would be writing a new test against a rule nobody wrote down, and a green invented assertion is worth less than a visible gap. The recovery behavior it covered is exercised by ReleaseGroupParserTests.StripsAnObfuscationSuffixBeforeReadingTheGroup, which did keep its rows.")]
    public void RecoversAnObfuscationNamedGroupOnlyWhenTheFileNameFollowsIt()
        => Assert.Fail("Unreachable: see the ignore reason.");

    /// <summary>
    /// Junk is refused by the kind-agnostic layer that runs before the declared patterns, so a rejected
    /// title never reaches them — observable here as a rejection that names no pattern.
    /// </summary>
    [Test]
    public void RejectsBeforeParsingRatherThanAfter()
        => Assert.Multiple(() =>
        {
            Assert.That(MoviesEngines.Parse("ce39afb7da6cf7c04eba3090f0a309f609883862"), Is.Null);
            Assert.That(MoviesEngines.Parser.CanParse("ce39afb7da6cf7c04eba3090f0a309f609883862"), Is.False);

            // Deliberately not asserted: that a digest followed by release-shaped text is also refused. It
            // is not, and it should not be — "<digest>.2018.1080p.BluRay.x264-RlsGrp" reads as a film with
            // an ugly title, which is the right answer for a feed that names files that way.
        });

    [Test]
    public void RefusesNullAndBlankText()
        => Assert.Multiple(() =>
        {
            Assert.That(MoviesEngines.Parse(string.Empty), Is.Null);
            Assert.That(MoviesEngines.Parse("   "), Is.Null);
            Assert.That(MoviesEngines.Parse("\t\n"), Is.Null);
            Assert.That(MoviesEngines.Parser.CanParse(string.Empty), Is.False);
        });

    /// <summary>
    /// A refusal is a null reading, never an exception. A feed carries hostile text and a parser that threw
    /// on it would take the poll down with it.
    /// </summary>
    [Test]
    public void ReportsTheRejectionAsANullParseRatherThanThrowing()
        => Assert.Multiple(() =>
        {
            Assert.That(() => MoviesEngines.Parse("THIS SHOULD NEVER PARSE"), Throws.Nothing);
            Assert.That(() => MoviesEngines.Parse(new string('a', 4000)), Throws.Nothing);
            Assert.That(MoviesEngines.Parse(new string('a', 4000)), Is.Null);
        });

    [TestCase(32)]
    [TestCase(40)]
    public void RefusesARandomAlphanumericRunOfHashLength(int length)
    {
        const string Alphabet = "abcdef0123456789";
        var random = new Random(length);
        var digest = new string(Enumerable
            .Range(0, length)
            .Select(_ => Alphabet[random.Next(Alphabet.Length)])
            .ToArray());

        Assert.That(MoviesEngines.Parse(digest), Is.Null, digest);
    }

    [Test]
    public void RefusesEveryDigestInAChain()
    {
        string[] chain =
        [
            "ba27283b17c00d01193eacc02a8ba98eeb523a76",
            "45a55debe3856da318cc35882ad07e43cd32fd15",
            "86420f8ee425340d8894bf3bc636b66404b95f18",
            "ce39afb7da6cf7c04eba3090f0a309f609883862",
        ];

        Assert.That(
            chain.Where(digest => MoviesEngines.Parse(digest) is not null),
            Is.Empty,
            "One digest recognized is one release name invented.");
    }

    [TestCase("0e895c37245186812cb08aab1529cf8ee389dd05.mkv")]
    [TestCase("abc.mkv")]
    [TestCase("b00bs.mkv")]
    [TestCase("123.mkv")]
    [TestCase("abc.xyz.af6021c37f7852.mkv")]
    public void RefusesTheObfuscatedFileNameSoTheFolderIsTried(string fileName)
        => Assert.That(MoviesEngines.Parse(fileName), Is.Null, fileName);

    [TestCase("Some.Hashed.Release.2018.720p.WEB-DL.AAC2.0.H.264-Mercury", "Some Hashed Release")]
    [TestCase("Movie.2018.DVDRip.XviD-RADARR", "Movie")]
    [TestCase("Movie.2018.1080p.BluRay.x264-RADARR", "Movie")]
    [TestCase("Movie.2018.1080p.BluRay.x264", "Movie")]
    [TestCase("Movie 2018 720p WEB-DL DD5 1 H 264-ECI", "Movie")]
    [TestCase("Movie.2018.1080p.WEB-DL.DD5.1.H264-RARBG", "Movie")]
    [TestCase("Movie.Title.2018.720p.HDTV.H.264", "Movie Title")]
    public void ReadsTheContainingFolderWhenTheFileNameIsObfuscated(
        string folderName,
        string title)
    {
        var parsed = MoviesEngines.Parse(folderName);

        Assert.Multiple(() =>
        {
            Assert.That(parsed, Is.Not.Null, folderName);
            Assert.That(parsed!.Title, Is.EqualTo(title));
        });
    }
}
