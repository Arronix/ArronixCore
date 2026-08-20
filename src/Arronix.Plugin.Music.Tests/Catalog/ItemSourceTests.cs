using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Arronix.Abstractions.Intent;
using Arronix.Abstractions.Shape;

namespace Arronix.Plugin.Music.Tests.Catalog;

/// <summary>
/// Exercises the catalog projection and the working surfaces it answers for.
/// </summary>
[TestFixture]
public class ItemSourceTests
{
    private readonly MusicItemSource _source = new();

    [Test]
    public void TheSourceServesTheKindItsShapeDeclares()
    {
        Assert.That(_source.MediaKind, Is.EqualTo(MusicShape.Kind));
    }

    [Test]
    public async Task TheRootLevelProjectsWithoutAParent()
    {
        var page = await _source.QueryAsync(new ItemQuery
        {
            Kind = MusicShape.Kind,
            Level = MusicShape.PerformerLevel,
        });

        Assert.That(page.Items, Is.Not.Empty);
        Assert.That(page.TotalCount, Is.EqualTo(page.Items.Count));

        foreach (var item in page.Items)
        {
            Assert.That(item.Parent, Is.Null);
            Assert.That(item.HasChildren, Is.True);
        }
    }

    [Test]
    public async Task PagingReportsTheFullCountAndReturnsAWindow()
    {
        var full = await _source.QueryAsync(new ItemQuery
        {
            Kind = MusicShape.Kind,
            Level = MusicShape.WorkLevel,
            PageSize = 200,
        });

        var first = await _source.QueryAsync(new ItemQuery
        {
            Kind = MusicShape.Kind,
            Level = MusicShape.WorkLevel,
            PageSize = 1,
        });

        Assert.That(first.Items, Has.Count.EqualTo(1));
        Assert.That(first.TotalCount, Is.EqualTo(full.TotalCount));
        Assert.That(first.PageSize, Is.EqualTo(1));
    }

    [Test]
    public async Task TextSearchNarrowsTheProjection()
    {
        var page = await _source.QueryAsync(new ItemQuery
        {
            Kind = MusicShape.Kind,
            Level = MusicShape.WorkLevel,
            TextSearch = "rainbows",
        });

        Assert.That(page.Items, Has.Count.EqualTo(1));
        Assert.That(page.Items[0].Title, Is.EqualTo("In Rainbows"));
    }

    [Test]
    public async Task EveryProjectedFieldIsDeclaredOnItsLevel()
    {
        foreach (var level in MusicShape.Declaration.Levels)
        {
            var declared = level.Fields.Select(field => field.FieldId).ToHashSet(System.StringComparer.Ordinal);

            var page = await _source.QueryAsync(new ItemQuery
            {
                Kind = MusicShape.Kind,
                Level = level.Id,
                PageSize = 500,
            });

            foreach (var item in page.Items)
            {
                foreach (var fieldId in item.Fields.Keys)
                {
                    Assert.That(
                        declared,
                        Does.Contain(fieldId),
                        $"Level '{level.Id}' projects an undeclared field '{fieldId}'.");
                }
            }
        }
    }

    [Test]
    public async Task EveryProjectedCoordinateNamesASpaceItsLevelAdmits()
    {
        foreach (var level in MusicShape.Declaration.Levels)
        {
            var page = await _source.QueryAsync(new ItemQuery
            {
                Kind = MusicShape.Kind,
                Level = level.Id,
                PageSize = 500,
            });

            foreach (var item in page.Items)
            {
                foreach (var reading in item.Coordinates.Readings)
                {
                    Assert.That(level.CoordinateSpaceIds, Does.Contain(reading.SpaceId));
                }
            }
        }
    }

    [Test]
    public async Task AnUnknownIdentifierSchemeResolvesToNothing()
    {
        var resolved = await _source.ResolveExternalAsync(ExternalId.Of("tvdb", "81189"));

        Assert.That(resolved, Is.Null);
    }

