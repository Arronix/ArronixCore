using System.Globalization;
using Arronix.Abstractions.Client;
using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Shape;

namespace Arronix.Client.Contracts;

/// <summary>Why a projection may not be rendered, and which kind of disagreement it is.</summary>
/// <param name="Outcome">The kind of failure, so a diagnostic and a test can tell them apart.</param>
/// <param name="Message">What was wrong, naming the field it was wrong at.</param>
internal sealed record ProjectionDefect(ContractPayloadOutcome Outcome, string Message);

/// <summary>
/// Proves a projection is the one its contract's schema describes, and captures what it proved.
/// </summary>
/// <remarks>
/// <para>
/// A projection is output from code this page downloaded, so nothing about its shape is guaranteed by the
/// type system. The walk is iterative and charged: one budget covers every descriptor, value, item and
/// choice, each list is read once and charged before anything iterates it, and depth plus a path-scoped
/// open set close the other two ways a graph can be unbounded.
/// </para>
/// <para>
/// Reading once is only half the rule. What is proved is copied into arrays as it is proved, and the
/// captured projection is the one a consumer renders; a list that answers one way while it is checked and
/// another way while it is drawn cannot reach a reader.
/// </para>
/// </remarks>
internal static class ProjectionAudit
{
    /// <summary>
    /// Describes why one projection may not be rendered against one schema, or nothing when it may.
    /// </summary>
    /// <param name="entityType">The entity type the contract declared.</param>
    /// <param name="schema">The contract's own schema, captured when it was admitted.</param>
    /// <param name="projection">What projecting produced.</param>
    /// <param name="trusted">
    /// What was proved, copied out of the contract's own objects. Every collection in it is this client's,
    /// so nothing a consumer reads can change after it was checked. Null when a defect is returned.
    /// </param>
    /// <returns>The defect, or <see langword="null"/> when the projection holds together.</returns>
    internal static ProjectionDefect? Describe(
        Type entityType,
        IReadOnlyList<FieldDescriptor> schema,
        ProjectedEntity? projection,
        out ProjectedEntity? trusted)
    {
        ArgumentNullException.ThrowIfNull(entityType);
        ArgumentNullException.ThrowIfNull(schema);

        ProjectedEntity? captured = null;
        ProjectionDefect? defect;

        // Every list read below belongs to the contract, and one may throw from Count, an indexer or an
        // enumerator as easily as it may answer wrongly. A throw is the same refusal as a bad answer.
        try
        {
            defect = Inspect(entityType, schema, projection, ref captured);
        }
        catch (Exception failure) when (!ProcessFailure.IsFatal(failure))
        {
            trusted = null;
            return new ProjectionDefect(
                ContractPayloadOutcome.ValueInvariant,
                $"This contract's projection could not be read: {failure.Message}");
        }

        trusted = defect is null ? captured : null;
        return defect;
    }

