using System;
using Arronix.Common.Naming;

namespace Arronix.Common.Tests.Naming;

/// <summary>Proves the one token-name equality rule shared by declaration, ownership, and rendering.</summary>
[TestFixture]
public sealed class NamingTokenNameTests
{
    [TestCase("Entry Title", "entrytitle")]
    [TestCase("entry.title", "entrytitle")]
    [TestCase("ENTRY_TITLE", "entrytitle")]
    [TestCase("{Entry-Title}", "entrytitle")]
    public void Canonicalize_FoldsGrammarEquivalentSpellings(string input, string expected)
    {
        Assert.That(NamingTokenName.Canonicalize(input), Is.EqualTo(expected));
    }

    [Test]
    public void Canonicalize_PreservesSupplementaryPlaneLettersAndFoldsTheirCase()
    {
        const string DeseretCapitalLongI = "\U00010400";
        const string DeseretSmallLongI = "\U00010428";

        var upper = NamingTokenName.Canonicalize(DeseretCapitalLongI);
        var lower = NamingTokenName.Canonicalize(DeseretSmallLongI);

        Assert.That(upper, Is.Not.Empty.And.EqualTo(lower));
    }

    [Test]
    public void Canonicalize_DoesNotPutUnboundedInputOnTheStack()
    {
        var input = new string('A', 100_000);

        var canonical = NamingTokenName.Canonicalize(input);

        Assert.That(canonical, Has.Length.EqualTo(input.Length));
        Assert.That(canonical, Is.All.EqualTo('a'));
    }

    [Test]
    public void Canonicalize_RejectsNull()
    {
        Assert.That(() => NamingTokenName.Canonicalize(null!), Throws.TypeOf<ArgumentNullException>());
    }
}
