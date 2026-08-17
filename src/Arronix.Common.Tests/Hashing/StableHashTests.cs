using System;
using System.IO.Hashing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Arronix.Common.Hashing;

namespace Arronix.Common.Tests.Hashing;

/// <summary>
/// Covers the stable digest: that it is a function of the text alone, that it is the same everywhere, and
/// that it is safe to take from many threads at once.
/// </summary>
[TestFixture]
public class StableHashTests
{
    private const string AccentedText = "Bjørn Café Þórsson";

    [Test]
    public void Compute_EncodesTextAsUtf8()
    {
        var expected = XxHash3.HashToUInt64(Encoding.UTF8.GetBytes(AccentedText));

        Assert.That(StableHash.Compute(AccentedText), Is.EqualTo(expected));
    }

    /// <summary>
    /// The digest this replaces encoded with the ambient encoding, which makes it a function of the
    /// machine's code page: the same string hashed on two installations produced two different values, and
    /// anything persisted from it stopped matching. Hashing a non-ASCII string through a single-byte
    /// encoding must not agree with hashing it through UTF-8.
    /// </summary>
    [Test]
    public void Compute_DoesNotDependOnASingleByteCodePage()
    {
        var singleByte = XxHash3.HashToUInt64(Encoding.Latin1.GetBytes(AccentedText));

        Assert.That(StableHash.Compute(AccentedText), Is.Not.EqualTo(singleByte));
    }

    [Test]
    public void Compute_AgreesBetweenTheStringAndSpanOverloads()
    {
        Assert.That(StableHash.Compute(AccentedText), Is.EqualTo(StableHash.Compute(AccentedText.AsSpan())));
    }

    [Test]
    public void Compute_HandlesTextLongerThanTheStackBuffer()
    {
        var long1 = new string('é', 4096);
        var expected = XxHash3.HashToUInt64(Encoding.UTF8.GetBytes(long1));

        Assert.That(StableHash.Compute(long1), Is.EqualTo(expected));
    }

    [Test]
    public void Compute_HandlesTextExactlyAtTheStackBufferBoundary()
    {
        Assert.Multiple(() =>
        {
            for (var length = 80; length <= 90; length++)
            {
                var text = new string('é', length);

                Assert.That(
                    StableHash.Compute(text),
                    Is.EqualTo(XxHash3.HashToUInt64(Encoding.UTF8.GetBytes(text))),
                    $"length {length}");
            }
        });
    }

    [Test]
    public void Compute_DoesNotThrowOnAnUnpairedSurrogate()
    {
        Assert.That(() => StableHash.Compute("broken \ud800 title"), Throws.Nothing);
    }

    [Test]
    public void Compute_RejectsANullString()
    {
        Assert.That(() => StableHash.Compute((string)null!), Throws.TypeOf<ArgumentNullException>());
    }

    [Test]
    public void Compute_IsEmptyStringSafe()
    {
        Assert.That(StableHash.Compute(string.Empty), Is.EqualTo(XxHash3.HashToUInt64([])));
    }

    /// <summary>
    /// The implementation this replaces held one mutable algorithm instance behind a lock, so concurrent
    /// callers were serialized and a missing lock would have corrupted the result. A pure static function
    /// has neither problem; hashing the same value from many threads must give one answer.
    /// </summary>
    [Test]
    public void Compute_IsSafeAndConsistentUnderConcurrency()
    {
        var expected = StableHash.Compute(AccentedText);

        var results = new ulong[256];

        Parallel.For(0, results.Length, index => results[index] = StableHash.Compute(AccentedText));

        Assert.That(results, Is.All.EqualTo(expected));
    }

    [Test]
    public void ComputeNonNegativeInt32_NeverReturnsANegativeValue()
    {
        var values = Enumerable
            .Range(0, 5000)
            .Select(index => StableHash.ComputeNonNegativeInt32($"partition-{index}"))
            .ToArray();

        Assert.That(values, Is.All.GreaterThanOrEqualTo(0));
    }

    [Test]
    public void ComputeNonNegativeInt32_IsStableForTheSameInput()
    {
        Assert.That(
            StableHash.ComputeNonNegativeInt32(AccentedText),
            Is.EqualTo(StableHash.ComputeNonNegativeInt32(AccentedText)));
    }

    [Test]
    public void ComputeToken_IsSixteenLowercaseHexadecimalCharacters()
    {
        var token = StableHash.ComputeToken(AccentedText);

        Assert.Multiple(() =>
        {
            Assert.That(token, Has.Length.EqualTo(16));
            Assert.That(token, Does.Match("^[0-9a-f]{16}$"));
        });
    }

    [Test]
    public void ComputeToken_PadsASmallDigestToTheFullWidth()
    {
        // A digest whose leading bytes are zero must still render at the full width, or two tokens of
        // different lengths would compare unequal as strings while naming the same digest.
        var tokens = Enumerable
            .Range(0, 2000)
            .Select(index => StableHash.ComputeToken(index.ToString(System.Globalization.CultureInfo.InvariantCulture)))
            .ToArray();

        Assert.That(tokens, Has.All.Length.EqualTo(16));
    }

    [Test]
    public void ComputeToken_DistinguishesTextThatDiffersOnlyByAccent()
    {
        Assert.That(StableHash.ComputeToken("resume"), Is.Not.EqualTo(StableHash.ComputeToken("résumé")));
    }
}
