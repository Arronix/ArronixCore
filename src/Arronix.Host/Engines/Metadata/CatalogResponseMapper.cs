// The shape (ARX0013), providers (ARX0015) and definition (ARX0019) contracts are experimental until 1.0.
#pragma warning disable ARX0013
#pragma warning disable ARX0015
#pragma warning disable ARX0019

using System.Linq;
using System.Text.Json;
using Arronix.Abstractions.Definition;
using Arronix.Abstractions.Providers;
using Arronix.Abstractions.Shape;

namespace Arronix.Host.Engines.Metadata;

/// <summary>
/// Executes a catalog's declared response maps: JSON documents in, <see cref="MetadataNode"/>s out.
/// </summary>
/// <remarks>
/// <para>
/// The mapping logic the four per-kind catalogers hand-wrote, run from data: each
/// <see cref="ResponseMap"/> names the level it populates, where the record's external identifier
/// lives, the field rows with their converters, and — for group responses that embed their members —
/// a member path whose elements re-enter the member level's map
/// (<c>declarative-media-kinds.md</c> §2.8; exhibit section 7's collection map).
/// </para>
/// <para>
/// A map addressing a grouping axis is carried on the member level in this milestone:
/// <see cref="MetadataNode"/> has no axis discriminator, which is reported as a declaration gap rather
/// than papered over with a fake level.
/// </para>
/// </remarks>
internal sealed class CatalogResponseMapper
{
    private readonly CatalogDeclaration _declaration;
    private readonly CatalogValueConverters _converters;
    private readonly MediaLevelId _rootLevel;

    /// <summary>
    /// Initializes a new instance of the <see cref="CatalogResponseMapper"/> class.
    /// </summary>
    /// <param name="declaration">The catalog declaration.</param>
    /// <param name="shape">The kind's shape, naming the root level and the axis member levels.</param>
    /// <param name="converters">The converter set.</param>
    public CatalogResponseMapper(CatalogDeclaration declaration, MediaShape shape, CatalogValueConverters converters)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        ArgumentNullException.ThrowIfNull(shape);
        ArgumentNullException.ThrowIfNull(converters);

        _declaration = declaration;
        _converters = converters;
        _rootLevel = shape.Levels.First(level => level.Parent is null).Id;

        foreach (var map in declaration.Responses)
        {
            foreach (var row in map.Rows.Where(row => !CatalogValueConverters.IsKnown(row.Converter)))
            {
                // Unknown converter = load failure, never a silent fallback (ResponseMapRow contract).
                throw new ArgumentException(
                    $"Response row '{row.JsonPath}' names unknown converter '{row.Converter}'.",
                    nameof(declaration));
            }
        }
    }

    /// <summary>
    /// Finds the map for a level.
    /// </summary>
    /// <param name="levelId">The level, or null for the shape's root level.</param>
    /// <returns>The map, or null when the declaration has none.</returns>
    public ResponseMap? MapForLevel(string? levelId) =>
        _declaration.Responses.FirstOrDefault(map =>
            string.Equals(map.LevelId, levelId ?? _rootLevel.ToString(), StringComparison.Ordinal));

    /// <summary>
    /// Finds the map for a grouping axis.
    /// </summary>
    /// <param name="axisId">The axis.</param>
    /// <returns>The map, or null when the declaration has none.</returns>
    public ResponseMap? MapForAxis(string axisId) =>
        _declaration.Responses.FirstOrDefault(map =>
            string.Equals(map.AxisId, axisId, StringComparison.Ordinal));

    /// <summary>
    /// Maps one response element into nodes: the subject, and its members when the map embeds them.
    /// </summary>
    /// <param name="element">The response element.</param>
    /// <param name="map">The map to run.</param>
    /// <param name="parentId">The parent identifier carried onto the produced node.</param>
    /// <param name="enrich">
    /// Runs over each node's mapped fields with the element it was mapped from — the derivation hook.
    /// </param>
    /// <returns>The nodes, subject first. Empty when the element carries no identifier.</returns>
    public IReadOnlyList<MetadataNode> Map(
        JsonElement element,
        ResponseMap map,
        ExternalId? parentId = null,
        Action<IDictionary<string, FieldValue>, JsonElement>? enrich = null)
    {
        ArgumentNullException.ThrowIfNull(map);

        var nodes = new List<MetadataNode>();
        var idText = JsonPathReader.FirstText(element, map.ExternalIdPath);

        if (idText is not { Length: > 0 })
        {
            return nodes;
        }

        var id = ExternalId.Of(map.ExternalIdScheme, idText);
        var fields = new Dictionary<string, FieldValue>(StringComparer.Ordinal);

        foreach (var row in map.Rows)
        {
            var value = _converters.Convert(JsonPathReader.Evaluate(element, row.JsonPath), row.Converter);

            if (value is not null)
            {
                fields[row.FieldId] = value;
            }
        }

        enrich?.Invoke(fields, element);

        var title = fields.TryGetValue("title", out var titleValue) && titleValue.Text is { Length: > 0 } text
            ? text
            : string.Empty;

        var level = map.LevelId is { Length: > 0 } declared
            ? MediaLevelId.FromString(declared)
            : _rootLevel;

        nodes.Add(new MetadataNode(level, id, parentId, title, fields, CoordinateSet.Empty));

        if (map.MemberPath is { Length: > 0 } memberPath)
        {
            // Members re-enter the member level's own map, parented to this subject.
            var memberMap = MapForLevel(null);

            if (memberMap is not null)
            {
                foreach (var member in JsonPathReader.Evaluate(element, memberPath))
                {
                    nodes.AddRange(Map(member, memberMap, id, enrich));
                }
            }
        }

        return nodes;
    }
}
