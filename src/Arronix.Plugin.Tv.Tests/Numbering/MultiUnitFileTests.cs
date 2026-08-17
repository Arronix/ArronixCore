#pragma warning disable ARX0013 // Shape contracts are experimental; these tests cover an implementation of them.
#pragma warning disable ARX0015 // Provider contracts are experimental; ValidationOutcome is shared with them.
#pragma warning disable ARX0016 // Intent contracts are experimental; the workbench proposal is one.
#pragma warning disable ARX0017 // Wire contracts are experimental; ActionResult is one.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Intent;
using Arronix.Abstractions.Shape;
using Arronix.Abstractions.Wire;
using Arronix.Plugin.Tv.Seed;

namespace Arronix.Plugin.Tv.Tests.Numbering;

/// <summary>
/// Proves the one-file-to-many-units binding end to end, and the span constraint that bounds it.
/// </summary>
/// <remarks>
/// <para>This is the cardinality no single foreign key can express. The binding declares
/// <c>AtMostOneFilePerUnit = true</c> and <c>AtMostOneUnitPerFile = false</c>, and those two booleans
/// <b>are</b> the uniqueness constraints — so every assertion below is either a direct reading of one of
/// them or an attempt to violate one.</para>
/// <para>The span constraint is the other half. A file may straddle the inner ordinal and never the outer
/// one; the reference implementation expresses that as a thrown exception, and expressing it as declared
/// data means it can be enforced in three independent places without any of them knowing what a run is.</para>
/// </remarks>
[TestFixture]
public sealed class MultiUnitFileTests
{
    private TvCatalog _catalog = null!;
    private TvReleaseMatcher _matcher = null!;
    private TvRenamePolicy _naming = null!;
    private TvItemSource _items = null!;

    [SetUp]
    public void SetUp()
    {
        _catalog = TvCatalog.CreateSeeded();
        _matcher = new TvReleaseMatcher(_catalog);
        _naming = new TvRenamePolicy(_catalog);
        _items = new TvItemSource(_catalog);
    }