    private static ProjectionDefect? Inspect(
        Type entityType,
        IReadOnlyList<FieldDescriptor> schema,
        ProjectedEntity? projection,
        ref ProjectedEntity? captured)
    {
        if (projection is null)
        {
            return new ProjectionDefect(
                ContractPayloadOutcome.ProjectionFailed,
                "This contract projected the value into nothing.");
        }

        if (!ReferenceEquals(projection.EntityType, entityType))
        {
            return new ProjectionDefect(
                ContractPayloadOutcome.ProjectedTypeMismatch,
                $"This contract projected a '{Name(projection.EntityType)}' where it declares "
                + $"'{Name(entityType)}'.");
        }

        // Read once: a second read may answer differently, and what was checked would not be what was walked.
        var fields = projection.Fields;

        if (fields is null)
        {
            return new ProjectionDefect(
                ContractPayloadOutcome.SchemaDisagreement,
                "This contract's projection carries no field list.");
        }

        var count = fields.Count;
        var declared = schema.Count;

        if (count != declared)
        {
            return new ProjectionDefect(
                ContractPayloadOutcome.SchemaDisagreement,
                $"This contract projected {count} field(s) where its schema declares {declared}.");
        }

        var budget = new Budget();

        if (Spend(budget, count) is { } exhausted)
        {
            return exhausted;
        }

        var descriptors = new FieldDescriptor[count];
        var values = new FieldValue[count];

        for (var index = 0; index < count; index++)
        {
            var projected = fields[index];
            var expected = schema[index];

            if (projected is null)
            {
                return new ProjectionDefect(
                    ContractPayloadOutcome.SchemaDisagreement,
                    $"This contract projected nothing at position {index}, where its schema declares "
                    + $"'{expected.FieldId}'.");
            }

            // Object identity, in order, one each: dropping, reordering or duplicating moves a position,
            // and an equal clone is a second description of a field nothing chose between.
            if (!ReferenceEquals(projected.Descriptor, expected))
            {
                return new ProjectionDefect(
                    ContractPayloadOutcome.SchemaDisagreement,
                    $"This contract projected the field '{Identify(projected.Descriptor)}' at position "
                    + $"{index}, where its schema declares '{expected.FieldId}'. A projected field carries "
                    + "the schema's own descriptor, not a copy of it.");
            }

            if (projected.Value is null)
            {
                return new ProjectionDefect(
                    ContractPayloadOutcome.ValueInvariant,
                    $"'{expected.FieldId}' has no value. A field an item holds nothing for is absent, "
                    + "which is a value.");
            }

            values[index] = projected.Value;

            if (FreezeDescriptor(expected, budget, out descriptors[index]) is { } undescribable)
            {
                return undescribable;
            }
        }

        var trusted = new ProjectedField[count];

        for (var index = 0; index < count; index++)
        {
            var field = descriptors[index];

            if (FreezeValue(field, values[index], field.FieldId, budget, out var value) is { } defect)
            {
                return defect;
            }

            trusted[index] = new ProjectedField(field, value!);
        }

        captured = new ProjectedEntity(entityType, trusted);
        return null;
    }

    /// <summary>
    /// Proves one declared field and its components, and copies them into lists this client owns.
    /// </summary>
    private static ProjectionDefect? FreezeDescriptor(
        FieldDescriptor root,
        Budget budget,
        out FieldDescriptor frozen)
    {
        frozen = root;
        var open = new HashSet<FieldDescriptor>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<DescriptorFrame>();
        var top = new DescriptorFrame(root, root.FieldId ?? "?", 1, null, 0);
        pending.Push(top);

        while (pending.Count > 0)
        {
            var frame = pending.Peek();

            if (frame.Entered)
            {
                pending.Pop();
                open.Remove(frame.Source);
                frame.Complete();
                continue;
            }

            frame.Entered = true;

            if (frame.Depth > ClientContractLimits.MaxDepth)
            {
                return Invalid(frame.Path, $"nests deeper than {ClientContractLimits.MaxDepth} levels");
            }

            if (Spend(budget, 1) is { } exhausted)
            {
                return exhausted;
            }

            if (!open.Add(frame.Source))
            {
                return Invalid(frame.Path, "is described by a field that contains itself");
            }

            if (Describable(frame, budget) is { } undescribable)
            {
                return undescribable;
            }

            for (var index = frame.Components.Length - 1; index >= 0; index--)
            {
                var component = frame.Components[index];

                if (component is null)
                {
                    return Invalid(frame.Path, $"declares nothing as its component at position {index}");
                }

                pending.Push(new DescriptorFrame(
                    component,
                    frame.Path + "." + (component.FieldId ?? "?"),
                    frame.Depth + 1,
                    frame,
                    index));
            }
        }

        frozen = top.Frozen!;
        return null;
    }

