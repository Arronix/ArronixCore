
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Shape;
using Arronix.Plugin.Tv.Seed;

namespace Arronix.Plugin.Tv.Tests.Naming;

/// <summary>
/// Pins the contract fixes this milestone absorbed: the rename seam carries the file being named, so the
/// file-fact tokens resolve; and the quality model carries the revision axis on the typed slot instead of
/// dropping it.
/// </summary>
[TestFixture]
public sealed class FileFactsNamingTests
{
    private TvCatalog _catalog = null!;
    private TvRenamePolicy _naming = null!;

    [SetUp]
    public void SetUp()
    {
        _catalog = TvCatalog.CreateSeeded();
        _naming = new TvRenamePolicy(_catalog);
    }

    [Test]
    public async Task FileFactsResolveTheQualityAndGroupTokens()
    {
        var unit = _catalog.Episodes[0];

        var name = await _naming.GenerateFileNameAsync(
            MediaItemId.FromInt64(unit.Id),
            FactsFor("WEBDL-1080p", new QualityRevision(2, 0, false), "NTb"),
            TvRenamePolicy.OrdinalTemplate + " {Release Group}");

        Assert.Multiple(() =>
        {
            Assert.That(name, Does.Contain("WEBDL-1080p Proper"));
            Assert.That(name, Does.Contain("NTb"));
            Assert.That(name, Does.Not.Contain("{"));
        });
    }

    [Test]
    public async Task WithoutAFileTheFileFactTokensRenderAsAbsent()
    {
        var unit = _catalog.Episodes[0];

        var name = await _naming.GenerateFileNameAsync(
            MediaItemId.FromInt64(unit.Id),
            file: null,
            TvRenamePolicy.OrdinalTemplate);

        Assert.Multiple(() =>
        {
            Assert.That(name, Does.Not.Contain("WEBDL"));
            Assert.That(name, Does.Not.Contain("{"));
        });
    }

    [Test]
    public void ARepackRendersItsOwnMarkerAndARealReissueAppendsReal()
    {
        var unit = _catalog.Episodes[0];

        var repack = _naming.GenerateFileNameAsync(
                MediaItemId.FromInt64(unit.Id),
                FactsFor("HDTV-720p", new QualityRevision(2, 0, true), group: null),
                "{Quality Full}")
            .GetAwaiter()
            .GetResult();

        var real = _naming.GenerateFileNameAsync(
                MediaItemId.FromInt64(unit.Id),
                FactsFor("HDTV-720p", new QualityRevision(2, 1, false), group: null),
                "{Quality Full}")
            .GetAwaiter()
            .GetResult();

        Assert.Multiple(() =>
        {
            Assert.That(repack, Is.EqualTo("HDTV-720p Repack"));
            Assert.That(real, Is.EqualTo("HDTV-720p Proper REAL"));
        });
    }

    [Test]
    public void TheParsedRevisionRidesTheTierAndBreaksTheUpgradeTie()
    {
        var model = new TvQualityModel();

        var plain = model.EvaluateQuality(Parsed(revision: null));
        var proper = model.EvaluateQuality(Parsed(revision: "proper"));

        Assert.Multiple(() =>
        {
            Assert.That(plain.Revision, Is.Null);
            Assert.That(proper.Revision, Is.EqualTo(new QualityRevision(2, 0, false)));

            // Same rung, so weight ties; the revision axis decides — the answer the flattened encoding
            // could never give.
            Assert.That(model.IsUpgrade(plain, proper), Is.True);
            Assert.That(model.IsUpgrade(proper, plain), Is.False);
            Assert.That(model.IsUpgrade(proper, proper), Is.False);
        });
    }

    private static ParsedRelease Parsed(string? revision) => new(
        TvIds.MediaKind,
        "The Expanse",
        Quality: "webdl",
        AdditionalMetadata: new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [TvReleaseFields.Resolution] = "1080p",
            [TvReleaseFields.Revision] = revision ?? string.Empty,
        });

    private static MediaFileFacts FactsFor(string tierName, QualityRevision revision, string? group) => new()
    {
        Id = new MediaFileId(1),
        Path = "/library/tv/file.mkv",
        SizeBytes = 1_000_000,
        Quality = TvShape.Ladder.First(
                tier => string.Equals(tier.Name, tierName, StringComparison.Ordinal))
            with
            { Revision = revision },
        ReleaseGroup = group,
    };
}
