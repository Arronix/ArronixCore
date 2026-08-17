// The shape contracts are experimental until 1.0.
#pragma warning disable ARX0013

using System.Globalization;
using System.IO;
using System.Linq;
using Arronix.Abstractions.Shape;

namespace Arronix.Host.Engines.Naming;

/// <summary>
/// Derives naming-token bindings from a media shape and the items being named — the
/// <c>{&lt;Level.Name&gt; &lt;Field.Name&gt;}</c> rule of <c>docs/design/naming-and-tokens.md</c> §2.2.
/// </summary>
/// <remarks>
/// <para>
/// The shape <i>is</i> the vocabulary: fourteen of fourteen surveyed token names fall out of the level
/// and field names (§2.1), so a plugin declares no tokens at all — this deriver replaces the four
/// hand-written token dictionaries (<c>MoviesNaming.cs</c>, <c>TvNaming.cs</c>, <c>MusicNaming.cs</c>,
/// <c>BooksNaming.cs</c>) with one function of the shape.
/// </para>
/// <para>
/// Rules implemented here: D1 (level fields; <c>Title</c> semantics ⇒ elastic), D3 (coordinate
/// components), D4/D5 (date and label spaces), D6/D8 (sequence axes and their exceptions), D12
/// (external identifiers, level-prefixed), plus the host-global file tokens of §2.3 from
/// <see cref="MediaFileFacts"/>. Grouping-axis tokens (D9–D11) bind through the owning level's declared
/// fields in this milestone; file-link ordinals (D13) await the storage layer's link table.
/// </para>
/// </remarks>
internal sealed class ShapeTokenDeriver
{
    private readonly MediaShape _shape;
    private readonly Dictionary<string, MediaLevel> _levels;

    /// <summary>
    /// Initializes a new instance of the <see cref="ShapeTokenDeriver"/> class.
    /// </summary>
    /// <param name="shape">The declared shape.</param>
    public ShapeTokenDeriver(MediaShape shape)
    {
        ArgumentNullException.ThrowIfNull(shape);
        _shape = shape;
        _levels = shape.Levels.ToDictionary(level => level.Id.ToString(), level => level, StringComparer.Ordinal);
    }

    /// <summary>
    /// Binds every derivable token for an item and its ancestors, plus the file's host globals.
    /// </summary>
    /// <param name="chain">The item and its ancestors, leaf last. May be empty for file-only renders.</param>
    /// <param name="file">The file being named, when one exists.</param>
    /// <returns>The bindings.</returns>
    public NamingTokenBindings Bind(IReadOnlyList<ItemView> chain, MediaFileFacts? file)
    {
        ArgumentNullException.ThrowIfNull(chain);

        var bindings = new NamingTokenBindings();

        for (var depth = 0; depth < chain.Count; depth++)
        {
            BindItem(bindings, chain[depth], depth);
        }

        if (file is not null)
        {
            BindFile(bindings, file);
        }

        return bindings;
    }

    private void BindItem(NamingTokenBindings bindings, ItemView item, int depth)
    {
        if (!_levels.TryGetValue(item.Ref.Level.ToString(), out var level))
        {
            return;
        }

        // The title binds first so a declared title field may overwrite it with the same value in a
        // richer kind (the field row carries the semantics; ItemView.Title is the guaranteed floor).
        bindings.Set($"{level.Name} Title", item.Title, TokenElasticity.Elastic, depth);

        foreach (var field in level.Fields)
        {
            if (!item.Fields.TryGetValue(field.FieldId, out var value))
            {
                continue;
            }

            var text = FieldValueText.Render(value);

            if (text.Length == 0)
            {
                continue;
            }

            var elasticity = (field.Semantics & FieldSemantics.Title) != 0
                ? TokenElasticity.Elastic
                : TokenElasticity.Rigid;

            bindings.Set(TokenBinding.Of($"{level.Name} {field.Name}", text, elasticity, depth) with
            {
                Year = YearOf(item),
            });
        }

        foreach (var external in item.ExternalIds)
        {
            // Level prefix is mandatory (D12): the same scheme legitimately appears on several levels.
            bindings.Set($"{level.Name} {external.Scheme} Id", external.Value, TokenElasticity.Rigid, depth);
        }

        BindCoordinates(bindings, item, level, depth);
    }

