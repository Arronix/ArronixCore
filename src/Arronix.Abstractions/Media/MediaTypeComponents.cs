using System.Linq.Expressions;
using Arronix.Abstractions.Intent;
using Arronix.Abstractions.Shape;

namespace Arronix.Abstractions.Media;

/// <summary>One typed source-facing query plan.</summary>
public sealed record QueryDefinition<TItem>
    where TItem : class, IMediaItem
{
    /// <summary>Gets the query tiers in semantic precedence order.</summary>
    public IReadOnlyList<QueryTierDefinition<TItem>> Tiers { get; init; } = [];

    /// <summary>Gets alternative title spellings in semantic precedence order.</summary>
    public IReadOnlyList<QueryAliasDefinition<TItem>> Aliases { get; init; } = [];
}

/// <summary>One tier of a typed source query plan.</summary>
public sealed record QueryTierDefinition<TItem>(string Id, string SearchId)
    where TItem : class, IMediaItem
{
    /// <summary>Gets the origins on which this tier applies. Empty means every origin.</summary>
    public IReadOnlyList<SearchOrigin> Origins { get; init; } = [];

    /// <summary>Gets the identity roles without which this tier cannot run.</summary>
    public IReadOnlyList<IdentifierRole> RequiredIdentityRoles { get; init; } = [];

    /// <summary>Gets typed conditions which must hold before this tier can run.</summary>
    public IReadOnlyList<IItemPropertyDefinition<TItem>> Requirements { get; init; } = [];

    /// <summary>Gets structured source arguments.</summary>
    public IReadOnlyList<IQueryArgumentDefinition<TItem>> Arguments { get; init; } = [];

    /// <summary>Gets the optional free-text query.</summary>
    public Expression<Func<TItem, string?>>? FreeText { get; init; }

    /// <summary>Gets whether the tier intentionally sends no terms.</summary>
    public bool HasNoTerms { get; init; }

    /// <summary>Gets whether the tier emits one query per alias spelling.</summary>
    public bool FanOutPerAlias { get; init; }

    /// <summary>Gets whether aliases accompany the structured query.</summary>
    public bool CarryAliases { get; init; }
}

/// <summary>One typed argument in a source query.</summary>
public interface IQueryArgumentDefinition<TItem>
    where TItem : class, IMediaItem
{
    /// <summary>Gets the source-facing term.</summary>
    SearchTerm Term { get; }

    /// <summary>Gets whether the argument is omitted when no value exists.</summary>
    bool OmitWhenAbsent { get; }

    /// <summary>Gets the item expression supplying the value, when it comes from an item property.</summary>
    LambdaExpression? Property { get; }

    /// <summary>Gets the external identity role supplying the value, when it is an identifier.</summary>
    IdentifierRole? IdentityRole { get; }
}

/// <summary>A source query argument supplied by an item expression.</summary>
public sealed record QueryPropertyArgument<TItem, TValue>(
    SearchTerm Term,
    Expression<Func<TItem, TValue>> Value,
    bool OmitWhenAbsent = false) : IQueryArgumentDefinition<TItem>
    where TItem : class, IMediaItem
{
    LambdaExpression IQueryArgumentDefinition<TItem>.Property => Value;

    IdentifierRole? IQueryArgumentDefinition<TItem>.IdentityRole => null;
}

/// <summary>A source query argument supplied by one external identity role.</summary>
public sealed record QueryIdentityArgument<TItem>(
    SearchTerm Term,
    IdentifierRole Role,
    bool OmitWhenAbsent = false) : IQueryArgumentDefinition<TItem>
    where TItem : class, IMediaItem
{
    LambdaExpression? IQueryArgumentDefinition<TItem>.Property => null;

    IdentifierRole? IQueryArgumentDefinition<TItem>.IdentityRole => Role;
}