    /// <summary>Holds a descriptor to the shape a consumer can draw, charging the lists it names.</summary>
    private static ProjectionDefect? Describable(DescriptorFrame frame, Budget budget)
    {
        var field = frame.Source;
        var path = frame.Path;

        if (Sized(field.FieldId, ClientContractLimits.MaxIdentifierLength, budget) is { } identifier)
        {
            return Invalid(path, $"declares a field identifier that {identifier}");
        }

        if (Sized(field.Name, ClientContractLimits.MaxTextLength, budget) is { } name)
        {
            return Invalid(path, $"declares a display name that {name}");
        }

        if (field.Description is { } description)
        {
            if (description.Length > ClientContractLimits.MaxTextLength)
            {
                return Invalid(path, $"declares a description of {description.Length} characters");
            }

            if (Charge(budget, description.Length) is { } budgeted)
            {
                return Invalid(path, $"declares a description that {budgeted}");
            }
        }

        if (field.Unit is { } unit)
        {
            if (unit.Length > ClientContractLimits.MaxIdentifierLength)
            {
                return Invalid(path, $"declares a unit of {unit.Length} characters");
            }

            if (Charge(budget, unit.Length) is { } spent)
            {
                return Invalid(path, $"declares a unit that {spent}");
            }
        }

        var components = field.Components;

        if (components is null)
        {
            return Invalid(path, "declares a null component list");
        }

        var componentCount = components.Count;

        if (Spend(budget, componentCount) is { } componentBudget)
        {
            return componentBudget;
        }

        if (field.ValueKind == FieldValueKind.Composite)
        {
            if (componentCount == 0)
            {
                return Invalid(path, "is a composite that declares no components");
            }
        }
        else if (componentCount != 0)
        {
            return Invalid(path, $"declares {componentCount} component(s) and is not a composite");
        }

        // Copied as they are read, so what is walked is what is drawn.
        frame.Components = new FieldDescriptor[componentCount];

        for (var index = 0; index < componentCount; index++)
        {
            frame.Components[index] = components[index];
        }

        var choices = field.Choices;

        if (choices is null)
        {
            return Invalid(path, "declares a null choice list");
        }

        var choiceCount = choices.Count;

        if (Spend(budget, choiceCount) is { } choiceBudget)
        {
            return choiceBudget;
        }

        if (field.ValueKind != FieldValueKind.Enumerated)
        {
            return choiceCount == 0
                ? null
                : Invalid(path, $"declares {choiceCount} choice(s) and is not enumerated");
        }

        if (choiceCount == 0)
        {
            return Invalid(path, "is enumerated and declares no choices, so no value it carries can be one "
                + "of them");
        }

        frame.Choices = new FacetValue[choiceCount];

        for (var index = 0; index < choiceCount; index++)
        {
            var choice = choices[index];

            if (Sized(choice.Value, ClientContractLimits.MaxIdentifierLength, budget) is { } stored)
            {
                return Invalid(path, $"declares a choice whose stored value {stored}");
            }

            if (Sized(choice.Name, ClientContractLimits.MaxTextLength, budget) is { } shown)
            {
                return Invalid(path, $"declares a choice whose display name {shown}");
            }

            frame.Choices[index] = choice;
        }

        return null;
    }

    /// <summary>Proves one value against its frozen descriptor, and copies what it proved.</summary>
    private static ProjectionDefect? FreezeValue(
        FieldDescriptor root,
        FieldValue value,
        string path,
        Budget budget,
        out FieldValue? frozen)
    {
        frozen = null;
        var open = new HashSet<FieldValue>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<ValueFrame>();
        var top = new ValueFrame(root, value, path, 1, element: false, null, 0);
        pending.Push(top);

        while (pending.Count > 0)
        {
            var frame = pending.Peek();

            if (frame.Entered)
            {
                pending.Pop();
                open.Remove(frame.Source);
                frame.Complete();
                continue;
            }

            frame.Entered = true;

            if (frame.Depth > ClientContractLimits.MaxDepth)
            {
                return Invalid(frame.Path, $"nests deeper than {ClientContractLimits.MaxDepth} levels");
            }

            if (Spend(budget, 1) is { } exhausted)
            {
                return exhausted;
            }

            if (!open.Add(frame.Source))
            {
                return Invalid(frame.Path, "contains itself");
            }

            if (Enter(pending, frame, budget) is { } defect)
            {
                return defect;
            }
        }

        frozen = top.Frozen;
        return null;
    }