    private void BindCoordinates(NamingTokenBindings bindings, ItemView item, MediaLevel level, int depth)
    {
        foreach (var spaceId in level.CoordinateSpaceIds)
        {
            var space = _shape.CoordinateSpaces.FirstOrDefault(candidate =>
                string.Equals(candidate.SpaceId, spaceId, StringComparison.Ordinal));

            if (space is null || !item.Coordinates.TryGet(spaceId, out var reading))
            {
                continue;
            }

            switch (reading.Value.Kind)
            {
                case CoordinateKind.Date when reading.Value.Date is { } date:
                    // D4: the space name is the token; Sonarr's {Air Date}.
                    bindings.Set(space.Name, date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), TokenElasticity.Rigid, depth);
                    break;

                case CoordinateKind.Label when reading.Value.Label is { Length: > 0 } label:
                    // D5: equatable, not orderable — bound as text.
                    bindings.Set(space.Name, label, TokenElasticity.Rigid, depth);
                    break;

                case CoordinateKind.Ordinal:
                    for (var index = 0; index < space.Components.Count && index < reading.Value.Ordinals.Length; index++)
                    {
                        var ordinal = reading.Value.Ordinals[index];
                        var text = ordinal.ToString(CultureInfo.InvariantCulture);

                        // D8: a sequence exception's name becomes the axis token's value — the rule
                        // that replaces every hard-coded "SeasonNumber > 0" in surveyed naming code.
                        var axis = level.SequenceAxes.FirstOrDefault(candidate =>
                            string.Equals(candidate.SpaceId, spaceId, StringComparison.Ordinal)
                            && candidate.ComponentIndex == index);

                        var exception = axis?.Exceptions.FirstOrDefault(candidate => candidate.Value == ordinal);

                        if (exception is { Name.Length: > 0 })
                        {
                            bindings.Set($"{axis!.Name} Name", exception.Value.Name, TokenElasticity.Rigid, depth);
                        }

                        // D3: the component name is the token.
                        bindings.Set(space.Components[index].Name, text, TokenElasticity.Rigid, depth);

                        if (axis is not null)
                        {
                            // D6: the axis token, deduplicated by canonical name — axis wins over D3
                            // when both name the same word, because later writes win.
                            bindings.Set(axis.Name, text, TokenElasticity.Rigid, depth);
                        }
                    }

                    break;

                default:
                    break;
            }
        }
    }

    private static void BindFile(NamingTokenBindings bindings, MediaFileFacts file)
    {
        // The host-global vocabulary of §2.3: values come from host-owned file state, so no shape can
        // derive them and no kind may collide with them.
        var quality = file.Quality;
        var revision = quality.Revision ?? QualityRevision.Initial;
        var proper = revision.Version > 1 ? "Proper" : string.Empty;
        var real = revision.Real > 0 ? "REAL" : string.Empty;

        bindings.Set(TokenBinding.Of("Quality Title", quality.Name, TokenElasticity.Droppable));
        bindings.Set(TokenBinding.Of("Quality Proper", proper, TokenElasticity.Droppable));
        bindings.Set(TokenBinding.Of("Quality Real", real, TokenElasticity.Droppable));
        bindings.Set(TokenBinding.Of(
            "Quality Full",
            string.Join(' ', new[] { quality.Name, proper, real }.Where(part => part.Length != 0)),
            TokenElasticity.Droppable));

        if (file.ReleaseGroup is { Length: > 0 } group)
        {
            bindings.Set(TokenBinding.Of("Release Group", group, TokenElasticity.Elastic));
        }

        if (file.SceneName is { Length: > 0 } scene)
        {
            bindings.Set(TokenBinding.Of("Original Title", scene, TokenElasticity.Elastic));
        }

        if (file.OriginalFileName is { Length: > 0 } original)
        {
            bindings.Set(TokenBinding.Of(
                "Original Filename",
                Path.GetFileNameWithoutExtension(original),
                TokenElasticity.Elastic));
        }

        if (file.Languages.Count > 0)
        {
            bindings.Set(TokenBinding.Of(
                "Languages",
                string.Join('+', file.Languages.Select(language => language.Name)),
                TokenElasticity.Droppable));
        }

        var extension = Path.GetExtension(file.Path);

        if (extension.Length > 1)
        {
            // {Ext} carries the extension without its dot (§6.6).
            bindings.Set(TokenBinding.Of("Ext", extension[1..]));
        }

        foreach (var (facetId, value) in file.TechnicalFacets)
        {
            bindings.Set(TokenBinding.Of(facetId, value, TokenElasticity.Droppable));
        }

        foreach (var (facetId, value) in file.KindFacets)
        {
            // D15: per-kind file markers, keyed by the declaring TechnicalFacet identifier.
            bindings.Set(TokenBinding.Of(facetId, value, TokenElasticity.Droppable));
        }
    }

    private static int? YearOf(ItemView item)
    {
        foreach (var key in new[] { "year", "releaseYear" })
        {
            if (item.Fields.TryGetValue(key, out var value) && value.Number is { } year && year > 0)
            {
                return (int)year;
            }
        }

        return null;
    }
}
