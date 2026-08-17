// Exercises the experimental quality-axes contracts.
#pragma warning disable ARX0021

using System;
using System.Linq;
using System.Reflection;
using Arronix.Abstractions.Quality;

namespace Arronix.Abstractions.Tests.Quality;

/// <summary>
/// Pins the two modes a preference does <b>not</b> have.
/// </summary>
/// <remarks>
/// <para>
/// A preference that could skip an axis makes comparison lexicographic with skipping, which is not
/// transitive: three points absent on three different axes produce a strict preference cycle, and because
/// a grab happens whenever the comparison says "better", a cycle is an unbounded download loop. A
/// preference that could refuse is a requirement wearing a preference's clothes, which is the merge the
/// whole model exists to undo.
/// </para>
/// <para>
/// The assertions are reflection-driven because naming the missing members in source would not compile,
/// which is the point — this fixture exists so the absence stays deliberate rather than becoming an
/// omission somebody later fills in.
/// </para>
/// </remarks>
[TestFixture]
public class PreferenceVocabularyTests
{
    [Test]
    public void APreferenceHasExactlyTwoWaysToReadSilenceAndNeitherCanCycle()
    {
        Assert.That(
            Enum.GetNames<PreferenceUnknownMode>().Order(StringComparer.Ordinal),
            Is.EqualTo(new[] { "Assume", "Lowest" }),
            "Both surviving modes map an absent reading to a fixed element of the axis's order, which is "
            + "what keeps each precedence entry a total preorder.");
    }

    [TestCase("Ignore")]
    [TestCase("Refuse")]
    public void ThePreferenceVocabularyDoesNotOfferTheDangerousMode(string absentMember)
    {
        var offered = typeof(PreferenceUnknown)
            .GetMembers(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance)
            .Select(static member => member.Name)
            .ToArray();

        Assert.That(
            offered,
            Has.No.Member(absentMember),
            $"'{absentMember}' must be unrepresentable on a preference, not merely discouraged.");
    }

    [Test]
    public void ARequirementKeepsAllFourWaysToReadSilence()
    {
        Assert.That(
            Enum.GetNames<UnknownEvidenceMode>().Order(StringComparer.Ordinal),
            Is.EqualTo(new[] { "Assume", "Ignore", "Lowest", "Refuse" }),
            "Neither mode orders anything here, so both are safe — and a floor that ignores an absent "
            + "reading is what lets one cutoff cover an axis with a legitimate reason to be silent.");
    }

    [Test]
    public void TheOrderingSectionIsTypedAgainstTheRestrictedVocabulary()
    {
        var whenUnknown = typeof(AxisPreference).GetProperty(nameof(AxisPreference.WhenUnknown));

        Assert.That(whenUnknown, Is.Not.Null);
        Assert.That(
            whenUnknown!.PropertyType,
            Is.EqualTo(typeof(PreferenceUnknown)),
            "The restriction is enforced by the type rather than by an analyzer, because here it is cheap "
            + "to make structural.");
    }

    [Test]
    public void AnAssumptionMustBeAValue()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => PreferenceUnknown.Assume(AxisValue.None), Throws.ArgumentException);
            Assert.That(() => UnknownEvidence.Assume(AxisValue.None), Throws.ArgumentException);
        });
    }
}