    /// <summary>Proves one value and schedules whatever it contains.</summary>
    private static ProjectionDefect? Enter(Stack<ValueFrame> pending, ValueFrame frame, Budget budget)
    {
        var field = frame.Field;
        var value = frame.Source;
        var path = frame.Path;
        var kind = value.Kind;

        if (kind != field.ValueKind)
        {
            return Invalid(path, $"carries a {kind} value where '{field.FieldId}' declares {field.ValueKind}");
        }

        // Read once: everything below decides about these exact objects.
        var items = value.Items;
        var absent = value.IsAbsent;
        var slots = Populated(value);

        if (field.Multivalued && !frame.Element)
        {
            return EnterList(pending, frame, items, absent, slots, budget);
        }

        if (kind == FieldValueKind.Composite)
        {
            return EnterComposite(pending, frame, items, absent, slots, budget);
        }

        if (absent)
        {
            return slots == Slot.None
                ? null
                : Invalid(path, $"is absent and carries {Describe(slots)}. Absent means the item has no "
                    + "value for the field");
        }

        var permitted = Permitted(kind);

        if (permitted is null)
        {
            return Invalid(path, $"declares the value shape {(int)kind}, which is not one this client draws");
        }

        return slots != permitted.Value && !(kind == FieldValueKind.Artwork && slots == Slot.Link)
            ? Invalid(path, $"is a {kind} carrying {Describe(slots)}, and a {kind} carries "
                + $"{Describe(permitted.Value)}")
            : Content(field, value, kind, path, budget);
    }

    /// <summary>Proves the container of a multivalued field and schedules its elements.</summary>
    private static ProjectionDefect? EnterList(
        Stack<ValueFrame> pending,
        ValueFrame frame,
        IReadOnlyList<FieldValue>? items,
        bool absent,
        Slot slots,
        Budget budget)
    {
        var path = frame.Path;

        if (absent)
        {
            return slots == Slot.None ? null : Invalid(path, $"is absent and carries {Describe(slots)}");
        }

        if (items is null)
        {
            return Invalid(path, "is a present multivalued field with no list. A field holding no values is "
                + "an empty list; a field with no value at all is absent, and the two are different facts");
        }

        if (slots != Slot.Items)
        {
            return Invalid(path, $"is a multivalued field carrying {Describe(slots)}, and a list of values "
                + "carries only its elements");
        }

        var count = items.Count;

        if (Spend(budget, count) is { } exhausted)
        {
            return exhausted;
        }

        frame.Open(count);

        for (var index = count - 1; index >= 0; index--)
        {
            var element = items[index];

            if (element is null)
            {
                return Invalid(path, $"holds nothing at position {index}");
            }

            pending.Push(new ValueFrame(
                frame.Field,
                element,
                Position(path, index),
                frame.Depth + 1,
                element: true,
                frame,
                index));
        }

        return null;
    }

    /// <summary>Proves one composite tuple and schedules its parts against their own components.</summary>
    private static ProjectionDefect? EnterComposite(
        Stack<ValueFrame> pending,
        ValueFrame frame,
        IReadOnlyList<FieldValue>? items,
        bool absent,
        Slot slots,
        Budget budget)
    {
        var field = frame.Field;
        var path = frame.Path;

        if (absent)
        {
            return slots == Slot.None ? null : Invalid(path, $"is absent and carries {Describe(slots)}");
        }

        if (items is null)
        {
            return Invalid(path, "is a present composite with no components");
        }

        if (slots != Slot.Items)
        {
            return Invalid(path, $"is a composite carrying {Describe(slots)}, and a composite carries only "
                + "its components");
        }

        // The frozen components: read once when the field was proved, and the same list ever since.
        var components = field.Components;
        var declared = components.Count;
        var count = items.Count;

        if (count != declared)
        {
            return Invalid(path, $"carries {count} component(s) where its field declares {declared}");
        }

        if (Spend(budget, count) is { } exhausted)
        {
            return exhausted;
        }

        frame.Open(count);

        for (var index = count - 1; index >= 0; index--)
        {
            var part = items[index];
            var descriptor = components[index];

            if (part is null)
            {
                return Invalid(path, $"holds nothing for its component '{descriptor.FieldId}'");
            }

            pending.Push(new ValueFrame(
                descriptor,
                part,
                path + "." + descriptor.FieldId,
                frame.Depth + 1,
                element: false,
                frame,
                index));
        }

        return null;
    }

