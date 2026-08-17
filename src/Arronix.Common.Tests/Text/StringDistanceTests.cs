using System;
using Arronix.Common.Text;

namespace Arronix.Common.Tests.Text;

/// <summary>
/// Covers the edit-distance primitive, including the asymmetric weighting that domain matching policies
/// build on.
/// </summary>
[TestFixture]
public class StringDistanceTests
{
    [TestCase("", "", 0)]
    [TestCase("abc", "abc", 0)]
    [TestCase("abc", "abcd", 1)]
    [TestCase("abcd", "abc", 1)]
    [TestCase("abc", "abd", 1)]
    [TestCase("abc", "adc", 1)]
    [TestCase("abcdefgh", "abcghdef", 4)]
    [TestCase("a.b.c.", "abc", 3)]
    [TestCase("", "abc", 3)]
    [TestCase("abc", "", 3)]
    public void Levenshtein_CountsSingleCostEdits(string source, string target, int expected)
    {
        Assert.That(StringDistance.Levenshtein(source, target), Is.EqualTo(expected));
    }

    [TestCase("abc", "abcd", 1)]
    [TestCase("abcd", "abc", 3)]
    [TestCase("abc", "abd", 3)]
    [TestCase("abcdefgh", "abcghdef", 8)]
    public void Levenshtein_AppliesTheWeightsItIsGiven(string source, string target, int expected)
    {
        Assert.That(
            StringDistance.Levenshtein(source, target, insertionCost: 1, deletionCost: 3, substitutionCost: 3),
            Is.EqualTo(expected));
    }

    [Test]
    public void Levenshtein_IsCaseSensitive()
    {
        Assert.That(StringDistance.Levenshtein("ABC", "abc"), Is.EqualTo(3));
    }

    [Test]
    public void Levenshtein_HandlesInputLongerThanTheStackBuffer()
    {
        var source = new string('a', 400);
        var target = new string('a', 400).Insert(200, "b");

        Assert.That(StringDistance.Levenshtein(source, target), Is.EqualTo(1));
    }

    [TestCase(-1, 1, 1)]
    [TestCase(1, -1, 1)]
    [TestCase(1, 1, -1)]
    public void Levenshtein_RejectsNegativeCosts(int insertion, int deletion, int substitution)
    {
        Assert.That(
            () => StringDistance.Levenshtein("a", "b", insertion, deletion, substitution),
            Throws.TypeOf<ArgumentOutOfRangeException>());
    }
}
