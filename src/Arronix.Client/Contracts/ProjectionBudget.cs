using Arronix.Abstractions.Client;

namespace Arronix.Client.Contracts;

/// <summary>Why a contract's shape may not be described, and which kind of disagreement it is.</summary>
/// <param name="Outcome">The kind of failure, so a diagnostic and a test can tell them apart.</param>
/// <param name="Message">What was wrong, naming the field it was wrong at.</param>
internal sealed record ProjectionDefect(ContractPayloadOutcome Outcome, string Message)
{
    /// <summary>States a defect against the field it was found at.</summary>
    internal static ProjectionDefect At(string path, string what)
        => new(ContractPayloadOutcome.ValueInvariant, $"'{path}' {what}.");
}

/// <summary>
/// What one bounded walk over a contract-produced shape has left to spend.
/// </summary>
/// <remarks>
/// Two dimensions, because a shape can be unreasonable in two ways: too many values, and too much text in
/// however many there are. Each string is bounded on its own and the node budget bounds how many there are,
/// but the two multiply well past what a browser should hold, so the total is charged as well.
/// </remarks>
internal sealed class ProjectionBudget
{
    private int _nodes;
    private int _characters;

    /// <summary>Initializes a full budget.</summary>
    internal ProjectionBudget()
        : this(ClientContractLimits.MaxNodes, ClientContractLimits.MaxProjectionCharacters)
    {
    }

    private ProjectionBudget(int nodes, int characters)
    {
        _nodes = nodes;
        _characters = characters;
    }

    /// <summary>Gets what this budget has left, as a budget a later walk continues from.</summary>
    /// <returns>A new budget; spending it does not spend this one.</returns>
    /// <remarks>
    /// A schema is walked once, when its contract is admitted, and every projection of that contract
    /// renders it again. One total covers both, so the schema is charged where it is read and each
    /// projection continues from what it left.
    /// </remarks>
    internal ProjectionBudget Remainder() => new(_nodes, _characters);

    /// <summary>Charges values, refusing a shape that asks for more than there is.</summary>
    /// <param name="cost">How many values.</param>
    /// <returns>The refusal, or <see langword="null"/>.</returns>
    internal ProjectionDefect? Spend(int cost)
    {
        if (cost < 0 || cost > _nodes)
        {
            return new ProjectionDefect(
                ContractPayloadOutcome.ValueInvariant,
                $"This contract's projection describes more than {ClientContractLimits.MaxNodes} values.");
        }

        _nodes -= cost;
        return null;
    }

    /// <summary>Charges text against the total one projection may render.</summary>
    /// <param name="length">How many characters.</param>
    /// <returns>A phrase describing the refusal, or <see langword="null"/>.</returns>
    internal string? Charge(int length)
    {
        if (length > _characters)
        {
            return $"is past the {ClientContractLimits.MaxProjectionCharacters} characters one projection "
                + "may render in total";
        }

        _characters -= length;
        return null;
    }

    /// <summary>Describes text that is missing, blank or too long, or nothing when it is none of those.</summary>
    /// <param name="value">The text.</param>
    /// <param name="maximum">The most characters this text may carry.</param>
    /// <returns>A phrase describing the refusal, or <see langword="null"/>.</returns>
    /// <remarks>A semantic identifier made of spaces names nothing and renders as though it were unlabeled.</remarks>
    internal string? Sized(string? value, int maximum)
        => value switch
        {
            null => "is not stated",
            { Length: 0 } => "is empty",
            _ when value.Length > maximum => $"is {value.Length} characters, past the {maximum} allowed",
            _ when string.IsNullOrWhiteSpace(value) => "is white space",
            _ => Charge(value.Length),
        };
}