/// <summary>One ordered source-query alias row.</summary>
public sealed record QueryAliasDefinition<TItem>(
    string Id,
    Expression<Func<TItem, IEnumerable<string?>>> Spellings)
    where TItem : class, IMediaItem
{
    /// <summary>Gets whether only accepted languages contribute spellings.</summary>
    public bool FilterByAcceptedLanguages { get; init; }

    /// <summary>Gets whether this alias may accompany queries but never create one by itself.</summary>
    public bool NeverOwnQuery { get; init; }
}

/// <summary>The typed naming choices a host cannot derive from the item shape.</summary>
public sealed record NamingDefinition<TItem>
    where TItem : class, IMediaItem
{
    /// <summary>Gets the default file template.</summary>
    public string? FileTemplate { get; init; }

    /// <summary>Gets the default item-folder template.</summary>
    public string? FolderTemplate { get; init; }

    /// <summary>Gets typed group-folder templates.</summary>
    public IReadOnlyList<IGroupNamingDefinition<TItem>> GroupFolders { get; init; } = [];

    /// <summary>Gets the fixed path skeleton.</summary>
    public string FolderSpine { get; init; } = "{root}/{folder}";

    /// <summary>Gets typed rules that insert group segments.</summary>
    public IReadOnlyList<IGroupNamingSelection<TItem>> GroupSelections { get; init; } = [];

    /// <summary>Gets semantic requirements over a user-authored file template.</summary>
    public IReadOnlyList<TypedTemplateRequirement<TItem>> Requirements { get; init; } = [];

    /// <summary>Gets typed token fallbacks.</summary>
    public IReadOnlyList<ITokenFallbackDefinition<TItem>> Fallbacks { get; init; } = [];

    /// <summary>Gets the fallback for an entirely empty result.</summary>
    public FileFact? EmptyResultFallback { get; init; }
}

/// <summary>A group folder template retaining its closed group type.</summary>
public interface IGroupNamingDefinition<TItem>
    where TItem : class, IMediaItem
{
    /// <summary>Gets the closed group type.</summary>
    Type GroupType { get; }

    /// <summary>Gets the user-authored template.</summary>
    string Template { get; }
}

/// <summary>A group folder template.</summary>
public sealed record GroupNamingDefinition<TItem, TGroup>(string Template) : IGroupNamingDefinition<TItem>
    where TItem : class, IMediaItem
    where TGroup : class, IMediaGroup<TItem>
{
    /// <inheritdoc />
    public Type GroupType => typeof(TGroup);
}

/// <summary>A typed rule selecting one group folder segment.</summary>
public interface IGroupNamingSelection<TItem>
    where TItem : class, IMediaItem
{
    /// <summary>Gets the closed group type.</summary>
    Type GroupType { get; }

    /// <summary>Gets the stable rule identifier.</summary>
    string RuleId { get; }
}

/// <summary>A rule inserting one group folder segment when grouping is enabled.</summary>
public sealed record GroupNamingSelection<TItem, TGroup>(string RuleId) : IGroupNamingSelection<TItem>
    where TItem : class, IMediaItem
    where TGroup : class, IMediaGroup<TItem>
{
    /// <inheritdoc />
    public Type GroupType => typeof(TGroup);
}

/// <summary>A typed semantic requirement over a user-authored naming template.</summary>
public sealed record TypedTemplateRequirement<TItem>(
    string Id,
    string Requirement,
    Func<INamingTemplateFacts<TItem>, bool> IsSatisfied)
    where TItem : class, IMediaItem;

/// <summary>A typed token fallback.</summary>
public interface ITokenFallbackDefinition<TItem>
    where TItem : class, IMediaItem
{
    /// <summary>Gets the item expression naming the token.</summary>
    LambdaExpression Property { get; }

    /// <summary>Gets fallback file facts in precedence order.</summary>
    IReadOnlyList<FileFact> Order { get; }
}