    /// <summary>Holds a present scalar's payload to what its shape means.</summary>
    private static ProjectionDefect? Content(
        FieldDescriptor field,
        FieldValue value,
        FieldValueKind kind,
        string path,
        Budget budget)
        => kind switch
        {
            FieldValueKind.Text or FieldValueKind.MultilineText or FieldValueKind.FilePath =>
                Sized(value.Text, ClientContractLimits.MaxTextLength, budget) is { } text
                    ? Invalid(path, $"carries text that {text}")
                    : null,

            FieldValueKind.Enumerated => Choice(field, value.Text, path, budget),

            FieldValueKind.Decimal => double.IsFinite(value.Real!.Value)
                ? null
                : Invalid(path, $"carries the number {Rendered(value.Real.Value)}"),

            // A proportion where one means whole; a meter drawn from anything else clamps a wrong value
            // into a plausible one.
            FieldValueKind.Ratio => double.IsFinite(value.Real!.Value)
                    && value.Real.Value >= 0d
                    && value.Real.Value <= 1d
                ? null
                : Invalid(path, $"carries the proportion {Rendered(value.Real.Value)}, and a proportion "
                    + "runs from zero to one"),

            FieldValueKind.Duration => value.Duration!.Value >= TimeSpan.Zero
                ? null
                : Invalid(path, $"carries the elapsed time {value.Duration.Value}, which runs backwards"),

            FieldValueKind.ByteSize or FieldValueKind.Count => value.Number!.Value >= 0
                ? null
                : Invalid(path, $"carries {value.Number.Value.ToString(CultureInfo.InvariantCulture)}, and "
                    + $"a {kind} is never negative"),

            FieldValueKind.Reference => Reference(value.Reference!.Value, path, budget),

            FieldValueKind.Ordinal => value.Ordinals!.Value.Length > 0
                ? null
                : Invalid(path, "carries an ordinal with no components"),

            FieldValueKind.ExternalIdentifier => External(value.External!.Value, path, budget),

            FieldValueKind.Language => Spoken(value.Language!, path, budget),

            FieldValueKind.Quality =>
                Sized(value.Quality!.Name, ClientContractLimits.MaxIdentifierLength, budget) is { } quality
                    ? Invalid(path, $"carries a quality whose name {quality}")
                    : null,

            FieldValueKind.Link => BrowserAddress.DescribeLink(value.Link) is { } link
                ? new ProjectionDefect(ContractPayloadOutcome.AddressUnsafe, $"'{path}': {link}")
                : Charge(budget, value.Link!.OriginalString.Length) is { } budgeted
                    ? Invalid(path, $"carries an address that {budgeted}")
                    : null,

            FieldValueKind.Artwork => Artwork(value, path, budget),

            _ => null,
        };

    /// <summary>Holds an enumerated value to the choices its own field declared.</summary>
    /// <remarks>An unknown value fails; showing it as text puts arbitrary text through a closed field.</remarks>
    private static ProjectionDefect? Choice(FieldDescriptor field, string? stored, string path, Budget budget)
    {
        if (Sized(stored, ClientContractLimits.MaxIdentifierLength, budget) is { } sized)
        {
            return Invalid(path, $"carries a stored choice that {sized}");
        }

        var choices = field.Choices;
        var count = choices.Count;

        for (var index = 0; index < count; index++)
        {
            if (string.Equals(choices[index].Value, stored, StringComparison.Ordinal))
            {
                return null;
            }
        }

        return Invalid(path, $"carries '{stored}', which '{field.FieldId}' does not declare as a choice");
    }