    [Test]
    public async Task TheManualImportSurfaceProposesOneRowPerFile()
    {
        var proposal = await _source.ProposeAsync(
            MusicItemSource.ManualImportWorkbenchId,
            new Dictionary<string, string>(System.StringComparer.Ordinal)
            {
                [MusicItemSource.FolderInputId] = "/library/incoming",
                [MusicItemSource.PressingInputId] = "201",
                [MusicItemSource.FilesInputId] = "01 Airbag.flac;02 Paranoid Android.flac",
            });

        Assert.That(proposal.WorkbenchId, Is.EqualTo(MusicItemSource.ManualImportWorkbenchId));
        Assert.That(proposal.Rows, Has.Count.EqualTo(2));

        foreach (var row in proposal.Rows)
        {
            Assert.That(row.Values.ContainsKey(MusicWorkbench.TargetColumnId), Is.True);
            Assert.That(row.Values[MusicWorkbench.TargetColumnId].Reference, Is.Not.Null);
            Assert.That(
                row.Values[MusicWorkbench.TargetColumnId].Reference!.Value.Level,
                Is.EqualTo(MusicShape.RecordingLevel));
        }
    }

    [Test]
    public async Task AProposalWithoutAPressingSaysWhyRatherThanGuessing()
    {
        var proposal = await _source.ProposeAsync(
            MusicItemSource.ManualImportWorkbenchId,
            new Dictionary<string, string>(System.StringComparer.Ordinal)
            {
                [MusicItemSource.FolderInputId] = "/library/incoming",
                [MusicItemSource.FilesInputId] = "01 Unknown.flac",
            });

        Assert.That(proposal.Issues, Is.Not.Empty);
        Assert.That(proposal.Rows[0].IncludedByDefault, Is.False);
    }

    [Test]
    public async Task EveryProposedColumnIsADeclaredColumn()
    {
        var descriptor = MusicIntent.Declaration.Workbenches
            .Single(workbench => string.Equals(
                workbench.WorkbenchId,
                MusicItemSource.ManualImportWorkbenchId,
                System.StringComparison.Ordinal));

        var declared = descriptor.Columns
            .Select(column => column.Field.FieldId)
            .ToHashSet(System.StringComparer.Ordinal);

        var proposal = await _source.ProposeAsync(
            MusicItemSource.ManualImportWorkbenchId,
            new Dictionary<string, string>(System.StringComparer.Ordinal)
            {
                [MusicItemSource.FolderInputId] = "/library/incoming",
                [MusicItemSource.PressingInputId] = "204",
                [MusicItemSource.FilesInputId] = "A1 So What.flac",
            });

        foreach (var row in proposal.Rows)
        {
            foreach (var columnId in row.Values.Keys)
            {
                Assert.That(declared, Does.Contain(columnId));
            }
        }
    }

    [Test]
    public async Task CommittingReportsWhatItAccepted()
    {
        var proposal = await _source.ProposeAsync(
            MusicItemSource.InteractiveSearchWorkbenchId,
            new Dictionary<string, string>(System.StringComparer.Ordinal)
            {
                [MusicWorkbench.WorkInputId] = "101",
            });

        Assert.That(proposal.Rows, Has.Count.EqualTo(2));

        var result = await _source.CommitAsync(new WorkbenchCommit(
            MusicItemSource.InteractiveSearchWorkbenchId,
            proposal.Rows,
            [proposal.Rows[1].RowId]));

        Assert.That(result.Accepted, Is.True);
        Assert.That(result.Message, Does.Contain("1 of 2"));
    }

    [Test]
    public async Task AnUndeclaredSurfaceIsRefusedRatherThanImprovised()
    {
        var proposal = await _source.ProposeAsync(
            "not-a-surface",
            new Dictionary<string, string>(System.StringComparer.Ordinal));

        Assert.That(proposal.Rows, Is.Empty);
        Assert.That(proposal.Issues, Is.Not.Empty);
    }
}
