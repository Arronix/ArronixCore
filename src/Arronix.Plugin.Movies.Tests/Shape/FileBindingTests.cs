#pragma warning disable ARX0013 // Shape contracts are experimental; a media extension is their intended implementer.
#pragma warning disable ARX0021 // Quality contracts are experimental; these tests exercise the axes model.

using System.Linq;
using Arronix.Abstractions.Shape;
using Arronix.Plugin.Movies.Tests.Support;

namespace Arronix.Plugin.Movies.Tests.Shape;

/// <summary>
/// The one-to-one file binding: the degenerate corner of the <c>(unit, file, ordinal?)</c> join, and the
/// reason the join is modeled as a join rather than as a foreign key on either side.
/// </summary>
[TestFixture]
public class FileBindingTests
{
    private static FileBinding Binding => MoviesDeclaration.Shape.FileBinding;

    [Test]
    public void AnchorsAndUnitsAtTheSameLevel()
        => Assert.Multiple(() =>
        {
            Assert.That(Binding.AnchorLevelId, Is.EqualTo(MoviesDeclaration.Level.Id));
            Assert.That(Binding.UnitLevelId, Is.EqualTo(MoviesDeclaration.Level.Id));
        });

    /// <summary>
    /// Both uniqueness constraints hold at once. No other surveyed media kind can say this: a television
    /// episode may share a file with its neighbors, and an album track may be one of many in a single
    /// stream.
    /// </summary>
    [Test]
    public void HoldsBothUniquenessConstraints()
        => Assert.Multiple(() =>
        {
            Assert.That(Binding.AtMostOneFilePerUnit, Is.True);
            Assert.That(Binding.AtMostOneUnitPerFile, Is.True);
        });

    /// <summary>
    /// The gate's rule: an ordinal is meaningful only when a unit may span more than one file. A movie's
    /// unit never does, so declaring one would be a contradiction the gate rejects.
    /// </summary>
    [Test]
    public void DeclaresNoMeaningfulOrdinal()
        => Assert.Multiple(() =>
        {
            Assert.That(Binding.OrdinalIsMeaningful, Is.False);
            Assert.That(
                Binding.AtMostOneFilePerUnit && Binding.OrdinalIsMeaningful,
                Is.False,
                "An ordinal is meaningful only when a unit may span more than one file.");
        });

    /// <summary>
    /// A span constraint says which coordinate components a single file may cover. With one file per unit
    /// there is nothing to span, so the list is empty rather than trivially satisfied.
    /// </summary>
    [Test]
    public void DeclaresNoSpanConstraints() => Assert.That(Binding.SpanConstraints, Is.Empty);

    [Test]
    public void BindsToAFileBearingLevel()
    {
        var shape = MoviesDeclaration.Shape;
        var anchor = shape.Levels.Single(level => level.Id == Binding.AnchorLevelId);
        var unit = shape.Levels.Single(level => level.Id == Binding.UnitLevelId);

        Assert.Multiple(() =>
        {
            Assert.That(anchor.Roles.HasFlag(MediaLevelRoles.FileBearing), Is.True);
            Assert.That(unit.Roles.HasFlag(MediaLevelRoles.FileBearing), Is.True);
        });
    }

    /// <summary>
    /// The video family claims the container extensions a movie arrives in, and it is the only family
    /// there is — which is why a movie never has to answer the question that a book or a music library
    /// does: which ladder does this file belong to.
    /// </summary>
    [Test]
    public void DeclaresOneFormatFamilyThatDoesNotCoexist()
    {
        var families = MoviesDeclaration.Shape.FormatFamilies;

        Assert.That(families, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(families[0].FamilyId, Is.EqualTo("video"));
            Assert.That(families[0].CoexistsWithOtherFamilies, Is.False);
            Assert.That(families[0].FileExtensions, Does.Contain(".mkv").And.Contain(".mp4"));
        });
    }

    /// <summary>
    /// A disc image is a container a movie legitimately arrives in, so the family claims the extension: a
    /// file with no family has no quality at all, and "we refuse this" is a policy answer that needs the
    /// file to have been read first. Refusing it is the shipped policy's business and not the shape's,
    /// which is the separation a ladder could not make — the ladder ranked a whole disc near the top while
    /// its own comment said most users do not want one.
    /// </summary>
    [Test]
    public void ClaimsTheDiscImageExtensionSoThePolicyCanRefuseIt()
    {
        var family = MoviesDeclaration.Shape.FormatFamilies[0];

        Assert.Multiple(() =>
        {
            Assert.That(family.FileExtensions, Does.Contain(".iso"));
            Assert.That(
                MoviesDeclaration.Policy.Requirements.Select(requirement => requirement.Axis.Value),
                Does.Contain("Packaging"));
        });
    }

    [Test]
    public void DeclaresEveryExtensionLowerCasedAndDotted()
    {
        foreach (var extension in MoviesDeclaration.Shape.FormatFamilies[0].FileExtensions)
        {
            Assert.That(extension, Does.StartWith("."), extension);
            Assert.That(extension, Is.EqualTo(extension.ToLowerInvariant()), extension);
        }
    }
}
