using Arronix.Abstractions.Client;
using Arronix.Abstractions.Shape;
using Arronix.Client.Diagnostics;

namespace Arronix.Client.Contracts;

/// <summary>
/// One contract's projection schema, read once at admission and held two ways.
/// </summary>
/// <remarks>
/// <para>
/// A schema is a shape a contract's own code returns, and every list in it — the roots, each field's
/// components, each field's choices — belongs to the contract. Reading the root list once is not enough:
/// what is hashed at admission, what a payload is later validated against, and what a page renders would
/// otherwise be three separate reads of objects that may answer differently each time.
/// </para>
/// <para>
/// So the whole graph is read once, in one bounded walk, and two facts are kept apart.
/// <see cref="Admitted"/> is the contract's own root descriptor objects, used for exactly one thing:
/// requiring a projected field to carry the descriptor this contract was admitted with, at its own
/// position. <see cref="Frozen"/> is a deep copy this client owns, and is what the published schema hash
/// covers, what a payload's values are proved against, and what a page renders.
/// </para>
/// </remarks>
internal sealed class ClientContractSchema
{
    private readonly ProjectionBudget _spent;

    private ClientContractSchema(
        IReadOnlyList<FieldDescriptor> admitted,
        IReadOnlyList<FieldDescriptor> frozen,
        ProjectionBudget spent)
    {
        Admitted = admitted;
        Frozen = frozen;
        _spent = spent;
    }

    /// <summary>Gets the contract's own root descriptors, captured in the admission read.</summary>
    /// <remarks>Compared by object identity and never read into; nothing renders from these.</remarks>
    internal IReadOnlyList<FieldDescriptor> Admitted { get; }

    /// <summary>Gets the client-owned copy: root order, components, choices and scalar content.</summary>
    internal IReadOnlyList<FieldDescriptor> Frozen { get; }

    /// <summary>Gets how many fields the schema declares.</summary>
    internal int Count => Admitted.Count;

    /// <summary>Gets a budget for one projection, continuing from what this schema already spent.</summary>
    /// <returns>A new budget each time, because each projection renders this schema once.</returns>
    /// <remarks>
    /// One total covers a rendering, and a rendering is this schema plus one projection's values. Charging
    /// the schema once, here, and continuing from the remainder is what keeps that one number true when the
    /// schema is read at admission and the values are read per payload.
    /// </remarks>
    internal ProjectionBudget Remaining() => _spent.Remainder();

    /// <summary>
    /// Reads a declared schema once and freezes it, or says why it cannot be described.
    /// </summary>
    /// <param name="declared">The schema, exactly as the declaration answered with it.</param>
    /// <param name="schema">What was read, when it could be read.</param>
    /// <returns>The defect, or <see langword="null"/>.</returns>
    internal static ProjectionDefect? Freeze(
        IReadOnlyList<FieldDescriptor>? declared,
        out ClientContractSchema? schema)
    {
        schema = null;

        if (declared is null)
        {
            return new ProjectionDefect(
                ContractPayloadOutcome.SchemaDisagreement,
                "This contract's projection schema is not a list of fields.");
        }

        ClientContractSchema? captured = null;
        ProjectionDefect? defect;

        // Every list read below belongs to the contract, and one may throw from Count, an indexer or an
        // enumerator as easily as it may answer wrongly. A throw is the same refusal as a bad answer.
        try
        {
            defect = Read(declared, ref captured);
        }
        catch (Exception failure) when (!ProcessFailure.IsFatal(failure))
        {
            return new ProjectionDefect(
                ContractPayloadOutcome.SchemaDisagreement,
                $"This contract's projection schema could not be read: {failure.Message}");
        }

        schema = defect is null ? captured : null;
        return defect;
    }

    private static ProjectionDefect? Read(
        IReadOnlyList<FieldDescriptor> declared,
        ref ClientContractSchema? captured)
    {
        var budget = new ProjectionBudget();
        var count = declared.Count;

        if (budget.Spend(count) is { } exhausted)
        {
            return exhausted;
        }

        var admitted = new FieldDescriptor[count];
        var frozen = new FieldDescriptor[count];

        for (var index = 0; index < count; index++)
        {
            // Read once. Everything below — the hash, every later payload, every render — is about this
            // object and the copy taken from it, never about a second answer.
            var root = declared[index];

            if (root is null)
            {
                return new ProjectionDefect(
                    ContractPayloadOutcome.SchemaDisagreement,
                    $"This contract's projection schema declares nothing at position {index}.");
            }

            admitted[index] = root;

            if (FreezeField(root, budget, out frozen[index]) is { } undescribable)
            {
                return undescribable;
            }
        }

        captured = new ClientContractSchema(admitted, frozen, budget);
        return null;
    }

