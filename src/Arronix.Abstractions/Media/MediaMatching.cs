using System.Linq.Expressions;

namespace Arronix.Abstractions.Media;

/// <summary>How a lookup key is multiplied into the spellings a release may use.</summary>
public enum KeyExpansion
{
    /// <summary>The key is looked up as derived.</summary>
    None = 0,

    /// <summary>Roman numerals and their decimal spellings are treated as equivalent.</summary>
    RomanNumerals = 1
}

/// <summary>What an absent release-side statement means for an agreement rule.</summary>
public enum Agreement
{
    /// <summary>An absent statement fails the rule.</summary>
    Reject = 0,

    /// <summary>An absent statement satisfies the rule.</summary>
    Accept = 1
}

/// <summary>A parsed release fact that a candidate item can be held to agree with.</summary>
public enum ReadingFact
{
    /// <summary>The year stated alongside the title.</summary>
    TitleYear = 0
}

/// <summary>One ordered item-title key layer.</summary>
public sealed record MatchKeyLayer<TItem>(
    string Id,
    Expression<Func<TItem, IEnumerable<string?>>> Keys,
    KeyExpansion Expansion = KeyExpansion.None)
    where TItem : class, IMediaItem;

/// <summary>A typed agreement between one release reading and candidate item values.</summary>
public interface IMatchAgreement<TItem>
    where TItem : class, IMediaItem
{
    /// <summary>Dispatches the closed value type to a kind-blind compiler.</summary>
    void Accept(IMatchAgreementVisitor<TItem> visitor);
}

/// <summary>The host side of a typed match agreement.</summary>
public interface IMatchAgreementVisitor<TItem>
    where TItem : class, IMediaItem
{
    /// <summary>Visits one agreement while retaining its closed item-side value type.</summary>
    void Visit<TValue>(MatchAgreement<TItem, TValue> agreement)
        where TValue : struct;
}

/// <summary>A closed agreement definition retaining its value type.</summary>
public sealed record MatchAgreement<TItem, TValue>(
    ReadingFact Reading,
    Expression<Func<TItem, IEnumerable<TValue?>>> CandidateValues,
    Agreement WhenAbsent = Agreement.Reject,
    double? Floor = null) : IMatchAgreement<TItem>
    where TItem : class, IMediaItem
    where TValue : struct
{
    /// <inheritdoc />
    public void Accept(IMatchAgreementVisitor<TItem> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        visitor.Visit(this);
    }
}