/// <summary>A token fallback retaining the property's closed value type.</summary>
public sealed record TokenFallbackDefinition<TItem, TValue>(
    Expression<Func<TItem, TValue>> Value,
    IReadOnlyList<FileFact> Order) : ITokenFallbackDefinition<TItem>
    where TItem : class, IMediaItem
{
    LambdaExpression ITokenFallbackDefinition<TItem>.Property => Value;
}

/// <summary>The typed summary projection of a media item.</summary>
public sealed record SummaryDefinition<TItem>
    where TItem : class, IMediaItem
{
    /// <summary>Gets the headline expression.</summary>
    public Expression<Func<TItem, string?>>? Headline { get; init; }

    /// <summary>Gets the maximum headline length.</summary>
    public int HeadlineMaxLength { get; init; } = 256;

    /// <summary>Gets the summary-body expression.</summary>
    public Expression<Func<TItem, string?>>? Body { get; init; }

    /// <summary>Gets the maximum body length.</summary>
    public int BodyMaxLength { get; init; } = 300;

    /// <summary>Gets additional summary fields.</summary>
    public IReadOnlyList<SummaryFieldDefinition<TItem>> Fields { get; init; } = [];

    /// <summary>Gets summaries for durable group relationships.</summary>
    public IReadOnlyList<IGroupSummaryDefinition<TItem>> Groups { get; init; } = [];
}

/// <summary>One typed item summary field.</summary>
public sealed record SummaryFieldDefinition<TItem>(
    string Label,
    Expression<Func<TItem, object?>> Value,
    SummaryFieldWeight Weight = SummaryFieldWeight.Secondary)
    where TItem : class, IMediaItem;

/// <summary>A typed group summary.</summary>
public interface IGroupSummaryDefinition<TItem>
    where TItem : class, IMediaItem
{
    /// <summary>Gets the closed group type.</summary>
    Type GroupType { get; }

    /// <summary>Gets the headline expression.</summary>
    LambdaExpression Headline { get; }

    /// <summary>Gets additional summary fields.</summary>
    IReadOnlyList<IGroupSummaryFieldDefinition> Fields { get; }
}

/// <summary>One typed summary of a durable group.</summary>
public sealed record GroupSummaryDefinition<TItem, TGroup>(
    Expression<Func<TGroup, string?>> HeadlineValue,
    IReadOnlyList<IGroupSummaryFieldDefinition> Fields) : IGroupSummaryDefinition<TItem>
    where TItem : class, IMediaItem
    where TGroup : class, IMediaGroup<TItem>
{
    /// <inheritdoc />
    public Type GroupType => typeof(TGroup);

    LambdaExpression IGroupSummaryDefinition<TItem>.Headline => HeadlineValue;
}

/// <summary>One field in a group summary.</summary>
public interface IGroupSummaryFieldDefinition
{
    /// <summary>Gets the display label.</summary>
    string Label { get; }

    /// <summary>Gets the typed group expression.</summary>
    LambdaExpression Value { get; }

    /// <summary>Gets the field's summary weight.</summary>
    SummaryFieldWeight Weight { get; }
}

/// <summary>One typed field in a group summary.</summary>
public sealed record GroupSummaryFieldDefinition<TGroup, TValue>(
    string Label,
    Expression<Func<TGroup, TValue>> TypedValue,
    SummaryFieldWeight Weight = SummaryFieldWeight.Secondary) : IGroupSummaryFieldDefinition
{
    LambdaExpression IGroupSummaryFieldDefinition.Value => TypedValue;
}

/// <summary>Typed exceptions to the intent surface derived from item attributes.</summary>
public sealed record IntentDefinition<TItem>
    where TItem : class, IMediaItem
{
    /// <summary>Gets the default browse axis identifier.</summary>
    public string DefaultBrowseId { get; init; } = "all";

    /// <summary>Gets the default browse axis name.</summary>
    public string DefaultBrowseName { get; init; } = "All";

    /// <summary>Gets explicit sort directions.</summary>
    public IReadOnlyList<ISortDefinition<TItem>> Sorts { get; init; } = [];

    /// <summary>Gets fields hidden only from browse axes.</summary>
    public IReadOnlyList<IItemPropertyDefinition<TItem>> HiddenBrowseFields { get; init; } = [];

    /// <summary>Gets semantic tones for media-owned states.</summary>
    public IReadOnlyList<IStateToneDefinition> StateTones { get; init; } = [];
}

