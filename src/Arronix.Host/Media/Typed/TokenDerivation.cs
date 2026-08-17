using System.Linq;
using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Shape;

// The derivation reads and produces experimental contracts throughout.
#pragma warning disable ARX0013
#pragma warning disable ARX0020

namespace Arronix.Host.Media.Typed;

/// <summary>
/// Derives the naming tokens a kind contributes to templates.
/// </summary>
/// <remarks>
/// <para>
/// Four rules and no list. A nameable field contributes its own token; the entity's title contributes the
/// host's title transforms as well; a group's title contributes the group's; a per-file facet contributes
/// one; and the identity role contributes the stamp a folder name carries.
/// </para>
/// <para>
/// <b>What this derivation does not produce, and the consequence.</b> The host-global tokens — quality,
/// file size, languages, the release group and the rest — belong to a host token registry, not to any kind,
/// and are not derived here. Until that registry exists the derived set is missing them, so a default
/// template mentioning one will not validate. That is a real ordering constraint between this and the host
/// naming work, and it is stated here rather than discovered by whoever loads the first typed kind.
/// </para>
/// </remarks>
internal static class TokenDerivation
{
    /// <summary>
    /// The transforms the host applies to any entity's title, spelled as token suffixes.
    /// </summary>
    /// <remarks>
    /// Host-owned rather than per-kind, and that is the point: two kinds that each wrote their own article
    /// handling diverged, and the divergence was invisible because each was locally consistent.
    /// </remarks>
    private static readonly (string Suffix, string Description)[] TitleTransforms =
    [
        ("Clean", "the title with punctuation and diacritics removed"),
        ("The", "the title with a leading article moved to the end"),
        ("CleanThe", "the cleaned title with a leading article moved to the end"),
        ("FirstCharacter", "the first character of the title, for a fan-out folder")
    ];

    /// <summary>
    /// Derives the tokens for one kind.
    /// </summary>
    /// <param name="levelWord">The word a template spells the item level with.</param>
    /// <param name="item">The item type's reading.</param>
    /// <param name="groups">The kind's grouping axes, paired with the word a template spells each with.</param>
    /// <param name="facets">The per-file facets the kind's format families declare.</param>
    /// <param name="hasIdentityRole">Whether the kind declares any external-identity role.</param>
    /// <returns>The tokens.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    internal static IReadOnlyList<NamingToken> Derive(
        string levelWord,
        ItemTypeReader item,
        IReadOnlyList<(string Word, ItemTypeReader Reading)> groups,
        IReadOnlyList<TechnicalFacet> facets,
        bool hasIdentityRole)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(groups);
        ArgumentNullException.ThrowIfNull(facets);

        var tokens = new List<NamingToken>();

        foreach (var field in item.Fields.Where(static candidate => candidate.IsNameable))
        {
            tokens.Add(TokenFor(levelWord, field));
        }

        tokens.AddRange(TransformTokensFor(levelWord, item));

        if (hasIdentityRole)
        {
            // The identity stamp, as the primary catalog writes it: "scheme-value". A kind spelling a
            // catalog's own name into a folder template is that catalog's name leaking into a media kind;
            // rendering whichever catalog is installed is the same folder for a TMDb-keyed library and a
            // correct one for any other.
            tokens.Add(new NamingToken(
                $"{{{levelWord} Id}}",
                $"the {levelWord.ToLowerInvariant()}'s primary catalog identifier, as scheme-value",

                // The example is vendor-neutral on purpose, and the paragraph above is why: an example
                // reading "tmdb-335984" would put a catalog's name in every kind's published token list,
                // which is the leak this token exists to avoid. The shape is what the example has to show.
                "catalog-335984"));
        }

        foreach (var group in groups)
        {
            tokens.Add(TokenFor(group.Word, group.Reading.Title));
            tokens.AddRange(TransformTokensFor(group.Word, group.Reading));
        }

        foreach (var facet in facets)
        {
            tokens.Add(new NamingToken(
                $"{{{DerivedNames.TokenWord(facet.FacetId)} Tags}}",
                $"the file's {facet.Name.ToLowerInvariant()}",
                facet.Name));
        }

        return tokens;
    }

    private static NamingToken TokenFor(string word, DerivedField field) =>
        new(
            $"{{{word} {DerivedNames.TokenWord(field.Property.Name)}}}",
            field.Descriptor.Description ?? field.Descriptor.Name,
            field.Example ?? string.Empty);

    private static IEnumerable<NamingToken> TransformTokensFor(string word, ItemTypeReader reading)
    {
        var title = reading.Title;

        return TitleTransforms.Select(transform => new NamingToken(
            $"{{{word} {DerivedNames.TokenWord(title.Property.Name)}{transform.Suffix}}}",
            transform.Description,
            string.Empty));
    }
}