    /// <summary>Holds an item reference to a triple that can resolve; all three are minted together.</summary>
    private static ProjectionDefect? Reference(MediaItemRef reference, string path, Budget budget)
    {
        if (Sized(reference.Kind.Value, ClientContractLimits.MaxIdentifierLength, budget) is { } kind)
        {
            return Invalid(path, $"carries a reference whose media kind {kind}");
        }

        if (Sized(reference.Level.Value, ClientContractLimits.MaxIdentifierLength, budget) is { } level)
        {
            return Invalid(path, $"carries a reference whose level {level}");
        }

        return reference.Id.Value > 0
            ? null
            : Invalid(path, $"carries a reference to item "
                + $"{reference.Id.Value.ToString(CultureInfo.InvariantCulture)}, and an assigned identifier "
                + "is positive");
    }

    private static ProjectionDefect? External(ExternalId external, string path, Budget budget)
    {
        if (Sized(external.Scheme, ClientContractLimits.MaxIdentifierLength, budget) is { } scheme)
        {
            return Invalid(path, $"carries an identifier whose scheme {scheme}");
        }

        return Sized(external.Value, ClientContractLimits.MaxIdentifierLength, budget) is { } stored
            ? Invalid(path, $"carries an identifier whose value {stored}")
            : null;
    }

    private static ProjectionDefect? Spoken(Language language, string path, Budget budget)
    {
        if (Sized(language.Code, ClientContractLimits.MaxIdentifierLength, budget) is { } code)
        {
            return Invalid(path, $"carries a language whose code {code}");
        }

        return Sized(language.Name, ClientContractLimits.MaxTextLength, budget) is { } name
            ? Invalid(path, $"carries a language whose name {name}")
            : null;
    }

    /// <summary>Holds artwork to a whole image whose address a browser may load.</summary>
    private static ProjectionDefect? Artwork(FieldValue value, string path, Budget budget)
    {
        var image = value.Image;

        if (image is null)
        {
            // An address alone is the shape a producer holding nothing else uses, held to the same rule
            // and charged the same way: it is text this client renders either way.
            if (BrowserAddress.DescribeArtwork(value.Link) is { } bare)
            {
                return new ProjectionDefect(ContractPayloadOutcome.AddressUnsafe, $"'{path}': {bare}");
            }

            return Charge(budget, value.Link!.OriginalString.Length) is { } spent
                ? Invalid(path, $"carries an address that {spent}")
                : null;
        }

        if (Sized(image.Role, ClientContractLimits.MaxIdentifierLength, budget) is { } role)
        {
            return Invalid(path, $"carries an image whose role {role}");
        }

        if (BrowserAddress.DescribeArtwork(image.Address) is { } address)
        {
            return new ProjectionDefect(ContractPayloadOutcome.AddressUnsafe, $"'{path}': {address}");
        }

        if (Charge(budget, image.Address.OriginalString.Length) is { } budgeted)
        {
            return Invalid(path, $"carries an image whose address {budgeted}");
        }

        if (Measurement(image.Width) is { } width)
        {
            return Invalid(path, $"carries an image whose width {width}");
        }

        return Measurement(image.Height) is { } height
            ? Invalid(path, $"carries an image whose height {height}")
            : null;
    }

    private static string? Measurement(int? edge)
        => edge switch
        {
            null => null,
            <= 0 => "is not a positive number of pixels",
            > ClientContractLimits.MaxImageEdge =>
                $"is larger than the {ClientContractLimits.MaxImageEdge} pixels an image may state",
            _ => null,
        };

