using System.Linq;
using Arronix.Abstractions.Client;
using Arronix.Abstractions.Shape;
using Arronix.Client.Rendering;

namespace Arronix.Client.Contracts;

/// <summary>One image a projected field carries, as a proof reads it.</summary>
/// <param name="Role">What the image is for.</param>
/// <param name="Address">Where it is fetched from, or the inline payload itself.</param>
/// <param name="Width">Its width in pixels, when the supplier stated one.</param>
/// <param name="Height">Its height in pixels, when the supplier stated one.</param>
public sealed record ContractPayloadImageProof(string Role, string Address, int? Width, int? Height);

/// <summary>One projected field, as a proof reads it.</summary>
/// <param name="FieldId">The identifier the contract declares the field by.</param>
/// <param name="Name">Its display name.</param>
/// <param name="ValueKind">The shape of its values, by name.</param>
/// <param name="Multivalued">Whether the field holds a list.</param>
/// <param name="Absent">Whether the item has no value for it, which an empty list is not.</param>
/// <param name="ItemCount">How many entries its list holds, when it holds one.</param>
/// <param name="Text">What this client renders it as.</param>
/// <param name="Images">The images it carries, whole rather than as addresses.</param>
public sealed record ContractPayloadFieldProof(
    string FieldId,
    string Name,
    string ValueKind,
    bool Multivalued,
    bool Absent,
    int? ItemCount,
    string Text,
    IReadOnlyList<ContractPayloadImageProof> Images);

/// <summary>
/// One payload's outcome in a form a proof harness can read.
/// </summary>
/// <remarks>
/// Separate from <see cref="ContractPayloadReport"/>, which carries live CLR types no serializer writes.
/// Everything here is a string, a number or a list of them, and the text is the text on the page.
/// </remarks>
public sealed record ContractPayloadProof(
    string Outcome,
    string Address,
    string? AssemblyName,
    string? EntryPointType,
    string? EntityTypeName,
    int? PayloadLength,
    int FieldCount,
    IReadOnlyList<ContractPayloadFieldProof> Fields,
    string? Failure)
{
    /// <summary>Reads one report into the form a proof harness reads.</summary>
    /// <param name="report">The report.</param>
    /// <returns>The proof.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="report"/> is <see langword="null"/>.</exception>
    public static ContractPayloadProof Of(ContractPayloadReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var fields = report.Projection?.Fields ?? [];

        return new ContractPayloadProof(
            report.Outcome.ToString(),
            report.Address,
            report.AssemblyName,
            report.EntryPointType,
            report.EntityTypeName,
            report.PayloadLength,
            fields.Count,
            [.. fields.Select(Read)],
            report.Failure);
    }

    private static ContractPayloadFieldProof Read(ProjectedField field)
        => new(
            field.Descriptor.FieldId,
            field.Descriptor.Name,
            field.Descriptor.ValueKind.ToString(),
            field.Descriptor.Multivalued,
            field.Value.IsAbsent,
            field.Value.Items?.Count,
            FieldValueFormatter.Format(field.Descriptor, field.Value),
            Images(field.Value));

    /// <summary>Collects the whole images one field carries, however deeply it carries them.</summary>
    /// <remarks>Bounded by the same budget the projection was proved against.</remarks>
    private static IReadOnlyList<ContractPayloadImageProof> Images(FieldValue value)
    {
        var found = new List<ContractPayloadImageProof>();
        var pending = new Stack<FieldValue>();
        var remaining = ClientContractLimits.MaxNodes;
        pending.Push(value);

        while (pending.Count > 0 && remaining-- > 0)
        {
            var current = pending.Pop();

            if (current.Image is { } image)
            {
                found.Add(new ContractPayloadImageProof(
                    image.Role,
                    image.Address.ToString(),
                    image.Width,
                    image.Height));
            }

            if (current.Items is not { } items)
            {
                continue;
            }

            var count = items.Count;

            if (count > remaining)
            {
                break;
            }

            for (var index = count - 1; index >= 0; index--)
            {
                pending.Push(items[index]);
            }
        }

        return found;
    }
}