    [TestCase("The.Expanse.S01E01E02.1080p.WEB-DL.x264-NTb")]
    [TestCase("The.Expanse.S01E01-E02.1080p.WEB-DL.x264-NTb")]
    [TestCase("The.Expanse.S01E01-02.1080p.WEB-DL.x264-NTb")]
    public void EverySpellingOfAMultiUnitReleaseParsesToTheSameTwoOrdinals(string releaseTitle)
    {
        Assert.That(TvTitleParser.TryParse(releaseTitle, out var parsed), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(parsed!.Kind, Is.EqualTo(TvReleaseKind.SeasonEpisode));
            Assert.That(parsed.SeasonNumber, Is.EqualTo(1));
            Assert.That(parsed.EpisodeNumbers, Is.EqualTo(new[] { 1, 2 }));
            Assert.That(parsed.IsMultiUnit, Is.True);
        });
    }

    [Test]
    public void ACodecTokenIsNotMistakenForAFurtherOrdinal()
    {
        // "S01E01.x264" must not read as units 1 and 264. The continuation group admits only "-", "E" or
        // "x" between ordinals and no separator of any other kind, which is what makes that unrepresentable.
        Assert.That(TvTitleParser.TryParse("The.Expanse.S01E01.1080p.HDTV.x264-GRP", out var parsed), Is.True);

        Assert.That(parsed!.EpisodeNumbers, Is.EqualTo(new[] { 1 }));
    }

    [Test]
    public void AMultiUnitReleaseResolvesToSeveralUnitsAndSaysSo()
    {
        var outcome = Match("The.Expanse.S01E01E02.1080p.WEB-DL.x264-NTb");

        Assert.Multiple(() =>
        {
            Assert.That(outcome.RejectionReason, Is.Null);
            Assert.That(outcome.Units, Has.Count.EqualTo(2));
            Assert.That(
                outcome.Units.Select(TitleOf),
                Is.EqualTo(new[] { "Dulcinea", "The Big Empty" }));
            Assert.That(outcome.Confidence, Is.EqualTo(MatchConfidence.High));
            Assert.That(outcome.Warnings, Has.Some.Contains("satisfies 2 units"));
        });
    }

    [Test]
    public void TheDeclaredBindingIsWhatPermitsIt()
    {
        var binding = new TvShape().Shape.FileBinding;

        Assert.Multiple(() =>
        {
            Assert.That(
                binding.AtMostOneUnitPerFile,
                Is.False,
                "a file satisfying several units is only legal because this is false");
            Assert.That(
                binding.AtMostOneFilePerUnit,
                Is.True,
                "and the reverse arrangement is illegal because this is true");
        });
    }

    [Test]
    public void AMultiUnitFileNameRendersInEveryDeclaredStyle()
    {
        var first = Unit(1, 1);
        var second = Unit(1, 2);
        var third = Unit(1, 3);

        Assert.Multiple(() =>
        {
            Assert.That(
                RenderMany([first, second], TvMultiUnitStyle.Extend),
                Is.EqualTo("The Expanse - S01E01-02 - Dulcinea + The Big Empty"));
            Assert.That(
                RenderMany([first, second], TvMultiUnitStyle.Repeat),
                Is.EqualTo("The Expanse - S01E01E02 - Dulcinea + The Big Empty"));
            Assert.That(
                RenderMany([first, second], TvMultiUnitStyle.Scene),
                Is.EqualTo("The Expanse - S01E01-E02 - Dulcinea + The Big Empty"));
            Assert.That(
                RenderMany([first, second], TvMultiUnitStyle.Duplicate),
                Is.EqualTo("The Expanse - S01E01.S01E02 - Dulcinea + The Big Empty"));
            Assert.That(
                RenderMany([first, second, third], TvMultiUnitStyle.Range),
                Does.Contain("S01E01-03"));
            Assert.That(
                RenderMany([first, second, third], TvMultiUnitStyle.PrefixedRange),
                Does.Contain("S01E01-E03"));
        });
    }

    [Test]
    public void ASingleUnitRendersIdenticallyWhicheverStyleIsChosen()
    {
        var only = Unit(1, 4);

        foreach (var style in Enum.GetValues<TvMultiUnitStyle>())
        {
            Assert.That(
                RenderMany([only], style),
                Is.EqualTo("The Expanse - S01E04 - CQB"),
                $"style '{style}' changed a single-unit name");
        }
    }

    [Test]
    public void NamingRefusesUnitsThatStraddleTheOuterOrdinal()
    {
        var lastOfFirstRun = Unit(1, 6);
        var firstOfSecondRun = Unit(2, 1);

        var thrown = Assert.ThrowsAsync<ArgumentException>(() => _naming.GenerateFileNameForUnitsAsync(
            [MediaItemId.FromInt64(lastOfFirstRun.Id), MediaItemId.FromInt64(firstOfSecondRun.Id)],
            TvRenamePolicy.OrdinalTemplate));

        Assert.That(thrown!.Message, Does.Contain("must-not-span"));
    }

    [Test]
    public void NamingRefusesUnitsFromDifferentLibraryEntries()
    {
        var expanse = Unit(1, 1);
        var bebop = _catalog.Episodes.Single(unit => unit.Title == "Asteroid Blues");

        Assert.ThrowsAsync<ArgumentException>(() => _naming.GenerateFileNameForUnitsAsync(
            [MediaItemId.FromInt64(expanse.Id), MediaItemId.FromInt64(bebop.Id)],
            TvRenamePolicy.OrdinalTemplate));
    }

    [Test]
    public void TheManualImportProposalFillsAMultivaluedUnitColumn()
    {
        var proposal = Propose(
            "/downloads/The.Expanse.S01E01E02.1080p.WEB-DL.x264-NTb.mkv\n"
            + "/downloads/The.Expanse.S01E03.1080p.WEB-DL.x264-NTb.mkv");

        Assert.That(proposal.Rows, Has.Count.EqualTo(2));

        var multi = proposal.Rows[0].Values[TvWorkbenches.UnitsColumn];
        var single = proposal.Rows[1].Values[TvWorkbenches.UnitsColumn];

        Assert.Multiple(() =>
        {
            Assert.That(multi.Items, Is.Not.Null);
            Assert.That(multi.Items!, Has.Count.EqualTo(2));
            Assert.That(multi.Items!.All(item => item.Reference.HasValue), Is.True);
            Assert.That(single.Items, Is.Not.Null);
            Assert.That(single.Items!, Has.Count.EqualTo(1));
            Assert.That(
                proposal.Rows[0].Values[TvWorkbenches.PositionColumn].Text,
                Is.EqualTo("S01E01, S01E02"));
            Assert.That(proposal.Rows[0].IncludedByDefault, Is.True);
        });
    }

    [Test]
    public void CommittingAMultiUnitRowIsAccepted()
    {
        var result = Commit(Row("0", Unit(1, 1), Unit(1, 2)));

        Assert.Multiple(() =>
        {
            Assert.That(result.Accepted, Is.True);
            Assert.That(result.Validation!.IsValid, Is.True);
        });
    }

    [Test]
    public void CommittingARowWhoseUnitsStraddleTheOuterOrdinalIsRefused()
    {
        var result = Commit(Row("0", Unit(1, 6), Unit(2, 1)));

        Assert.Multiple(() =>
        {
            Assert.That(result.Accepted, Is.False);
            Assert.That(
                result.Validation!.Failures.Select(failure => failure.Message),
                Has.Some.Contains("must-not-span"));
        });
    }

    [Test]
    public void CommittingTwoRowsClaimingTheSameUnitIsRefused()
    {
        // The other half of the binding: at most one file per unit. Two files claiming one unit is exactly
        // the constraint that is true, and it has to be caught even though the multi-unit direction is open.
        var result = Commit(Row("0", Unit(1, 1)), Row("1", Unit(1, 1), Unit(1, 2)));

        Assert.Multiple(() =>
        {
            Assert.That(result.Accepted, Is.False);
            Assert.That(
                result.Validation!.Failures.Select(failure => failure.Message),
                Has.Some.Contains("at most one file per unit"));
        });
    }

    [Test]
    public void ExcludedRowsAreNotValidated()
    {
        var offending = Row("0", Unit(1, 6), Unit(2, 1));

        var result = _items
            .CommitAsync(new WorkbenchCommit(TvWorkbenches.ManualImport, [offending], ["0"]))
            .GetAwaiter()
            .GetResult();

        Assert.That(result.Accepted, Is.True);
    }

    [Test]
    public void AWholeRunReleaseYieldsManyUnitsThatShareOneOuterOrdinal()
    {
        var outcome = Match("The.Expanse.S01.COMPLETE.1080p.BluRay.x264-GRP");

        var runs = outcome.Units
            .Select(reference => _catalog.TryGetEpisode(reference.Id.Value, out var unit) && unit is not null
                ? unit.SeasonNumber
                : -1)
            .Distinct()
            .ToList();

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Units, Has.Count.EqualTo(6));
            Assert.That(runs, Is.EqualTo(new[] { 1 }), "a whole-run release never straddles the outer ordinal");
        });
    }

    private MatchOutcome Match(string releaseTitle)
        => _matcher.Match(new MatchRequest
        {
            MediaKind = TvIds.MediaKind,
            Text = releaseTitle,
            Source = MatchSource.ReleaseName
        });

    private WorkbenchProposal Propose(string files)
        => _items
            .ProposeAsync(
                TvWorkbenches.ManualImport,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [TvWorkbenches.FilesInput] = files
                })
            .GetAwaiter()
            .GetResult();

    private ActionResult Commit(params WorkbenchRow[] rows)
        => _items
            .CommitAsync(new WorkbenchCommit(TvWorkbenches.ManualImport, rows, []))
            .GetAwaiter()
            .GetResult();

    private static WorkbenchRow Row(string rowId, params TvEpisodeRecord[] units) => new()
    {
        RowId = rowId,
        Values = new Dictionary<string, FieldValue>(StringComparer.Ordinal)
        {
            [TvWorkbenches.PathColumn] = FieldValue.OfFilePath($"/downloads/{rowId}.mkv"),
            [TvWorkbenches.UnitsColumn] = FieldValue.OfItems(
                FieldValueKind.Reference,
                [.. units.Select(unit => FieldValue.OfReference(TvCatalog.ReferenceTo(unit)))])
        }
    };

    private TvEpisodeRecord Unit(int season, int episode)
        => _catalog.TryGetByAired(1, season, episode, out var unit) && unit is not null
            ? unit
            : throw new InvalidOperationException(
                $"The seeded catalog has no unit at {season.ToString(CultureInfo.InvariantCulture)}."
                + episode.ToString(CultureInfo.InvariantCulture));

    private string TitleOf(MediaItemRef reference)
        => _catalog.TryGetEpisode(reference.Id.Value, out var unit) && unit is not null
            ? unit.Title
            : throw new InvalidOperationException("Unknown unit.");

    private string RenderMany(IReadOnlyList<TvEpisodeRecord> units, TvMultiUnitStyle style)
        => _naming
            .GenerateFileNameForUnitsAsync(
                [.. units.Select(unit => MediaItemId.FromInt64(unit.Id))],
                TvRenamePolicy.OrdinalTemplate,
                style)
            .GetAwaiter()
            .GetResult();
}