    /// <summary>Proves one declared field and its components, and copies them into lists this client owns.</summary>
    private static ProjectionDefect? FreezeField(
        FieldDescriptor root,
        ProjectionBudget budget,
        out FieldDescriptor frozen)
    {
        frozen = root;
        var open = new HashSet<FieldDescriptor>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<Frame>();
        var top = new Frame(root, root.FieldId ?? "?", 1, null, 0);
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
                return ProjectionDefect.At(frame.Path, $"nests deeper than {ClientContractLimits.MaxDepth} levels");
            }

            if (budget.Spend(1) is { } exhausted)
            {
                return exhausted;
            }

            if (!open.Add(frame.Source))
            {
                return ProjectionDefect.At(frame.Path, "is described by a field that contains itself");
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
                    return ProjectionDefect.At(frame.Path, $"declares nothing as its component at position {index}");
                }

                pending.Push(new Frame(
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
    private static ProjectionDefect? Describable(Frame frame, ProjectionBudget budget)
    {
        var field = frame.Source;
        var path = frame.Path;

        if (budget.Sized(field.FieldId, ClientContractLimits.MaxIdentifierLength) is { } identifier)
        {
            return ProjectionDefect.At(path, $"declares a field identifier that {identifier}");
        }

        if (budget.Sized(field.Name, ClientContractLimits.MaxTextLength) is { } name)
        {
            return ProjectionDefect.At(path, $"declares a display name that {name}");
        }

        if (field.Description is { } description)
        {
            if (description.Length > ClientContractLimits.MaxTextLength)
            {
                return ProjectionDefect.At(path, $"declares a description of {description.Length} characters");
            }

            if (budget.Charge(description.Length) is { } budgeted)
            {
                return ProjectionDefect.At(path, $"declares a description that {budgeted}");
            }
        }

        if (field.Unit is { } unit)
        {
            if (unit.Length > ClientContractLimits.MaxIdentifierLength)
            {
                return ProjectionDefect.At(path, $"declares a unit of {unit.Length} characters");
            }

            if (budget.Charge(unit.Length) is { } spent)
            {
                return ProjectionDefect.At(path, $"declares a unit that {spent}");
            }
        }

        var components = field.Components;

        if (components is null)
        {
            return ProjectionDefect.At(path, "declares a null component list");
        }

        var componentCount = components.Count;

        if (budget.Spend(componentCount) is { } componentBudget)
        {
            return componentBudget;
        }

        if (field.ValueKind == FieldValueKind.Composite)
        {
            if (componentCount == 0)
            {
                return ProjectionDefect.At(path, "is a composite that declares no components");
            }
        }
        else if (componentCount != 0)
        {
            return ProjectionDefect.At(path, $"declares {componentCount} component(s) and is not a composite");
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
            return ProjectionDefect.At(path, "declares a null choice list");
        }

        var choiceCount = choices.Count;

        if (budget.Spend(choiceCount) is { } choiceBudget)
        {
            return choiceBudget;
        }

        if (field.ValueKind != FieldValueKind.Enumerated)
        {
            return choiceCount == 0
                ? null
                : ProjectionDefect.At(path, $"declares {choiceCount} choice(s) and is not enumerated");
        }

        if (choiceCount == 0)
        {
            return ProjectionDefect.At(path, "is enumerated and declares no choices, so no value it carries "
                + "can be one of them");
        }

        frame.Choices = new FacetValue[choiceCount];

        for (var index = 0; index < choiceCount; index++)
        {
            var choice = choices[index];

            if (budget.Sized(choice.Value, ClientContractLimits.MaxIdentifierLength) is { } stored)
            {
                return ProjectionDefect.At(path, $"declares a choice whose stored value {stored}");
            }

            if (budget.Sized(choice.Name, ClientContractLimits.MaxTextLength) is { } shown)
            {
                return ProjectionDefect.At(path, $"declares a choice whose display name {shown}");
            }

            frame.Choices[index] = choice;
        }

        return null;
    }

    /// <summary>One declared field being proved, and the copy being built from it.</summary>
    private sealed class Frame(
        FieldDescriptor source,
        string path,
        int depth,
        Frame? parent,
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
}
