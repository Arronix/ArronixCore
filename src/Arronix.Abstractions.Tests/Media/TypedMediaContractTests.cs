using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Media;
using Arronix.Abstractions.Shape;

namespace Arronix.Abstractions.Tests.Media;

/// <summary>
/// The typed authoring surface, as a plugin author meets it.
/// </summary>
/// <remarks>
/// These are contract-shape assertions rather than behaviour: the attribute vocabulary is a public surface
/// whose usage rules are the whole of what it promises, and a vocabulary that quietly became inheritable or
/// applicable to a field would change what an author can write without anything failing.
/// </remarks>
[TestFixture]
public sealed class TypedMediaContractTests
{
    private static IReadOnlyList<Type> Attributes =>
        [.. typeof(IMediaItem).Assembly
            .GetExportedTypes()
            .Where(type => type.Namespace == typeof(IMediaItem).Namespace)
            .Where(type => typeof(Attribute).IsAssignableFrom(type))
            .OrderBy(type => type.Name, StringComparer.Ordinal)];

    [Test]
    public void TheVocabularyIsTheOneTheDesignNames() =>
        Assert.That(
            Attributes.Select(type => type.Name).ToArray(),
            Is.EquivalentTo(
            [
                nameof(ArtworkAttribute),
                nameof(CountAttribute),
                nameof(DerivedAttribute),
                nameof(DisambiguationAttribute),
                nameof(DisplayAttribute),
                nameof(EditableAttribute),
                nameof(FilterableAttribute),
                nameof(GroupableAttribute),
                nameof(IdentityAttribute),
                nameof(Abstractions.Media.IgnoreAttribute),
                nameof(MultilineAttribute),
                nameof(ProgressAttribute),
                nameof(ProminenceAttribute),
                nameof(RatioAttribute),
                nameof(SearchableAttribute),
                nameof(SizeAttribute),
                nameof(SortableAttribute),
                nameof(StatusAttribute),
                nameof(TimestampAttribute),
                nameof(TitleAttribute),
                nameof(UnitAttribute)
            ]));

    [Test]
    public void EveryAttributeAppliesToAPropertyOnlyAndIsNotInherited()
    {
        var defects = Attributes
            .Select(type => (type, usage: type.GetCustomAttribute<AttributeUsageAttribute>()))
            .Where(pair => pair.usage is null
                || pair.usage.ValidOn != AttributeTargets.Property
                || pair.usage.Inherited
                || pair.usage.AllowMultiple)
            .Select(pair => pair.type.Name)
            .ToArray();

        Assert.That(
            defects,
            Is.Empty,
            "The vocabulary describes properties. One that applied elsewhere, or that a derived type "
            + "inherited, would let a fact about one property arrive from somewhere an author cannot see.");
    }

    [Test]
    public void EveryAttributeIsSealed() =>
        Assert.That(Attributes.Where(type => !type.IsSealed).Select(type => type.Name).ToArray(), Is.Empty);

    [Test]
    public void NoAttributeTakesAnIdentifierString()
    {
        // The dividing rule made mechanical: an attribute states a fact about one property in isolation, so
        // anything that has to name something else by identifier is relating two things and belongs on the
        // builder, where the reference is an expression the compiler checks.
        var carriers = Attributes
            .Except([typeof(UnitAttribute), typeof(DisplayAttribute)])
            .Where(type => type.GetConstructors()
                .Any(constructor => constructor.GetParameters()
                    .Any(parameter => parameter.ParameterType == typeof(string))))
            .Select(type => type.Name)
            .ToArray();

        Assert.That(carriers, Is.Empty);
    }

    [Test]
    public void AnEmptyIdentifierSetIsSharedAndCarriesNothing()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ExternalIdSet.Empty.Values, Is.Empty);
            Assert.That(ExternalIdSet.Of(), Is.SameAs(ExternalIdSet.Empty));
            Assert.That(ArtworkSet.Empty.Images, Is.Empty);
            Assert.That(ArtworkSet.Of(), Is.SameAs(ArtworkSet.Empty));
        });
    }

    [Test]
    public void AnIdentifierSetFindsOnlyTheCanonicalSchemeSpelling()
    {
        var set = ExternalIdSet.Of(ExternalId.Of("tmdb", "335984"), ExternalId.Of("imdb", "tt1856101"));

        Assert.Multiple(() =>
        {
            Assert.That(set.TryGet("tmdb", out var found), Is.True);
            Assert.That(found.Value, Is.EqualTo("335984"));
            Assert.That(set.TryGet("TMDB", out _), Is.False);
            Assert.That(set.TryGet("tvdb", out _), Is.False);
        });
    }

    [Test]
    public void AnArtworkSetFindsByRoleWithoutRegardToCase()
    {
        var set = ArtworkSet.Of(new ArtworkImage("poster", new Uri("https://example.invalid/p.jpg")));

        Assert.Multiple(() =>
        {
            Assert.That(set.TryGet("Poster", out var image), Is.True);
            Assert.That(image!.Address, Is.EqualTo(new Uri("https://example.invalid/p.jpg")));
            Assert.That(set.TryGet("fanart", out _), Is.False);
        });
    }

    /// <summary>
    /// A minimal entity, written the way a plugin author writes one.
    /// </summary>
    /// <remarks>
    /// It exists to prove that the short attribute names bind. <c>Identity</c> is also the name of a
    /// namespace in this assembly and <c>Prominence</c> is also the name of an enumeration; both are in
    /// scope here, and if either collided the vocabulary would be unusable in exactly the file that needs
    /// it.
    /// </remarks>
    /// <summary>A row shape, not a media entity: the identity attribute is the legacy explicit form.</summary>
    private sealed class IdentityNamed
    {
        [Identity]
        public required MediaItemId Id { get; init; }
    }

    private sealed class Sample : IMediaItem
    {
        public ExternalIdSet ExternalIds { get; init; } = ExternalIdSet.Empty;

        [Title, Searchable, Prominence(Prominence.Primary)]
        public required string Title { get; init; }

        public Language? TitleLanguage { get; init; }

        public string? Overview { get; init; }

        public ArtworkSet Artwork { get; init; } = ArtworkSet.Empty;

        public CatalogRecordState CatalogState { get; init; }
    }

    [Test]
    public void TheShortAttributeNamesBindEvenBesideANamespaceAndAnEnumerationOfTheSameName()
    {
        var title = typeof(Sample).GetProperty(nameof(Sample.Title))!;

        Assert.Multiple(() =>
        {
            Assert.That(typeof(IdentityNamed).GetProperty(nameof(IdentityNamed.Id))!
                .GetCustomAttribute<IdentityAttribute>(), Is.Not.Null);
            Assert.That(title.GetCustomAttribute<TitleAttribute>(), Is.Not.Null);
            Assert.That(
                title.GetCustomAttribute<ProminenceAttribute>()!.Prominence,
                Is.EqualTo(Prominence.Primary));
        });
    }
}