/// <summary>One typed sort override.</summary>
public interface ISortDefinition<TItem>
    where TItem : class, IMediaItem
{
    /// <summary>Gets the typed property expression.</summary>
    LambdaExpression Property { get; }

    /// <summary>Gets whether the useful end is the beginning.</summary>
    bool Ascending { get; }
}

/// <summary>One typed sort override.</summary>
public sealed record SortDefinition<TItem, TValue>(
    Expression<Func<TItem, TValue>> Value,
    bool Ascending) : ISortDefinition<TItem>
    where TItem : class, IMediaItem
{
    LambdaExpression ISortDefinition<TItem>.Property => Value;
}

/// <summary>A typed item property retained in a heterogeneous definition list.</summary>
public interface IItemPropertyDefinition<TItem>
    where TItem : class, IMediaItem
{
    /// <summary>Gets the typed property expression.</summary>
    LambdaExpression Property { get; }
}

/// <summary>One typed item property.</summary>
public sealed record ItemPropertyDefinition<TItem, TValue>(Expression<Func<TItem, TValue>> Value)
    : IItemPropertyDefinition<TItem>
    where TItem : class, IMediaItem
{
    LambdaExpression IItemPropertyDefinition<TItem>.Property => Value;
}

/// <summary>One typed state-tone association.</summary>
public interface IStateToneDefinition
{
    /// <summary>Gets the enum value.</summary>
    Enum State { get; }

    /// <summary>Gets its user meaning.</summary>
    StateTone Tone { get; }
}

/// <summary>One state-tone association retaining the state enumeration.</summary>
public sealed record StateToneDefinition<TState>(TState Value, StateTone Tone) : IStateToneDefinition
    where TState : struct, Enum
{
    Enum IStateToneDefinition.State => Value;
}

/// <summary>One typed workbench declaration.</summary>
public interface IWorkbenchDefinition<TItem>
    where TItem : class, IMediaItem
{
    /// <summary>Gets the closed row type.</summary>
    Type RowType { get; }

    /// <summary>Gets the stable workbench identifier.</summary>
    string Id { get; }

    /// <summary>Gets the user-facing name.</summary>
    string Name { get; }

    /// <summary>Gets its subject.</summary>
    WorkbenchSubject Subject { get; }

    /// <summary>Gets required inputs.</summary>
    IReadOnlyList<WorkbenchInputDefinition> Inputs { get; }

    /// <summary>Gets the commit label.</summary>
    string CommitLabel { get; }

    /// <summary>Gets the commit consequence.</summary>
    Consequence CommitConsequence { get; }
}

/// <summary>A typed workbench whose row is its schema.</summary>
public sealed record WorkbenchDefinition<TItem, TRow>(string Id, string Name) : IWorkbenchDefinition<TItem>
    where TItem : class, IMediaItem
{
    /// <inheritdoc />
    public Type RowType => typeof(TRow);

    /// <inheritdoc />
    public WorkbenchSubject Subject { get; init; } = WorkbenchSubject.LibraryItems;

    /// <inheritdoc />
    public IReadOnlyList<WorkbenchInputDefinition> Inputs { get; init; } = [];

    /// <inheritdoc />
    public string CommitLabel { get; init; } = "Commit";

    /// <inheritdoc />
    public Consequence CommitConsequence { get; init; } = Consequence.Safe;
}

/// <summary>One input collected before a workbench proposal is requested.</summary>
public sealed record WorkbenchInputDefinition(string Id, string Name, IdentifierRole? IdentityRole = null);