    /// <summary>Describes text that is missing, blank or too long, or nothing when it is none of those.</summary>
    /// <remarks>A semantic identifier made of spaces names nothing and renders as though it were unlabeled.</remarks>
    private static string? Sized(string? value, int maximum, Budget budget)
        => value switch
        {
            null => "is not stated",
            { Length: 0 } => "is empty",
            _ when value.Length > maximum => $"is {value.Length} characters, past the {maximum} allowed",
            _ when string.IsNullOrWhiteSpace(value) => "is white space",
            _ => Charge(budget, value.Length),
        };

    /// <summary>Charges text against the total one projection may render.</summary>
    /// <remarks>
    /// Each string is bounded on its own and a graph may hold thousands of them, so the per-value limits
    /// multiply out well past what a browser should hold from a payload that is itself capped. The whole
    /// rendering is charged, so the total cannot be reached by repetition.
    /// </remarks>
    private static string? Charge(Budget budget, int length)
    {
        if (length > budget.Characters)
        {
            return $"is past the {ClientContractLimits.MaxProjectionCharacters} characters one projection "
                + "may render in total";
        }

        budget.Characters -= length;
        return null;
    }

    /// <summary>What a walk over one projection has left to spend.</summary>
    private sealed class Budget
    {
        internal int Nodes { get; set; } = ClientContractLimits.MaxNodes;

        internal int Characters { get; set; } = ClientContractLimits.MaxProjectionCharacters;
    }

    private static ProjectionDefect Invalid(string path, string what)
        => new(ContractPayloadOutcome.ValueInvariant, $"'{path}' {what}.");

    /// <summary>Charges values against the walk's budget, refusing a shape that asks for more.</summary>
    private static ProjectionDefect? Spend(Budget budget, int cost)
    {
        if (cost < 0 || cost > budget.Nodes)
        {
            return new ProjectionDefect(
                ContractPayloadOutcome.ValueInvariant,
                $"This contract's projection describes more than {ClientContractLimits.MaxNodes} values.");
        }

        budget.Nodes -= cost;
        return null;
    }

