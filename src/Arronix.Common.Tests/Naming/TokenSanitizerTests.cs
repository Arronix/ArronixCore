using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Arronix.Common.Naming;

namespace Arronix.Common.Tests.Naming;

/// <summary>
/// Covers name sanitization, length limits and collision handling.
/// </summary>
[TestFixture]
public class TokenSanitizerTests
{
    /// <summary>The name-length limit every common file system agrees on.</summary>
    private const int NameLimit = 255;

    [TestCase("Title: Subtitle", "Title Subtitle")]
    [TestCase("What/Where", "WhatWhere")]
    [TestCase("back\\slash", "backslash")]
    [TestCase("who?", "who")]
    [TestCase("star*", "star")]
    [TestCase("pipe|name", "pipename")]
    [TestCase("angle<brackets>", "anglebrackets")]
    [TestCase("quoted \"name\"", "quoted name")]
    public void SanitizeComponent_RemovesCharactersNoFileSystemAccepts(string input, string expected)
    {
        Assert.That(TokenSanitizer.SanitizeComponent(input), Is.EqualTo(expected));
    }

    [Test]
    public void SanitizeComponent_RemovesControlCharacters()
    {
        Assert.That(
            TokenSanitizer.SanitizeComponent("bell\u0007and\u001bescape"),
            Is.EqualTo("bellandescape"));
    }

    [Test]
    public void SanitizeComponent_CollapsesWhitespaceLeftBehindByARemoval()
    {
        Assert.That(TokenSanitizer.SanitizeComponent("A  |  B"), Is.EqualTo("A B"));
    }

    [TestCase("trailing dot.", "trailing dot")]
    [TestCase("trailing space ", "trailing space")]
    [TestCase("both . ", "both")]
    public void SanitizeComponent_TrimsWhatWindowsWouldSilentlyStrip(string input, string expected)
    {
        Assert.That(TokenSanitizer.SanitizeComponent(input), Is.EqualTo(expected));
    }

    [Test]
    public void SanitizeComponent_KeepsALeadingDot()
    {
        Assert.That(TokenSanitizer.SanitizeComponent(".config"), Is.EqualTo(".config"));
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase(".")]
    [TestCase("..")]
    [TestCase("///")]
    public void SanitizeComponent_NeverReturnsSomethingThatIsNotAName(string input)
    {
        Assert.That(TokenSanitizer.SanitizeComponent(input), Is.EqualTo(TokenSanitizer.EmptyNamePlaceholder));
    }

    [TestCase("CON", "CON_")]
    [TestCase("nul", "nul_")]
    [TestCase("Aux", "Aux_")]
    [TestCase("COM1", "COM1_")]
    [TestCase("LPT9", "LPT9_")]
    [TestCase("NUL.txt", "NUL_.txt")]
    public void SanitizeComponent_DisarmsANameTheDeviceNamespaceHasClaimed(string input, string expected)
    {
        var sanitized = TokenSanitizer.SanitizeComponent(input);

        Assert.Multiple(() =>
        {
            Assert.That(sanitized, Is.EqualTo(expected));
            Assert.That(TokenSanitizer.IsReservedName(sanitized), Is.False);
        });
    }

    [TestCase("CONSOLE")]
    [TestCase("COM10")]
    [TestCase("Contact")]
    public void SanitizeComponent_LeavesANameThatMerelyStartsLikeADevice(string input)
    {
        Assert.That(TokenSanitizer.SanitizeComponent(input), Is.EqualTo(input));
    }

    [Test]
    public void SanitizeComponent_RejectsAMissingValue()
    {
        Assert.That(() => TokenSanitizer.SanitizeComponent(null!), Throws.TypeOf<ArgumentNullException>());
    }

    [Test]
    public void TruncateComponent_LeavesANameThatAlreadyFits()
    {
        Assert.That(TokenSanitizer.TruncateComponent("short.mkv", NameLimit), Is.EqualTo("short.mkv"));
    }

    /// <summary>
    /// Counting characters instead of UTF-8 bytes is what makes an accented title fail to write on Linux:
    /// 200 accented characters are 200 characters and 400 bytes, so a limit applied to the character count
    /// produces a name the file system rejects outright.
    /// </summary>
    [Test]
    public void TruncateComponent_MeasuresTheBudgetInUtf8BytesNotCharacters()
    {
        var accented = new string('é', 200) + ".mkv";

        var truncated = TokenSanitizer.TruncateComponent(accented, NameLimit);

        Assert.Multiple(() =>
        {
            Assert.That(Encoding.UTF8.GetByteCount(truncated), Is.LessThanOrEqualTo(NameLimit));
            Assert.That(truncated.Length, Is.LessThan(accented.Length));
            Assert.That(truncated, Does.EndWith(".mkv"));
        });
    }

    [Test]
    public void TruncateComponent_KeepsTheExtensionThatDecidesWhatCanOpenTheFile()
    {
        var name = new string('a', 400) + ".mkv";

        Assert.That(TokenSanitizer.TruncateComponent(name, NameLimit), Does.EndWith(".mkv"));
    }

    [Test]
    public void TruncateComponent_DropsAnExtensionThatWouldLeaveNoRoomForAName()
    {
        var name = new string('a', 40) + "." + new string('b', 40);

        var truncated = TokenSanitizer.TruncateComponent(name, 20);

        Assert.Multiple(() =>
        {
            Assert.That(Encoding.UTF8.GetByteCount(truncated), Is.LessThanOrEqualTo(20));
            Assert.That(truncated, Does.Not.Contain("."));
        });
    }