    private static string Rendered(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    private static string Position(string path, int index)
        => path + "[" + index.ToString(CultureInfo.InvariantCulture) + "]";

    private static string Identify(FieldDescriptor? descriptor)
        => descriptor is null ? "nothing" : descriptor.FieldId ?? "an unnamed field";

    private static string Name(Type? type) => type?.FullName ?? type?.Name ?? "nothing";

    /// <summary>The payload slots one value has filled in.</summary>
    [Flags]
    private enum Slot
    {
        None = 0,
        Text = 1,
        Number = 2,
        Real = 4,
        Flag = 8,
        Instant = 16,
        Date = 32,
        Duration = 64,
        Ordinals = 128,
        Reference = 256,
        External = 512,
        Link = 1024,
        Image = 2048,
        Quality = 4096,
        Language = 8192,
        Items = 16384
    }

    /// <summary>Reads every slot exactly once and reports which are filled.</summary>
    private static Slot Populated(FieldValue value)
    {
        var slots = Slot.None;

        if (value.Text is not null)
        {
            slots |= Slot.Text;
        }

        if (value.Number is not null)
        {
            slots |= Slot.Number;
        }

        if (value.Real is not null)
        {
            slots |= Slot.Real;
        }

        if (value.Flag is not null)
        {
            slots |= Slot.Flag;
        }

        if (value.Instant is not null)
        {
            slots |= Slot.Instant;
        }

        if (value.Date is not null)
        {
            slots |= Slot.Date;
        }

        if (value.Duration is not null)
        {
            slots |= Slot.Duration;
        }

        if (value.Ordinals is not null)
        {
            slots |= Slot.Ordinals;
        }

        if (value.Reference is not null)
        {
            slots |= Slot.Reference;
        }

        if (value.External is not null)
        {
            slots |= Slot.External;
        }

        if (value.Link is not null)
        {
            slots |= Slot.Link;
        }

        if (value.Image is not null)
        {
            slots |= Slot.Image;
        }

        if (value.Quality is not null)
        {
            slots |= Slot.Quality;
        }

        if (value.Language is not null)
        {
            slots |= Slot.Language;
        }

        if (value.Items is not null)
        {
            slots |= Slot.Items;
        }

        return slots;
    }

    /// <summary>The slot each value shape is carried in; artwork has two, a whole image or a bare address.</summary>
    private static Slot? Permitted(FieldValueKind kind) => kind switch
    {
        FieldValueKind.Text or FieldValueKind.MultilineText or FieldValueKind.FilePath
            or FieldValueKind.Enumerated => Slot.Text,
        FieldValueKind.Integer or FieldValueKind.ByteSize or FieldValueKind.Count => Slot.Number,
        FieldValueKind.Decimal or FieldValueKind.Ratio => Slot.Real,
        FieldValueKind.Boolean => Slot.Flag,
        FieldValueKind.Date => Slot.Date,
        FieldValueKind.Instant => Slot.Instant,
        FieldValueKind.Duration => Slot.Duration,
        FieldValueKind.Ordinal => Slot.Ordinals,
        FieldValueKind.Reference => Slot.Reference,
        FieldValueKind.ExternalIdentifier => Slot.External,
        FieldValueKind.Link => Slot.Link,
        FieldValueKind.Language => Slot.Language,
        FieldValueKind.Quality => Slot.Quality,
        FieldValueKind.Artwork => Slot.Image,
        FieldValueKind.Composite => Slot.Items,
        _ => null,
    };

    private static string Describe(Slot slots) => slots == Slot.None ? "no payload" : slots.ToString();

    /// <summary>One declared field being proved, and the copy being built from it.</summary>
    private sealed class DescriptorFrame(
        FieldDescriptor source,
        string path,
        int depth,
        DescriptorFrame? parent,
        int slot)
    {
        internal FieldDescriptor Source { get; } = source;

        internal string Path { get; } = path;

        internal int Depth { get; } = depth;

        internal bool Entered { get; set; }

        /// <summary>The components read once when this field was proved.</summary>
        internal FieldDescriptor[] Components { get; set; } = [];

        /// <summary>The choices read once when this field was proved.</summary>
        internal FacetValue[] Choices { get; set; } = [];

        internal FieldDescriptor? Frozen { get; private set; }

        private FieldDescriptor[] FrozenComponents { get; set; } = [];

        /// <summary>Assembles this field from the components that completed under it.</summary>
        internal void Complete()
        {
            Frozen = Source with { Components = FrozenComponents, Choices = Choices };
            parent?.Accept(slot, Frozen);
        }

        private void Accept(int index, FieldDescriptor component)
        {
            if (FrozenComponents.Length != Components.Length)
            {
                FrozenComponents = new FieldDescriptor[Components.Length];
            }

            FrozenComponents[index] = component;
        }
    }

    /// <summary>One value being proved, and the copy being built from it.</summary>
    private sealed class ValueFrame(
        FieldDescriptor field,
        FieldValue source,
        string path,
        int depth,
        bool element,
        ValueFrame? parent,
        int slot)
    {
        private FieldValue[]? _children;

        internal FieldDescriptor Field { get; } = field;

        internal FieldValue Source { get; } = source;

        internal string Path { get; } = path;

        internal int Depth { get; } = depth;

        internal bool Element { get; } = element;

        internal bool Entered { get; set; }

        internal FieldValue? Frozen { get; private set; }

        /// <summary>Records that this value holds a list, so the copy replaces it rather than reusing it.</summary>
        internal void Open(int count) => _children = new FieldValue[count];

        /// <summary>Assembles this value from the entries that completed under it.</summary>
        internal void Complete()
        {
            Frozen = _children is null ? Source : Source with { Items = _children };
            parent?.Accept(slot, Frozen);
        }

        private void Accept(int index, FieldValue child) => _children![index] = child;
    }
}