    [Test]
    public void TruncateComponent_NeverSplitsASurrogatePair()
    {
        // Each emoji is one code point stored as two chars and four UTF-8 bytes.
        var name = string.Concat(System.Linq.Enumerable.Repeat("\U0001F600", 100));

        var truncated = TokenSanitizer.TruncateComponent(name, 50);

        Assert.Multiple(() =>
        {
            Assert.That(Encoding.UTF8.GetByteCount(truncated), Is.LessThanOrEqualTo(50));
            Assert.That(truncated.Length % 2, Is.Zero, "a surrogate pair was cut in half");
            Assert.That(char.IsHighSurrogate(truncated[^1]), Is.False);
        });
    }

    [Test]
    public void TruncateComponent_NeverSeparatesALetterFromItsAccent()
    {
        // "e" plus COMBINING ACUTE ACCENT is one grapheme cluster: two chars, three UTF-8 bytes.
        var name = string.Concat(System.Linq.Enumerable.Repeat("e\u0301", 100));

        var truncated = TokenSanitizer.TruncateComponent(name, 51);

        Assert.Multiple(() =>
        {
            Assert.That(Encoding.UTF8.GetByteCount(truncated), Is.LessThanOrEqualTo(51));
            Assert.That(truncated, Does.Not.EndWith("e"));
        });
    }

    [Test]
    public void TruncateComponent_DoesNotLeaveATrailingDotWhereItCut()
    {
        var truncated = TokenSanitizer.TruncateComponent("abcdefg.hij.klm", 8);

        Assert.That(truncated, Does.Not.EndWith("."));
    }

    [Test]
    public void TruncateComponent_RejectsAnImpossibleBudget()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                () => TokenSanitizer.TruncateComponent("name", 0),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(
                () => TokenSanitizer.TruncateComponent("name", -1),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        });
    }

    [Test]
    public void CombineWithinLimits_KeepsTheWholePathInsideTheLimit()
    {
        var folder = Path.Combine("/media", new string('d', 60));
        var fileName = new string('f', 400) + ".mkv";

        var combined = TokenSanitizer.CombineWithinLimits(folder, fileName, 200, NameLimit);

        Assert.Multiple(() =>
        {
            Assert.That(Encoding.UTF8.GetByteCount(combined), Is.LessThanOrEqualTo(200));
            Assert.That(combined, Does.StartWith(folder));
        });
    }

    [Test]
    public void CombineWithinLimits_AppliesWhicheverLimitBitesFirst()
    {
        var combined = TokenSanitizer.CombineWithinLimits("/media", new string('f', 400), 4000, 32);

        Assert.That(Encoding.UTF8.GetByteCount(Path.GetFileName(combined)), Is.LessThanOrEqualTo(32));
    }

    [Test]
    public void CombineWithinLimits_ReportsAFolderThatLeavesNoRoom()
    {
        Assert.That(
            () => TokenSanitizer.CombineWithinLimits("/media/library", "file.mkv", 10, NameLimit),
            Throws.TypeOf<PathTooLongException>());
    }

    [Test]
    public void CombineWithinLimits_RejectsMissingParts()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                () => TokenSanitizer.CombineWithinLimits(" ", "file.mkv", 100, 100),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                () => TokenSanitizer.CombineWithinLimits("/media", " ", 100, 100),
                Throws.TypeOf<ArgumentException>());
        });
    }

    [Test]
    public void MakeUnique_KeepsThePreferredNameWhenItIsFree()
    {
        Assert.That(
            TokenSanitizer.MakeUnique("file.mkv", static _ => false, NameLimit),
            Is.EqualTo("file.mkv"));
    }

    [Test]
    public void MakeUnique_NumbersBeforeTheExtension()
    {
        var taken = new HashSet<string>(StringComparer.Ordinal) { "file.mkv" };

        Assert.That(TokenSanitizer.MakeUnique("file.mkv", taken.Contains, NameLimit), Is.EqualTo("file (2).mkv"));
    }

    [Test]
    public void MakeUnique_CountsPastEveryNameAlreadyTaken()
    {
        var taken = new HashSet<string>(StringComparer.Ordinal)
        {
            "file.mkv", "file (2).mkv", "file (3).mkv",
        };

        Assert.That(TokenSanitizer.MakeUnique("file.mkv", taken.Contains, NameLimit), Is.EqualTo("file (4).mkv"));
    }

    [Test]
    public void MakeUnique_FitsTheNumberInsideTheBudgetRatherThanPastIt()
    {
        var name = new string('a', 300) + ".mkv";
        var taken = new HashSet<string>(StringComparer.Ordinal) { name };

        var unique = TokenSanitizer.MakeUnique(name, taken.Contains, NameLimit);

        Assert.Multiple(() =>
        {
            Assert.That(Encoding.UTF8.GetByteCount(unique), Is.LessThanOrEqualTo(NameLimit));
            Assert.That(unique, Does.EndWith(" (2).mkv"));
        });
    }

    [Test]
    public void MakeUnique_TerminatesWhenEveryCandidateIsTaken()
    {
        Assert.That(
            () => TokenSanitizer.MakeUnique("file.mkv", static _ => true, NameLimit),
            Throws.TypeOf<IOException>());
    }

    [Test]
    public void MakeUnique_RejectsMissingArguments()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                () => TokenSanitizer.MakeUnique(" ", static _ => false, NameLimit),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                () => TokenSanitizer.MakeUnique("file.mkv", null!, NameLimit),
                Throws.TypeOf<ArgumentNullException>());
            Assert.That(
                () => TokenSanitizer.MakeUnique("file.mkv", static _ => false, 0),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        });
    }

    [Test]
    public void IsReservedName_RejectsAMissingValue()
    {
        Assert.That(() => TokenSanitizer.IsReservedName(null!), Throws.TypeOf<ArgumentNullException>());
    }
}
